namespace Bulkexport
open FSharp.Data
open System.IO
open System.Collections.Generic
open System.Diagnostics
open Common.svdmpDTO

module OIB =
    [<Literal>]
    let ResolutionFolder = __SOURCE_DIRECTORY__
    
    type OIBData = {
        BoardName: string
    }
    type EssentialProvider = XmlProvider<"samples\\Board_2023-04-30-13.46.33_10821.essential.xml", ResolutionFolder=ResolutionFolder>

    let readRecipeFile (recipeFileName:string) (globalTime:Stopwatch) ttd =
        async{
            let waitingTime =
                max 0L (6L - globalTime.ElapsedMilliseconds + ttd)
                |> int

            if waitingTime > 0 then
                do! Async.Sleep waitingTime //delay during recording

            return EssentialProvider.Load recipeFileName
        }

    let parseEssentials (essential:XmlProvider<"samples\\Board_2023-04-30-13.46.33_10821.essential.xml", ResolutionFolder=ResolutionFolder>.Board) =    
        {BoardName = essential.Recipe.Split(@"\") |> Array.last}