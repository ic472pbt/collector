namespace Bulkexport

open System
open Microsoft.Win32
open System.Diagnostics
open System.IO
open EmittingService
open System.Threading.Tasks
open System.Drawing

//open GrpcModule
//open Definition

module JobsGrpc =
    open Records
    open Bulkexport.Diagnostics
    open DSL
    open Funcs
    open XmlModule
    open Recipe
    open Common.svdmpDTO

    [<Literal>]
    let RECIPE_WATCHER_DIR = @"C:\Sirio\Work\RecipeData\BoardTypeData"

    [<Literal>]
    let NET_MOVER_QUEUE_CAPACITY = 100

    [<Literal>]
    let FILE_MOVER_QUEUE_CAPACITY = 100

    let eventsCounter = ref 0
    let deletedCounter = ref 0
    let mutable point1 = 0L
    let mutable point2 = 0L
    let mutable point3 = 0L
    let mutable point4 = 0L
    let mutable counter1 = 0
    let mutable counter2 = 0
    let mutable counter3 = 0
    let mutable counter4 = 0


    ///Timer for time to delay (ttd)
    let globalTime = new System.Diagnostics.Stopwatch()
    ///retry cleaner kicker for every 0.5s
    let cleanerKicker = new System.Timers.Timer(500.0)
    let reporter = new System.Timers.Timer(5000.0)

    ///Event for threads synchronization
    let event =
        new System.Threading.AutoResetEvent(false)
    ///<summary>Service worker</summary>
    ///<param name="sourceDir">Path to directory where to pick up new files</param>
    ///<param name="disableRecipe">Do not collect recipe files.</param>
    ///<param name="recursiveWatching">Recursive directory monitoring.</param>
    ///<param name="verbose">Display messages with low priority.</param>
    ///<param name="badFilesSchedule">Bad files clearing interval (hours).</param>
    type MainJob(sourceDir, disableRecipe, recursiveWatching, verbose, badFilesSchedule: int) as mj =
        ///bad files cleaner for every 24h
        let hours = float badFilesSchedule

        let badFilesCleaner =
            new System.Timers.Timer(15.0 * 60.0 * 1000.0)

        let diagnosticsAgent = diagnosticsAgent verbose
        //let monitoredDir = new DirectoryInfo(sourceDir)

        let msgInfo = Low >> diagnosticsAgent.Post
        let msgUrgent = High >> diagnosticsAgent.Post

        let exceptionHandler line (ex: Exception) text =
            Printf.ksprintf msgUrgent "line %i %s %s %s" line ex.Message ex.StackTrace text
        
        ///write the stream to the hard disk and free up resources
        let writeOut (stream: MemoryStream) (name: string) keepOpen =
            async {                
                use file = File.Create name
                stream.Position <- 0L
                stream.CopyToAsync file 
                    |> Async.AwaitIAsyncResult 
                    |> Async.Ignore 
                    |> Async.RunSynchronously
                if not keepOpen then
                    file.FlushAsync() 
                    |> Async.AwaitIAsyncResult 
                    |> Async.Ignore 
                    |> Async.RunSynchronously
                    stream.Dispose()
                    }

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
                                            with
                                            | ex -> exceptionHandler 128 ex ("second try delete failed path: " + name))

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
                            with
                            | ex ->
                                exceptionHandler 141 ex ("delete error on path: " + fileName)

                                cleanerRetryAgent.Post << Some
                                <| (fileName, DateTime.Now)

                            return! infiniteLoop (counter + 1)
                        }

                    infiniteLoop 0)

        let reentryLock = ref String.Empty

        let mutable mStream = new MemoryStream()

        [<DefaultValue>]
        val mutable mTrans: SourceImageTransform

        let mutable mTimestamp = DateTime.Now
        let mutable mSourceFolder = String.Empty


        do
            async {
                EmittingService.Program.Main(Common.Channel.bridgeInstance)
            }
            |> Async.Start
        /// Move files to network location via grpc tunnel
        let netMoverTask =
            MailboxProcessor<NetMoverMessage>.Start
                (fun inbox ->
                    let mutable innerCounter = 0
                    let rec infiniteLoop (currentRecipe:ResizeArray<BoardInfoDTO>) =
                        async {
                            match! inbox.Receive() with
                            | RecipeMapping recipeMapping ->
                                // refresh the recipe
                                return! infiniteLoop recipeMapping
                            | Dump (stream, name, trans, timeStamp, _ttd) ->
                                if trans.Leads |> fst |> List.length > 1 then
                                    mStream <- stream
                                    mj.mTrans <- trans
                                    mTimestamp <- timeStamp
                                    mSourceFolder <- if not (String.IsNullOrEmpty name) then FileInfo(name).Directory.Name else name
                                    let mBoardData =
                                            let OIDmatch =
                                              try
                                                name 
                                                    |> Path.GetFileNameWithoutExtension 
                                                    |> (fun fn -> new string ((fn.Split('_').[3].ToCharArray() |> Array.rev).[0..3] |> Array.rev)) 
                                                    |> Int32.Parse
                                              with _ -> 
                                                printfn "bad OID %s" name
                                                0
                                            if currentRecipe.Count > OIDmatch then
                                                Some currentRecipe.[OIDmatch]                                                
                                            else 
                                                None
                                    // printfn "%A" mj.mBoardData
                                    let spl = mj.mTrans.HashCode.Split(";")
                                    
                                    let leadsF =
                                        mj.mTrans.Leads
                                            |> fst
                                            |> List.take 2
                                            |> Array.ofList
                                    let dto =
                                                                {
                                                                    picture = mStream
                                                                    componentType = spl.[1]
                                                                    boardInfo = mBoardData
                                                                    size_nm =
                                                                        Size(int (mj.mTrans.ChipSize.X * 1000000.0), int (mj.mTrans.ChipSize.Y * 1000000.0))
                                                                    position_mm = PointF()
                                                                    scale_mm_px = SizeF(float32 mj.mTrans.Scale.X, float32 mj.mTrans.Scale.Y)
                                                                    angle_rad = of_rad mj.mTrans.resultAngle
                                                                    orientation_rad = of_rad mj.mTrans.presentationAngle
                                                                    binning = (int) mj.mTrans.Binning
                                                                    hashkey = spl.[0]
                                                                    isDumped = false //not collecting errors
                                                                    timestamp =
                                                                        mTimestamp
                                                                            .ToString("yyyyMMddTHHmmss.ffffzzz")
                                                                            .Replace(":", String.Empty)
                                                                    body_nm =
                                                                        new Size(
                                                                            mj.mTrans.BodyRect.X / 1.0<nm> |> int,
                                                                            mj.mTrans.BodyRect.Y / 1.0<nm> |> int
                                                                        )
                                                                    DipfType = mj.mTrans.DipfType
                                                                    svdmp = mj.mTrans.Svdmp
                                                                    SourceFolder = mSourceFolder
                                                                    leads =
                                                                        (leadsF
                                                                         |> Array.map
                                                                             (fun L ->
                                                                                 {
                                                                                     number = L.number
                                                                                     numOfGrids = L.numOfGrids
                                                                                     pitch = (L.pitch / 1.0<nm> |> float32)
                                                                                     groupPitch = (L.groupPitch / 1.0<nm> |> float32)
                                                                                     Vector =
                                                                                         PointF(
                                                                                             L.Vector.X / 1.0<nm> |> float32,
                                                                                             L.Vector.Y / 1.0<nm> |> float32
                                                                                         )
                                                                                     angle = (L.angle / 1.0<deg> |> float32)
                                                                                     Position =
                                                                                         (match L.Position with
                                                                                          | LW lw ->
                                                                                              {
                                                                                                  Type = "LW"
                                                                                                  Angle = (lw.angle / 1.0<deg> |> float32)
                                                                                                  Size =
                                                                                                      SizeF(
                                                                                                          lw.Size.X / 1.0<nm> |> float32,
                                                                                                          lw.Size.Y / 1.0<nm> |> float32
                                                                                                      )
                                                                                              }
                                                                                          | LG lw ->
                                                                                              {
                                                                                                  Type = "LG"
                                                                                                  Angle = (lw.angle / 1.0<deg> |> float32)
                                                                                                  Size =
                                                                                                      SizeF(
                                                                                                          lw.Size.X / 1.0<nm> |> float32,
                                                                                                          lw.Size.Y / 1.0<nm> |> float32
                                                                                                      )
                                                                                              }
                                                                                          | LGrid lw ->
                                                                                              {
                                                                                                  Type = "LGrid"
                                                                                                  Angle = (lw.angle / 1.0<deg> |> float32)
                                                                                                  Size =
                                                                                                      SizeF(
                                                                                                          lw.Size.X / 1.0<nm> |> float32,
                                                                                                          lw.Size.Y / 1.0<nm> |> float32
                                                                                                      )
                                                                                              }
                                                                                          | _ -> {Type = "NA"; Angle = 0.0F; Size = SizeF()})
                                                                                 }))
                                                                }
                                    msgInfo "push data"
                                    innerCounter <- innerCounter + 1
                                    do! Common.Channel.bridgeInstance.Push(dto) |> Async.Ignore
                                    msgInfo $"data pushed {innerCounter}" 
                            | Error (stream, originalFullPath) ->
                                try
                                    let origName = (!//originalFullPath ^/ "_" + !/originalFullPath)
                                    reentryLock.Value <- origName
                                    do! writeOut stream origName false
                                with ex ->
                                    exceptionHandler 280 ex ""
                                    if inbox.CurrentQueueLength < 10000 then //retry
                                        (stream, originalFullPath) |> (Error >> inbox.Post)
                                    else
                                        stream.Dispose()
                            return! infiniteLoop currentRecipe
                        }

                    infiniteLoop (new ResizeArray<_>()))

        /// transform files
        let transformer =
            MailboxProcessor<FileNameMsg * bool>.Start
                (fun inbox ->
                    let counter = ref 0
                    let sw = Stopwatch()
                    let mutable lastWarning = DateTime.Now
                    let dropCounter = ref 0

                    let rec infiniteLoop () =
                        async {
                            match! inbox.Receive() with
                            | RecipeRecord (recipe, recipeFileName), _ ->
                                // TODO: exception handling
                                try
                                    let rArray = ResizeArray<BoardInfoDTO>()
                                    match parseRecipe recipeFileName recipe with
                                    | Some {componentTypeIdsLookup=ComponentTypeIdsLookup; componentLocationsLookup=ComponentLocationsLookup; refs=refs; recipe=recipe} ->
                                        for pa in recipe.MachineDependencies.ProcessingAreas do
                                            for phc in pa.WorkingPlan.Layer.PlaceHeadCycles do
                                                for pls in phc.Pls do rArray.Add (refs.[pls.LocId])
                                    | None -> ()
                                    rArray |> RecipeMapping |> netMoverTask.Post
                                with ex -> exceptionHandler 336 ex String.Empty
                            | FileRecord (file,ttd), skip ->
                                sw.Start()
                                let mutable bmpSize = Size()
                                let mutable rectFit = Rectangle()
                                let point0 = globalTime.ElapsedMilliseconds
                                let xSource = new Parser(file.Stream)
                                do point1 <- point1 + globalTime.ElapsedMilliseconds - point0
                                counter1 <- counter1 + 1

                                try
                                    try
                                        let imgExists = xSource.GetNode("SiplaceVisionDump/Images/Image[1]/@Width")
                                        let skipMe = (not << isNull) <| xSource.GetNode("/SiplaceVisionDump/DIPF/PreviewImage")
                                        let transform = if skip || skipMe then None else Some xSource.``get transform``
                                        do point2 <- point2 + globalTime.ElapsedMilliseconds - point0
                                        counter2 <- counter2 + 1
                                        let data =
                                            if not (isNull imgExists || skip || skipMe) then
                                                let transform = transform.Value
                                                let imageWidth = int (imgExists.Value)

                                                let imageHeight =
                                                    int (xSource.GetNode("SiplaceVisionDump/Images/Image[1]/@Height").Value)

                                                let imgNode =
                                                    xSource
                                                        .GetNode(
                                                            "SiplaceVisionDump/Images/Image[1]/ImageData"
                                                        )
                                                        .Value

                                                let bytes =
                                                    DecodeBmpFromString imgNode imageWidth imageHeight 1
                                                do point3 <-  point3 + globalTime.ElapsedMilliseconds - point0
                                                counter3 <- counter3 + 1
                                                use bmp =
                                                    arrayToImage imageWidth imageHeight bytes

                                                // apply pretransform
                                                let (bodyRectWithLeadsPaddingPx,
                                                     _bodyRectWithLeadsPaddingMm,
                                                     _bodyRectWithLeads_mm,
                                                     _bodyRectWithLeads_px) =
                                                    transform.BodySize()

                                                let angle =
                                                    transform.presentationAngle
                                                    + transform.resultAngle
                                                    |> of_rad
                                                    //|> (~-)

                                                let rotatedRegion =
                                                    Rectangle(Point(0, 0), bodyRectWithLeadsPaddingPx)
                                                    |> rotateRectangle angle

                                                let imageCenter =
                                                    PointF(float32 bmp.Width |> (*) 0.5F, float32 bmp.Height |> (*) 0.5F)

                                                let move =
                                                    (transform.result.X
                                                     / (transform.Scale.X )
                                                     |> of_px
                                                     |> float32,
                                                     transform.result.Y
                                                     / (transform.Scale.Y )
                                                     |> of_px
                                                     |> float32)
                                                    //|> rotate -(of_rad transform.presentationAngle)

                                                let targetCenter =
                                                    imageCenter.X
                                                    - float32 rotatedRegion.Width / 2.0F
                                                    + fst move,
                                                    imageCenter.Y
                                                    - float32 rotatedRegion.Height / 2.0F
                                                    - snd move

                                                let croppingRectangle =
                                                    Rectangle(
                                                        Point((fst targetCenter) |> int, (snd targetCenter) |> int),
                                                        rotatedRegion.Size
                                                    )

                                                bmpSize <- bmp.Size
                                                rectFit <- fitToBitmapD bmp croppingRectangle
                                                use cut = bmp.Clone(rectFit, bmp.PixelFormat)

                                                let bs = new MemoryStream()
                                                cut.Save(bs, Imaging.ImageFormat.Png)
                                                bs.Position <- 0L
                                                // File.WriteAllBytes(@"r:\test.png",bs.ToArray())
                                                do point4 <-  point4 + globalTime.ElapsedMilliseconds - point0
                                                counter4 <- counter4 + 1
                                                (bs, file.OriginalFullName)
                                            else
                                                (new MemoryStream(), String.Empty)

                                        if netMoverTask.CurrentQueueLength < NET_MOVER_QUEUE_CAPACITY && not (skip || skipMe) then

                                            netMoverTask.Post <| Dump (fst data, snd data, transform.Value, file.DateTime, ttd)
                                            incr counter
                                        else
                                            (fst data).Dispose()
                                            incr dropCounter
                                            if DateTime.Now - lastWarning > TimeSpan.FromSeconds 5.0 then
                                                lastWarning <- DateTime.Now
                                                Printf.ksprintf msgInfo "%A. Grpc server transmitting queue overflow or a bad file. Dropped %i messages." DateTime.Now (!dropCounter)
                                    with
                                    | ex -> msgUrgent (sprintf "%s %s bmp: %A; crop: %A" ex.Message ex.StackTrace bmpSize rectFit)
                                finally 
                                    file.Stream.Dispose()
                                    xSource.Dispose()
                                    
                                sw.Stop()
                                //report file mover is alive
                                if !counter % 100 = 0 then
                                    Printf.ksprintf
                                        msgInfo
                                        "transformer heartbeat %s counter %i throughput %f files/s"
                                        file.FileName
                                        !counter
                                        (float !counter / sw.Elapsed.TotalSeconds)
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
                            | FileRecord (file, ttd) ->
                                let hasError =
                                    !.file.FileName = ".svdmp"
                                    && erroneousSvdmp file.Stream

                                //delete anyway
                                cleanerAgent.Post(file.OriginalFullName, file.ReadLock, file.WriteLock)

                                let nozzleImage = isNozzle file.Stream
                                if hasError then
                                    (file.Stream, file.OriginalFullName)
                                    |> (Error >> netMoverTask.Post)
                                else 

                                    let noOverload = transformer.CurrentQueueLength < FILE_MOVER_QUEUE_CAPACITY
                                    if noOverload && not nozzleImage then
                                        //transform file

                                        transformer.Post (msg, false)
                                    else
                                        if not noOverload then 
                                            if transformer.CurrentQueueLength < 2 * FILE_MOVER_QUEUE_CAPACITY then

                                                transformer.Post (msg, true)
                                            else
                                                Printf.ksprintf
                                                    msgUrgent
                                                    "High queue length in transformer. Dropping %s"
                                                    file.FileName
                                                file.Stream.Dispose()
                                        else
                                            file.Stream.Dispose()
                                            
                                        incr deletedCounter
                                        file.FileName
                                        |> if nozzleImage then 
                                                Printf.ksprintf
                                                    msgInfo
                                                    "Nozzle image file received. Dropping %s"
                                            else
                                                Printf.ksprintf
                                                    msgUrgent
                                                    "Transformer overloaded. Dropping %s"

                            | _ -> transformer.Post (msg, false)

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
                            | RecipeMsg {fileName=recipeFileName; time=ttd} ->
                                try
                                    let! recipe = readRecipeFile recipeFileName globalTime ttd
                                    (recipe,recipeFileName) |> RecipeRecord |> errorDetectionAgent.Post 
                                with _ ->
                                    if File.Exists recipeFileName then
                                        match nextStep with
                                        | None -> Printf.ksprintf msgInfo "RECIPE 3 retries %s" recipeFileName
                                        | Some agent ->
                                            Printf.ksprintf msgInfo "RECIPE repost %s" recipeFileName

                                            if agent.CurrentQueueLength < 100 then
                                                {fileName=recipeFileName; time=globalTime.ElapsedMilliseconds}
                                                    |> RecipeMsg |> agent.Post
                                    else
                                        Printf.ksprintf msgUrgent "RECIPE file got deleted unexpectedly %s" recipeFileName


                            | FileRecord _ | RecipeRecord _ -> ()
                            | FileName (fileNameAndTime, replaced, dt) ->
                                counter.Value <- counter.Value + 1
                                let fileName = !/fileNameAndTime.fileName

                                if //errorDetectionAgent.CurrentQueueLength < FILE_MOVER_QUEUE_CAPACITY
                                    not (fileName.EndsWith "SvThumbs.xml") then
                                    let newFileName =
                                        sprintf
                                            "%s_%s"
                                            (dt
                                                .ToString("yyyyMMddTHHmmss.ffffzzz")
                                                .Replace(":", String.Empty))
                                            fileName

                                    let waitingTime =
                                        max 0L (6L - globalTime.ElapsedMilliseconds + fileNameAndTime.time)
                                        |> int

                                    if waitingTime > 0 then
                                        do! Async.Sleep waitingTime //delay during recording

                                    try //try to get exclusive write lock
                                        //read, lock file and send
                                        let readLock =
                                            new FileStream(
                                                fileNameAndTime.fileName,
                                                FileMode.Open,
                                                FileAccess.Read,
                                                FileShare.Write ||| FileShare.Delete
                                            )

                                        let writeLock =
                                            try //try to get exclusive write lock
                                                new FileStream(
                                                    fileNameAndTime.fileName,
                                                    FileMode.Open,
                                                    FileAccess.Write,
                                                    FileShare.Read
                                                )
                                            with
                                            | _ ->
                                                readLock.Dispose()
                                                raise <| IOException()

                                        let ms = new MemoryStream()

                                        do!
                                            readLock.CopyToAsync ms
                                            |> Async.AwaitTask

                                        //fileStream.Dispose()
                                        //report file mover is alive
                                        if !counter % 99 = 0 then
                                            Printf.ksprintf
                                                msgInfo
                                                "filemover %i heartbeat %s counter %i"
                                                num
                                                fileNameAndTime.fileName
                                                !counter

                                        if not ms.CanRead then
                                            msgInfo "stream closed 2"

                                        let relativeFileName =
                                            fileNameAndTime.fileName
                                                .Replace(replaced, String.Empty)
                                                .Replace(fileName, newFileName)

                                        FileRecord (
                                            { Stream = ms
                                              NewFileName = newFileName
                                              FileName = relativeFileName
                                              NoTries = 0
                                              DateTime = dt
                                              OriginalFullName = fileNameAndTime.fileName
                                              ReadLock = readLock
                                              WriteLock = writeLock }
                                              , fileNameAndTime.time) |> errorDetectionAgent.Post
                                    with
                                    //file is busy
                                    | :? IOException as _ec ->
                                        if File.Exists fileNameAndTime.fileName then
                                            match nextStep with
                                            | None -> Printf.ksprintf msgInfo "SVDMP 3 retries %s" fileNameAndTime.fileName
                                            | Some agent ->
                                                Printf.ksprintf msgInfo "SVDMP repost %s" fileNameAndTime.fileName

                                                if agent.CurrentQueueLength < 100 then
                                                    agent.Post(
                                                        FileName(
                                                            {fileNameAndTime with time = globalTime.ElapsedMilliseconds},
                                                            replaced,
                                                            dt
                                                        )
                                                    )
                                        else
                                            incr deletedCounter

                                    | ex -> exceptionHandler 412 ex ""
                                else
                                    incr deletedCounter


                            | Die -> transformer.Post (msg, false)

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
                eventsCounter,
                deletedCounter,
                [ firstLine; secondLine; thirdLine ],
                transformer
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
            reporter.AutoReset <- true
            reporter.Elapsed.Add(fun _ -> Printf.ksprintf msgInfo "parse: %f; get transf: %f; decode: %f; transf: %f" (float point1/float counter1) (float point2/float counter2) (float (point3)/float counter3) (float point4/float counter4) )
            reporter.Start()


        ///File system events Handler, that tracks file creation
        let fw = new FileSystemWatcher(sourceDir, "*.*")

        let exclusions =
            [ "NozzleImages"
              "AutoTeach"
              "DCAMInfo" ]

        let watcherHandler (replacedName: string) (f: FileSystemEventArgs) =

            if File.Exists f.FullPath
               && exclusions
                  |> List.forall (not << f.FullPath.Contains) then
                let msg =
                    FileName({fileName = f.FullPath; time = globalTime.ElapsedMilliseconds}, replacedName, DateTime.Now)

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
        
            if not disableRecipe && Directory.Exists RECIPE_WATCHER_DIR then
                diagnosticsAgent.Post(Low($"start recipe dir watching {RECIPE_WATCHER_DIR}"))
                Directory.EnumerateFiles(RECIPE_WATCHER_DIR, "*.xml") 
                    |> Seq.sortBy (fun f -> File.GetLastWriteTime(f))
                    |> Seq.tryLast
                    |> Option.iter (fun lastFile -> {fileName = lastFile; time = globalTime.ElapsedMilliseconds} |> RecipeMsg |> firstLine.Post)
                let recipeWatcher (f: FileSystemEventArgs) =
                    try
                        diagnosticsAgent.Post(Low($"new recipe detected {f.Name}"))
                        {fileName = f.FullPath; time = globalTime.ElapsedMilliseconds} 
                            |> RecipeMsg |> firstLine.Post        
                    with ex -> exceptionHandler 690 ex ""

                let rw =
                    new FileSystemWatcher(RECIPE_WATCHER_DIR, "*.xml")

                rw.EnableRaisingEvents <- true
                rw.NotifyFilter <- fw.NotifyFilter ||| NotifyFilters.LastWrite
                rw.IncludeSubdirectories <- false

                rw.Created.Add recipeWatcher

        member _.SendToZip message = transformer.Post message
        member _.SendToFileMover message = firstLine.Post message
