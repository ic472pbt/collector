using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;

namespace MapOutToHdd.classes
{
    internal class MappingService : BackgroundService
    {
        private readonly ILogger<MappingService> logger;
        // The port number must match the port of the gRPC server.
        const int BULKEXPORT_GRPC_SERVER_PORT = 5001;
        const string configFile = @"c:\cybord-config\maptohdd-settings.json";
        const string bucketName = "cy-datalake";

        // private readonly string s3path;
        // private readonly string uploaderId;
        private readonly string rootDir;
        private List<Dictionary<string, string>>? hosts;

        private readonly string Host;
        private readonly HttpClient httpClient;
        public MappingService(ILogger<MappingService> logger)
        {
            this.logger = logger;
            string ConfigString = File.ReadAllText(configFile);
            Dictionary<string, string>? config = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(ConfigString);

            //uploaderId = ReadKey(config, "UPLOADER_ID");
            rootDir = ReadKey(config, "ROOT_DIRECTORY");
            //s3path = $"RawData\\AOI\\{uploaderId}";

            Host = ReadKey(config, "host");
            httpClient = new();
        }

        private static string ReadKey(Dictionary<string, string>? config, string keyName)
        {
            if (config == null || !config.ContainsKey(keyName)) throw new Exception($"config key %s not found {keyName}");
            else return config[keyName];
        }
        private async Task BEClient(string host, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    using var channel = GrpcChannel.ForAddress($"http://{host}:{BULKEXPORT_GRPC_SERVER_PORT}");
                    logger.LogInformation("connected to http://{host}:{BULKEXPORT_GRPC_SERVER_PORT}", host, BULKEXPORT_GRPC_SERVER_PORT);
                    var client = new VisionPictureSource.VisionPictureSourceClient(channel);

                    logger.LogInformation("health check {host}", host);
                    var health = client.Check(new HealthCheckRequest(), cancellationToken: ct);
                    if (health.Status == HealthCheckResponse.Types.ServingStatus.Serving || health.Status == HealthCheckResponse.Types.ServingStatus.NotServing)
                    {
                        logger.LogInformation(" OK from {host}", host);
                        logger.LogInformation("downstreaming call {host}", host);
                        var reply = client.SendPictures(new Void(), cancellationToken: ct);
                        while (await reply.ResponseStream.MoveNext(ct))
                        {
                            logger.LogInformation("got {name}", reply.ResponseStream.Current.ComponentName);
                            Google.Protobuf.JsonFormatter formatter = new Google.Protobuf.JsonFormatter(Google.Protobuf.JsonFormatter.Settings.Default.WithIndentation());
                            var output = formatter.Format(reply.ResponseStream.Current);
                            //using MemoryStream ms = new();
                            //string keyPath = $"{s3path}/{reply.ResponseStream.Current.Timestamp[..4]}/{reply.ResponseStream.Current.Timestamp[..8]}/{uploaderId}_{reply.ResponseStream.Current.Timestamp[..13]}/{reply.ResponseStream.Current.PictureFileName}";
                            string dirPath = Path.Combine(rootDir, host, reply.ResponseStream.Current.SourceFolder);
                            if (!Directory.Exists(dirPath)) Directory.CreateDirectory(dirPath);
                            string newFileName = Guid.NewGuid().ToString() + ".json";
                            using Stream stream = File.OpenWrite(Path.Combine(dirPath, newFileName));
                            reply.ResponseStream.Current.WriteTo(stream);

                            logger.LogInformation(" written {name}", newFileName);
                        }
                    }
                    else
                        logger.LogInformation(" no response from {host}.", host);
                }
                catch (Exception e)
                {
                    logger.LogError(e, message: "host: {host}", host);
                }
                await Task.Delay(TimeSpan.FromMinutes(5.0), ct);
            }
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // httpClient.DefaultRequestHeaders.Accept.Clear();
            // var response = await httpClient.GetAsync(HostsAPI, stoppingToken);
            {
                try
                {

                    Task.WaitAll(new string[] { Host }
                            .Select(host => BEClient(host, stoppingToken))
                            .ToArray()
                    , stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    // When the stopping token is canceled, for example, a call made from services.msc,
                    // we shouldn't exit with a non-zero exit code. In other words, this is expected...
                }
                catch { Environment.Exit(1); }
            }
        }
    }
}
