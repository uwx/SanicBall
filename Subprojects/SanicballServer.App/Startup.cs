using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SanicballCore.Server;
using SanicballServer.App.Services;

namespace SanicballServer.App
{
    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddSingleton<ISanicballRoomsService, SanicballRoomsService>();
            services.AddHostedService((services) => (SanicballRoomsService)services.GetService<ISanicballRoomsService>());
            services.AddMvc(o => o.EnableEndpointRouting = false);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseForwardedHeaders();
            }

            app.Use(async (r, c) =>
            {
                if (!r.WebSockets.IsWebSocketRequest)
                {
                    r.Response.Headers.Append("Access-Control-Allow-Origin", "*");
                }

                await c();
            });

            app.UseWebSockets();
            app.UseMvc();

            var service = app.ApplicationServices.GetService<ISanicballRoomsService>();
            service.CreateRoomAsync(new RoomConfig() { ServerName = "Wan Kerr Co. Ltd.", MaxPlayers = 16, ShowOnList = true });
        }
    }
}
