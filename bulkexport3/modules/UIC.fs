namespace Bulkexport

open System.IO
open System.Drawing
open DSL

module UIC =
    [<Literal>]
    let RESOLUTION = 59.24

    [<Literal>]
    let EXPANSION_COEFF = 1.5

    let expansion = (*) EXPANSION_COEFF >> int

    let stripOption xRes =
        match xRes with
        | Some res -> res
        | _ -> RESOLUTION

    ///Check the measurmement equals some value within tolerance.
    let cptol tol measure1 value2 = abs (measure1 - value2) < tol

    let selectFloatNode = Funcs.selectNode float
    let selectBoolNode = Funcs.selectNode (System.Boolean.Parse)
    let selectStringNode = Funcs.selectNode id

    type Parser(bmpStream: MemoryStream, xmlStream: MemoryStream) as p =
        [<DefaultValue>]
        val mutable canProceed: bool

        do
            bmpStream.Position <- 0L
            xmlStream.Position <- 0L
            p.canProceed <- true

        let xmlString =
            use stringReader =
                new StreamReader(xmlStream, System.Text.Encoding.UTF8, false, 1024, leaveOpen = true)

            stringReader.ReadToEnd()
        //find resolution
        let nodes =
            [| for tag in [ "PelSizeY"
                            "PelSizeX"
                            "PhysOrient>"
                            "XComponentSize" ] do
                   yield selectFloatNode xmlString 0 tag |]

        let handednessFlip =
            let ho, _ =
                selectBoolNode xmlString 0 "HandednessFlip"

            match ho with
            | Some handedness -> handedness
            | _ -> false

        let resolution =
            (nodes.[1] |> fst |> stripOption, nodes.[0] |> fst |> stripOption)

        let xComponentSize, nextIdx = nodes.[3]

        let yComponentSize, nextIdx =
            selectFloatNode xmlString nextIdx "YComponentSize"

        let xExpect, nextIdx =
            selectFloatNode xmlString nextIdx "XExpected"

        let yExpect, nextIdx =
            selectFloatNode xmlString nextIdx "YExpected"

        let thetaExpect, nextIdx =
            selectFloatNode xmlString nextIdx "ThetaExpected"

        let xShift, nextIdx =
            selectFloatNode xmlString nextIdx "XFound"

        let yShift, nextIdx =
            selectFloatNode xmlString nextIdx "YFound"

        let angleSearch, _ =
            selectFloatNode xmlString nextIdx "AngleFound"

        let resultCode, _nextIdx =
            selectStringNode xmlString nextIdx "ResultCode"

        let xComponentSizePx =
            match xComponentSize with
            | Some size -> expansion (size / fst resolution)
            | _ ->
                let xBodySize, _ = selectFloatNode xmlString 0 "XBodySize"

                match xBodySize with
                | Some size -> expansion (size / fst resolution)
                | _ ->
                    p.canProceed <- false
                    0

        do
            match resultCode with
            | None -> p.canProceed <- false
            | Some result -> p.canProceed <- p.canProceed && result = "PCVIS_SUCCESS"

        member _.Orientation =
            match fst nodes.[2] with
            | Some alpha -> alpha * OneDeg
            | _ -> ZeroDeg

        member my.XComponentSizePx = xComponentSizePx

        member my.YComponentSizePx =
            match yComponentSize with
            | Some size -> expansion (size / snd resolution)
            | _ ->
                let yBodySize, _ = selectFloatNode xmlString 0 "YBodySize"

                match yBodySize with
                | Some size -> expansion (size / snd resolution)
                | _ ->
                    my.canProceed <- false
                    0
        ///Expected angle
        member _.Theta =
            match thetaExpect with
            | Some alpha -> alpha / 1000.0 * OneDeg
            //* (if handednessFlip then -1.0 else 1.0)
            | _ -> ZeroDeg

        member my.XShiftPx =
            match xExpect, xShift with
            | Some shift, Some expect -> (shift + expect) / fst resolution |> int
            | _ ->
                my.canProceed <- false
                0

        member my.YShiftPx =
            match yExpect, yShift with
            | Some shift, Some expect -> (shift + expect) / snd resolution |> int
            | _ ->
                my.canProceed <- false
                0
        ///Angle correction
        member my.Angle =
            match angleSearch with
            | Some a -> a / 1000.0 * OneDeg
            | _ ->
                my.canProceed <- false
                ZeroDeg

        member p.Cropped =
            if p.canProceed then
                use bitmap = new Bitmap(bmpStream)

                let cropRect =
                    let turn =
                        -(p.Theta + p.Angle) |> (deg2rad >> off 1.0<rad>)

                    let flip =
                        if handednessFlip then
                            if cptol OneDeg p.Orientation StraightDeg then
                                (-1, 1)
                            else
                                (1, -1)
                        else if (List.exists (cptol OneDeg p.Theta) [ ZeroDeg; StraightDeg ]) //then
                                && cptol OneDeg p.Orientation StraightDeg then
                            (-1, -1)
                        else
                            (1, 1)

                    let location =
                        new Point(
                            (bitmap.Width - p.XComponentSizePx) / 2
                            + p.XShiftPx * fst flip,
                            (bitmap.Height - p.YComponentSizePx) / 2
                            + snd flip * p.YShiftPx
                        )

                    let size =
                        new Size(p.XComponentSizePx, p.YComponentSizePx)

                    new Rectangle(location, size)
                    |> Funcs.rotateRectangle turn


                //rotatedR, sprintf " (%i %i %f %f)" (fst flip) (snd flip) p.Theta p.Orientation
                //rotatedR
                //new Rectangle(
                //    int rX  + rotatedR.X,
                //    int rY  + rotatedR.Y,
                //    rotatedR.Width,
                //    rotatedR.Height
                //)

                cropRect
                |> (Region >> extract from bitmap >> Some)
            else
                None
