// Learn more about F# at https://fsharp.org
// See the 'F# Tutorial' project for more help.
open System
open System.IO

[<Literal>]
let CACHE_LIMIT = 400

let memoize fn =
    let cache =
        new System.Collections.Generic.Dictionary<_, _>()

    (fun x ->
        match cache.TryGetValue x with
        | true, v -> v
        | false, _ ->
            let v = fn (x)
            cache.Add(x, v)
            v)

type DataType =
    | Single of string
    | Double of string * string

//[<PermissionSet(SecurityAction.Demand, "FullTrust")>]
let generateFiles (sourceDir: string) (targetDir: string) ext speed : int =
    let mutable counter = 0L
    let hddTimer = new Diagnostics.Stopwatch()
    let readFile fileName = File.ReadAllBytes fileName
   // let readFileMem = memoize readFile

    //let filesQueue =
    //     new Collections.Generic.Queue<string * int>()

    let mills = 1000L / speed

    if not << Directory.Exists <| targetDir then
        Directory.CreateDirectory targetDir |> ignore

    let dummiesDir = DirectoryInfo(sourceDir)

    let attr =
        [ FileAttributes.Hidden
          FileAttributes.ReadOnly
          FileAttributes.System ]

    printfn "Scan directory"
    
    let rec scanFiles (di:DirectoryInfo) = 
        seq {
            for file in di.EnumerateFiles(ext) do
                yield file
            for dir in di.EnumerateDirectories() do
                yield! scanFiles dir 
        }

    let (dummiesFiles: DataType []), mode =

        let files = Array.ofSeq <| scanFiles dummiesDir

        let modePairs =
            files
            |> Array.exists (fun p -> Path.GetExtension p.Name = ".bmp")
            && files
               |> Array.exists (fun p -> Path.GetExtension p.Name = ".xml")

        (if modePairs then
             files
             |> Array.filter (fun p -> Path.GetExtension p.Name = ".bmp")
        //     |> (fun a -> a.Length, a)
        //     |> (fun (len, a) -> a |> Seq.take (min len 400))
             |> Array.map (fun p -> Double(p.FullName, p.FullName.Replace(".bmp", ".xml")))
        //     |> Seq.toArray
         else
             files
          //   |> (fun a -> a.Length, a)
          //   |> (fun (len, a) -> a |> Seq.take (min len 400))
             |> Seq.filter (fun f -> attr |> List.exists (f.Attributes.HasFlag) |> not)
             |> Seq.map (fun p -> Single(p.FullName))
             |> Seq.toArray),
        modePairs

    printfn "found %i files" (dummiesFiles.Length)
    let timer = new Diagnostics.Stopwatch()
    let waiter = new System.Threading.AutoResetEvent(false)

    let writeOut fullName incr =
        let directoryName = Path.GetDirectoryName fullName
        let fullTargetPath = directoryName.Replace(sourceDir, targetDir)
        if not << Directory.Exists <| fullTargetPath then
            ignore << Directory.CreateDirectory <| fullTargetPath 

        let fn =
            fullTargetPath
            + (sprintf @"\file_%i%s" counter (Path.GetExtension fullName))
        if File.Exists fullName then
            try
                let str = readFile fullName
                hddTimer.Start()
                File.WriteAllBytes(fn, str) |> ignore
                hddTimer.Stop()
                counter <- counter + incr

                if counter % 40L = 0L && counter > 0L then
                    printfn
                        "%f files/sec. \n Rated hdd writing time %ims per 100 files"
                        (float counter / float timer.ElapsedMilliseconds
                         * 1000.)
                        (hddTimer.ElapsedMilliseconds * 100L / counter)

            //               filesQueue.Enqueue(fn, 5)
            with ex -> printfn "%s" ex.Message

    let rand = new Random()

    let mainJob (emitter: System.Timers.Timer) =
        let enterTime = timer.ElapsedMilliseconds
        let di = new DirectoryInfo(targetDir)
       
        let fls =
            scanFiles di
            |> Array.ofSeq
            |> Array.sortBy (fun fi -> fi.LastWriteTime)

        let len = fls.Length

        if len >= 150 then
            //let mutable i = 0
            for i in 0..len-150 do
                try
                    fls.[i].Delete()
                with ex -> printfn "line 132: %s" ex.Message
                //i <- i + 1
        //    if ttl > 0 && File.Exists fileName then
        //        filesQueue.Enqueue(fileName, ttl - 1)

        match dummiesFiles.[rand.Next(dummiesFiles.Length)] with
        | Single fullName -> writeOut fullName 1L
        | Double (bmp, xml) ->
            writeOut bmp 0L
            System.Threading.Thread.Sleep(int mills)
            writeOut xml 1L
        emitter.Interval <- max 1.0 (mills - (timer.ElapsedMilliseconds - enterTime) |> float)
        emitter.Enabled <- true

    if dummiesFiles.Length <= 0 then
        printfn "No dummies found"
        1
    else
        printfn "Run gen"
        timer.Start()
        let emitter = new System.Timers.Timer(float mills)

        do
            emitter.AutoReset <- false
            emitter.Elapsed.Add (fun _ -> mainJob(emitter))
            emitter.Enabled <- true

        if waiter.WaitOne() then
            0
        else
            1

[<EntryPoint>]
let main argv =
    printfn "v1.0.4"

    if argv.Length < 2 then
        printfn
            "Usage: FilesGen \"path to dummies source folder\" \"path to dummies destination folder\" \"files filter ie *.* or *.csv\" \"speed files/sec default 20 max 1000\""

        0
    else
        (if argv.Length < 3 then
             generateFiles argv.[0] argv.[1] "*.*" 20L
         elif argv.Length < 4 then
             generateFiles argv.[0] argv.[1] argv.[2] 20L
         else
             generateFiles argv.[0] argv.[1] argv.[2] (int64 argv.[3]))
