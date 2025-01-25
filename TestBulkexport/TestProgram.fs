module TestProgram

open NUnit.Framework
open FsCheck.NUnit
open System
open System.IO

#if ASEC
open Bulkexport.JobsAsec
#endif
#if BACK
open Bulkexport.JobsBack
#else
open Bulkexport.Jobs
#endif
open Bulkexport.Records
open Bulkexport.DSL

let R = new Random()

let putSvdmpFiles (checkedDirs: DirectoryInfo []) =
    //put svdmp files to checked dirs
    let bytes : byte [] = Array.zeroCreate 500

    for i in 1 .. 10 do
        checkedDirs
        |> Array.iter
            (fun di ->
                R.NextBytes(bytes)
                File.WriteAllBytes(di.FullName ^/ i.ToString() + ".svdmp", bytes))

let putSvThumbsFiles (checkedDirs: DirectoryInfo []) =
    //put svdmp files to checked dirs
    let bytes : byte [] = Array.zeroCreate 500

    for _ in 1 .. checkedDirs.Length do
        checkedDirs
        |> Array.iter
            (fun di ->
                R.NextBytes(bytes)
                File.WriteAllBytes(di.FullName ^/ "SvThumbs.xml", bytes))

let measureSvdmpFiles (di: DirectoryInfo) =
    di.EnumerateFiles("*.svdmp") |> Seq.length

let measureSvThumbsFiles (di: DirectoryInfo) =
    di.EnumerateFiles("SvThumbs.xml") |> Seq.length



#if !GIT
[<TestFixture>]
type SourceFilesBehaviorTest()  as T =
    [<DefaultValue>]
    val mutable sourceDir: string
    [<DefaultValue>]
    val mutable destDir: string
    [<DefaultValue>]
    val mutable mJ: MainJob

    let testSubdirs = [| 0..2 |]
    let checkedDirs: DirectoryInfo [] = Array.zeroCreate testSubdirs.Length

    let directoryTestTemplate genFun (measureFun: DirectoryInfo -> int) assertFun =    
        genFun checkedDirs
        System.Threading.Thread.Sleep 4000
        event.Reset() |> ignore
        T.mJ.SendToZip Die
        event.WaitOne(4000) |> ignore
        assertFun (checkedDirs |> Array.sumBy measureFun)

    [<SetUp>]
    member T.Setup () = 
        T.sourceDir <- Path.GetTempPath() ^/ "SvDumpFiles"

        if Directory.Exists T.sourceDir then
            Directory.Delete(T.sourceDir, true)

        T.destDir <- Path.GetTempPath() ^/ "Target"
        //create source and target
        for dir in [ T.sourceDir; T.destDir ] do
            if (not << Directory.Exists) dir then
                Directory.CreateDirectory dir |> ignore
        //create subdirectories in source
        for i in testSubdirs do
                let subDirectory = T.sourceDir ^/ i.ToString() in
                checkedDirs.[i] <- 
                    if (not << Directory.Exists) subDirectory then
                        Directory.CreateDirectory subDirectory
                    else
                        new DirectoryInfo(subDirectory)
        T.mJ <-
            new MainJob(
                T.sourceDir,
                T.destDir,
                10,
                disableRecipe = true,
                recursiveWatching = true,
                cLevel = Ionic.Zlib.CompressionLevel.Level0,
                verbose = true, 
                badFilesSchedule=None
            )

    [<Test>]
    member _.``source directories are clean in recursive mode``() =
        directoryTestTemplate putSvdmpFiles measureSvdmpFiles Assert.Zero

    [<Test>]
    member _.``keep SvThumbs_xml``() =
        directoryTestTemplate putSvThumbsFiles measureSvThumbsFiles Assert.NotZero
#endif