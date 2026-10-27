// Port of the tests of rust/crates/campfire/src/integrations/opengraph/{html,document,metadata}.rs and of
// opengraph/tests.rs
module Campfire.App.Tests.OpengraphTests

open System
open System.Collections.Generic
open System.IO
open System.IO.Compression
open System.Net
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging.Abstractions
open Xunit
open Campfire.App.Integrations
open Campfire.App.Tests.IntegrationsSupport
open Campfire.App.Tests.Support

// --- html.rs -------------------------------------------------------------------------------------------------------

let private title (html: string) : string option =
    OpengraphDocument.metaElements ("<meta charset=utf-8>" + html)
    |> List.tryFindBack (fun m -> m.Attr "property" = Some "og:title")
    |> Option.bind (fun m -> m.Attr "content" |> Option.filter (fun c -> c <> ""))

let private titleOf (content: string) : string option =
    title $"<meta property=\"og:title\" content=\"a{content}b\">"

/// OG3, class B (README): what libxml2 (the reference's Nokogiri 1.19.4) answered where AngleSharp, which follows the HTML5 spec, answers
/// otherwise. Listed by input, with AngleSharp's answer; a listed probe that stops differing, or one unlisted that starts to, fails.
let private classBReferences: (string * string option) list =
    [ "&AMP;", Some "a&b"
      "&#128;", Some "a\u20acb"
      "&#150;", Some "a\u2013b"
      "&#xD800;", Some "a\ufffdb"
      "&#1114112;", Some "a\ufffdb"
      "&#;", Some "a&#;b"
      "&#x;", Some "a&#x;b" ]

/// Probed against the reference's Nokogiri 1.19.4 (the expectations are libxml2's).
let private referenceProbes: (string * string) list =
        [ "&apos;", "a'b"
          "&eacute", "a&eacuteb"
          "&eacute;x", "aéxb"
          "&#233", "aéb"
          "&#233x", "aéxb"
          "&#xE9", "aປ"
          "&#xe9;", "aéb"
          "&AMP;", "a&AMP;b"
          "&Eacute;", "aÉb"
          "&unknown;", "a&unknown;b"
          "& x", "a& xb"
          "&#65;&#x41;", "aAAb"
          "&#128;", "a\u0080b"
          "&#150;", "a\u0096b"
          "&#xD800;", "a"
          "&#1114112;", "a"
          "&lt", "a&ltb"
          "&amp;amp;", "a&amp;b"
          "&hellip;", "a…b"
          "&nbsp", "a&nbspb"
          "&#;", "a"
          "&#x;", "a" ]

[<Fact>]
let ``decodes references like libxml2 but for the class B probes`` () =
    let classB = Map.ofList classBReferences
    for (reference, expected) in referenceProbes do
        match classB.TryFind reference with
        | Some angleSharp ->
            Assert.True((titleOf reference = angleSharp), $"{reference}: expected {angleSharp} but got {titleOf reference}")
            Assert.True((titleOf reference <> Some expected), $"{reference} is listed as differing but now answers {expected} as libxml2 does")
        | None -> Assert.True((titleOf reference = Some expected), $"{reference}: expected {expected} but got {titleOf reference}")

let private classBTokens: (string * string option) list =
    [ "<textarea><meta property=\"og:title\" content=\"in textarea\"></textarea>", None
      "<title><meta property=\"og:title\" content=\"in title\"></title>", None
      "<template><meta property=\"og:title\" content=\"in template\"></template>", None
      "<meta property=\"og:title\" content=\"line1\r\nline2\">", Some "line1\nline2"
      "<meta/property=\"og:title\"/content=\"slashes\">", Some "slashes"
      "<meta property=\"og:title\" content=\"unterminated>", None
      "<meta property=\"og:title\" content=\"\u0000nul\">", Some "\ufffdnul" ]

