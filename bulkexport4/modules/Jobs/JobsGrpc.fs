namespace Bulkexport

open System
open Microsoft.Win32
open System.Diagnostics
open System.IO
open Ionic.Zip
open GrpcModule
open Definition
open Google.Protobuf

module JobsGrpc =
    open Records
    open Bulkexport.Diagnostics
    open DSL
    open Funcs

    [<Literal>]
    let RECIPE_WATCHER_DIR = "C:\\Sirio\\Work\\RecipeData"

    [<Literal>]
    let NET_MOVER_QUEUE_CAPACITY = 3

    [<Literal>]
    let FILE_MOVER_QUEUE_CAPACITY = 100

    let eventsCounter = ref 0
    let deletedCounter = ref 0

    ///Timer for time to delay (ttd)
    let globalTime = new System.Diagnostics.Stopwatch()
    ///retry cleaner kicker for every 0.5s
    let cleanerKicker = new System.Timers.Timer(500.0)

    ///Event for threads synchronization
    let event =
        new System.Threading.AutoResetEvent(false)
    ///<summary>Service worker</summary>
    ///<param name="sourceDir">Path to directory where to pick up new files</param>
    ///<param name="netDir">Network path (or local) where to send zip files.</param>
    ///<param name="foldSize">Number of packed files in one zip.</param>
    ///<param name="disableRecipe">Do not collect recipe files.</param>
    ///<param name="recursiveWatching">Recursive directory monitoring.</param>
    ///<param name="cLevel">Zip compession level.</param>
    ///<param name="verbose">Display messages with low priority.</param>
    ///<param name="badFilesSchedule">Bad files clearing interval (hours).</param>
    type MainJob(sourceDir, netDir, foldSize, disableRecipe, recursiveWatching, cLevel, verbose, badFilesSchedule:int option) =
        ///bad files cleaner for every 24h
        let hours = match badFilesSchedule with Some h -> float h | None -> 24.0 in 
        let badFilesCleaner = 
            new System.Timers.Timer(15.0 * 60.0 * 1000.0)

        let diagnosticsAgent = diagnosticsAgent verbose
        //let monitoredDir = new DirectoryInfo(sourceDir)

        let msgInfo = Low >> diagnosticsAgent.Post
        let msgUrgent = High >> diagnosticsAgent.Post

        let exceptionHandler line (ex: Exception) text =
            Printf.ksprintf msgUrgent "line %i %s %s %s" line ex.Message ex.StackTrace text
        ///network drive mapping recovery
#if !TEST
        let reconnect2NetMapping (delay) =

            try
                use key =
                    Registry.LocalMachine.OpenSubKey Service.SVCPARAMKEY

                let target =
                    if isNull key then
                        null
                    else
                        msgInfo "U: drive mapping recovery"
                        key.GetValue "Mapping"

                if (not << isNull) target then
                    if delay then
                        async { do! Async.Sleep 10000 }
                        |> Async.RunSynchronously

                    let user = key.GetValue("User").ToString()
                    let passwd = key.GetValue("Password").ToString()
                    use restartingProcess = new Process()

                    do
                        restartingProcess.StartInfo.FileName <- "net.exe"
                        restartingProcess.StartInfo.Arguments <- "use U: /delete /y U:"
                        restartingProcess.StartInfo.WindowStyle <- ProcessWindowStyle.Hidden
                        restartingProcess.StartInfo.UseShellExecute <- false

                        if restartingProcess.Start() then
                            restartingProcess.WaitForExit()

                        restartingProcess.StartInfo.Arguments <-
                            sprintf "use U: %s /user:%s %s" (target.ToString()) user passwd

                        if restartingProcess.Start() then
                            restartingProcess.WaitForExit()
            with ex -> exceptionHandler 139 ex ""
#endif
        do
#if !TEST
            reconnect2NetMapping (false)
