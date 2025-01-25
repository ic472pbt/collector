module TestBulkexport

open NUnit.Framework
open FsCheck.NUnit
open System
open System.IO
open Bulkexport.Funcs
open System.Drawing

let π = Math.PI

[<SetUp>]
let Setup () = ()

[<Property>]
let ``erroneous svdmps filtering`` prefixGen postfixGen erroneous s =
    let prefix, postfix = abs prefixGen + 5, abs postfixGen + 5
    let ms = new MemoryStream()
    ms.Write(Array.replicate prefix 33uy, 0, prefix)
    let stringWriter = new StreamWriter(ms)

    if erroneous then
        stringWriter.Write ErrorNeedles.[if s then 1 else 0]
        stringWriter.Flush()

    ms.Write(Array.replicate postfix 33uy, 0, postfix)
    (postfix <= 493 && erroneous) = erroneousSvdmp ms || isNozzle ms

[<TestFixture>]
type RotatePointTest() =
    [<Test>]
    member _.``rotate 4 points counter clockwise by 90deg``() =
        let toInt t =
            (fst t) * 1.0F |> int, (snd t) * 1.0F |> int

        let points =
            [ (0.0F, 1.0F)
              (1.0F, 0.0F)
              (0.0F, -1.0F)
              (-1.0F, 0.0F) ]

        let expected =
            points.Tail @ [ points.Head ] |> List.map toInt

        let rotatedPoints = List.map (rotate (-π / 2.0)) points
        let pointsInt = rotatedPoints |> List.map toInt
        Assert.IsTrue(List.forall2 ((=)) pointsInt expected)

[<TestFixture>]
type RotateRectangleTest() =
    [<Property>]
    let ``conservation of area`` w h =
        let r =
            new Rectangle(0, 0, abs w + 1, abs h + 1)

        let rotated = r |> rotateRectangle (π / 2.0)
        r.Width * r.Height = rotated.Height * rotated.Width

[<TestFixture>]
type SelectNodeTest() =
    let genTestString (prefix: string) (suffix: string) (needle: float) (tagName) =
        let sb = new System.Text.StringBuilder()

        let stack =
            sb
                .Append(prefix)
                .Append("<tag>")
                .AppendFormat("{0}", needle.ToString().Replace(",", "."))
                .Append("</tag>")
                .Append(suffix)
                .ToString()

        match fst (selectNode float stack 0 tagName) with
        | Some F -> F = needle
        | None -> false

    [<Property>]
    let ``extract float from tag`` prefix suffix (needle: float) =
        if Double.IsFinite needle then
            genTestString prefix suffix needle "tag"
        else
            true

    [<Property>]
    let ``extract float from tag with end bracket`` prefix suffix (needle: float) =
        if Double.IsFinite needle then
            genTestString prefix suffix needle "tag>"
        else
            true