let private tokenizerProbes: (string * string option) list =
        [ "<script><meta property=\"og:title\" content=\"in script\"></script><meta property=\"og:title\" content=\"after\">", Some "after"
          "<style><meta property=\"og:title\" content=\"in style\"></style>", None
          "<textarea><meta property=\"og:title\" content=\"in textarea\"></textarea>", Some "in textarea"
          "<title><meta property=\"og:title\" content=\"in title\"></title>", Some "in title"
          "<noscript><meta property=\"og:title\" content=\"in noscript\"></noscript>", Some "in noscript"
          "<template><meta property=\"og:title\" content=\"in template\"></template>", Some "in template"
          "<svg><meta property=\"og:title\" content=\"in svg\"></svg>", Some "in svg"
          "<!-- <meta property=\"og:title\" content=\"comment\"> --><p>", None
          "<meta property=og:title content=unquoted>", Some "unquoted"
          "<meta property=\"og:title\" content=\"line1\r\nline2\">", Some "line1\r\nline2"
          "<meta property='og:title' content='single'>", Some "single"
          "<meta property = \"og:title\" content = \"spaced\">", Some "spaced"
          "<META PROPERTY=\"og:title\" CONTENT=\"upper\">", Some "upper"
          "<meta property=\"og:title\"content=\"nospace\">", Some "nospace"
          "<meta/property=\"og:title\"/content=\"slashes\">", None
          "<meta property=\"og:title\" content=\"<b>tag</b>\">", Some "<b>tag</b>"
          "<meta property=\"og:title\" content=\"a\">b\">", Some "a"
          "<meta property=\"og:title\" content=\"unterminated>", Some "unterminated>"
          "<meta property=\"og:title\" content=unq\"uoted>", Some "unq\"uoted"
          "<meta property=\"og:title\" content=a&amp;b>", Some "a&b"
          "<!--> <meta property=\"og:title\" content=\"after empty comment\"> -->", Some "after empty comment"
          "<!---> <meta property=\"og:title\" content=\"after dash comment\"> -->", Some "after dash comment"
          "<!DOCTYPE html><meta property=\"og:title\" content=\"doctype\">", Some "doctype"
          "<?xml version=\"1.0\"?><meta property=\"og:title\" content=\"pi\">", Some "pi"
          "<![CDATA[ <meta property=\"og:title\" content=\"cdata\"> ]]>", None
          "<p <meta property=\"og:title\" content=\"broken\">", None
          "< meta property=\"og:title\" content=\"space\">", None
          "<meta property=\"og:title\" content=\"tab\there\">", Some "tab\there"
          "<meta property=\"og:title\" content=\"\u0000nul\">", None ]

/// OG3, as above, for the tokenizer: libxml2's answers are the expectations, AngleSharp's are the listed ones.
[<Fact>]
let ``tokenizes like libxml2 but for the class B probes`` () =
    let classB = Map.ofList classBTokens
    for (html, expected) in tokenizerProbes do
        match classB.TryFind html with
        | Some angleSharp ->
            Assert.True((title html = angleSharp), $"{html}: expected {angleSharp} but got {title html}")
            Assert.True((title html <> expected), $"{html} is listed as differing but now answers {expected} as libxml2 does")
        | None -> Assert.True((title html = expected), $"{html}: expected {expected} but got {title html}")

/// OG3: the class B list is the 14 probes that differ (13 when `&#x;` was not yet a probe).
[<Fact>]
let ``the class B probes are listed`` () =
    Assert.Equal(7, classBReferences.Length)
    Assert.Equal(7, classBTokens.Length)

[<Fact>]
let ``decodes bytes like libxml2 reading utf8`` () =
    let decode (bytes: byte list) = OpengraphDocument.decode (Array.ofList bytes)
    Assert.Equal("café ÿ x", decode [ yield! Encoding.ASCII.GetBytes "caf"; 0xc3uy; 0xa9uy; yield! Encoding.ASCII.GetBytes " "; 0xffuy; yield! Encoding.ASCII.GetBytes " x" ])
    Assert.Equal("ÿ café x", decode [ 0xffuy; yield! Encoding.ASCII.GetBytes " caf"; 0xc3uy; 0xa9uy; yield! Encoding.ASCII.GetBytes " x" ])
    Assert.Equal("\u0093q\u0094", decode [ 0x93uy; byte 'q'; 0x94uy ])
    Assert.Equal("\u0082 ", decode [ 0x82uy; 0xa0uy ])