#endif
            try
                on (not << Directory.Exists) (failwithf "%s directory does not exist") sourceDir
                //create sub directory on net drive
                on (not << Directory.Exists) (Directory.CreateDirectory >> ignore) netDir

                printfn "Waiting for incoming files in %s ..." sourceDir
                printfn "enter q for exit"
            with ex -> exceptionHandler 123 ex ""

        //Retry to delete good files from the origial folder
        let cleanerRetryAgent =
            let delaySpan = TimeSpan.FromSeconds 2.0

            MailboxProcessor<(string * DateTime) option>.Start
                (fun inbox ->
                    let rec infiniteLoop (queue: (string * DateTime) list) =
                        async {
                            let! msg = inbox.Receive()

                            let nextQueue =
                                match msg with
                                | Some itm -> itm :: queue
                                | None ->
                                    let fordelete, forstay =
                                        queue
                                        |> partition (fun (_, ttl) -> DateTime.Now - ttl >= delaySpan)

                                    fordelete
                                    |> List.iter
                                        (fun (name, _) ->
                                            try
                                                on File.Exists File.Delete name
                                            //Console.WriteLine("deleted {0} at {1} from {2}", name, DateTime.Now, ttl)
                                            with ex ->
                                                exceptionHandler 128 ex ("second try delete failed path: " + name))

                                    forstay

                            return! infiniteLoop (nextQueue)
                        }

                    infiniteLoop ([]))

        ///Delete good files from the origial folder
        let cleanerAgent =
            MailboxProcessor<string * FileStream * FileStream>.Start
                (fun inbox ->
                    let rec infiniteLoop counter =
                        async {
                            let! fileName, readLock, writeLock = inbox.Receive()

                            //                            Printf.kprintf msgInfo "cleaner %i" counter
                            try
                                writeLock.Dispose()
                                File.Delete fileName
                                readLock.Dispose()
                            with ex ->
                                exceptionHandler 141 ex ("delete error on path: " + fileName)

                                cleanerRetryAgent.Post << Some
                                <| (fileName, DateTime.Now)

                            return! infiniteLoop (counter + 1)
                        }

                    infiniteLoop 0)
        
        let reentryLock = ref String.Empty
        /// Move files to network location via grpc tunnel
        let netMoverTask =
            MailboxProcessor<NetMoverMessage>.Start
                (fun inbox ->
                    let client = new VisionPictureSource()
                    let s = new Grpc.Core.Server()
                    do 
                        s.Ports.Add <| new Grpc.Core.ServerPort("localhost", 5005, Grpc.Core.ServerCredentials.Insecure) |> ignore
                        s.Services.Add(Definition.VisionPictureSource.VisionPictureSourceMethodBinder.BindService(client))
                        s.Start()
                    let rec infiniteLoop () =
                        async {
                            match! inbox.Receive() with
                            | Dump(_,stream, name, die) ->                                
                                if client.Send({Svdmp.DefaultValue with Picture=ValueOption.Some(ByteString.CopyFrom(stream.ToArray()))}) then
                                    stream.Dispose()
                            | Error(_) -> ()
                            return! infiniteLoop ()
                        }
                    infiniteLoop ())

        /// transform files
        let transformer =
            MailboxProcessor<FileNameMsg>.Start
                (fun inbox ->
                    let rec infiniteLoop () =
                        async {
                            match! inbox.Receive() with
                            | FileRecord(file) ->
                                netMoverTask.Post(Dump(None,file.Stream,file.NewFileName,false))
                            | _ -> ()
                            return! infiniteLoop ()
                        }

                    infiniteLoop ())

        let errorDetectionAgent =
            MailboxProcessor<FileNameMsg>.Start
                (fun inbox ->
                    let rec infiniteLoop () =
                        async {
                            let! msg = inbox.Receive()

                            match msg with
                            | FileRecord file ->
                                let hasError = !.file.FileName = ".svdmp" && erroneousSvdmp file.Stream
                                
                                //delete anyway
                                cleanerAgent.Post (file.OriginalFullName, file.ReadLock, file.WriteLock)
                                if hasError then
                                    //                                        if netMoverTask.CurrentQueueLength < NET_MOVER_QUEUE_CAPACITY then
                                    (file.Stream, netDir ^/ "errors" + file.FileName, file.OriginalFullName)
                                    |> (Error >> netMoverTask.Post)
                                //                                        else
//                                            file.Stream.Dispose()
                                else
                                    if transformer.CurrentQueueLength < FILE_MOVER_QUEUE_CAPACITY && (not << isNozzle) file.Stream then
                                        //transform file
                                        transformer.Post msg
                                    else
                                        incr deletedCounter
                                        Printf.ksprintf msgUrgent "Transformer overloaded or nozzle image file received. Dropping %s" file.FileName
                                        file.Stream.Dispose()
                            | _ -> transformer.Post msg

                            return! infiniteLoop ()
                        }

                    infiniteLoop ())


        let fileMoverAgent (nextStep: MailboxProcessor<FileNameMsg> option, num) =
            MailboxProcessor.Start
                (fun inbox ->
                    let counter = ref 0

                    let rec messageLoop () =
                        async {
                            let! msg = inbox.Receive()

                            match msg with
                            | FileRecord _ -> ()
                            | FileName (replaced, fullFileName, dt, ttd) ->
                                incr counter
                                let fileName = !/fullFileName

                                if //errorDetectionAgent.CurrentQueueLength < FILE_MOVER_QUEUE_CAPACITY
                                    not (fileName.EndsWith "SvThumbs.xml") then
                                    let newFileName =
                                        sprintf "%s_%s" (dt.ToString("yyyyMMddTHHmmss.ffffzzz").Replace(":",String.Empty)) fileName 

                                    let waitingTime =
                                        max 0L (6L - globalTime.ElapsedMilliseconds + ttd)
                                        |> int

                                    if waitingTime > 0 then
                                        do! Async.Sleep waitingTime //delay during recording

                                    try
                                        //read, lock file and send
                                        let readLock =
                                            new FileStream(
                                                fullFileName,
                                                FileMode.Open,
                                                FileAccess.Read,
                                                FileShare.Write ||| FileShare.Delete
                                            )

                                        let writeLock =
                                            try //try to get exclusive write lock
                                                new FileStream(
                                                    fullFileName,
                                                    FileMode.Open,
                                                    FileAccess.Write,
                                                    FileShare.Read
                                                )
                                            with _ -> readLock.Dispose(); raise <| IOException()

                                        let ms = new MemoryStream()

                                        do!
                                            readLock.CopyToAsync ms
                                            |> Async.AwaitIAsyncResult
                                            |> Async.Ignore

                                        //fileStream.Dispose()
                                        //report file mover is alive
                                        if !counter % 99 = 0 then
                                            Printf.ksprintf
                                                msgInfo
                                                "filemover %i heartbeat %s counter %i"
                                                num
                                                fullFileName
                                                !counter

                                        if not ms.CanRead then
                                            msgInfo "stream closed 2"

                                        let relativeFileName =
                                            fullFileName
                                                .Replace(replaced, String.Empty)
                                                .Replace(fileName, newFileName)

                                        { Stream = ms
                                          NewFileName = newFileName
                                          FileName = relativeFileName
                                          NoTries = 0
                                          DateTime = dt
                                          OriginalFullName = fullFileName
                                          ReadLock = readLock
                                          WriteLock = writeLock
                                        }
                                        |> (FileRecord >> errorDetectionAgent.Post)
                                    with
                                    //file is busy
                                    | :? IOException as _ec ->
                                        if File.Exists fullFileName then
                                            match nextStep with
                                            | None -> Printf.ksprintf msgInfo "3 retries %s" fullFileName
                                            | Some agent ->
                                                Printf.ksprintf msgInfo "repost %s" fullFileName

                                                if agent.CurrentQueueLength < 100 then
                                                    agent.Post(
                                                        FileName(
                                                            replaced,
                                                            fullFileName,
                                                            dt,
                                                            globalTime.ElapsedMilliseconds
                                                        )
                                                    )
                                        else
                                            incr deletedCounter

                                    | ex -> exceptionHandler 412 ex ""
                                else
                                    incr deletedCounter


                            | Die -> transformer.Post msg

                            return! messageLoop ()
                        //ReadLock = readLock
                        //WriteLock = writeLock
                        }

                    // start the loop
                    messageLoop ())

        let thirdLine = fileMoverAgent (None, 3)
        let secondLine = fileMoverAgent (Some thirdLine, 2)
        let firstLine = fileMoverAgent (Some secondLine, 1)

        let watchdog =
            new Watchdog(
                diagnosticsAgent,
                netMoverTask,
                transformer,
                eventsCounter,
                deletedCounter,
                [ firstLine; secondLine; thirdLine ]
            )

        do
            watchdog.Start()
            globalTime.Start()
            cleanerKicker.AutoReset <- true
            cleanerKicker.Elapsed.Add(fun _ -> cleanerRetryAgent.Post None)
            cleanerKicker.Start()
            badFilesCleaner.AutoReset <- true
            badFilesCleaner.Elapsed.Add(badFilesClenerFun sourceDir hours)
            badFilesCleaner.Start()

        ///File system events Handler, that tracks file creation
        let fw = new FileSystemWatcher(sourceDir, "*.*")
        let exclusions = ["NozzleImages"; "AutoTeach"; "DCAMInfo"]
        let watcherHandler (replacedName: string) (f: FileSystemEventArgs) =

            if File.Exists f.FullPath && exclusions |> List.forall (not << f.FullPath.Contains)  then
                let msg =
                    FileName(replacedName, f.FullPath, DateTime.Now, globalTime.ElapsedMilliseconds)
                if not << f.FullPath.Equals <| !reentryLock then            
                    if firstLine.CurrentQueueLength < 70 then
                        firstLine.Post(msg)
                    elif secondLine.CurrentQueueLength < 50 then
                        secondLine.Post(msg)
                    elif thirdLine.CurrentQueueLength < 80 then
                        thirdLine.Post(msg)
                    else
                        Printf.kprintf msgUrgent "all queues are full %s" f.Name
                    incr eventsCounter
                

        do
            fw.EnableRaisingEvents <- true
            fw.NotifyFilter <- fw.NotifyFilter ||| NotifyFilters.LastWrite
            //deep monitoring should be ON to support UIC machines
            fw.IncludeSubdirectories <- recursiveWatching
            fw.Created.Add(watcherHandler sourceDir)

            //lock imitation
            //fw.Created.Add( fun f -> System.Threading.Thread.Sleep 2
            //                         let _ = new FileStream(f.FullPath, FileMode.Open, FileAccess.Read)
            //                         printfn "%s" f.Name
            //               )

            if not disableRecipe
               && Directory.Exists RECIPE_WATCHER_DIR then
                diagnosticsAgent.Post(Low("setup recipe dir watching"))
                //let id = let R = new Random() in R.Next()
                let recipeCollector (time: DateTime) =
                    async {
                        do! Async.Sleep 45000

                        try
                            diagnosticsAgent.Post(Low("compress recipe dir"))
                            use arc = new ZipFile()
                            arc.CompressionLevel <- Ionic.Zlib.CompressionLevel.BestSpeed
                            arc.AddDirectory(RECIPE_WATCHER_DIR) |> ignore
                            let ms = new MemoryStream()

                            let arcname =
                                sprintf "%s\\%s.recipe.zip" netDir (Guid.NewGuid().ToString())

                            arc.Save(ms)
                            (netMoverTask.Post << Dump) ((None, ms, arcname, false))
                        with ex -> exceptionHandler 421 ex ""
                    }

                let guardInt = TimeSpan.FromMinutes(1.0)
                let lastTime = ref (DateTime.Now - guardInt - guardInt)

                let rw =
                    new FileSystemWatcher(RECIPE_WATCHER_DIR, "*.*")

                rw.EnableRaisingEvents <- true
                rw.NotifyFilter <- fw.NotifyFilter ||| NotifyFilters.LastWrite
                rw.IncludeSubdirectories <- true

                rw.Created.Add
                    (fun _ ->
                        if DateTime.Now - !lastTime > guardInt then
                            lastTime := DateTime.Now
                            recipeCollector (!lastTime) |> Async.Start)

        member _.SendToZip message = transformer.Post message
        member _.SendToFileMover message = firstLine.Post message
