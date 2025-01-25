using Grpc.Core;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EmittingService
{
    public class VisionPictureSourceService : VisionPictureSource.VisionPictureSourceBase
    {
        private readonly ILogger<VisionPictureSourceService> _logger;
        private bool SendHelloMsg = true;
        public VisionPictureSourceService(ILogger<VisionPictureSourceService> logger)
        {
            _logger = logger;
        }

        public override Task<HealthCheckResponse> Check(HealthCheckRequest request, ServerCallContext context)
        {
            return Task.FromResult(new HealthCheckResponse { Status = Program.isRunning ? HealthCheckResponse.Types.ServingStatus.Serving : HealthCheckResponse.Types.ServingStatus.NotServing });
        }

        public override async Task SendPictures(Void request, IServerStreamWriter<Component> responseStream, ServerCallContext context)
        {
            bool doNotExit = true;
            while (doNotExit)
            {
                try
                {
                    if (SendHelloMsg) {
                        Program.isRunning = true;
                        SendHelloMsg = false;
                    }
                    else
                    {
                        var res = await Program.Reciever.Pop();
                        await responseStream.WriteAsync(
                new Component
                {
                    Picture = Google.Protobuf.ByteString.CopyFrom(res.picture.ToArray()),
                    ComponentMeasures = new ComponentMeasures { SizeX = res.size_nm.Width, SizeY = res.size_nm.Height },
                    Hashkey = res.hashkey,
                    PackageCase = res.packageCase,
                    ComponentName = res.componentType,
                    Packaging = res.packaging,
                    ImageProperties = new ImageProperties
                    {
                        ComponentCenterX = res.position_mm.X,
                        ComponentCenterY = res.position_mm.Y,
                        ScaleX = res.scale_mm_px.Width,
                        ScaleY = res.scale_mm_px.Height,
                        Angle = res.angle_rad,
                        Orientation = res.orientation_rad,
                        Binning = res.binning,
                        BodyRectW = res.body_nm.Width,
                        BodyRectH = res.body_nm.Height
                    },
                    IsDumped = res.isDumped,
                    Timestamp = res.timestamp,
                    DipfType = res.DipfType,
                    SourceFolder = res.SourceFolder,
                    Svdmp = res.svdmp,
                    Lead1 = new Lead { Number = res.leads[0].number, NumOfGrids = res.leads[0].numOfGrids, Pitch = res.leads[0].pitch, Angle = res.leads[0].angle, GroupPitch = res.leads[0].groupPitch, Vector = new FloatVector { X = res.leads[0].Vector.X, Y = res.leads[0].Vector.Y }, Position = new LeadPosition { Type = res.leads[0].Position.Type, Angle = res.leads[0].Position.Angle, Size = new FloatVector { X = res.leads[0].Position.Size.Width, Y = res.leads[0].Position.Size.Height } } },
                    Lead2 = new Lead { Number = res.leads[1].number, NumOfGrids = res.leads[1].numOfGrids, Pitch = res.leads[1].pitch, Angle = res.leads[1].angle, GroupPitch = res.leads[1].groupPitch, Vector = new FloatVector { X = res.leads[1].Vector.X, Y = res.leads[1].Vector.Y }, Position = new LeadPosition { Type = res.leads[1].Position.Type, Angle = res.leads[1].Position.Angle, Size = new FloatVector { X = res.leads[1].Position.Size.Width, Y = res.leads[1].Position.Size.Height } } },
                    BoardPlacement = res.boardInfo == null ? null : new BoardPlacement
                    {
                        Refdes = res.boardInfo.Value.refDes,
                        BoardId= res.boardInfo.Value.boardId,
                        BoardName= res.boardInfo.Value.boardName,
                        PanelMatrixOID = res.boardInfo.Value.boardmatrix,
                        Location = new LocationOnBoard { PosA = res.boardInfo.Value.rotation, PosX = res.boardInfo.Value.coordinates.X, PosY = res.boardInfo.Value.coordinates.Y }
                    }
                });
                        res.picture.Dispose();
                        Program.isRunning = true;
                    }
                }
                catch (Exception ex)
                {
                    _ = ex;
                    System.Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss}: {ex.Message}");
                    doNotExit = !doNotExit;
                    Program.isRunning = false;
                    SendHelloMsg = true;
                }
            }
        }
    }
}
