using System;
using System.Linq;
using System.Text;

using Mealmate.Api.Application.Middlewares;
using Mealmate.Core.Configuration;
using Mealmate.Core.Entities;
using Mealmate.Infrastructure.Data;
using Mealmate.Infrastructure.IoC;
using Mealmate.Infrastructure.Misc;

using Autofac;
using Autofac.Extensions.DependencyInjection;

using FluentValidation.AspNetCore;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using AutoMapper;
using Autofac.Core.Lifetime;
using NSwag;
using System.Collections.Generic;
using Autofac.Core;
using NSwag.Generation.Processors.Security;
using Microsoft.Extensions.Hosting;

namespace Mealmate.Api
{
    public class Startup
    {
        public Startup(IConfiguration configuration, IWebHostEnvironment hostingEnvironment)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(hostingEnvironment.ContentRootPath)
                .AddJsonFile("appsettings.json")
                .AddEnvironmentVariables();

            _config = builder.Build();

            Configuration = configuration;
            HostingEnvironment = hostingEnvironment;
            MealmateSettings = configuration.Get<MealmateSettings>();
        }

        private readonly IConfigurationRoot _config;

        public IConfiguration Configuration { get; }
        public IWebHostEnvironment HostingEnvironment { get; }
        public MealmateSettings MealmateSettings { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public IServiceProvider ConfigureServices(IServiceCollection services)
        {
            return
            services
                .AddSingleton(_config)
                .AddCustomMvc()
                .AddCustomDbContext(MealmateSettings)
                .AddCustomIdentity()
                .AddCustomSwagger()
                .AddCustomConfiguration(Configuration)
                .AddCustomAuthentication(MealmateSettings)
                .AddCustomIntegrations(HostingEnvironment);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            app.UseCors("CorsPolicy");

            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();

            app.UseMiddleware<LoggingMiddleware>();

            // Enable middleware to serve generated Swagger as a JSON endpoint.
            app.UseOpenApi();
            app.UseSwaggerUi3();
            app.UseMiddleware<LoggingMiddleware>();
            app.UseRouting();
            app.UseAuthorization();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                   name: "default",
                   pattern: "{controller}/{action=Index}/{id?}");

            });
        }
    }

    static class CustomExtensionsMethods
    {
        public static IServiceCollection AddCustomMvc(this IServiceCollection services)
        {
            // Add framework services.
            services
                .AddMvc(configure =>
                {
                    configure.EnableEndpointRouting = false;
                })
                .AddFluentValidation(fv =>
                {
                    fv.RunDefaultMvcValidationAfterFluentValidationExecutes = false;
                })
                .AddNewtonsoftJson(options =>
                {
                    options.SerializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
                    options.SerializerSettings.ReferenceLoopHandling = ReferenceLoopHandling.Ignore;
                })
                .AddControllersAsServices();

            services.AddCors(options =>
            {
                options.AddPolicy("CorsPolicy",
                    builder => builder
                    .SetIsOriginAllowed((host) => true)
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials());
            });
            return services;
        }

        public static IServiceCollection AddCustomDbContext(this IServiceCollection services, MealmateSettings MealmateSettings)
        {
            // use in-memory database
            //services.AddDbContext<MealmateContext>(c => c.UseInMemoryDatabase("Mealmate"));

            // Add Mealmate DbContext
            services
                .AddEntityFrameworkSqlServer()
                .AddDbContext<MealmateContext>(options =>
                        options
                        .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking)
                        .UseSqlServer(MealmateSettings.ConnectionString,
                        sqlOptions =>
                        {
                            sqlOptions.EnableRetryOnFailure(maxRetryCount: 10, maxRetryDelay: TimeSpan.FromSeconds(30), errorNumbersToAdd: null);
                            sqlOptions.MigrationsAssembly("Mealmate.Api");
                            sqlOptions.MigrationsHistoryTable("__MigrationsHistory", "Migration");
                        }
                    ),
                    ServiceLifetime.Transient
                 );

            return services;
        }

        public static IServiceCollection AddCustomIdentity(this IServiceCollection services)
        {
            var sp = services.BuildServiceProvider();
            using (var scope = sp.CreateScope())
            {
                var existingUserManager = scope.ServiceProvider.GetService<UserManager<User>>();

                if (existingUserManager == null)
                {
                    services.AddIdentity<User, Role>(
                        //cfg =>
                        //{
                        //    User.RequireUniqueEmail = true;
                        //}
                        )
                        .AddEntityFrameworkStores<MealmateContext>()
                        .AddDefaultTokenProviders();
                }
            }

            return services;
        }

        public static IServiceCollection AddCustomSwagger(this IServiceCollection services)
        {
            services.AddSwaggerDocument(config =>
            {
                config.DocumentProcessors.Add(new SecurityDefinitionAppender("JWT Token", new OpenApiSecurityScheme
                {
                    Type = OpenApiSecuritySchemeType.ApiKey,
                    Name = "Authorization",
                    Description = "Copy 'Bearer ' + valid JWT token into field",
                    In = OpenApiSecurityApiKeyLocation.Header
                }));

                config.PostProcess = document =>
                {
                    document.Info.Version = "v1";
                    document.Info.Title = "Mealmate HTTP API";
                    document.Info.Description = "The Mealmate Service HTTP API";
                    document.Info.TermsOfService = "Terms Of Service";
                };
            });

            return services;
        }

        public static IServiceCollection AddCustomConfiguration(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddOptions();
            services.Configure<MealmateSettings>(configuration);

            services.Configure<ApiBehaviorOptions>(options =>
            {
                options.InvalidModelStateResponseFactory = context =>
                {
                    var problemDetails = new ValidationProblemDetails(context.ModelState)
                    {
                        Instance = context.HttpContext.Request.Path,
                        Status = StatusCodes.Status400BadRequest,
                        Detail = "Please refer to the errors property for additional details."
                    };

                    return new BadRequestObjectResult(problemDetails)
                    {
                        ContentTypes = { "application/problem+json", "application/problem+xml" }
                    };
                };
            });

            return services;
        }

        public static IServiceCollection AddCustomAuthentication(this IServiceCollection services, MealmateSettings MealmateSettings)
        {
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
              .AddJwtBearer(options =>
              {
                  options.TokenValidationParameters = new TokenValidationParameters
                  {
                      //ValidateIssuer = true,
                      //ValidateAudience = true,
                      ValidateLifetime = true,
                      ValidateIssuerSigningKey = true,

                      ValidIssuer = MealmateSettings.Tokens.Issuer,
                      ValidAudience = MealmateSettings.Tokens.Audience,
                      IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(MealmateSettings.Tokens.Key))
                  };
              });

            return services;
        }

        public static IServiceProvider AddCustomIntegrations(this IServiceCollection services, IWebHostEnvironment hostingEnvironment)
        {
            services.AddHttpContextAccessor();

            var fileProvider = new AppFileProvider(hostingEnvironment.ContentRootPath);
            var typeFinder = new WebAppTypeFinder(fileProvider);

            //configure autofac
            var containerBuilder = new ContainerBuilder();

            //register type finder

            containerBuilder.RegisterInstance(fileProvider).As<IAppFileProvider>().SingleInstance();
            containerBuilder.RegisterInstance(typeFinder).As<ITypeFinder>().SingleInstance();

            //populate Autofac container builder with the set of registered service descriptors
            containerBuilder.Populate(services);

            //find dependency registrars provided by other assemblies
            var dependencyRegistrars = typeFinder.FindClassesOfType<IDependencyRegistrar>();


            //create and sort instances of dependency registrars
            var instances = dependencyRegistrars
                .Select(dependencyRegistrar => (IDependencyRegistrar)Activator.CreateInstance(dependencyRegistrar))
                .OrderBy(dependencyRegistrar => dependencyRegistrar.Order);

            //register all provided dependencies
            foreach (var dependencyRegistrar in instances)
                dependencyRegistrar.Register(containerBuilder, typeFinder);

            var lifetimeScope = containerBuilder.Build()
                                .BeginLifetimeScope(MatchingScopeLifetimeTags.RequestLifetimeScopeTag);

            return new AutofacServiceProvider(lifetimeScope);
        }
    }
}