module GrpcModule
open Definition
open Grpc.Core
open System.Threading.Tasks

    type VisionPictureSource() =
        inherit VisionPictureSource.VisionPictureSourceBase()
        let holder = new System.Threading.AutoResetEvent(false)
        let mutable message = Svdmp.DefaultValue

        member __.Send(msg) =
            message <- msg
            holder.Set()

        override _.SendPictures a outStream c =
            async{
                let mutable stayInLoop = true
                while stayInLoop do
                    try
                        if holder.WaitOne() then
                            do! outStream.WriteAsync(message) |> Async.AwaitTask
                    with _ -> stayInLoop <- false
            } 
            |> Async.StartAsTask :> Task
