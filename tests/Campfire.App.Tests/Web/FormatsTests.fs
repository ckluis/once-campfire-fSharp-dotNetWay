// plans/design-kit-auth.md section 4, unit 2.7: the kit's golden cases for `request.formats` and `respond_to`
// (`FormatTests`), re-pointed at `Web.Formats`. The two groups the framework's Accept parser does not reproduce are
// class B (README Known differences) and are pinned here as what the framework does: Ruby's `to_f` reading of odd
// `q` values and `text/xml` folding.
module Campfire.App.Tests.Web.FormatsTests

open Microsoft.AspNetCore.Http
open Xunit
open Campfire.App.Web

let private request (accept: string | null) (path: string) (xhr: bool) (query: string) : HttpContext =
    let http = DefaultHttpContext()
    http.Request.Path <- PathString path
    http.Request.QueryString <- QueryString query
    if not (isNull accept) then http.Request.Headers.Accept <- accept
    if xhr then http.Request.Headers["X-Requested-With"] <- "XMLHttpRequest"
    http

let private symbols (result: Result<Format list, Error>) : string list =
    match result with
    | Ok formats -> formats |> List.map Formats.symbol
    | Error e -> failwith $"{e}"

let private fmts (accept: string | null) (path: string) (xhr: bool) = symbols (Formats.requested (request accept path xhr ""))

[<Fact>]
let ``browser accept falls back to html`` () =
    let chrome = "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8"
    Assert.Equal<string list>([ "html" ], fmts chrome "/rooms/1" false)
    Assert.Equal<string list>([ "html" ], fmts null "/rooms/1" false)
    Assert.Equal<string list>([ "*/*" ], fmts "*/*" "/rooms/1" false)

[<Fact>]
let ``turbo form submission`` () =
    let turbo = "text/vnd.turbo-stream.html, text/html, application/xhtml+xml"
    Assert.Equal<string list>([ "turbo_stream"; "html" ], fmts turbo "/rooms/1/messages" false)

[<Fact>]
let ``quality ordering`` () =
    Assert.Equal<string list>([ "json"; "html" ], fmts "text/html;q=0.5, application/json" "/" false)
    Assert.Equal<string list>([ "html" ], fmts "application/json, */*" "/" false)
    Assert.Equal<string list>([ "html" ], fmts "*/*, application/json;q=0.1" "/" false)
    Assert.Equal<string list>([ "json"; "html" ], fmts "application/json;q=0.1,text/html;q=0.1" "/" false)

[<Fact>]
let ``q values the framework reads are Rails' q values`` () =
    for accept, order in
        [ "text/html;q=0.5, application/json;q=0.6", [ "json"; "html" ]
          "text/html;q=0.9, application/json;q=0.5", [ "html"; "json" ]
          "text/html;q=1, application/json;q=0", [ "html"; "json" ]
          "application/json;q=0, text/html;q=0.001", [ "html"; "json" ]
          "text/html;q=1e-1, application/json;q=0.5", [ "html"; "json" ] ] do
        // The last case is class B: the framework does not read exponents, so the item loses its q and keeps 1.0.
        Assert.True((order = symbols (Formats.parseAccept accept)), $"{accept}: got {symbols (Formats.parseAccept accept)}")
    Assert.Equal<string list>([ "json"; "*/*" ], symbols (Formats.parseAccept "application/json;q=0.5, */*"))

[<Fact>]
let ``q values Ruby reads and the framework does not are class B`` () =
    // Rails' `Mime::Type.parse` reads `q=` with `String#to_f` (`abc` is 0.0, so json first); the framework drops a `q`
    // it cannot read and the item keeps 1.0 (html first).
    Assert.Equal<string list>([ "html"; "json" ], symbols (Formats.parseAccept "text/html;q=abc, application/json;q=0.1"))
    Assert.Equal<string list>([ "html"; "json" ], symbols (Formats.parseAccept "text/html;q=1e17, application/json;q=1e18"))

[<Fact>]
let ``single types`` () =
    Assert.Equal<string list>([ "json" ], fmts "application/json" "/" false)
    Assert.Equal<string list>([ "json" ], fmts "application/json; charset=utf-8" "/" false)
    Assert.Equal<string list>([ "svg" ], fmts "image/svg+xml" "/" false)
    Assert.Equal<string list>([], fmts "application/x-unknown" "/" false)
    Assert.True((Formats.requested (request "garbage" "/" false "")).IsError)

[<Fact>]
let ``wildcards expand in registration order`` () =
    Assert.Equal<string list>([ "html"; "text"; "js" ], fmts "text/*" "/" false |> List.truncate 3)
    Assert.Contains("turbo_stream", fmts "text/*" "/" false)

[<Fact>]
let ``xml: text xml is application xml, and folding is class B`` () =
    Assert.Equal<string list>([ "xml" ], fmts "text/xml" "/" false)
    Assert.Equal<string list>([ "xml"; "rss" ], fmts "text/xml, application/rss+xml" "/" false)
    // Rails moves `application/xml` after a `+xml` type of the same quality listed later; the framework's order is the
    // header's.
    Assert.Equal<string list>([ "xml"; "rss" ], fmts "application/xml, application/rss+xml" "/" false)

[<Fact>]
let ``path extension and format param`` () =
    Assert.Equal<string list>([ "svg" ], fmts null "/users/1/avatar.svg" false)
    Assert.Equal<string list>([ "json" ], fmts null "/messages.json" false)
    Assert.Equal<string list>([ "html" ], fmts null "/x.unknownext" false)
    Assert.Equal<string list>([ "json" ], symbols (Formats.requested (request "text/html" "/" false "?format=json")))
    Assert.Empty(symbols (Formats.requested (request null "/" false "?format=nope")))

[<Fact>]
let ``xhr defaults to js`` () =
    Assert.Equal<string list>([ "js" ], fmts null "/" true)
    let http = request null "/" true ""
    http.Request.ContentType <- "application/json"
    Assert.Equal<string list>([ "json" ], symbols (Formats.requested http))

[<Fact>]
let ``negotiation`` () =
    let negotiate formats order = Formats.negotiate formats order
    Assert.Equal(Some Html, negotiate [ TurboStream; Html ] [ Html; Json ])
    Assert.Equal(Some Html, negotiate [ Any ] [ Html; Json ])
    Assert.Equal(None, negotiate [ Other "png" ] [ Html; Json ])
    Assert.Equal(Some(Other "png"), negotiate [ Other "png" ] [ Html; Any ])
    Assert.Equal(None, negotiate [] [ Html ])

[<Fact>]
let ``respond_to answers 406 for a format nobody offers, and turbo frames are the header`` () =
    let turbo = request "text/vnd.turbo-stream.html, text/html" "/" false ""
    Assert.Equal(Ok TurboStream, Formats.respondTo turbo [ TurboStream; Html ])
    Assert.Equal(Ok Html, Formats.respondTo turbo [ Html ])
    Assert.Equal(Ok Json, Formats.respondTo (request "*/*" "/" false "") [ Json; Html ])
    match Formats.respondTo (request "application/json" "/" false "") [ Html ] with
    | Error UnknownFormat -> ()
    | other -> failwith $"{other}"
    Assert.False(Formats.isTurboFrame turbo)
    turbo.Request.Headers["Turbo-Frame"] <- "messages"
    Assert.True(Formats.isTurboFrame turbo)
