using Authentication.DomainService.Utilities;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Text;

namespace SeliseBlocks.OIDC.Driver
{
    public static class OidcDriverServiceExtention
    {
        public static void RegisterBlocksOidcDriverService(this IServiceCollection services)
        {
            services.RegisterAllServices();
            services.AddSingleton<IOidcDriverService, OidcDriverService>();
        }
    }

}
