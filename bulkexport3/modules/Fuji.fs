namespace Bulkexport

open System
open DSL
[<Obsolete("Dead end. No benefits from cropping.")>]
module Fuji =
    open System.IO
    open System.Drawing
    open System.Xml

    [<Literal>]
    let IntSize = 4

    [<Literal>]
    let HeaderLength = 384L

    ///litle-endian to big-endian converter
    let l2b (bytes: byte []) =
        if BitConverter.IsLittleEndian then
            Array.Reverse(bytes)

        BitConverter.ToInt32(bytes, 0)

    ///stream advance to desired number of bytes
    let advance distance (mng: MemoryStream) =
        mng.Seek(distance, SeekOrigin.Current) |> ignore

    let readRecord (binaryReader: BinaryReader) =
        binaryReader.ReadInt32() |> ignore
        let recLength = binaryReader.ReadBytes IntSize |> l2b
        // skip 4 bytes
        binaryReader.ReadInt32() |> ignore

        (new string (binaryReader.ReadChars(recLength)))
            .Split('\x00')

    let innerParser (xmlText: string, tagname) =
        let root = new XmlDocument()
        do root.LoadXml xmlText

        for data in root.SelectNodes("//" + tagname) do
            if (not << String.IsNullOrEmpty) (data.InnerText) then
                let decoded =
                    data.InnerText
                    |> (Convert.FromBase64String
                        >> Text.Encoding.ASCII.GetString)

                data.InnerXml <- decoded

        root

    type Parser(mng: MemoryStream) =
        let _ = mng.Seek(HeaderLength, SeekOrigin.Begin)
        let binaryReader = new BinaryReader(mng)
        // read image stream and it's end position
        // fsharplint:disable-next-line NonPublicValuesNames
        let image, EOIPosition =
            // read image stream length
            // fsharplint:disable-next-line NonPublicValuesNames
            let JFIFlength = binaryReader.ReadBytes IntSize |> l2b in
            let bytes : byte [] = Array.zeroCreate JFIFlength
            mng.Seek(4L, SeekOrigin.Current) |> ignore
            mng.Read(bytes, 0, JFIFlength) |> ignore
            let ms = new MemoryStream(bytes)
            ms.Position <- 0L
            //File.WriteAllBytes("r:\\image.jpg",bytes)
            new Bitmap(ms), mng.Position
        //skip IEND lable
        do advance 12L mng
        let mutable k = readRecord binaryReader

        do
            while k.[0] <> "Results" do
                k <- readRecord binaryReader
        // Read result tag
        let decodedResults = innerParser (k.[1], "result")
        //do printfn "%s" decodedResults.InnerXml

        ///Fuji jpg image
        member _.Image = image

        ///Coordinates of the raw images on the pane
        member _.Mosaics =
            seq {
                for roi in decodedResults.SelectNodes "//m_roi" do
                    let coords =
                        [| "upperLeft_/x"
                           "upperLeft_/y"
                           "lowerRight_/x"
                           "lowerRight_/y" |]
                        |> Array.map (
                            roi.SelectSingleNode
                            >> (fun node ->
                                if DSL.isNull (node) then
                                    0
                                else
                                    int node.InnerText)
                        )

                    yield
                        new Rectangle(coords.[0], coords.[1], coords.[2] - coords.[0] + 1, coords.[3] - coords.[1] + 1)
            }

        member me.Smithereens : Bitmap seq =
            me.Mosaics
            |> Seq.map (WhatToExtract.Region >> extract from image)

        member me.ReMNG =
                let ms = new MemoryStream()
                mng.Position <- 0L
                let binaryWriter = new BinaryWriter(ms)
                let bytes: byte [] = Array.zeroCreate 384
                mng.Read(bytes,0,int HeaderLength) |> ignore
                ms.Write(bytes,0,int HeaderLength) 
                //sequentially put images
                for bmp in me.Smithereens do
                    let bmpStream = new MemoryStream()
                    bmp.Save(bmpStream,Imaging.ImageFormat.Bmp)
                    bmpStream.Position <- 0L
                    //put the stream length first
                    binaryWriter.Write bmpStream.Length
                    //put the stream next
                    bmpStream.CopyTo ms
                mng.Position <- EOIPosition
                mng.CopyTo ms
                ms
