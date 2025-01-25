namespace Bulkexport

open System
open Microsoft.Win32
open System.Diagnostics
open System.Drawing
open System.IO
open Ionic.Zip

module Jobs =
    open Records
    open Bulkexport.Diagnostics
    open DSL
    open Funcs
    open UIC

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
    ///force zip to finalize every 10 min
    let compressorKicker = new System.Timers.Timer(10 * 60 * 1000 |> float)

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
        let reconnect2NetMapping (delay) = async{

            try
                let key =
                    Registry.LocalMachine.OpenSubKey Service.SVCPARAMKEY

                let target =
                    if isNull key then
                        null
                    else
                        msgUrgent "U: drive mapping recovery"
                        key.GetValue "Mapping"

                if (not << isNull) target then
                    if delay then
                        do! Async.Sleep 10000

                    let user = key.GetValue("User").ToString()
                    let passwd = key.GetValue("Password").ToString()
                    use restartingProcess = new Process()

                    do
                        restartingProcess.StartInfo.FileName <- "net.exe"
                        restartingProcess.StartInfo.Arguments <- "use U: /delete /y"
                        restartingProcess.StartInfo.WindowStyle <- ProcessWindowStyle.Hidden
                        restartingProcess.StartInfo.RedirectStandardOutput <- true
                        restartingProcess.StartInfo.UseShellExecute <- false

                        if restartingProcess.Start() then
                            restartingProcess.WaitForExit()

                        restartingProcess.StartInfo.Arguments <-
                            sprintf @"use U: %s /user:%s %s" (target.ToString()) user passwd
                        if restartingProcess.Start() then
                            let buffer: char [] = Array.zeroCreate 1024
                            let count = restartingProcess.StandardOutput.Read(buffer,0,1024)
                            msgUrgent (new String(buffer,0,count))
                            restartingProcess.WaitForExit()
            with ex -> exceptionHandler 139 ex ""
        }
#endif
        do
#if !TEST
            reconnect2NetMapping (false) |> Async.RunSynchronously
