using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MapOutToTheCloud.classes
{
    public static class ServiceBaseLiveTimeHostExtension
    {
        public static IHostBuilder UseServiceBaseLifetime(this
        IHostBuilder hostBuilder)
        {
            return hostBuilder.ConfigureServices((hostContext,
            services) => services.AddSingleton<IHostLifetime,
            ServiceBaseLifeTime>());
        }
        public static Task RunAsServiceAsync(this IHostBuilder
        hostBuilder, CancellationToken cancellationToken = default)
        {
            return
            hostBuilder.UseServiceBaseLifetime().Build()
           .RunAsync(cancellationToken);
        }
    }
}
