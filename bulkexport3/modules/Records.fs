namespace Bulkexport

module Records =
    open System
    open System.IO
    open Ionic.Zip

    type MyZipOutputStream(stream: Stream, leaveOpen: bool) =
        inherit ZipOutputStream(stream, leaveOpen)
        let containsEntries = ref false
        member _.PutNextEntry(entryName) = 
            containsEntries := true
            base.PutNextEntry entryName
        member _.HasEntries = !containsEntries

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
    ///<summary>Message envelope.
    ///<para> <c>Die</c> - Signal for handler to stop it's work.</para>
    ///<para> <c>FileName</c> - A mesage for file mover agent. File name * date and time * time to delay.</para>
    ///<para> <c>FileStream</c> - A mesage for zip archiver agent. Stream to compress * name * path * tries * date and time.</para>
    ///</summary>
    type FileNameMsg =
#if ASEC
        | Kick
#endif
        | Die
        | FileName of string * string * DateTime * int64
        | FileRecord of FileRecord //MemoryStream * string * string * int * DateTime

    type BmpXmlPair =
        { Bmp: FileNameMsg option
          Xml: FileNameMsg option }

        member me.PutBmp(msg) = { Bmp = Some(msg); Xml = me.Xml }

        member me.PutXml(msg) = { Xml = Some(msg); Bmp = me.Bmp }

    ///Empty bmp-xml couple
    let zeroPair = { Bmp = None; Xml = None }

    // DateTime - archive start time
    type NetMoverMessage =
        | Dump of MyZipOutputStream option * MemoryStream * string * bool
        | Error of MemoryStream * string * string
