using Grpc.Net.Client;
using MapOutToHdd;
using MapOutToHdd.classes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Logging.EventLog;
using System;
using System.Diagnostics;

var asService = !(Debugger.IsAttached || args.Contains("--console"));
IHostBuilder builder = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options =>
    {
        options.ServiceName = "Cybord's AWS Uploader";
    })
    .ConfigureServices(services =>
    {
        if(OperatingSystem.IsWindows())
        LoggerProviderOptions.RegisterProviderOptions<
            EventLogSettings, EventLogLoggerProvider>(services); 
        services.AddHostedService<MappingService>();
    })
    .ConfigureLogging((context, logging) =>
    {
        // See: https://github.com/dotnet/runtime/issues/47303
        logging.AddConfiguration(
            context.Configuration.GetSection("Logging"));
     //   logging.AddConsole();
    });
builder.UseEnvironment(asService ? Environments.Production : Environments.Development);

if (asService)
{
    Console.WriteLine("service mode");
    await builder.Build().RunAsync();
}
else
{
    Console.WriteLine("console mode");
    await builder.RunConsoleAsync();
}