[<Fact>]
let ``finds the meta encoding like nokogiri`` () =
    let encoding (html: string) = OpengraphDocument.metaEncoding (OpengraphDocument.metaElements html)
    Assert.Equal(Some "iso-8859-1", encoding "<meta charset=\"iso-8859-1\">")
    Assert.Equal(Some "", encoding "<meta charset=\"\">")
    Assert.Equal(Some "iso-8859-1", encoding "<meta http-equiv=\"content-type\" content=\"text/html; charset=iso-8859-1\">")
    Assert.Equal(
        None,
        encoding "<meta http-equiv=\"Content-Type\" content=\"text/html\"><meta http-equiv=\"Content-Type\" content=\"charset=utf-8\">"
    )
    Assert.Equal(None, encoding "<meta http-equiv=\"refresh\" content=\"charset=utf-8\">")
    Assert.Equal(None, encoding "<meta property=\"og:title\" content=\"x\">")

// --- document.rs ---------------------------------------------------------------------------------------------------

/// test/models/opengraph/document_test.rb
[<Fact>]
let ``extracts opengraph tags`` () =
    let expected =
        [ "title", "Hey!"; "url", "https://example.com"; "image", "https://example.com/image.png"; "description", "desc.." ]
    let html =
        "<html><head><meta property=\"og:url\" content=\"https://example.com\"><meta property=\"og:title\" content=\"Hey!\"><meta property=\"og:description\" content=\"desc..\"><meta property=\"og:image\" content=\"https://example.com/image.png\"></head></html>"
    let attributes (html: string) = OpengraphDocument.opengraphAttributes (Some(Encoding.UTF8.GetBytes html))
    Assert.Equal<(string * string) list>(expected, attributes html)
    Assert.Equal<(string * string) list>(expected, attributes (html.Replace("property=", "name=")))

    let html =
        "<html><head><meta name=\"og:url\" content=\"https://example.com\"><meta name=\"og:title\" content=\"Hey!\"><meta name=\"og:description\" content=\"Hello â\u0080\u0099World\"><meta name=\"og:image\" content=\"https://example.com/image.png\"></head></html>"
    Assert.Equal(("description", "Hello World"), (attributes html)[3])
    Assert.True((OpengraphDocument.opengraphAttributes None).IsEmpty)

/// Pages as large as a fetch allows, built to make a parser do quadratic work, parse in time proportional to their size.
[<Fact>]
let ``parses pathological pages quickly`` () =
    let limit = OpengraphFetch.MaxBodySize
    let fill (openTag: string) (item: int -> string) (close: string) : string =
        let page = StringBuilder(openTag)
        let mutable i = 0
        while page.Length <= limit - close.Length - 64 do
            page.Append(item i) |> ignore
            i <- i + 1
        page.Append(close).ToString()
    let pages =
        [ fill "<meta property=\"og:title\" content=\"x\" " (fun i -> $"a{i:D7} ") ">"
          fill "<meta charset=utf-8>" (fun i -> $"<meta property=\"og:t{i}\" content=\"x\">") "<meta property=\"og:title\" content=\"x\">"
          fill "<meta property=\"og:title\" content=\"" (fun _ -> "&amp;é") "\">" ]
    for page in pages do
        let started = DateTime.UtcNow
        let found = OpengraphDocument.opengraphAttributes (Some(Encoding.UTF8.GetBytes page))
        // The second page is 130,000 metas long and its title is the last: the element cap stops the parse before it.
        if page.StartsWith "<meta charset=utf-8>" then Assert.Empty found else Assert.Equal("title", fst found[0])
        Assert.True((DateTime.UtcNow - started) < TimeSpan.FromSeconds 5.0, $"{DateTime.UtcNow - started} for {page.Substring(0, 60)}...")

