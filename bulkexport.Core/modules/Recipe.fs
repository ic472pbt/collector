namespace Bulkexport
open FSharp.Data
open System.IO
open System.Collections.Generic
open System.Diagnostics
open Common.svdmpDTO

module Recipe =
    [<Literal>]
    let ResolutionFolder = __SOURCE_DIRECTORY__
    
    type ComponentType = {deviceId: int; componentType: int}
    [<NoComparison>]
    type RecipeMappingType = 
        {
            componentTypeIdsLookup: Map<int,string>
            componentLocationsLookup: Map<int,ComponentType>
            refs: Dictionary<int,BoardInfoDTO>
            recipe: XmlProvider<"samples\\2a9edc3b-d8d6-444c-b748-67806e179010.xml", ResolutionFolder=ResolutionFolder>.PcbData
        }

    type RecipeProvider = XmlProvider<"samples\\2a9edc3b-d8d6-444c-b748-67806e179010.xml", ResolutionFolder=ResolutionFolder>
    type Setup = XmlProvider<"samples\\0c149ff6-e7c4-4a0e-9cd3-158244406713.xml", ResolutionFolder=ResolutionFolder>

    let readRecipeFile (recipeFileName:string) (globalTime:Stopwatch) ttd =
        async{
            let waitingTime =
                max 0L (6L - globalTime.ElapsedMilliseconds + ttd)
                |> int

            if waitingTime > 0 then
                do! Async.Sleep waitingTime //delay during recording

            return RecipeProvider.Load recipeFileName
        }

    let parseRecipe (recipeFileName:string) (recipe:XmlProvider<"samples\\2a9edc3b-d8d6-444c-b748-67806e179010.xml", ResolutionFolder=ResolutionFolder>.PcbData) =
        let setupFileName = Path.Combine((new FileInfo(recipeFileName)).Directory.Parent.FullName, "SetupData", $"{recipe.PcbDependencies.SetupKey}.xml")
        if File.Exists setupFileName then
            let setup = Setup.Load setupFileName

            let ComponentTypeIdsLookup: Map<int,string> = 
                setup.ComponentSetup.ComponentTypeList 
                    |> Array.fold (fun state ct -> state.Add(ct.Id, ct.Name.Split('\\') |> Array.last)) Map.empty
    
            let ComponentLocationsLookup: Map<int,ComponentType> = 
                setup.ComponentSetup.ComponentLocationList 
                    |> Array.fold (fun state cl -> state.Add(cl.Id, {deviceId=cl.DeviceId; componentType=cl.ComponentTypeId})) Map.empty

    
            let refs = new Dictionary<int,BoardInfoDTO>()

            recipe.ImageDefinitionList 
                |> Array.filter (fun idl -> idl.LocationList.IsSome)
                |> Array.iter (fun idl -> 
                                    idl.LocationList 
                                        |> Option.iter (fun LL -> LL.Locations 
                                                                    |> Array.iter (fun L -> 
                                                                        refs.[L.Id] <- {refDes=L.Name; boardmatrix = (idl.PanelMatrixOid |> Option.defaultValue 0); rotation=L.A; coordinates = System.Drawing.Point(L.X, L.Y); boardName=idl.Name; boardId=idl.Id})))
            Some {componentTypeIdsLookup=ComponentTypeIdsLookup; componentLocationsLookup=ComponentLocationsLookup; refs=refs; recipe=recipe}
        else None