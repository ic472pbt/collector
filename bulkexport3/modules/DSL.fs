namespace Bulkexport

module DSL =
    open System
    open System.IO
    open System.Drawing
    //DSL -->
    [<Measure>]
    type b

    [<Measure>]
    type Mb

    [<Measure>]
    type deg

    [<Literal>]
    ///1 degree
    let OneDeg = 1.0<deg>

    [<Literal>]
    ///0 degrees
    let ZeroDeg = 0.0<deg>

    [<Literal>]
    ///180 degrees
    let StraightDeg = 180.0<deg>

    [<Measure>]
    type rad

    let inline onb x = x * 1L<b>
    let inline b2Mb x = x / 1048576L<b> * 1L<Mb>
    let inline deg2rad (x: float<deg>) = x / 180.0<deg> * Math.PI * 1.0<rad>
    let inline rad2deg (x: float<rad>) = x * 180.0<deg> / (Math.PI * 1.0<rad>)
    let inline off x arg = arg / x
    ///Path.Combine
    let (^/) a b = Path.Combine(a, b)
    ///Path.GetFileName
    let (!/) (a: string) = Path.GetFileName a
    ///Path.GetDirectoryName
    let (!//) (a: string) = Path.GetDirectoryName a
    ///Path.GetExtension
    let (!.) (a: string) = Path.GetExtension a
    ///Increment operator
    let inline (++) a = incr a

    // fsharplint:disable-next-line Hints
    let inline isNull obj = obj = null
    let from = ()
    let on condition run argument = if condition argument then run argument
    let ``copy file back`` source destination = File.Copy(source, destination)

    let fitToBitmap (img: Image) (rect: Rectangle) =
        let shrink =
            new Point((if rect.X < 0 then -rect.X else 0), (if rect.Y < 0 then -rect.Y else 0))

        let press =
            new Point(
                (if rect.Right > img.Width - 1 then
                     rect.Right - img.Width + 1
                 else
                     0),
                if rect.Bottom > img.Height - 1 then
                    rect.Bottom - img.Height + 1
                else
                    0
            )

        let squeeze =
            new Point(max press.X shrink.X, max press.Y shrink.Y)

        let x = rect.X + squeeze.X // |> (min img.Width >> max 0)
        let y = rect.Y + squeeze.Y //|> (min img.Height >> max 0)

        let w = rect.Width - 2 * squeeze.X
        //if rect.Right > img.Width - 1 then
        //    img.Width - x
        //else
        //    rect.Width

        let h = rect.Height - 2 * squeeze.Y
        //if rect.Bottom > img.Height - 1 then
        //    img.Height - y
        //else
        //    rect.Height

        new Rectangle(x, y, min (max w 0) img.Width, min (max h 0) img.Height)

    type WhatToExtract =
        | Itself
        | Region of Rectangle

    let rectangle (bmp: Bitmap) =
        new Rectangle(0, 0, bmp.Width, bmp.Height)

    let rec extract from (what: Bitmap) =
        function
        | Itself -> what.Clone(what |> rectangle, what.PixelFormat)
        | Region r ->
            let fit = fitToBitmap what r in

            if fit.Width * fit.Height = 0 then
                extract from what Itself
            else
                what.Clone(fit, what.PixelFormat)

    let partition predicate L =
        let rec innerLoop T F =
            function
            | [] -> T, F
            | h :: t ->
                let nT, nF =
                    if predicate h then
                        (h :: T), F
                    else
                        T, (h :: F)

                innerLoop nT nF t

        innerLoop [] [] L

//<-- DSL