// --- metadata.rs ---------------------------------------------------------------------------------------------------

/// Probed against the reference (`strip_tags` then `sanitize`).
[<Fact>]
let ``strips tags like rails`` () =
    for (input, expected) in
        [ "Tom & Jerry", "Tom &amp; Jerry"
          "a < b", "a &lt; b"
          "x&nbsp;y", "x&nbsp;y"
          " nb", "&nbsp;nb"
          "Hey!<script>alert('hi')</script>", "Hey!alert('hi')"
          "<!-- c -->t", "t"
          "a &lt;b&gt; c", "a &lt;b&gt; c"
          "<p>one</p><p>two</p>", "onetwo"
          "\"q\" 'a'", "\"q\" 'a'"
          "<style>x</style>y", "xy"
          "&amp;amp;", "&amp;amp;"
          "<b>bold</b>", "bold"
          "</script><img src=a onerror=prompt(1)>", ""
          " sp  ", " sp  "
          "<textarea>t<b>x</b></textarea>", "t&lt;b&gt;x&lt;/b&gt;" ] do
        match OpengraphMetadata.stripAndSanitize input with
        | Ok actual -> Assert.True((actual = expected), $"{input}: expected {expected} but got {actual}")
        | Error error -> failwith (UnfurlError.message error)

// --- opengraph/tests.rs --------------------------------------------------------------------------------------------

let private testdata (name: string) : JsonElement =
    let text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "testdata", name))
    (JsonDocument.Parse text).RootElement.Clone()

let private property (element: JsonElement) (name: string) : JsonElement option =
    match element.TryGetProperty name with
    | true, value when value.ValueKind <> JsonValueKind.Null -> Some value
    | _ -> None

let private route (spec: JsonElement) : Route =
    let s (key: string) = match property spec key with Some v -> str v | None -> ""
    let route = Route.create (s "method") (s "host") (s "path") (spec.GetProperty("status").GetInt32())
    let body =
        match property spec "body_b64" with
        | Some b64 -> Convert.FromBase64String(str b64)
        | None ->
            match property spec "body_repeat" with
            | Some repeat ->
                let parts = [ for item in repeat.EnumerateArray() -> item ]
                Encoding.UTF8.GetBytes(String.replicate (parts[1].GetInt32()) (str parts[0]))
            | None -> Encoding.UTF8.GetBytes(s "body")
    let body =
        match property spec "pad_to" with
        | Some padTo ->
            let n = padTo.GetInt32()
            Array.init n (fun i -> if i < body.Length then body[i] else byte ' ')
        | None -> body
    { route with
        Headers = [ for pair in spec.GetProperty("headers").EnumerateArray() -> str (pair.EnumerateArray() |> Seq.head), str (pair.EnumerateArray() |> Seq.item 1) ]
        Body = body
        Chunked = (match property spec "chunked" with Some c -> c.GetBoolean() | None -> false)
        Gzip = (match property spec "gzip" with Some g -> g.GetBoolean() | None -> false) }

let private answers (spec: JsonElement) : IPAddress list list =
    [ for list in spec.EnumerateArray() -> [ for ip in list.EnumerateArray() -> IPAddress.Parse(str ip) ] ]

/// Class B (README): the recorded cases whose answer is now different, by name, with the new response. `mailto:` is a URL `System.Uri`
/// parses (Rails raises `URI::InvalidComponentError` in `tweet_url?`), so the unfurl is nothing: 204, and no host is looked up.
let private classBCases: Map<string, int * string option> = Map [ "mailto raises", (204, None) ]

type private Shown =
    { Status: int
      Body: string option
      Error: string option
      Lookups: string list
      Requests: (string * string option * string * string option * string option * string option) list }

