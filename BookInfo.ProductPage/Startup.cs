using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using OpenTracing;
using OpenTracing.Util;
using Jaeger.Samplers;
using Jaeger;
using Prometheus;
using Microsoft.AspNetCore.Http.Extensions;
using BookInfo.ProductPage.Controllers;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;

namespace BookInfo.ProductPage
{
    public class Startup
    {
        private const string Liveness = "Liveness";
        private const string Readiness = "Readiness";
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers();
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "Book Reviews API", Version = "v1" });
            });
            
            //HealthChecks
            services.AddHealthChecks()
                    .AddCheck<ReviewsHealthCheck>("reviews_health_check", failureStatus: HealthStatus.Unhealthy,tags: new[] { Readiness })
                    .AddCheck<DetailsHealthCheck>("details_health_check", failureStatus: HealthStatus.Unhealthy,tags: new[] { Readiness });
                    
            services.AddOpenTracing();

            // Adds the Jaeger Tracer.
            services.AddSingleton<ITracer>(serviceProvider =>
            {
                string serviceName = serviceProvider.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>().ApplicationName;

                if (System.Environment.GetEnvironmentVariable("JAEGER_SERVICE_NAME") == null) 
                    Environment.SetEnvironmentVariable("JAEGER_SERVICE_NAME", serviceName);
                if (System.Environment.GetEnvironmentVariable("JAEGER_AGENT_HOST") == null) 
                    Environment.SetEnvironmentVariable("JAEGER_AGENT_HOST", "localhost");                
                if (System.Environment.GetEnvironmentVariable("JAEGER_AGENT_PORT") == null) 
                    Environment.SetEnvironmentVariable("JAEGER_AGENT_PORT", "6831");                
                if (System.Environment.GetEnvironmentVariable("JAEGER_SAMPLER_TYPE") == null) 
                    Environment.SetEnvironmentVariable("JAEGER_SAMPLER_TYPE", "const");

                var loggerFactory = new LoggerFactory();

                var config = Jaeger.Configuration.FromEnv(loggerFactory);
                var tracer = config.GetTracer();

                if (!GlobalTracer.IsRegistered())
                {
                    // Allows code that can't use DI to also access the tracer.
                    GlobalTracer.Register(tracer);
                }

                return tracer;
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            //Prometheus Metrics
            app.UseMetricServer();
            app.UseRequestMiddleware();  

            //Standard http metrics for Prometheus
            app.UseHttpMetrics();

            // Enable middleware to serve generated Swagger as a JSON endpoint.
            app.UseSwagger();

            // Enable middleware to serve swagger-ui (HTML, JS, CSS, etc.),
            // specifying the Swagger JSON endpoint.
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "Book Reviews V1");
            });

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapMetrics();
                endpoints.MapHealthChecks("/liveness", new HealthCheckOptions
                {
                    Predicate = check => check.Tags.Contains(Liveness),
                    ResultStatusCodes =
                    {
                        [HealthStatus.Healthy] = StatusCodes.Status200OK,
                        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
                    }
                });

                endpoints.MapHealthChecks("/readiness", new HealthCheckOptions
                {
                    Predicate = check => check.Tags.Contains(Readiness),
                    ResultStatusCodes =
                    {
                        [HealthStatus.Healthy] = StatusCodes.Status200OK,
                        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
                    }
                });
            });
        }
    }
}
