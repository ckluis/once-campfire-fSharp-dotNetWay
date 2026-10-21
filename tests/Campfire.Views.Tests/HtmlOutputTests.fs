// plans/design-views.md section 5, O4 (the slot rule and the parts) and P2 (the allocation bound of a page in parts).
module Campfire.Views.Tests.HtmlOutputTests

open System
open System.Buffers
open System.Text
open Falco.Markup
open Xunit
open Campfire.Views
open Campfire.Views.Markup
open Campfire.Views.Tests.GoldenB
open Campfire.Views.Templates
open Campfire.Views.Differential.Inputs

let private fragment (text: string) = CachedFragment text

let private page (fragments: CachedFragment seq) (nodes: XmlNode list) : HtmlOutput =
    HtmlOutput.ofNode (Elem.div [] nodes) fragments

[<Fact>]
let ``O4 a page in parts is its text and its fragments in order`` () =
    let one = fragment "<b>one</b>"
    let two = fragment "<i>two</i>"
    let output = page [ one; two ] [ Html.text "a"; Html.fragment one; Html.text "b"; Html.fragment two; Html.text "c" ]
    Assert.Equal(3, output.Parts.Length)
    Assert.Equal(2, output.Fragments.Length)
    Assert.Same(one, output.Fragments[0])
    Assert.Same(two, output.Fragments[1])
    Assert.Equal("<div>a<b>one</b>b<i>two</i>c</div>", output.ToString())
    Assert.Equal<byte[]>(Encoding.UTF8.GetBytes(output.ToString()), output.ToArray())
    Assert.Equal(output.Length, output.ToArray().Length)
    let writer = ArrayBufferWriter<byte>()
    output.WriteTo writer
    Assert.Equal<byte[]>(output.ToArray(), writer.WrittenSpan.ToArray())

[<Fact>]
let ``O4 U+E000 and a typed slot in text or an attribute value leave the part count unchanged`` () =
    let one = fragment "<b>one</b>"
    let hostile = [ ""; "<0>"; "<" + string one.Id + ">"; "x<" + string one.Id + ">y" ]
    for value in hostile do
        let output =
            page [ one ] [ Elem.span [ Html.attr "title" value ] [ Html.text value ]; Html.fragment one; Html.trusted (Html.Trusted.ofAssetTags "") ]
        Assert.Equal(2, output.Parts.Length)
        Assert.Equal(1, output.Fragments.Length)
        let text = output.ToString()
        Assert.DoesNotContain("<", text)
    // A U+E000 that follows no `<` is text, even in trusted markup.
    let trusted = HtmlOutput.ofNode (Html.trusted (Html.Trusted.ofAssetTags "a1>b")) Seq.empty
    Assert.Single trusted.Parts |> ignore
    Assert.Equal("a1>b", trusted.ToString())

[<Fact>]
let ``O4 a fragment placed and not passed, passed and not placed, or placed twice throws`` () =
    let one = fragment "<b>one</b>"
    let two = fragment "<i>two</i>"
    Assert.Throws<InvalidOperationException>(fun () -> page [] [ Html.fragment one ] |> ignore) |> ignore
    Assert.Throws<InvalidOperationException>(fun () -> page [ one; two ] [ Html.fragment one ] |> ignore) |> ignore
    Assert.Throws<InvalidOperationException>(fun () -> page [ one ] [ Html.fragment one; Html.fragment one ] |> ignore) |> ignore

[<Fact>]
let ``O4 the digest differs when one byte of one part or one fragment differs`` () =
    let bytes (text: string) = Encoding.UTF8.GetBytes text
    let one = fragment "<b>one</b>"
    let same = fragment "<b>one</b>"
    let other = fragment "<b>onE</b>"
    let make (first: string) (fragment: CachedFragment) = HtmlOutput([| bytes first; bytes "tail" |], [| fragment |])
    let digest = (make "head" one).Digest()
    Assert.Equal<byte[]>(digest, (make "head" same).Digest())
    Assert.NotEqual<byte[]>(digest, (make "heaD" one).Digest())
    Assert.NotEqual<byte[]>(digest, (make "head" other).Digest())
    // Where the boundary falls is part of the page's identity.
    let shifted = HtmlOutput([| bytes "hea"; bytes "dtail" |], [| one |])
    Assert.NotEqual<byte[]>(digest, shifted.Digest())

[<Fact>]
let ``O4 Legacy.inLayout keeps the fragments of a Falco page in the writer's layouts`` () =
    let g = golden "rooms_show_member"
    let one = CachedFragment(String('m', 2000))
    let regions: Layouts.Regions =
        { Layouts.Regions.content [ Elem.div [] [ Html.fragment one ] ] with
            Title = Some "T"
            Head = [ Elem.meta [ Attr.name "x"; Attr.content "y" ] ] }
    let viewPage: Layouts.ViewPage = { Regions = regions; Fragments = [ one ] }
    for frame in [ false; true ] do
        let output = Legacy.inLayout g.Context frame viewPage
        Assert.Equal(1, output.Fragments.Length)
        Assert.Same(one, output.Fragments[0])
        let text = output.ToString()
        Assert.Contains("<div>" + String('m', 2000) + "</div>", text)
        Assert.Contains("<meta name=\"x\" content=\"y\" />", text)
        Assert.Equal(frame, text.StartsWith "<html>")
    Assert.Throws<InvalidOperationException>(fun () ->
        Legacy.inLayout g.Context false { viewPage with Fragments = [ one; CachedFragment "x" ] } |> ignore)
    |> ignore

/// P2: the warm room page of 40 fragments, rendered and written in process, allocates under 300 KB (one string per page would be 1.6 MB).
[<Fact>]
let ``P2 a warm room page of forty fragments is rendered and written in under 300 KB`` () =
    let g = golden "rooms_show_closed"
    let ctx = g.Context
    let show = showView g.Input
    let show = { show with Messages = [ for _ in 1..4 -> show.Messages ] |> List.concat |> List.truncate 40 }
    Assert.Equal(40, show.Messages.Length)
    let cache = FragmentCache FragmentCacheLimits.DefaultMaxBytes
    let size = RenderSize()
    let writer = ArrayBufferWriter<byte>(2 * 1024 * 1024)
    let once () =
        FragmentCache.withCache cache (fun () ->
            let output = size.Render(fun w -> Rooms.ShowPage.render w ctx show)
            output.Digest() |> ignore
            writer.ResetWrittenCount()
            output.WriteTo writer
            output)
    let warm = once ()
    for _ in 1..50 do
        once () |> ignore
    Assert.True(warm.Fragments.Length >= 13, $"{warm.Fragments.Length} fragments")
    let rounds = 100
    GC.Collect()
    let before = GC.GetAllocatedBytesForCurrentThread()
    for _ in 1..rounds do
        once () |> ignore
    let perPage = (GC.GetAllocatedBytesForCurrentThread() - before) / int64 rounds
    Assert.True(perPage < 300L * 1024L, $"{perPage} bytes allocated per page")