#endif
            try
                on (not << Directory.Exists) (failwithf "%s directory does not exist") sourceDir
                //create sub directory on net drive
                on (not << Directory.Exists) (Directory.CreateDirectory >> ignore) netDir

                printfn "Waiting for incoming files in %s ..." sourceDir
                printfn "enter q for exit"
            with ex -> exceptionHandler 123 ex ""

        ///write the stream to the hard disk and free up resources
        let writeOut (stream: MemoryStream) (name: string) keepOpen =
                //let dirName = DirectoryInfo(Path.GetDirectoryName name)
                //msgUrgent (sprintf "check directory %s %b" dirName.Name dirName.Exists)
                //msgUrgent (sprintf "check directory %s %b" (dirName.Parent.Name) dirName.Parent.Exists)
                //msgUrgent (sprintf "check directory %s %b" (dirName.Parent.Parent.Name) dirName.Parent.Parent.Exists)
                use file = File.Create name
                stream.Position <- 0L
                stream.CopyTo file 
                    //|> Async.AwaitIAsyncResult 
                    //|> Async.Ignore 
                    //|> Async.RunSynchronously
                if not keepOpen then
                    file.Flush() 
                    //|> Async.AwaitIAsyncResult 
                    //|> Async.Ignore 
                    //|> Async.RunSynchronously
                    stream.Dispose()

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
        /// Move zip files to network location
        let netMoverTask =
            MailboxProcessor<NetMoverMessage>.Start
                (fun inbox ->
                    let rec infiniteLoop delayOnError =
                        let success = ref true

                        async {
                            match! inbox.Receive() with
                            | Dump (zfOpt, stream, name, die) ->
                                Printf.ksprintf msgInfo "net move %s" name

                                try
                                    try
                                        match zfOpt with
                                        | Some zf ->
                                            let haveEntries = zf.HasEntries
                                            zf.Dispose()
                                            if haveEntries then writeOut stream name false
                                        | _ -> 
                                            writeOut stream name false

                                    with ex ->
                                        success := false
                                        exceptionHandler 156 ex ""
#if !TEST
                                        do! reconnect2NetMapping delayOnError
#endif
                                        if inbox.CurrentQueueLength < NET_MOVER_QUEUE_CAPACITY then
                                            (zfOpt, stream, name, die) |> (Dump >> inbox.Post)
                                        else
                                            stream.Dispose()
                                finally
                                    ()
                                //    stream.Dispose()
                                if die then event.Set() |> ignore
                            | Error (stream, name, originalFullPath) ->
                                let folder = !//name

                                try
                                    on (not << Directory.Exists) (Directory.CreateDirectory >> ignore) folder
                                    do writeOut stream name true
                                    let origName = (!//originalFullPath ^/ "_" + !/originalFullPath)
                                    reentryLock := origName
                                    do writeOut stream origName false
                                with ex ->
                                    success := false
                                    exceptionHandler 239 ex ""
#if !TEST
                                    do! reconnect2NetMapping delayOnError
#endif
                                    if inbox.CurrentQueueLength < NET_MOVER_QUEUE_CAPACITY then
                                        (stream, name, originalFullPath) |> (Error >> inbox.Post)
                                    else
                                        stream.Dispose()

                            return! infiniteLoop (not !success)
                        }

                    infiniteLoop false)

        let getNewZipFilename targetDir (startTime: DateTime) =
            let path =
                (startTime.ToUniversalTime().ToString "yyyyMMddTHHmmssK", DateTime.UtcNow.ToString "yyyyMMddTHHmmssK") in

            targetDir ^/ (path ||> sprintf "%s-%s.svdmp.zip")

        let compressor =
            MailboxProcessor.Start
                (fun inbox ->
                    let counter = ref 0
                    let mutable startTime = DateTime.UtcNow
                    let mutable zipIsFinished = true

                    let rec infinitLoop
                        cou
                        fcou
                        (arc: MyZipOutputStream)
                        arcname
                        (stream: MemoryStream)
                        (pair: BmpXmlPair)
                        =
                        let c = ref cou

                        let compressFile
                            (
                                ms: MemoryStream,
                                dt,
                                extension,
                                origFileName: string
                                //_origFullName: string
                            ) =
                            compressorKicker.Stop()

                            if zipIsFinished then
                                startTime <- dt
                                zipIsFinished <- false

                            let getFileName = origFileName.Replace('\\', '/')

                            try
                                if not << arc.ContainsEntry <| getFileName then
                                    (++)c
                                    let ze = arc.PutNextEntry getFileName
                                    ms.Position <- 0L

                                    ze.CompressionLevel <-
                                        if extension = ".png" then
                                            Ionic.Zlib.CompressionLevel.None
                                        else
                                            cLevel

                                    ms.CopyTo arc

                                ms.Dispose()
                            with ex -> exceptionHandler 259 ex getFileName
                            compressorKicker.Start()

                        let cropPair { Bmp = bmpOpt; Xml = xmlOpt } =
                            try
                                match bmpOpt, xmlOpt with
                                | Some (FileRecord { Stream = bmpStream
                                                     NewFileName = _bmpname
                                                     FileName = origBmpFileName
                                                     NoTries = _
                                                     DateTime = dBmp
                                                     OriginalFullName = originalNameBmp 
                                                     ReadLock = _bmpReadLock
                                                     WriteLock = _bmpWriteLock}),
                                  Some (FileRecord { Stream = xmlStream
                                                     NewFileName = _xmlname
                                                     FileName = origXmlFileName
                                                     NoTries = _
                                                     DateTime = dXml
                                                     OriginalFullName = originalNameXml
                                                     ReadLock = _xmlReadLock
                                                     WriteLock =_xmlWriteLock
                                                     }) ->
                                    let parser = new Parser(bmpStream, xmlStream)

                                    match parser.Cropped with
                                    | Some img ->
                                        let ms = new MemoryStream()
                                        img.Save(ms, Imaging.ImageFormat.Png)
                                        bmpStream.Dispose()

                                        compressFile (
                                            ms,
                                            //           bmpname.Replace(".bmp", ".png"),
                                            dBmp,
                                            ".png",
                                            origBmpFileName.Replace(".bmp", ".png")
                                            //originalNameBmp , bmpReadLock, bmpWriteLock
                                        )

                                        compressFile (
                                            xmlStream,
                                            //     xmlname,
                                            dXml,
                                            ".xml",
                                            origXmlFileName
                                            //originalNameXml , xmlReadLock, xmlWriteLock
                                        )
                                    | None ->
                                        Printf.ksprintf msgInfo "parameters not found or bad file %s" origXmlFileName

                                        if netMoverTask.CurrentQueueLength < 20 then //NET_MOVER_QUEUE_CAPACITY then
                                            let dir = netDir ^/ "errors"

                                            [ (bmpStream, dir + origBmpFileName, originalNameBmp)
                                              (xmlStream, dir + origXmlFileName, originalNameXml) ]
                                            |> List.iter (Error >> netMoverTask.Post)
                                        else Printf.ksprintf msgInfo "netMover queue overflow"

                                | _ -> ()
                            with ex -> exceptionHandler 405 ex ""

                        async {
                            let! msg = inbox.Receive()
                            match msg with
                            | FileName (action, _, _ ,_) -> 
                                let c, nfcou, z, nStream, nArc =
                                    if action = "next" then 
                                        //check zip length and size limitations
                                        if netMoverTask.CurrentQueueLength < NET_MOVER_QUEUE_CAPACITY then
                                            (netMoverTask.Post << Dump) ((Some(arc), stream, arcname, false))
                                        else
                                            msgInfo "drop zip"
                                            arc.Dispose()
                                            stream.Dispose()

                                        zipIsFinished <- true

                                        let z = getNewZipFilename netDir startTime
                                        let nStream = new MemoryStream()
                                        let arc = new MyZipOutputStream(nStream, true)
                                        arc.CompressionLevel <- cLevel
                                        arc.Timestamp <- ZipEntryTimestamp.Windows
                                        arc.ParallelDeflateThreshold <- -1L
                                            //inbox.Post(msg)
                                        0, fcou + 1, z, nStream, arc
                                    else !c, fcou, arcname, stream, arc
                                return! infinitLoop c nfcou nArc z nStream zeroPair
                            | Die ->
                                msgInfo "Finishing zip. Please wait for the end of the job"

                                (netMoverTask.Post << Dump)
                                    (
                                        Some(arc),
                                        stream,
                                        arcname,
                                        true
                                    )
                            | FileRecord { Stream = ms
                                           NewFileName = name
                                           FileName = origFileName
                                           NoTries = _
                                           DateTime = dt
                                           OriginalFullName = originalName } ->

                                let mutable skipRecycle = false
                                (++)counter
                                //report compressor is alive
                                if !counter % 97 = 0 then
                                    Printf.ksprintf
                                        msgInfo
                                        "zipcompress heartbeat %s queue %i"
                                        origFileName
                                        inbox.CurrentQueueLength

                                let extension = !.name

                                if not ms.CanRead then
                                    Printf.ksprintf msgInfo "stream closed %s" origFileName

                                let newPair =
                                    if extension = ".bmp" then
                                        match pair with
                                        | { Bmp = None; Xml = None } ->
                                            skipRecycle <- true
                                            pair.PutBmp msg
                                        | { Bmp = None
                                            Xml = Some (FileRecord { Stream = a
                                                                     NewFileName = xmlname
                                                                     FileName = origXmlName
                                                                     NoTries = _
                                                                     DateTime = d
                                                                     OriginalFullName = originalXmlName }) } ->
                                            let cmp =
                                                [| originalXmlName; originalName |]
                                                |> Array.map Path.GetFileNameWithoutExtension

                                            if cmp.[0] = cmp.[1] then
                                                msg |> (pair.PutBmp >> cropPair)
                                                zeroPair
                                            else
                                                compressFile (
                                                    a,
                                                    d,
                                                    extension,
                                                    origXmlName
                                                    //originalName , xmlReadLock, xmlWriteLock
                                                )

                                                msg |> zeroPair.PutBmp
                                        | { Xml = None
                                            Bmp = Some (FileRecord { Stream = a
                                                                     NewFileName = bmpname
                                                                     FileName = origBmpName
                                                                     NoTries = _
                                                                     DateTime = d
                                                                     OriginalFullName = originalName }) } ->
                                            compressFile (
                                                a,
                                                d,
                                                extension,
                                                origBmpName
                                                //originalName , bmpReadLock, bmpWriteLock
                                            )

                                            msg |> zeroPair.PutBmp
                                        | _ -> zeroPair
                                    elif extension = ".xml" then
                                        match pair with
                                        | { Bmp = None; Xml = None } ->
                                            skipRecycle <- true
                                            pair.PutXml msg
                                        | { Xml = None
                                            Bmp = Some (FileRecord { Stream = a
                                                                     NewFileName = _bmpname
                                                                     FileName = origBmpName
                                                                     NoTries = _
                                                                     DateTime = d
                                                                     OriginalFullName = originalBmpName }) } ->
                                            let cmp =
                                                [| originalBmpName; originalName |]
                                                |> Array.map Path.GetFileNameWithoutExtension

                                            if cmp.[0] = cmp.[1] then
                                                msg |> (pair.PutXml >> cropPair)
                                                zeroPair
                                            else
                                                compressFile (
                                                    a,
                                                    d,
                                                    extension,
                                                    origBmpName
                                                    //originalName , bmpReadLock, bmpWriteLock
                                                )

                                                msg |> zeroPair.PutXml
                                        | { Bmp = None
                                            Xml = Some (FileRecord { Stream = a
                                                                     NewFileName = xmlname
                                                                     FileName = origXmlName
                                                                     NoTries = _
                                                                     DateTime = d
                                                                     OriginalFullName = originalName }) } ->
                                            compressFile (
                                                a,
                                                d,
                                                extension,
                                                origXmlName
                                                //originalName , xmlReadLock, xmlWriteLock
                                            )

                                            msg |> zeroPair.PutXml
                                        | _ -> zeroPair
                                    //elif extension=".mng" then // dead end
                                    //    let mng = new Fuji.Parser(ms)
                                    //    mng.Image.Save("r:\\test.jpg")
                                    //    compressFile (mng.ReMNG, name, dt, extension, origFileName)
                                    //    ms.Dispose()
                                    //    zeroPair
                                    else
                                        compressFile (
                                            ms,
                                            dt,
                                            extension,
                                            origFileName
                                            //originalName 
                                        )

                                        zeroPair


                                let c, nfcou, z, nStream, nArc =
                                    //check zip length and size limitations
                                    if (cou < foldSize
                                        && (stream.Length |> (onb >> b2Mb)) < 100L<Mb>)
                                       || skipRecycle then
                                        !c, fcou, arcname, stream, arc
                                    else
                                        //finallize zip file and create the new one
                                        if netMoverTask.CurrentQueueLength < NET_MOVER_QUEUE_CAPACITY then
                                            (netMoverTask.Post << Dump) ((Some(arc), stream, arcname, false))
                                        else
                                            msgInfo "drop zip"
                                            arc.Dispose()
                                            stream.Dispose()

                                        zipIsFinished <- true

                                        let z = getNewZipFilename netDir startTime
                                        let nStream = new MemoryStream()
                                        let arc = new MyZipOutputStream(nStream, true)
                                        arc.CompressionLevel <- cLevel
                                        arc.Timestamp <- ZipEntryTimestamp.Windows
                                        arc.ParallelDeflateThreshold <- -1L
                                        //inbox.Post(msg)
                                        1, fcou + 1, z, nStream, arc

                                return! infinitLoop c nfcou nArc z nStream newPair
                        //ReadLock = readLock
                        //WriteLock = writeLock
                        }

                    //first zip have service start time
                    let z = getNewZipFilename netDir startTime
                    let nStream = new MemoryStream()

                    let arc =
#if TEST
                        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance)
#endif
                        new MyZipOutputStream(nStream, true)

                    arc.CompressionLevel <- cLevel
                    arc.Timestamp <- ZipEntryTimestamp.Windows

                    infinitLoop 1 0 arc z nStream zeroPair)

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
                                    if compressor.CurrentQueueLength < FILE_MOVER_QUEUE_CAPACITY && (not << isNozzle) file.Stream then
                                        //compress file
                                        compressor.Post msg
                                    else
                                        incr deletedCounter
                                        Printf.ksprintf msgUrgent "Compressor overloaded or nozzle image file received. Dropping %s" file.FileName
                                        file.Stream.Dispose()
                            | _ -> compressor.Post msg

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

                                        do
                                            readLock.CopyTo ms
                                            //|> Async.AwaitIAsyncResult
                                            //|> Async.Ignore

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


                            | Die -> compressor.Post msg

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
                compressor,
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
            compressorKicker.AutoReset <- true
            compressorKicker.Elapsed.Add(fun _ -> compressor.Post <| FileName( "next", String.Empty, DateTime.Now, 0L))
            compressorKicker.Start()
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

        member _.SendToZip message = compressor.Post message
        member _.SendToFileMover message = firstLine.Post message
