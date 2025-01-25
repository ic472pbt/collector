namespace Bulkexport

open System
open System.IO
open System.Drawing
///Critical testable functions
module Funcs =
    let ErrorNeedles = [| "SvError"; "pe=\"NOZZLE\"" |]
    ///Check svdmp file for an error
    let erroneousSvdmp (ms: MemoryStream) =
        ms.Position <- max 0L (ms.Length - 500L)
        //convert bytes to string
        let buffer: byte [] = Array.zeroCreate 500
        let count = ms.Read(buffer,0,500)
        //use stringReader2 =
        //    new StreamReader(ms, System.Text.Encoding.UTF8, false, 500, leaveOpen = true)

        //for fast searching
        //let found2 = stringReader2.ReadToEnd().Contains ErrorNeedles.[0]
        //found2
        System.Text.UTF8Encoding.UTF8.GetString(buffer,0,count).Contains ErrorNeedles.[0]
    
    ///Check svdmp file contains nozzle image
    let isNozzle (ms: MemoryStream) =
        ms.Position <- 0L
        if ms.Length = 0L then true
        else
            let buffer: byte [] = Array.zeroCreate 500
            let count = ms.Read(buffer,0,int <| min 500L ms.Length)
            System.Text.UTF8Encoding.UTF8.GetString(buffer,0,count).Contains ErrorNeedles.[1]
            
            //use stringReader1 =
            //    new StreamReader(ms, System.Text.Encoding.UTF8, false, int <| min 500L ms.Length, leaveOpen = true)
            //let found1 = stringReader1.ReadToEnd().Contains ErrorNeedles.[1]
            //found1 //||
        
    ///Rotate point clockwise in coordinate system of screen.
    let rotate alpha point =
        let vector =
            [| for f in [ Math.Cos; Math.Sin ] do
                   yield (float32 << f) alpha |]

        fst (point) * vector.[0]
        - snd (point) * vector.[1],
        fst (point) * vector.[1]
        + snd (point) * vector.[0]

    let rotateQ alpha point =
        let vector =
            [| for f in [ Math.Cos; Math.Sin ] do
                   yield f (alpha) |> float32 |]

        fst (point) * vector.[0]
        + snd (point) * vector.[1],
        fst (point) * vector.[1]
        - snd (point) * vector.[0]

    let centerF (R: Rectangle) =
        new PointF(float32 R.X + float32 R.Width / 2.0F, float32 R.Y + float32 R.Height / 2.0F)

    ///Rotate rectangle clockwise in coordinate system of screen.
    let rotateRectangle alpha (R: Rectangle) =
        let center = centerF R

        let points =
            List.map
                ((fun t -> (fst >> float32) t - center.X, (snd >> float32) t - center.Y)
                 >> (rotate alpha))
                [ (R.X, R.Y)
                  (R.Right, R.Y)
                  (R.Right, R.Bottom)
                  (R.X, R.Bottom) ]

        let x =
            points |> (List.minBy fst >> fst >> (+) center.X)

        let y =
            points |> (List.minBy snd >> snd >> (+) center.Y)

        let r =
            points |> (List.maxBy fst >> fst >> (+) center.X)

        let b =
            points |> (List.maxBy snd >> snd >> (+) center.Y)

        new Rectangle(int x, int y, int (r - x), int (b - y))

    ///Extract value from a xml tag
    let selectNode typer (xmlString: string) (startIndex: int) (nodeName: string) =
        let startIdx = xmlString.IndexOf(nodeName, startIndex)

        let startPos =
            startIdx
            + nodeName.Length
            + (if nodeName.EndsWith ">" then 0 else 1)

        let endPos =
            xmlString.IndexOf(sprintf "</%s" nodeName, startPos)
            - 1

        if startIdx > -1 then
            Some(xmlString.[startPos..endPos] |> typer), endPos
        else
            None, startIndex

    ///remove old bad files
    let badFilesClenerFun scaningFolder hours (_: Timers.ElapsedEventArgs) =
        let span = TimeSpan.FromHours hours

        let rec collectFiles fromFolder =
            seq {
                for file in Directory.EnumerateFiles(fromFolder, "*.svdmp") do
                    yield file

                for dir in Directory.EnumerateDirectories fromFolder do
                    yield! collectFiles dir
            }

        collectFiles scaningFolder
        |> Seq.filter (fun file -> DateTime.Now - File.GetLastWriteTime file > span)
        |> Seq.iter
            (fun file ->
                try
                    File.Delete file
                with _ -> ())
