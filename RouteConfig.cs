using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Penguin.Web.Abstractions.Interfaces;

namespace Penguin.Cms.Modules.Files
{
    public class RouteConfig : IRouteConfig
    {
        public void RegisterRoutes(IRouteBuilder routes)
        {
            routes.MapRoute(
                name: "Client_Files",
                template: "Files/{*Path}",
                defaults: new { controller = "File", action = "ViewByPath" }
            );

            routes.MapRoute(
                "Admin_Files",
                "{area:exists}/Files/{*Path}",
                new { controller = "File", action = "ViewByPath" }
            );
        }
    }
}