namespace Bulkexport

open System
open System.IO
open System.Xml.XPath
open System.Xml
open System.Drawing

module XmlModule =
    let culture =
        new System.Globalization.CultureInfo("en-US")

    [<Measure>]
    type mm

    [<Measure>]
    type nm

    [<Measure>]
    type px

    [<Measure>]
    type deg

    [<Measure>]
    type rad

    [<Measure>]
    type perc

    let inline of_rad (α: float<rad>) = α / 1.0<rad>
    let deg2rad (x: float<deg>) = x * 0.01745329252<rad/deg>
    let inline nm2mm (x: float<nm>) = x / 1000000.0<nm/mm>
    let inline on_nm (x: float) = x * 1.0<nm>
    let inline on_rad (α: float) = α * 1.0<rad>
    let inline on_mm (x: float) = x * 1.0<mm>
    let inline of_mm (x: float<mm>) = x / 1.0<mm>
    let inline on_deg (α: float) = α * 1.0<deg>
    let of_px (x: float<px>) = x / 1.0<px>
    let inline strip_px (x: float<px>) = x |> of_px |> round |> int

    let (|Turn|) (α: float<rad>) =
        if (α - 1.5707963<rad> |> of_rad |> abs) < 0.5 then
            RotateFlipType.Rotate90FlipNone
        elif (α + 1.5707963<rad> |> of_rad |> abs) < 0.5 then
            RotateFlipType.Rotate270FlipNone
        elif (α - 3.141592<rad> |> of_rad |> abs) < 0.5
             || (α + 3.141592<rad> |> of_rad |> abs) < 0.5 then
            RotateFlipType.Rotate180FlipNone
        else
            RotateFlipType.RotateNoneFlipNone

    let getSingleSafe (element: XPathNavigator) =
        if not (isNull element) then
            let b = element.Value.Split(' ').[0] in Convert.ToDouble(b, culture)
        else
            0.0

    let getIntSafe (element: XmlNode) =
        if not (isNull element) then
            let b = element.Value in Convert.ToInt32(b)
        else
            0

    let getIntSafeN (element: XPathNavigator) =
        if not (isNull element) then
            let b = element.Value in Convert.ToInt32(b)
        else
            0

    type Shift<[<Measure>] 'm> =
        struct
            val X: float<'m>
            val Y: float<'m>
            new(x, y) = { X = x; Y = y }
        end
        static member (+)(a: Shift<'T>, b: Shift<'T>) = new Shift<'T>(a.X + b.X, a.Y + b.Y)
        static member nm2mm(x: Shift<nm>) = new Shift<mm>(nm2mm x.X, nm2mm x.Y)

    type LeadsType =
        | Wraparound
        | Gullwing
        | JBend
        | Polygoncircle
        | LeadCorner
        | LeadBlob
        | LeadCenteringPin
        | Unknown

    type TypeOrElseBuilder() =
        member _.Combine(a, b) =
            match fst a with
            | null -> b
            | _ -> a

        member _.ReturnFrom(x) = x
        member _.Delay(f) = f ()

    type GroupType =
        | Group
        | GroupGrid
        | NoGroup

    type GroupOrElseBuilder() =
        member _.Combine(a: XPathNodeIterator * GroupType, b) =
            match (fst a).Count with
            | 0 -> b
            | _ -> a

        member _.ReturnFrom(x) = x
        member _.Delay(f) = f ()

    type LeadWrap =
        { angle: float<deg>
          Size: Shift<nm> }
        member my.rotatedSize(α: float<deg>) =
            match deg2rad α with
            | Turn R ->
                match R with
                | RotateFlipType.RotateNoneFlipNone
                | RotateFlipType.Rotate180FlipNone -> new Shift<nm>(my.Size.Y, my.Size.X)
                | _ -> my.Size

    type LeadGullwing =
        { angle: float<deg>
          Size: Shift<nm>
          Contact: Shift<nm> }
        member my.rotadedSize(α: float<deg>) =
            match deg2rad α with
            | Turn R ->
                match R with
                | RotateFlipType.RotateNoneFlipNone
                | RotateFlipType.Rotate180FlipNone -> new Shift<nm>(my.Size.Y, my.Size.X)
                | _ -> my.Size

    type LeadPosition =
        | LW of LeadWrap
        | LG of LeadGullwing
        | LGrid of LeadWrap
        | LC of Shift<nm> * Shift<nm> * Shift<nm> * Shift<nm>
        | NaN

    type Lead =
        { number: int
          numOfGrids: int //for grid leads
          pitch: float<nm>
          groupPitch: float<nm> //for grid leads
          Vector: Shift<nm>
          angle: float<deg>
          Position: LeadPosition }
        member my.Rectangle (imageCenter: Point) (scale: Shift<mm / px>) =
            let a =
                (nm2mm
                 <| match my.Position with
                    | LW lw -> lw.Size.X
                    | LG lg -> lg.Size.X
                    | LGrid lg -> lg.Size.X
                    | LC _ -> 0.0<nm>)
                / scale.X
                |> of_px

            let b =
                (nm2mm
                 <| match my.Position with
                    | LW lw -> lw.Size.Y
                    | LG lg -> lg.Size.Y
                    | LGrid lg -> lg.Size.Y
                    | LC _ -> 0.0<nm>)
                / scale.Y
                |> of_px

            let w, h =
                match deg2rad my.angle with
                | Turn R ->
                    match R with
                    | RotateFlipType.RotateNoneFlipNone
                    | RotateFlipType.Rotate180FlipNone -> b, a
                    | _ -> a, b

            let v = my.Vector

            new Rectangle(
                ((nm2mm -v.X) / scale.X
                 |> strip_px
                 |> (+) imageCenter.X)
                - (w / 2.0 |> round |> int),
                ((nm2mm -v.Y) / scale.Y
                 |> strip_px
                 |> (+) imageCenter.Y)
                - (h / 2.0 |> round |> int),
                w |> round |> int,
                h |> round |> int
            )

    type SourceImageTransform =
        { presentationAngle: float<rad>
          resultAngle: float<rad>
          presentation: Shift<mm>
          result: Shift<mm>
          Scale: Shift<mm / px>
          BodyRect: Shift<nm>
          Flip: PointF
          Binning: float
          Leads: Lead list * Shift<nm>
          ChipSize: Shift<mm>
          NumOfLeads: int
          ComponentName: string
          HashCode: string 
          DipfType: string
          Svdmp: string}
        member my.BodySize() =
            //transpose the footprint PREP-33
            let fp =
                if my.BodyRect.X > my.BodyRect.Y then
                    new Shift<mm>(my.BodyRect.X |> nm2mm, my.BodyRect.Y |> nm2mm)
                else
                    new Shift<mm>(my.BodyRect.Y |> nm2mm, my.BodyRect.X |> nm2mm)

            let c = 0.4
            let bodyWithLeads = my.BodyRect + (snd my.Leads)
            let bodyWithLeadsMm = Shift<nm>.nm2mm bodyWithLeads

            let bodyWithLeads_px =
                new Size(
                    bodyWithLeadsMm.X / (my.Scale.X * my.Binning)
                    |> strip_px,
                    bodyWithLeadsMm.Y / (my.Scale.Y * my.Binning)
                    |> strip_px
                )

            let heightGrowth, widthGrowth =
                bodyWithLeads.X * c, bodyWithLeads.Y * c

            let position = (my.Leads |> fst |> List.head).Position

            let wryVector =
                my.Leads
                |> fst
                |> List.tryFind (fun lead -> lead.Vector.X * lead.Vector.Y > 0.0<nm^2>)

            match position, my.NumOfLeads, wryVector with
            | LW _, 2, None ->
                let newHeight, newWidth =
                    if bodyWithLeads.X > bodyWithLeads.Y then
                        let a = bodyWithLeads.Y + heightGrowth in a, fp.X * a / fp.Y
                    else
                        let a = bodyWithLeads.X + widthGrowth in fp.Y * a / fp.X, a

                let ``size in mm`` =
                    new Shift<mm>(newWidth |> nm2mm, newHeight |> nm2mm)

                new Size(
                    ``size in mm``.X / (my.Scale.X * my.Binning)
                    |> strip_px,
                    ``size in mm``.Y / (my.Scale.Y * my.Binning)
                    |> strip_px
                ),
                fp,
                bodyWithLeadsMm,
                bodyWithLeads_px
            | _ ->
                let newHeight, newWidth =
                    if bodyWithLeads.X > bodyWithLeads.Y then
                        let a = bodyWithLeads.Y + heightGrowth in a, bodyWithLeads.X * a / bodyWithLeads.Y
                    else
                        let a = bodyWithLeads.X + widthGrowth in bodyWithLeads.Y * a / bodyWithLeads.X, a

                let ``size in mm`` =
                    new Shift<mm>(newWidth |> nm2mm, newHeight |> nm2mm)

                new Size(
                    ``size in mm``.X / (my.Scale.X * my.Binning)
                    |> strip_px,
                    ``size in mm``.Y / (my.Scale.Y * my.Binning)
                    |> strip_px
                ),
                ``size in mm``,
                bodyWithLeadsMm,
                bodyWithLeads_px

    let group2lead (aScale: float) (g: XPathNavigator) : Lead =
        let commonNodes =
            [| "@Pitch"
               "@VectorX"
               "@VectorY"
               "@Angle" |]
            |> Array.map (g.SelectSingleNode >> getSingleSafe)

        let nol =
            g.SelectSingleNode("@NumberOfLeads")
            |> getIntSafeN

        let pitch = on_nm commonNodes.[0]
        let orelse = new TypeOrElseBuilder()

        let leadNode, leadType =
            orelse {
                return! g.SelectSingleNode("LeadWraparound"), Wraparound
                return! g.SelectSingleNode("LeadGullwing"), Gullwing
                return! g.SelectSingleNode("LeadBlob"), LeadBlob
                return! g.SelectSingleNode("LeadJBend"), JBend
                return! g.SelectSingleNode("LeadPolygoncircle"), Polygoncircle
                return! g.SelectSingleNode("LeadCorner"), LeadCorner
                return! g.SelectSingleNode("LeadCenteringPin"), LeadCenteringPin

                return! null, Unknown
            }

        match leadType with
        | Unknown ->
            { number = nol
              numOfGrids = 0
              pitch = pitch
              groupPitch = 0.0<nm>
              Vector = new Shift<nm>(on_nm commonNodes.[1], on_nm commonNodes.[2])
              angle = commonNodes.[3] / aScale |> on_deg
              Position =
                  LW
                  <| { angle = on_deg <| 0.0
                       Size = new Shift<nm>(0.0<nm>, 0.0<nm>) } }
        //failwithf "Unknown lead type %s" g.InnerXml
        | LeadCorner ->
            let Nodes =
                [| "@X0"
                   "@X1"
                   "@X2"
                   "@X3"
                   "@Y0"
                   "@Y1"
                   "@Y2"
                   "@Y3" |]
                |> Array.map (leadNode.SelectSingleNode >> getSingleSafe)

            let p1 =
                new Shift<nm>(on_nm Nodes.[0], on_nm Nodes.[4])

            let p2 =
                new Shift<nm>(on_nm Nodes.[1], on_nm Nodes.[5])

            let p3 =
                new Shift<nm>(on_nm Nodes.[2], on_nm Nodes.[6])

            let p4 =
                new Shift<nm>(on_nm Nodes.[3], on_nm Nodes.[7])

            { number = nol
              numOfGrids = 0
              pitch = on_nm commonNodes.[0]
              groupPitch = 0.0<nm>
              Vector = new Shift<nm>(on_nm commonNodes.[1], on_nm commonNodes.[2])
              angle = commonNodes.[3] / aScale |> on_deg
              Position = LC <| (p1, p2, p3, p4) }
        | Polygoncircle ->
            let Nodes =
                [| "@Diameter" |]
                |> Array.map (leadNode.SelectSingleNode >> getSingleSafe)

            { number = nol
              numOfGrids = 0
              pitch = on_nm commonNodes.[0]
              groupPitch = 0.0<nm>
              Vector = new Shift<nm>(on_nm commonNodes.[1], on_nm commonNodes.[2])
              angle = commonNodes.[3] / aScale |> on_deg
              Position =
                  LW
                  <| { angle = on_deg <| 0.0
                       Size = new Shift<nm>(on_nm Nodes.[0], on_nm Nodes.[0]) } }
        | LeadCenteringPin ->
            let Nodes =
                [| "@SizeX"; "@SizeY" |]
                |> Array.map (leadNode.SelectSingleNode >> getSingleSafe)

            { number = nol
              numOfGrids = 0
              pitch = on_nm commonNodes.[0]
              groupPitch = 0.0<nm>
              Vector = new Shift<nm>(0.0<nm>, 0.0<nm>)
              angle = 0.0<deg>
              Position =
                  LW
                  <| { angle = 0.0<deg>
                       Size = new Shift<nm>(on_nm Nodes.[0], on_nm Nodes.[1]) } }
        | Wraparound
        | LeadBlob ->
            let Nodes =
                [| "@Angle"; "@SizeX"; "@SizeY" |]
                |> Array.map (leadNode.SelectSingleNode >> getSingleSafe)

            { number = nol
              numOfGrids = 0
              pitch = on_nm commonNodes.[0]
              groupPitch = 0.0<nm>
              Vector = new Shift<nm>(on_nm commonNodes.[1], on_nm commonNodes.[2])
              angle = commonNodes.[3] / aScale |> on_deg
              Position =
                  LW
                  <| { angle = on_deg <| Nodes.[0] / aScale
                       Size = new Shift<nm>(on_nm Nodes.[1], on_nm Nodes.[2]) } }
        | Gullwing
        | JBend ->
            let Nodes =
                [| "@Angle"
                   "@SizeX"
                   "@SizeY"
                   "@ContactX"
                   "@ContactY" |]
                |> Array.map (leadNode.SelectSingleNode >> getSingleSafe)

            { number = nol
              numOfGrids = 0
              pitch = pitch
              groupPitch = 0.0<nm>
              Vector = new Shift<nm>(on_nm commonNodes.[1], on_nm commonNodes.[2])
              angle = commonNodes.[3] / aScale |> on_deg
              Position =
                  LG
                  <| { angle = on_deg <| Nodes.[0] / aScale
                       Size = new Shift<nm>(on_nm Nodes.[1], on_nm Nodes.[2])
                       Contact = new Shift<nm>(on_nm Nodes.[3], on_nm Nodes.[4]) } }

    let grid2lead (aScale: float) (g: XPathNavigator) =
        let commonNodes =
            [| "@Pitch"
               "@VectorX"
               "@VectorY"
               "@Angle"
               "@GroupPitch" |]
            |> Array.map (g.SelectSingleNode >> getSingleSafe)

        let nol =
            g.SelectSingleNode("@NumberOfLeads")
            |> getIntSafeN

        let nog =
            g.SelectSingleNode("@NumberOfGroups")
            |> getIntSafeN

        let leadNode = g.SelectSingleNode("LeadBallCircle")
        let typeorelse = new TypeOrElseBuilder()
        let leadNode, pinType =
            typeorelse {
                return! g.SelectSingleNode("LeadBallCircle"), "LeadBallCircle"
                return! g.SelectSingleNode("LeadPinRectangle"), "LeadPinRectangle"
                return! null, String.Empty
            }

        let Nodes =
            match pinType with
            | "LeadBallCircle" -> [| "@Diameter"; "@Diameter" |] |> Array.map (leadNode.SelectSingleNode >> getSingleSafe)
            | "LeadPinRectangle" -> [| "@SizeX"; "@SizeY" |] |> Array.map (leadNode.SelectSingleNode >> getSingleSafe)
            | _ -> [|0.0; 0.0|]
                

        { number = nol
          numOfGrids = nog
          pitch = on_nm commonNodes.[0]
          groupPitch = on_nm commonNodes.[4]
          Vector = new Shift<nm>(on_nm commonNodes.[1], on_nm commonNodes.[2])
          angle = commonNodes.[3] / aScale |> on_deg
          Position =
              LGrid
              <| { angle = 0.0<deg>
                   Size = new Shift<nm>(on_nm Nodes.[0], on_nm Nodes.[1]) } }

    type Parser(ms: MemoryStream) =
        do ms.Position <- 0L
        let xreader = XmlReader.Create(ms)
        let xpathdocument = new XPathDocument(xreader)
        let xmldox = xpathdocument.CreateNavigator()
        member _.GetNode(path: string) = xmldox.SelectSingleNode path

        member p.GetNodeSafe(path: string) =
            let node = p.GetNode path in

            if isNull node then
                System.String.Empty
            else
                node.Value

        member _.Select(path: string) = xmldox.Select path

        member _.``get transform`` =
            let a =
                [| ("SiplaceVisionDump/Actual/PresentationPosition")
                   ("SiplaceVisionDump/Result/Position")
                   ("SiplaceVisionDump/Sensor/SIPLACEVision_Sensor/Transformation")
                   "SiplaceVisionDump/Images/Image[1]/Transformation"
                   "SiplaceVisionDump/DIPF"
                   "SiplaceVisionDump/Result/SpecificResult"
                   "SiplaceVisionDump/ContextFromStationSoftware0001" 
                   "SiplaceVisionDump/FileInfo0001" 
                |]
                |> Array.map (xmldox.SelectSingleNode)

            let PPNodes =
                [| "@X"
                   "@Y"
                   "@AngleRad"
                   "@Scale"
                   "@Z" |]
                |> Array.map (a.[0].SelectSingleNode >> getSingleSafe)

            let RNodes =
                [| "@X"; "@Y"; "@AngleRad"; "@Scale" |]
                |> Array.map (a.[1].SelectSingleNode >> getSingleSafe)

            let INodes =
                [| "@xu"
                   "@yu"
                   "@Binning"
                   "@xFlip"
                   "@yFlip" |]
                |> Array.map (a.[3].SelectSingleNode >> getSingleSafe)

            let typeorelse = new TypeOrElseBuilder()

            let bodyNodes, _ =
                typeorelse {
                    return! a.[4].SelectSingleNode("BodyRect"), ""
                    return! a.[4].SelectSingleNode("BodyVCyl"), ""
                    return! a.[4].SelectSingleNode("BodyHCyl"), ""
                    return! null, ""
                }
            //parse DIPF block
            let dNodes =
                [| "@SizeX"; "@SizeY" |]
                |> Array.map (bodyNodes.SelectSingleNode >> getSingleSafe)

            let scaleAngle =
                a.[4].SelectSingleNode("@ScaleAngle")
                |> getSingleSafe

            let chipSize =
                if not (isNull (a.[5])) then
                    [| "ChipSize/@sizeX"
                       "ChipSize/@sizeY" |]
                    |> Array.map (a.[5].SelectSingleNode >> getSingleSafe)
                else
                    [| 0.0; 0.0 |]

            let hashcode =
                if not (isNull (a.[6])) then
                    Parser.SubXml
                        a.[6].Value
                        "VisionDumpContext/LocationInfo/PosIdForGui/PosId/@hashkey; VisionDumpContext/ComponentInfo/@ComponentName"
                else
                    String.Empty

            let bodyRect =
                new Shift<nm>(on_nm dNodes.[0], on_nm dNodes.[1])

            let orelse = new GroupOrElseBuilder()

            let groupNodes, groupType =
                orelse {
                    return! a.[4].Select("Group"), Group
                    return! a.[4].Select("GroupGrid"), GroupGrid
                    return! null, NoGroup
                }

            let groups = 
                  match groupType with
                    | NoGroup -> []
                    | _ ->
                        ([ while groupNodes.MoveNext() do
                               yield groupNodes.Current.Clone() ])

            let leads =
                match groupType with
                | Group -> groups |> List.map (group2lead scaleAngle)
                | GroupGrid -> 
                    try
                        groups |> List.map (grid2lead scaleAngle)
                    with ex ->
                        printfn "%s %s Unknown grid type in the file xml %s" ex.Message ex.StackTrace a.[4].InnerXml
                        [{ number = 0
                           numOfGrids = 0
                           pitch = 0.0<nm>
                           groupPitch = 0.0<nm>
                           Vector = Shift<nm>(0.0<nm>,0.0<nm>)
                           angle = 0.0<deg>
                           Position = LW{ angle = 0.0<deg>; Size = Shift<nm>(0.0<nm>,0.0<nm>) } }]
                | NoGroup -> 
                    printfn "Unknown group type in the file xml %s" a.[4].InnerXml
                    [{ number = 0
                       numOfGrids = 0
                       pitch = 0.0<nm>
                       groupPitch = 0.0<nm>
                       Vector = Shift<nm>(0.0<nm>,0.0<nm>)
                       angle = 0.0<deg>
                       Position = LW{ angle = 0.0<deg>; Size = Shift<nm>(0.0<nm>,0.0<nm>) } }]

            let inflate =
                leads
                |> List.fold
                    (fun acc L ->
                        match L.Position with
                        | LGrid _
                        | LC _
                        | NaN -> (0.0<nm>, 0.0<nm>)
                        | LW lg ->
                            match deg2rad L.angle with
                            | Turn R ->
                                match R with
                                | RotateFlipType.RotateNoneFlipNone
                                | RotateFlipType.Rotate180FlipNone ->
                                    let ΔX =
                                        abs L.Vector.X + (lg.Size.Y - bodyRect.X) / 2.0

                                    let ΔY =
                                        abs L.Vector.Y + (lg.Size.X - bodyRect.Y) / 2.0

                                    (fst acc + max 0.0<nm> ΔX, snd acc + max 0.0<nm> ΔY)
                                | _ ->
                                    let ΔX =
                                        abs L.Vector.X + (lg.Size.X - bodyRect.X) / 2.0

                                    let ΔY =
                                        abs L.Vector.Y + (lg.Size.Y - bodyRect.Y) / 2.0

                                    (fst acc + max 0.0<nm> ΔX, snd acc + max 0.0<nm> ΔY)
                        | LG lg ->
                            match deg2rad L.angle with
                            | Turn R ->
                                match R with
                                | RotateFlipType.RotateNoneFlipNone
                                | RotateFlipType.Rotate180FlipNone ->
                                    let ΔX =
                                        abs L.Vector.X + (lg.Size.Y - bodyRect.X) / 2.0

                                    let ΔY =
                                        abs L.Vector.Y + (lg.Size.X - bodyRect.Y) / 2.0

                                    (fst acc + max 0.0<nm> ΔX, snd acc + max 0.0<nm> ΔY)
                                | _ ->
                                    let ΔX =
                                        abs L.Vector.X + (lg.Size.X - bodyRect.X) / 2.0

                                    let ΔY =
                                        abs L.Vector.Y + (lg.Size.Y - bodyRect.Y) / 2.0

                                    (fst acc + max 0.0<nm> ΔX, snd acc + max 0.0<nm> ΔY)

                        )
                    (0.0<nm>, 0.0<nm>)

            let binning =
                if INodes.[2] = 0.0 then
                    1.0
                else
                    INodes.[2]

            let svdmp_xml =
                System.Text.StringBuilder()
                    .Append("<SiplaceVisionDump>")
                    .Append(let node = xmldox.SelectSingleNode "SiplaceVisionDump/FileInfo0001" in (if not (isNull node) then node.OuterXml else String.Empty))
                    .Append(let node = xmldox.SelectSingleNode "SiplaceVisionDump/ContextFromStationSoftware0001" in (if not (isNull node) then node.OuterXml else String.Empty))
                    .Append(let node = xmldox.SelectSingleNode "SiplaceVisionDump/Sensor/SIPLACEVision_Sensor" in (if not (isNull node) then node.OuterXml else String.Empty))
                    .Append(let node = xmldox.SelectSingleNode "SiplaceVisionDump/Result" in (if not (isNull node) then node.OuterXml else String.Empty))
                    .Append("</SiplaceVisionDump>")
                    .ToString()

            { presentationAngle = PPNodes.[2] |> on_rad
              resultAngle = RNodes.[2] |> on_rad
              presentation =
                  new Shift<mm>(
                      (PPNodes.[0] / (0.016893 * binning) |> on_mm),
                      (PPNodes.[1] / (0.016894 * binning) |> on_mm)
                  )
              result = new Shift<mm>(RNodes.[0] / binning |> on_mm, RNodes.[1] / binning |> on_mm)
              Scale = new Shift<mm / px>(INodes.[0] * 1.0<mm/px>, INodes.[1] * 1.0<mm/px>)
              BodyRect = new Shift<nm>(bodyRect.X, bodyRect.Y)
              Flip = new PointF(float32 INodes.[3], float32 INodes.[4])
              Binning = binning
              NumOfLeads = leads |> List.sumBy (fun L -> L.number)
              Leads = leads, new Shift<nm>(fst inflate, snd inflate)
              ChipSize = new Shift<mm>(on_mm chipSize.[0], on_mm chipSize.[1])
              ComponentName = a.[4].SelectSingleNode("@Name").Value
              HashCode = hashcode 
              DipfType = a.[7].SelectSingleNode("@DipfType").Value
              Svdmp = svdmp_xml}

        static member public SubXml (s: string) (path: string) =
            let xmldox = new XmlDocument()
            let mutable s1 = s.Replace("\r\n", " ")

            for _ = 0 to 5 do
                s1 <- s1.Replace("  ", " ")

            s1 <- s1.Replace("< ", "<")
            let pairs = path.Split(';')
            let sb = new System.Text.StringBuilder()
            let mutable value = String.Empty

            try
                xmldox.LoadXml(s1)

                for p in pairs do
                    try
                        let node = xmldox.SelectSingleNode(p)

                        value <-
                            if isNull node then
                                "no"
                            else
                                node.InnerText
                    with
                    | ex -> value <- "no"

                    let output = value
                    sb.AppendLine(output + ";") |> ignore
            with
            | _ -> ()

            sb.ToString().Replace("\r\n", String.Empty)

        member _.Dispose() = xreader.Dispose()

    let sh = 64uy

    let DecodeBmpFromString (encodedStr: string) imageWidth imageHeight bpp =
        let area = imageWidth * imageHeight * bpp
        let bytes: byte [] = Array.zeroCreate encodedStr.Length
        let baa: byte [] = Array.zeroCreate area

        for i = 0 to encodedStr.Length - 1 do
            bytes.[i] <- Convert.ToByte(encodedStr.[i]) - sh

        let lastI = area - 1
        let mutable col1 = 0uy
        let mutable col2 = col1
        let mutable col3 = col1
        let mutable j = 0

        for i in 0 .. 4 .. (bytes.Length - 1) do
            col1 <- bytes.[i] <<< 2
            col1 <- col1 + (bytes.[i + 1] >>> 4)

            col2 <- bytes.[i + 1] <<< 4
            col2 <- col2 + ((bytes.[i + 2]) >>> 2)

            col3 <- bytes.[i + 2] <<< 6
            col3 <- col3 + bytes.[i + 3]

            for k = 0 to bpp - 1 do
                baa.[j + k] <- col1

            if j + (2 * bpp - 1) <= lastI then
                for k = bpp to 2 * bpp - 1 do
                    baa.[j + k] <- col2

                if j + (3 * bpp - 1) <= lastI then
                    for k = 2 * bpp to 3 * bpp - 1 do
                        baa.[j + k] <- col3

            j <- j + 3 * bpp

        baa

    let arrayToImage imageWidth imageHeight (bytes: byte []) =
        let bmp =
            new Bitmap(imageWidth, imageHeight, Imaging.PixelFormat.Format8bppIndexed)

        let _palette = bmp.Palette
        let _entries = _palette.Entries

        for i = 0 to 255 do
            let b = Color.FromArgb(i, i, i)
            _entries.[i] <- b

        bmp.Palette <- _palette

        let bmpData =
            bmp.LockBits(new Rectangle(0, 0, imageWidth, imageHeight), Imaging.ImageLockMode.WriteOnly, bmp.PixelFormat)

        for i = 0 to imageHeight - 1 do
            System.Runtime.InteropServices.Marshal.Copy(
                bytes,
                i * imageWidth,
                bmpData.Scan0 + IntPtr(i * bmpData.Stride),
                imageWidth
            )

        bmp.UnlockBits(bmpData)
        bmp

    let fitToBitmapD (img: System.Drawing.Bitmap) (rect: Rectangle) =
        let x = rect.X |> min (img.Width - 1) |> max 0
        let y = rect.Y |> min (img.Height - 1) |> max 0

        let w =
            if rect.Right > img.Width - 1 then
                img.Width - x
            else
                rect.Width

        let h =
            if rect.Bottom > img.Height - 1 then
                img.Height - y
            else
                rect.Height

        new System.Drawing.Rectangle(x, y, min (max w 1) img.Width, min (max h 1) img.Height)
