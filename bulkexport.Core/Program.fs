namespace Bulkexport

open CommandLine
open Microsoft.Extensions.Hosting
open System.Threading
open Microsoft.Extensions.DependencyInjection
open System.Threading.Tasks
//r:\PartCameraDumps -v -r 
module program =
    open System
    open Microsoft.Win32
    open DSL
    open Records

    open Bulkexport.JobsGrpc
    [<Verb("run", true, HelpText = "Run as a regular program.")>]
    type RunOptions = {
        [<Value(0,MetaName = "SourcePath",HelpText = "path to source folder")>]
        InputPath : string
        [<Option('v')>]
        Verbose : bool
        [<Option('r', "recursive", Required = false, HelpText = "recursive directory watching")>]
        Recursive : bool
        [<Option('c', "collect", Required = false, HelpText = "collect recipes", Default = false)>]
        CollectRecipes : bool
        [<Option('i', "interval", Required = false, HelpText = "bad files cleaner trigger interval (hours)", Default = 24)>]
        BadCleanerInterval : int
    }
#if !TEST
    let inline startAsPlainTask (work : Async<unit>) = Task.Factory.StartNew(fun () -> work |> Async.RunSynchronously)
    type Export() =
        inherit BackgroundService() //(ServiceName = "BulkExport windows service")
        //do
        //    x.ServiceName <- "Files Export Windows Service"
        //    x.EventLog.Log <- "Application"
        let config = 
            @"c:\cybord-config\bulkexport-settings.json"
                |> IO.File.ReadAllText 
                |> Text.Json.JsonSerializer.Deserialize<Collections.Generic.Dictionary<string,string>>

        let readKey keyName =
             if not <| config.ContainsKey keyName then failwithf "config key %s not found" keyName
             config.[keyName]       

        let sourceDir = readKey "Source"
        let cleanerInterval = 72
        let disableRecipe = false
        let recursiveWatching = readKey "RecursiveWatching" |> (=) "True"

        let mJ =
            new MainJob(sourceDir, disableRecipe, recursiveWatching, true, cleanerInterval)

        override _.ExecuteAsync(stoppingToken: CancellationToken) = 
            async{while not stoppingToken.IsCancellationRequested && System.Console.ReadLine() <> "q" do ()} |> startAsPlainTask

        override _.StopAsync(stoppingToken: CancellationToken) =
        //    base.OnStop()
            async{
                event.Reset() |> ignore
                mJ.SendToFileMover Die
                event.WaitOne() |> ignore
            } |> startAsPlainTask

    [<EntryPoint>]
    let main argv =
        //let options = Parser.Default.ParseArguments<RunOptions> argv
        //match options with
        //| :? CommandLine.Parsed<RunOptions> as command when argv.Length > 0 ->
        //     let mJ =
        //         new MainJob(
        //             command.Value.InputPath,
        //             not command.Value.CollectRecipes,
        //             command.Value.Recursive,
        //             command.Value.Verbose,
        //             command.Value.BadCleanerInterval
        //         )
        //     //Termination event handler
        //     System.AppDomain.CurrentDomain.ProcessExit.Add
        //         (fun _ ->
        //             event.Reset() |> ignore
        //             mJ.SendToZip (Die, false)
        //             event.WaitOne() |> ignore)

        //     while System.Console.ReadLine() <> "q" do
        //         ()

        //     event.Reset() |> ignore
        //     mJ.SendToFileMover Die
        //     event.WaitOne() |> ignore
        //| _ -> //:? CommandLine.NotParsed<RunOptions> as errors
        do
            let len = argv.Length
            if len = 0 then
                let runAsAService =
                    use k =
                        Registry.LocalMachine.OpenSubKey(Service.SVC_SERVICE_KEY)

                    (not << isNull) <| k

                if runAsAService then
                    use host: IHost = 
                        Host.CreateDefaultBuilder().
                            UseWindowsService(fun options ->
                                                options.ServiceName <- "Files Export Windows Service"
                                               ).
                            ConfigureServices(fun services ->
                                                let export= new Export()
                                                services.Add(ServiceDescriptor(export.GetType(), export))
                                              ).
                            Build()

                    host.RunAsync() |> Async.AwaitTask |> Async.RunSynchronously
            else
                failwith "invalid arguments"


        //        printfn " \n Press enter..."
        //        System.Console.ReadLine() |> ignore
        //else
        //    let findUnInstall = (argv.[0] = "uninstall")

        //    if findUnInstall then
        //        let installer = new Service.ExportServiceInstaller()
        //        installer.Uninstall()
        //        installer.Dispose()
        //        printfn "Service bulkexport uninstalled"
        //    else

        //        let watchRecursive = argv |> Array.exists (fun s -> s = "-r")
        //        let cleanerInterval = 
        //            argv 
        //            |> Array.tryFind (fun s -> s.StartsWith "-i")
        //            |> Option.map (fun el -> let res = ref 0 in if System.Int32.TryParse(el.Remove(0,2), res) then !res else 24)

        //        if len > 3 then
        //            try
        //                let findInstall = (argv.[0] = "install")

        //                let disableRecipe =
        //                    argv |> (not << Array.exists (fun s -> s = "-c"))

        //                match findInstall with
        //                | true ->
        //                    use installer = new Service.ExportServiceInstaller()
        //                    let startService = argv |> Array.exists (fun s -> s = "-s")

        //                    installer.Install(
        //                        argv.[1],
        //                        argv.[2],
        //                        (System.Int32.Parse(argv.[3])),
        //                        disableRecipe,
        //                        watchRecursive,
        //                        cleanerInterval
        //                    )

        //                    if startService then
        //                        printfn "Service bulkexport installed, starting service %s" Service.SVC_APP_NAME
        //                        let service = new ServiceController(Service.SVC_APP_NAME)
        //                        let timeout = TimeSpan.FromMilliseconds(15000.0)
        //                        service.Start()
        //                        service.WaitForStatus(ServiceControllerStatus.Running, timeout)
        //                    else
        //                        printfn "Service bulkexport installed, not starting service %s" Service.SVC_APP_NAME
        //                | _ ->
        //                    let verbose = argv |> Array.exists (fun s -> s = "-v")

        //                    let mJ =
        //                        new MainJob(
        //                            argv.[0],
        //                            argv.[1],
        //                            System.Int32.Parse argv.[2],
        //                            disableRecipe,
        //                            watchRecursive,
        //                            CompressionLevel.BestSpeed,
        //                            verbose,
        //                            cleanerInterval
        //                        )
        //                    //Termination event handler
        //                    System.AppDomain.CurrentDomain.ProcessExit.Add
        //                        (fun _ ->
        //                            event.Reset() |> ignore
        //                            mJ.SendToZip Die
        //                            event.WaitOne() |> ignore)

        //                    while System.Console.ReadLine() <> "q" do
        //                        ()

        //                    event.Reset() |> ignore
        //                    mJ.SendToFileMover Die
        //                    event.WaitOne() |> ignore

        //            with ex ->
        //                System.Windows.Forms.MessageBox.Show(ex.ToString())
        //                |> ignore
        //        else
        //            let disableRecipe =
        //                argv |> (not << Array.exists (fun s -> s = "-c"))

        //            let mJ =
        //                new MainJob(
        //                    argv.[0],
        //                    argv.[1],
        //                    System.Int32.Parse argv.[2],
        //                    disableRecipe,
        //                    watchRecursive,
        //                    CompressionLevel.BestSpeed,
        //                    false,
        //                    cleanerInterval
        //                )
        //            //Termination event handler
        //            System.AppDomain.CurrentDomain.ProcessExit.Add
        //                (fun _ ->
        //                    event.Reset() |> ignore
        //                    mJ.SendToZip Die
        //                    event.WaitOne() |> ignore)

        //            while System.Console.ReadLine() <> "q" do
        //                ()

        //            event.Reset() |> ignore
        //            mJ.SendToFileMover Die
        //            event.WaitOne() |> ignore

        0 // return an integer exit code
#endif
