using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Hosting;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace EmittingService
{
    public class Program
    {
        public static Common.Channel.Bridge Reciever;
        public static bool isRunning = false;

        public static void Main(Common.Channel.Bridge reciever)
        {
            Reciever = reciever;
            AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
            CreateHostBuilder(new string[0]).Build().Run();
        }

        // Additional configuration is required to successfully run gRPC on macOS.
        // For instructions on how to configure Kestrel and gRPC clients on macOS, visit https://go.microsoft.com/fwlink/?linkid=2099682
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.ConfigureKestrel(options =>
                    {
                        options.Listen(IPAddress.Any, 5001, listenOptions =>
                        {
                            listenOptions.Protocols = HttpProtocols.Http2;
                            // listenOptions.UseHttps("<path to .pfx file>",
                            //     "<certificate password>");
                        });
                    });
                    webBuilder.UseStartup<Startup>();
                });
    }
}
