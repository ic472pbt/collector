namespace Bulkexport

open System.Diagnostics
open System
open System.Net
open System.Net.Sockets
open System.Text
open System.IO

module Diagnostics =
    open Records
    ///<summary>Diagnostics message priority.
    ///<para> <c>Low</c> - Message with low priority displayed only in verbose mode.</para>
    ///<para> <c>High</c> - Mesage with high priority always displayed.</para>
    ///</summary>
    type DiagMessage =
        | Low of string
        | High of string

    ///Agent what prints out diagnostic messages.
    ///Messages with low priority printed in verbose mode only. Messages with high priority always printed.
    let diagnosticsAgent verbose =
        MailboxProcessor.Start
            (fun inbox ->
                let server = new TcpListener(IPAddress.Any, 1212)
                let mutable speed = 0L
                let mutable lastReport = 0L
                let timer = new Stopwatch()
                timer.Start()

                let serverStarted =
                    try
                        server.Start()
                        ref true
                    with _ -> ref false

                let connected = ref false
                let connection : (TcpClient * NetworkStream) ref = ref (null, null)
                //let verboseToTcp = ref false
                let reconnect () =
                    async {
                        if !serverStarted then
                          try
                            let client = server.AcceptTcpClient()
                            let ns = client.GetStream()
                            //read error.log
                            //let pathToSelf = Path.GetDirectoryName <| Process.GetCurrentProcess().MainModule.FileName
                            //let errorLogName = Path.Combine(pathToSelf, "error.log")
                            //let errorMsg =
                            //    if File.Exists(errorLogName) then
                            //        "error.log " + File.ReadAllText errorLogName
                            //    else
                            //        String.Empty
                            let hello =
                                Encoding.Default.GetBytes(
                                    "Watch diagnostic messages. Type a digit meaning messages per second or wait for no limit or type 0.\n\r"
                                )

                            ns.Write(hello, 0, hello.Length)
                            ns.ReadTimeout <- 5000
                            let mutable go = true
                            while go do
                                try 
                                    let digit = ns.ReadByte()

                                    if digit > 47 && digit <58 then
                                        speed <-
                                            if digit = 0 then
                                                0L
                                            else
                                                min (max 0 (digit - 48)) 57 |> int64
                                        go <- false
                                with :? System.IO.IOException ->
                                    speed <- 0L
                                    go <- false

                            connected := true
                            connection := (client, ns)
                          with ex -> 
                            printfn "%A: reconnect %s" DateTime.Now ex.Message
                    }

                reconnect () |> Async.Start

                let reportToClient (msg: string) =
                    if !serverStarted && !connected then
                        let (client, ns) = !connection
                        try

                            if client.Connected then
                                let hello = Encoding.Default.GetBytes(msg + "\n\r")
                                ns.Write(hello, 0, hello.Length)
                                lastReport <- timer.ElapsedMilliseconds
                            else
                                connected := false
                                client.Dispose()
                                reconnect () |> Async.Start
                        with _ ->
                            if (not << isNull) client then client.Dispose()    
                            connected := false
                            reconnect () |> Async.Start

                    printfn "%s" msg

                //let verbosehandler =
                //   if verbose then
                //       (fun msg -> File.AppendAllLines(Path.Combine(Path.GetTempPath() , "verbose.log"),
                //                                       [DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToShortTimeString() + " " + msg]);
                //                                       printfn "%s" msg)
                //   else
                //       printfn "%s"
#if !TEST 
                let eventLog = new EventLog("Application")
                do eventLog.Source <- "BulkExport"
#endif                
                let appendText a b  = System.IO.File.WriteAllText(a,b)
                let rec messageLoop () =
                    async {
                        let skip =
                            speed > 0L
                            && timer.ElapsedMilliseconds - lastReport < 1000L / speed
#if TEST
                        let loggerFunction = printfn "%s"
#else
                        let loggerFunction = eventLog.WriteEntry //appendText @"r:\out.txt" //
#endif
                        match! inbox.Receive() with
                        | High s ->
                            Debug.Write s
                            try
                                Printf.kprintf loggerFunction "%s" s

                                if not skip then
                                    Printf.kprintf reportToClient "%s" s
                            with _ -> ()
                        //verbosehandler <| sprintf "%s" s
                        | Low s ->
                            Debug.Write s
                            if verbose && not skip then
                                try
                                    Printf.kprintf reportToClient "%s" s //Printf.kprintf verbosehandler "%s" s
                                with _ -> ()

                        return! messageLoop ()
                    }

                messageLoop ())
    //watchdog system
    type Watchdog
        (
            diagnosticsAgent: MailboxProcessor<DiagMessage>,
            netMoverAgent: MailboxProcessor<NetMoverMessage>,
            eventsCounter,
            deletedCounter,
            fileMoverAgent: MailboxProcessor<FileNameMsg> list,
            transformerAgent: MailboxProcessor<FileNameMsg*bool>
        ) =
        let selfTestTimer = new System.Timers.Timer(5000.0)
        let self = Process.GetCurrentProcess()
        let mutable lastTime = DateTime.Now
        let mutable lastProcTime = self.TotalProcessorTime
        let PC = new PerformanceCounter() // fsharplint:disable-line NonPublicValuesNames

        do
            PC.CategoryName <- "Process"
            PC.CounterName <- "Working Set - Private"
            PC.InstanceName <- "bulkexport"
        //main observer
        let watchdog (_a: System.Timers.ElapsedEventArgs) =
            //check CPU usage
            let curTime = DateTime.Now
            let curProcTime = self.TotalProcessorTime

            // fsharplint:disable-next-line NonPublicValuesNames
            let CPUUsage =
                (curProcTime.TotalMilliseconds
                 - lastProcTime.TotalMilliseconds)
                / curTime.Subtract(lastTime).TotalMilliseconds
                * 100.0

            lastTime <- curTime
            lastProcTime <- curProcTime
            //check RAM usage
            // fsharplint:disable-next-line NonPublicValuesNames
            let RAMUsage = Convert.ToInt32(PC.NextValue()) / (1024) //self.PrivateMemorySize64

            let readersState =
                fileMoverAgent
                |> List.map (fun a -> a.CurrentQueueLength.ToString())

            diagnosticsAgent.Post(
                Low
                <| sprintf
                    "id %i; cpu: %.1f%%; core: %.1f%%; ram: %i kB; Watch: %i; Read: [%s]; transform: %i; netQ: %i; ignored: %i"
                    self.Id
                    (CPUUsage
                     / Convert.ToDouble(Environment.ProcessorCount))
                    CPUUsage
                    RAMUsage
                    !eventsCounter
                    (String.Join(", ", readersState))
                    transformerAgent.CurrentQueueLength
                    netMoverAgent.CurrentQueueLength
                    !deletedCounter
            )

        member _.Start() =
            selfTestTimer.AutoReset <- true
            selfTestTimer.Elapsed.Add watchdog
            selfTestTimer.Start()

//end watchdog system
