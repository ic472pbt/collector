namespace Bulkexport

open Ionic.Zlib

module program =
    open System
#if !TEST
    open System.ServiceProcess
#endif
    open Microsoft.Win32
    open DSL
    open Records
    open Bulkexport.Jobs

#if !TEST
    type Export() as x =
        inherit ServiceBase(ServiceName = "BulkExport windows service")
        do
            x.ServiceName <- "Files Export Windows Service"
            x.EventLog.Log <- "Application"

        let k =
            Registry.LocalMachine.OpenSubKey Service.SVCPARAMKEY

        let sourceDir = k.GetValue("Source").ToString()
        let destDir = k.GetValue("Destination").ToString()
        let zipSize = k.GetValue("ZipSize") :?> int
        let cleanerInterval = 
            if k.GetValueNames() |> Array.exists (fun el -> el.Equals "CleanerInterval") then
                k.GetValue("CleanerInterval") :?> int |> Some 
            else None

        let disableRecipe =
            k.GetValue("DisableRecipe").ToString() = "True"

        let recursiveWatching =
            k.GetValue("RecursiveWatching").ToString() = "True"

        let cLevel =
            let cLevelEntry = k.GetValue("CompressionLevel") in

            if isNull cLevelEntry then
                CompressionLevel.BestSpeed
            else
                enum<CompressionLevel> (cLevelEntry :?> int)

        do k.Close()

        let mJ =
            new MainJob(sourceDir, destDir, zipSize, disableRecipe, recursiveWatching, cLevel, true, cleanerInterval)

        override _.OnStart(args) = base.OnStart(args)

        override _.OnStop() =
            base.OnStop()
            event.Reset() |> ignore
            mJ.SendToFileMover Die
            event.WaitOne() |> ignore

    [<EntryPoint>]
    let main argv =
        let len = argv.Length

        if len = 0 then
            let runAsAService =
                use k =
                    Registry.LocalMachine.OpenSubKey(Service.SVC_SERVICE_KEY)

                (not << isNull) <| k

            if runAsAService then
                ServiceBase.Run [| new Export() :> ServiceBase |]
            else
                printfn
                    "Usage: BulkExport \"path to source folder\" \"path to network destination folder\" zipFoldSize [-v] [-r] [-c] [-i24]"

                printfn
                    "       -v   - verbose
                            -c   - collect recipes
                            -r   - recursive directory watching
                            -iTime - bad files cleaner trigger interval, for example -i24"

                printfn "       BulkExport uninstall"

                printfn
                    "       BulkExport install \"path to source folder\"  \"path to network destination folder\" zipFoldSize [-s] [-r] [-i24]"

                printfn
                    "       -s start service
                            -r recursive directory watching
                            -iTime - bad files cleaner trigger interval, for example -i24"

                printfn " \n Press enter..."
                System.Console.ReadLine() |> ignore
        else
            let findUnInstall = (argv.[0] = "uninstall")

            if findUnInstall then
                let installer = new Service.ExportServiceInstaller()
                installer.Uninstall()
                installer.Dispose()
                printfn "Service bulkexport uninstalled"
            else

                let watchRecursive = argv |> Array.exists (fun s -> s = "-r")
                let cleanerInterval = 
                    argv 
                    |> Array.tryFind (fun s -> s.StartsWith "-i")
                    |> Option.map (fun el -> let res = ref 0 in if System.Int32.TryParse(el.Remove(0,2), res) then !res else 24)

                if len > 3 then
                    try
                        let findInstall = (argv.[0] = "install")

                        let disableRecipe =
                            argv |> (not << Array.exists (fun s -> s = "-c"))

                        match findInstall with
                        | true ->
                            use installer = new Service.ExportServiceInstaller()
                            let startService = argv |> Array.exists (fun s -> s = "-s")

                            installer.Install(
                                argv.[1],
                                argv.[2],
                                (System.Int32.Parse(argv.[3])),
                                disableRecipe,
                                watchRecursive,
                                cleanerInterval
                            )

                            if startService then
                                printfn "Service bulkexport installed, starting service %s" Service.SVC_APP_NAME
                                let service = new ServiceController(Service.SVC_APP_NAME)
                                let timeout = TimeSpan.FromMilliseconds(15000.0)
                                service.Start()
                                service.WaitForStatus(ServiceControllerStatus.Running, timeout)
                            else
                                printfn "Service bulkexport installed, not starting service %s" Service.SVC_APP_NAME
                        | _ ->
                            let verbose = argv |> Array.exists (fun s -> s = "-v")

                            let mJ =
                                new MainJob(
                                    argv.[0],
                                    argv.[1],
                                    System.Int32.Parse argv.[2],
                                    disableRecipe,
                                    watchRecursive,
                                    CompressionLevel.BestSpeed,
                                    verbose,
                                    cleanerInterval
                                )
                            //Termination event handler
                            System.AppDomain.CurrentDomain.ProcessExit.Add
                                (fun _ ->
                                    event.Reset() |> ignore
                                    mJ.SendToZip Die
                                    event.WaitOne() |> ignore)

                            while System.Console.ReadLine() <> "q" do
                                ()

                            event.Reset() |> ignore
                            mJ.SendToFileMover Die
                            event.WaitOne() |> ignore

                    with ex ->
                        System.Windows.Forms.MessageBox.Show(ex.ToString())
                        |> ignore
                else
                    let disableRecipe =
                        argv |> (not << Array.exists (fun s -> s = "-c"))

                    let mJ =
                        new MainJob(
                            argv.[0],
                            argv.[1],
                            System.Int32.Parse argv.[2],
                            disableRecipe,
                            watchRecursive,
                            CompressionLevel.BestSpeed,
                            false,
                            cleanerInterval
                        )
                    //Termination event handler
                    System.AppDomain.CurrentDomain.ProcessExit.Add
                        (fun _ ->
                            event.Reset() |> ignore
                            mJ.SendToZip Die
                            event.WaitOne() |> ignore)

                    while System.Console.ReadLine() <> "q" do
                        ()

                    event.Reset() |> ignore
                    mJ.SendToFileMover Die
                    event.WaitOne() |> ignore

        0 // return an integer exit code
#endif
