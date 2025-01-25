namespace Bulkexport

open System.Configuration.Install
open System.ServiceProcess
open System.Reflection
open System.Xml
open System.IO

module Service =
    [<Literal>]
    let SVC_APP_NAME = "bulkexport"

    [<Literal>]
    let SVC_SERVICE_KEY =
        @"SYSTEM\CurrentControlSet\Services\"
        + SVC_APP_NAME

    [<Literal>]
    let SVCPARAMKEY =
        @"SYSTEM\CurrentControlSet\Services\"
        + SVC_APP_NAME
        + @"\Parameters"
    [<Literal>]
    let OptionsFile = @"c:\Sirio\Type\VisionSensorTypes.xml" 

    ///Service installer/uninstaller class
    type ExportServiceInstaller() =
        inherit Installer()

        let spi =
            new ServiceProcessInstaller(Account = ServiceAccount.LocalSystem)

        let si =
            new ServiceInstaller(
                DisplayName = "BulkExport Service",
                Description = "Cybord BulkExport Service",
                ServiceName = "bulkexport",
                StartType = ServiceStartMode.Automatic
            )

        let dic = new System.Collections.Hashtable()

        member this.Install(source, dest, zipSize, disableRecipe: bool, recursiveWatching: bool, cleanerInterval: int option) =
            base.Installers.Add(spi) + base.Installers.Add(si)
            |> ignore

            let apath =
                sprintf "/assemblypath=%s" (Assembly.GetExecutingAssembly().Location)

            let ctx =
                [| apath
                   "/logToConsole=true"
                   "/showCallStack" |]

            this.Context <- new InstallContext("exportserviceinstall.log", ctx)
            base.Install(dic)
            base.Commit(dic)

            use k =
                Microsoft.Win32.Registry.LocalMachine.OpenSubKey(SVC_SERVICE_KEY, writable = true)

            k.CreateSubKey("Parameters") |> ignore

            use kArgs =
                Microsoft.Win32.Registry.LocalMachine.OpenSubKey(SVCPARAMKEY, writable = true)

            [ ("Source", source)
              ("Destination", dest) ]
            |> List.iter kArgs.SetValue

            kArgs.SetValue("ZipSize", zipSize)
            kArgs.SetValue("DisableRecipe", disableRecipe)
            if cleanerInterval.IsSome then
                kArgs.SetValue("CleanerInterval", cleanerInterval.Value)

            if recursiveWatching then
                kArgs.SetValue("RecursiveWatching", recursiveWatching)
            //set dump always key
            let saveColor = System.Console.ForegroundColor
            if File.Exists OptionsFile then
                try 
                    let xmlDoc = new XmlDocument();
                    xmlDoc.Load OptionsFile
                    let node = xmlDoc.SelectSingleNode "/VisionTypes/OptionalSettings/Operationmode/dump_always"
                    node.InnerText <- "true"
                    xmlDoc.Save OptionsFile
                    System.Console.ForegroundColor <- System.ConsoleColor.Yellow
                    this.Context.LogMessage "Please restart Siplace for changes to take effect."
                    System.Console.ForegroundColor <- saveColor
                with ex -> 
                    this.Context.LogMessage ex.Message
            else
                System.Console.ForegroundColor <- System.ConsoleColor.Red
                this.Context.LogMessage "Please set station to dump always manually"
                System.Console.ForegroundColor <- saveColor


        member this.Uninstall() =
            base.Installers.Add(spi) |> ignore
            base.Installers.Add(si) |> ignore

            let apath =
                sprintf "/assemblypath=%s" (Assembly.GetExecutingAssembly().Location)

            let ctx =
                [| apath
                   "/logToConsole=true"
                   "/showCallStack" |]

            this.Context <- new InstallContext("exportserviceinstall.log", ctx)
            let saveColor = System.Console.ForegroundColor
            if File.Exists OptionsFile then
                try
                    let xmlDoc = new XmlDocument();
                    xmlDoc.Load OptionsFile
                    let node = xmlDoc.SelectSingleNode "/VisionTypes/OptionalSettings/Operationmode/dump_always"
                    node.InnerText <- "false"
                    xmlDoc.Save OptionsFile
                    System.Console.ForegroundColor <- System.ConsoleColor.Yellow
                    this.Context.LogMessage "Please restart Siplace for changes to take effect."
                    System.Console.ForegroundColor <- saveColor
                with ex -> this.Context.LogMessage ex.Message
            base.Uninstall(null)
