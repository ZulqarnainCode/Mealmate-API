using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Mealmate.BusinessLayer.DataSeeders;
using Mealmate.DataAccess.Contexts;
using Mealmate.Entities.Identity;
using Mealmate.Repository.DependencyInjection;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Mealmate.Admin
{
    public class Startup
    {
        private readonly IWebHostEnvironment _env;
        private readonly IConfigurationRoot _config;

        public Startup(IWebHostEnvironment env)
        {
            _env = env;

            var builder = new ConfigurationBuilder()
                .SetBasePath(_env.ContentRootPath)
                .AddJsonFile("appsettings.json", false, true)
                .AddEnvironmentVariables();

            _config = builder.Build();
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton(_config);
            services.AddMvc();
            services.AddTransient<UserDataSeeder>();
            services.AddTransient<RoleDataSeeder>();
            services.AddDbContext<MealmateDbContext>()
                 .AddUnitOfWork<MealmateDbContext>();

            services.AddIdentity<User, Role>()
                   .AddEntityFrameworkStores<MealmateDbContext>();
            services.ConfigureApplicationCookie(config =>
            {
                config.LoginPath = "/Auth/SignIn";
                config.LogoutPath = "/Auth/SignOut";
                config.SlidingExpiration = true;

                config.AccessDeniedPath = "/Auth/AccessDenied";
                config.ReturnUrlParameter = "returnUrl";
            });

            services.Configure<IdentityOptions>(config =>
            {
                config.Password.RequireDigit = false;
                config.Password.RequireLowercase = false;
                config.Password.RequiredLength = 8;
                config.Password.RequiredUniqueChars = 0;
                config.Password.RequireNonAlphanumeric = false;
                config.Password.RequireUppercase = false;

                //config.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._";
                config.User.RequireUniqueEmail = true;
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app)
        {
            if (_env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseStaticFiles();

            app.UseRouting();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllerRoute(
                    name: "areas",
                    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}"
                  );

                endpoints.MapControllerRoute(
                    name: "default",
                    pattern: "{controller=Home}/{action=Index}/{id?}");

            });
        }
    }
}
