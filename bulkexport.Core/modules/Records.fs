namespace Bulkexport

open Recipe

module Records =
    open System
    open System.IO
    open XmlModule
    open Common.svdmpDTO

    [<Literal>]
    let ResolutionFolder = __SOURCE_DIRECTORY__

    [<NoComparison>]
    type FileRecord =
        { Stream: MemoryStream
          NewFileName: string
          FileName: string
          NoTries: int
          DateTime: DateTime
          OriginalFullName: string 
          ReadLock: FileStream
          WriteLock: FileStream
          }
    
    type FileNameAndTime = {fileName:string; time:int64}

    ///<summary>Message envelope.
    ///<para> <c>Die</c> - Signal for handler to stop it's work.</para>
    ///<para> <c>FileName</c> - A mesage for file mover agent. File name * date and time * time to delay.</para>
    ///<para> <c>FileStream</c> - A mesage for zip archiver agent. Stream to compress * name * path * tries * date and time.</para>
    ///</summary>
    [<NoComparison>]
    type FileNameMsg =
        | Die
        | RecipeMsg of FileNameAndTime
        | RecipeRecord of FSharp.Data.XmlProvider<"samples\\2a9edc3b-d8d6-444c-b748-67806e179010.xml", ResolutionFolder=ResolutionFolder>.PcbData * string
        | FileName of FileNameAndTime * string * DateTime
        | FileRecord of FileRecord *int64 //MemoryStream * string * string * int * DateTime

    [<NoComparison>]
    type BmpXmlPair =
        { Bmp: FileNameMsg option
          Xml: FileNameMsg option }

        member me.PutBmp(msg) = { Bmp = Some(msg); Xml = me.Xml }

        member me.PutXml(msg) = { Xml = Some(msg); Bmp = me.Bmp }

    ///Empty bmp-xml couple
    let zeroPair = { Bmp = None; Xml = None }

    [<NoComparison>]
    type NetMoverMessage =
        | Dump of MemoryStream * string * SourceImageTransform * DateTime * int64
        | Error of MemoryStream * string 
        | RecipeMapping of ResizeArray<BoardInfoDTO>