[<Fact>]
let ``unfurls like the reference`` () =
    task {
        let spec = testdata "opengraph_cases.json"
        let expected = [ for e in (testdata "opengraph_expected.json").EnumerateArray() -> e ]
        use server = FakeServer.Start [ for r in spec.GetProperty("routes").EnumerateArray() -> route r ]
        let publicIps = HashSet<IPAddress>([ for ip in spec.GetProperty("public_ips").EnumerateArray() -> IPAddress.Parse(str ip) ])

        let failures = List<string>()
        let cases = [ for c in spec.GetProperty("cases").EnumerateArray() -> c ]
        for (case', expected) in List.zip cases expected do
            let name = str (case'.GetProperty "name")
            Assert.Equal(str (expected.GetProperty "name"), name)
            let resolver = FakeResolver()
            for host in spec.GetProperty("hosts").EnumerateObject() do
                resolver.Set(host.Name, answers host.Value)
            let dialer = MappingDialer(publicIps, server.Addr)
            let clients, env = opengraphEnv resolver dialer false
            use _clients = clients
            let before = server.Received().Length

            let! outcome = Opengraph.unfurl env (str (case'.GetProperty "url"))
            let response =
                match outcome with
                | Ok(Unfurl.Json body) -> 200, Some body, None
                | Ok NoContent -> 204, None, None
                | Error(Raised cls) -> 500, None, Some cls
            let requests =
                server.Received()
                |> List.skip before
                |> List.map (fun r -> r.Method, r.Header "host", r.Target, r.Header "accept", None, r.Header "user-agent")
            let status, body, error = response
            let actual =
                { Status = status
                  Body = body
                  Error = error
                  Lookups = resolver.Lookups
                  Requests = requests }
            let wantedResponse = expected.GetProperty "response"
            let changed = classBCases.TryFind name
            let wanted =
                { Status = (match changed with Some(status, _) -> status | None -> wantedResponse.GetProperty("status").GetInt32())
                  Body = property wantedResponse "body" |> Option.map str
                  Error = (match changed with Some(_, error) -> error | None -> property wantedResponse "error" |> Option.map str)
                  Lookups = (if changed.IsSome then [] else [ for l in expected.GetProperty("lookups").EnumerateArray() -> str l ])
                  Requests =
                    [ for r in expected.GetProperty("requests").EnumerateArray() ->
                          let items = [ for item in r.EnumerateArray() -> if item.ValueKind = JsonValueKind.Null then None else Some(str item) ]
                          str (r.EnumerateArray() |> Seq.head), items[1], (items[2]).Value, items[3], None, items[5] ] }
            if actual <> wanted then failures.Add $"{name}:\n  expected {wanted}\n  actual   {actual}"
        Assert.True(failures.Count = 0, $"{failures.Count} of {cases.Length} cases differ:\n" + String.Join("\n", failures))
    }

/// The one recorded case that changed (class B): `mailto:` answers 204 and looks nothing up, where Rails raised.
[<Fact>]
let ``a mailto url unfurls nothing`` () =
    task {
        let resolver = FakeResolver()
        let clients, env = opengraphEnv resolver (MappingDialer(HashSet<IPAddress>(), IPEndPoint(IPAddress.Loopback, 9))) false
        use _clients = clients
        for url in [ "mailto:foo@example.com"; "mailto:foo@twitter.com"; "mailto:foo@x.com/status" ] do
            match! Opengraph.unfurl env url with
            | Ok NoContent -> ()
            | other -> failwith $"{url}: {other}"
        Assert.Empty resolver.Lookups
        Assert.Equal(Some(204, None), classBCases.TryFind "mailto raises")
    }

/// The probes of Ruby's `URI.parse` that `UriTests` pinned (unit 8.9 deleted the port of it), as `System.Uri` answers them (J6): what an
/// unfurl would look up (an http(s) URL with a host), and how the parts read.
[<Fact>]
let ``System.Uri reads the old URI probes`` () =
    let lookup (url: string) : string option =
        OpengraphUri.parse url
        |> ValueOption.toOption
        |> Option.filter OpengraphUri.isHttp
        |> Option.bind OpengraphUri.host
    let parsed (url: string) : Uri = (OpengraphUri.parse url).Value
    Assert.Equal(Some "x.com", lookup "https://x.com/dhh/status/1?s=20")
    Assert.Equal("?s=20", (parsed "https://x.com/dhh/status/1?s=20").Query)
    // An invalid character, no authority, an empty authority: nothing to look up.
    Assert.True((OpengraphUri.parse "http://exa mple.com/ ").IsNone)
    Assert.Equal(None, lookup "https:/rooms/1")
    Assert.Equal(None, lookup "https:rooms/1")
    Assert.Equal(None, lookup "https://")
    Assert.Equal(Some "[::1]", lookup "http://[::1]/x")
    // `mailto:` has no host here, and is not http.
    Assert.Equal(None, OpengraphUri.parse "mailto:a@b.com" |> ValueOption.toOption |> Option.bind OpengraphUri.host)
    // Default ports drop, the scheme and (class B) the host fold to lower case.
    Assert.Equal("https://x.com/a", (parsed "https://x.com:443/a").AbsoluteUri)
    Assert.Equal("https", (parsed "HTTPS://x.com/a").Scheme)
    Assert.Equal("http://x.com/a", (parsed "HTTP://X.com:80/a").AbsoluteUri)

/// Class B (README, Integrations): tweet detection reads `System.Uri`'s lower-cased host, so `https://Twitter.com/x/status/1` is a tweet
/// here and not in Rails, where Ruby keeps the host's case.
[<Fact>]
let ``a tweet url's host is read lower-cased`` () =
    let host (url: string) = OpengraphUri.parse url |> ValueOption.toOption |> Option.bind OpengraphUri.host
    Assert.Equal(Some "twitter.com", host "https://Twitter.com/x/status/1")
    Assert.Equal(Some "x.com", host "https://X.COM/x/status/1")

/// test/controllers/unfurl_links_controller_test.rb over plain HTTPS: the pinned address, with the certificate verified
/// against the host name.
[<Fact>]
let ``unfurls over https`` () =
    task {
        let page =
            "<html><head><meta property=\"og:url\" content=\"https://example.com\"><meta property=\"og:title\" content=\"Hey!\"><meta property=\"og:description\" content=\"desc..\"><meta property=\"og:image\" content=\"https://example.com/image.png\"></head></html>"
        use server =
            FakeServer.StartTls
                [ Route.create "GET" "www.example.com" "/" 200 |> Route.header "Content-Type" "text/html" |> Route.text page
                  Route.create "HEAD" "example.com" "/image.png" 200 |> Route.header "Content-Type" "image/png" ]
        let resolver = FakeResolver([ "www.example.com", [ "93.184.216.34" ]; "example.com", [ "93.184.216.35" ] ])
        let publicIps = HashSet [ IPAddress.Parse "93.184.216.34"; IPAddress.Parse "93.184.216.35" ]
        let dialer = MappingDialer(publicIps, server.Addr)
        let clients, env = opengraphEnv resolver dialer true
        use _clients = clients

        match! Opengraph.unfurl env "https://www.example.com" with
        | Ok(Unfurl.Json body) ->
            Assert.Equal(
                """{"title":"Hey!","url":"https://example.com","image":"https://example.com/image.png","description":"desc..","context_for_validation":{"context":null},"errors":{}}""",
                body
            )
        | other -> failwith $"{other}"
        Assert.Equal<string list>([ "93.184.216.34:443"; "93.184.216.35:443" ], dialer.Dialed |> List.map string)

        // A certificate that doesn't verify is a failed fetch.
        let untrustingClients, untrusted = opengraphEnv resolver dialer false
        use _untrusting = untrustingClients
        match! Opengraph.unfurl untrusted "https://www.example.com" with
        | Ok NoContent -> ()
        | other -> failwith $"{other}"
    }

/// www.example.com, at a fake public address that connects to `server`; the clients live as long as the provider.
let private envTo (server: IPEndPoint) : ServiceProvider * OpengraphEnv =
    let resolver = FakeResolver([ "www.example.com", [ "93.184.216.34" ] ])
    let dialer = MappingDialer(HashSet [ IPAddress.Parse "93.184.216.34" ], server)
    opengraphEnv resolver dialer false

/// A page followed by a gigabyte of zeros, gzipped to a megabyte, is past the 5MB limit as soon as that much is
/// inflated. The same page followed by less unfurls.
[<Fact>]
let ``stops reading a gzip bomb at the limit`` () =
    task {
        let page =
            "<meta property=\"og:title\" content=\"Hey!\"><meta property=\"og:url\" content=\"http://www.example.com/\"><meta property=\"og:description\" content=\"desc..\">"
        let page =
            use output = new MemoryStream()
            (use encoder = new GZipStream(output, CompressionLevel.Optimal, true)
             encoder.Write(Encoding.UTF8.GetBytes page))
            output.ToArray()
        let gzipped (path: string) (zeros: byte[]) =
            Route.create "GET" "*" path 200
            |> Route.header "Content-Type" "text/html"
            |> Route.header "Content-Encoding" "gzip"
            |> Route.body (Array.append page zeros)
        use server = FakeServer.Start [ gzipped "/" (gzipBomb 1024); gzipped "/small" (gzipBomb 2) ]
        let clients, env = envTo server.Addr
        use _clients = clients

        match! Opengraph.unfurl env "http://www.example.com/small" with
        | Ok(Unfurl.Json _) -> ()
        | other -> failwith $"{other}"
        let started = DateTime.UtcNow
        match! Opengraph.unfurl env "http://www.example.com/" with
        | Ok NoContent -> ()
        | other -> failwith $"{other}"
        Assert.True((DateTime.UtcNow - started) < TimeSpan.FromSeconds 2.0, $"{DateTime.UtcNow - started}")
    }

/// A server that keeps sending a byte at a time never trips a read timeout, but the unfurl as a whole gives up.
[<Fact>]
let ``gives up on a trickling page`` () =
    task {
        let handle, address = trickleServer "HTTP/1.1 200 OK\r\nContent-Type: text/html\r\nConnection: close\r\n\r\n"
        use _server = handle
        let started = DateTime.UtcNow
        let deadline = TimeSpan.FromMilliseconds 500.0
        let clients, env = envTo address
        use _clients = clients
        match! Opengraph.unfurlWithin env "http://www.example.com/" deadline NullLogger.Instance with
        | Ok NoContent -> ()
        | other -> failwith $"{other}"
        Assert.True((DateTime.UtcNow - started) < deadline * 2.0, $"{DateTime.UtcNow - started}")
    }

/// At most `MaxConcurrentUnfurls` unfurls are in flight at once, body reads included: pages that send their head at once and then
/// trickle forever hold their slots, and the unfurls past the limit never connect (the client's limiter alone lets them, since it
/// holds a permit only to the response head).
[<Fact>]
let ``unfurls past the limit wait while bodies are read`` () =
    task {
        let head = Encoding.ASCII.GetBytes "HTTP/1.1 200 OK\r\nContent-Type: text/html\r\nConnection: close\r\n\r\n"
        let handle, address, accepted =
            countingServer (fun client ->
                task {
                    use client = client
                    let stream = client.GetStream()
                    let! _ = stream.ReadAsync(Array.zeroCreate<byte> 4096, 0, 4096)
                    do! stream.WriteAsync(head, 0, head.Length)
                    while true do
                        do! stream.WriteAsync([| byte ' ' |], 0, 1)
                        do! Task.Delay 50
                })
        use _server = handle
        let clients, env = envTo address
        use _clients = clients
        let extra = 4
        let unfurls =
            [ for _ in 1 .. Opengraph.MaxConcurrentUnfurls + extra ->
                  Opengraph.unfurlWithin env "http://www.example.com/" (TimeSpan.FromSeconds 2.0) NullLogger.Instance ]
        // Let every unfurl that may start connect and get its head.
        let started = DateTime.UtcNow
        while accepted () < Opengraph.MaxConcurrentUnfurls && DateTime.UtcNow - started < TimeSpan.FromSeconds 1.5 do
            do! Task.Delay 20
        do! Task.Delay 200
        Assert.True(accepted () <= Opengraph.MaxConcurrentUnfurls, $"{accepted ()} connections")
        let! outcomes = Task.WhenAll unfurls
        Assert.All(outcomes, fun outcome -> Assert.Equal(Ok NoContent, outcome))
    }

// --- OG1, OG2, OG4: hostile pages and hostile redirects --------------------------------------------------------------

/// OG1: pages as large as a fetch allows, built to make a parser nest or multiply elements, stop at the caps (no more than
/// `MaxElements` are created, and a hostile page is reported as stopped) and in a bounded time.
[<Fact>]
let ``hostile pages are stopped by the caps`` () =
    let limit = OpengraphFetch.MaxBodySize
    let repeated (unit': string) = String.replicate (limit / unit'.Length) unit'
    let pages =
        [ "nested div", repeated "<div>"
          "nested span", repeated "<span>"
          "nested table", repeated "<table><tr><td>"
          "misnested formatting", repeated "<b><i><a href=x>"
          "b then p", repeated "<b><p>"
          "wide", repeated "<i>" ]
    for (name, page) in pages do
        let started = DateTime.UtcNow
        let reading = OpengraphDocument.read ("<meta charset=utf-8><meta property=\"og:title\" content=\"head\">" + page)
        let took = DateTime.UtcNow - started
        Assert.True(reading.Stopped, $"{name} was not stopped")
        Assert.True(reading.Elements <= OpengraphDocument.MaxElements, $"{name} created {reading.Elements} elements")
        Assert.True(took < TimeSpan.FromSeconds 10.0, $"{name} took {took}")
        // The head was read before the stop.
        Assert.Contains(reading.Metas, fun m -> m.Attr "content" = Some "head")

/// OG2: a head of metas, then 300 nested divs: the head still unfurls.
[<Fact>]
let ``a deep body still unfurls the head`` () =
    task {
        let page =
            "<html><head><meta property=\"og:url\" content=\"http://www.example.com/\"><meta property=\"og:title\" content=\"Hey!\"><meta property=\"og:description\" content=\"desc..\"></head><body>"
            + String.replicate 300 "<div>"
        use server = FakeServer.Start [ Route.create "GET" "www.example.com" "/" 200 |> Route.header "Content-Type" "text/html" |> Route.text page ]
        let clients, env = envTo server.Addr
        use _clients = clients
        match! Opengraph.unfurl env "http://www.example.com/" with
        | Ok(Unfurl.Json body) -> Assert.Contains("\"title\":\"Hey!\"", body)
        | other -> failwith $"{other}"
        Assert.True((OpengraphDocument.read page).Stopped)
    }

/// OG4: a `Location` to a private address is refused after a public first hop, whether it is a literal or a name, and nothing
/// connects there.
[<Fact>]
let ``each redirect hop is guarded`` () =
    task {
        let redirect (path: string) (location: string) =
            Route.create "GET" "www.example.com" path 302 |> Route.header "Location" location
        use server = FakeServer.Start [ redirect "/literal" "http://10.0.0.1/secret"; redirect "/named" "http://internal.example/secret"; redirect "/loopback" "http://127.0.0.1:1/" ]
        let resolver = FakeResolver([ "www.example.com", [ "93.184.216.34" ]; "internal.example", [ "10.0.0.1" ] ])
        let dialer = MappingDialer(HashSet [ IPAddress.Parse "93.184.216.34" ], server.Addr)
        let clients, env = opengraphEnv resolver dialer false
        use _clients = clients
        for path in [ "literal"; "named"; "loopback" ] do
            match! Opengraph.unfurl env $"http://www.example.com/{path}" with
            | Ok NoContent -> ()
            | other -> failwith $"{path}: {other}"
        Assert.Equal(3, server.Received().Length)
        Assert.All(dialer.Dialed, fun address -> Assert.Equal(IPAddress.Parse "93.184.216.34", address.Address))
    }
