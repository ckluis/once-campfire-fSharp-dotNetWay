// plans/design-close.md C3 and section 5 (FR1-FR4): Active Storage serves files with the framework's file result
// (`ActiveStorage.fileResult`, `TypedResults.PhysicalFile` with range processing), not Rack's `Files`.
module Campfire.App.Tests.FileResultTests

open System
open System.IO
open System.Text
open System.Text.Json
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Xunit
open Campfire.App
open Campfire.App.Tests.Support
open Campfire.Storage
open Campfire.Tests

let private str (e: JsonElement) : string = nonNull (e.GetString())

/// The answer to a `Range` request read as Rack's `get_byte_ranges` value: 200 is `None` (the whole file), 416 is `Some []`, and a
/// 206 is the one range of its `Content-Range`.
type private Answer =
    | Whole
    | Unsatisfiable
    | Ranges of (uint64 * uint64) list
    | Other of int

let private answerOf (reply: Reply) : Answer =
    match reply.Status with
    | 200 -> Whole
    | 416 -> Unsatisfiable
    | 206 ->
        let range = ((reply.Header "content-range").Value).Substring("bytes ".Length)
        let span = range.Substring(0, range.IndexOf '/').Split '-'
        Ranges [ (UInt64.Parse span[0], UInt64.Parse span[1]) ]
    | status -> Other status

let private rubyAnswer (ranges: JsonElement) : Answer =
    match ranges.ValueKind with
    | JsonValueKind.Array ->
        let all = [ for range in ranges.EnumerateArray() -> (range[0].GetUInt64(), range[1].GetUInt64()) ]
        if all.IsEmpty then Unsatisfiable else Ranges all
    | _ -> Whole

/// A loopback host with one route: the file of `?size=N` bytes (bytes `i % 256`), through `ActiveStorage.fileResult`.
let private fileHost (directory: string) : Task<Host> =
    startPipeline (fun app ->
        Microsoft.AspNetCore.Builder.RunExtensions.Run(
            app,
            RequestDelegate(fun http ->
                let size = Int32.Parse(string http.Request.Query["size"])
                let path = Path.Combine(directory, $"file-{size}")
                if not (File.Exists path) then File.WriteAllBytes(path, Array.init size (fun i -> byte (i % 256)))
                ActiveStorage.fileResult path "application/octet-stream" None http
            )
        ))

let private caseKey (header: string) (size: int) : string = header + "|" + string size

/// Each listed case and the framework's answer it lists (the `framework` field, written as the dump writes it).
let private knownDifferences () : Collections.Generic.Dictionary<string, string> =
    let document = JsonDocument.Parse(File.ReadAllText(Repo.path "tests/Campfire.App.Tests/testdata/byte-ranges-known-differences.json"))
    let table = Collections.Generic.Dictionary<string, string>()
    for case in document.RootElement.EnumerateArray() do
        table[caseKey (str (case.GetProperty "header")) ((case.GetProperty "size").GetInt32())] <- str (case.GetProperty "framework")
    table

/// FR1: the 200 `byte_ranges` vectors of `ruby_core.json` (what Rack answers, written by Ruby) against the file result's answer. The cases
/// in `byte-ranges-known-differences.json` must differ, and every other case must be equal: the list fails both ways.
[<Fact>]
let ``FR1 byte ranges answer as rack does except the listed cases`` () =
    task {
        let directory = Directory.CreateTempSubdirectory("fr1").FullName
        use! host = fileHost directory
        let listed = knownDifferences ()
        let cases = (Repo.vector "ruby_core").RootElement.GetProperty("byte_ranges").EnumerateArray() |> Seq.toArray
        Assert.Equal(200, cases.Length)
        let dump = Environment.GetEnvironmentVariable "CAMPFIRE_DUMP_BYTE_RANGES"
        let differing = ResizeArray<string>()
        let changed = ResizeArray<string>()
        let dumped = ResizeArray<string>()
        for case in cases do
            let size = case.GetProperty("size").GetInt32()
            let header = match case.GetProperty "header" with h when h.ValueKind = JsonValueKind.String -> Some(str h) | _ -> None
            let expected = rubyAnswer (case.GetProperty "ranges")
            let request = get $"/?size={size}"
            let request = match header with Some h -> request.With("range", h) | None -> request
            let! actual =
                task {
                    try
                        let! reply = host.Send request
                        return answerOf reply
                    with _ ->
                        return Other 0
                }
            let key = caseKey (defaultArg header "") size
            if actual <> expected then
                differing.Add key
                match listed.TryGetValue key with
                | true, answer when answer <> $"{actual}" -> changed.Add $"{key}: listed {answer}, now {actual}"
                | _ -> ()
                dumped.Add(JsonSerializer.Serialize({| header = defaultArg header ""; size = size; rack = $"{expected}"; framework = $"{actual}" |}))
            // A header of nothing is no header: nobody sends it.
            if header.IsNone then Assert.Equal(expected, actual)
        match dump with
        | null
        | "" -> ()
        | path -> File.WriteAllLines(path, dumped)
        let unexpected = differing |> Seq.filter (fun key -> not (listed.ContainsKey key)) |> Seq.toList
        let stale = listed.Keys |> Seq.filter (fun key -> not (differing.Contains key)) |> Seq.toList
        Assert.True(unexpected.IsEmpty, $"differ and are not listed: {unexpected}")
        Assert.True(stale.IsEmpty, $"listed and no longer differ: {stale}")
        Assert.True(changed.Count = 0, $"differ, but not as listed: {List.ofSeq changed}")
        Assert.Equal(95, listed.Count)
    }

/// The storage path is relative to the working directory in the image (`storage/files`); the framework's file result wants a rooted one.
[<Fact>]
let ``a relative path is served`` () =
    task {
        let directory = Directory.CreateTempSubdirectory("fr-relative").FullName
        let path = Path.Combine(directory, "file")
        File.WriteAllText(path, "relative")
        use! host =
            startPipeline (fun app ->
                Microsoft.AspNetCore.Builder.RunExtensions.Run(
                    app,
                    RequestDelegate(fun http -> ActiveStorage.fileResult (Path.GetRelativePath(Directory.GetCurrentDirectory(), path)) "text/plain" None http)
                ))
        let! reply = host.Send(get "/")
        Assert.Equal((200, "relative"), (reply.Status, reply.Text))
    }

/// A file gone before the answer began is an error, not an empty 200 (the file result throws; only a client that went away is swallowed).
[<Fact>]
let ``a missing file is not answered as an empty 200`` () =
    task {
        let directory = Directory.CreateTempSubdirectory("fr-missing").FullName
        use! host =
            startPipeline (fun app ->
                Microsoft.AspNetCore.Builder.RunExtensions.Run(
                    app,
                    RequestDelegate(fun http -> ActiveStorage.fileResult (Path.Combine(directory, "gone")) "text/plain" None http)
                ))
        let! reply = host.Send(get "/")
        Assert.Equal(500, reply.Status)
    }

/// A seeded app's proxy path and disk path for blob 5, and the file behind it.
let private blobPaths (test: Test) : Task<string * string * string> =
    task {
        let _, blobs, _ = sessionVectors ()
        let redirectPath = blobs[0]
        let proxy = Text.RegularExpressions.Regex("/redirect/").Replace(redirectPath, "/proxy/", 1)
        let! redirect = test.Send(get redirectPath)
        let location = (redirect.Header "location").Value
        let disk = location.Substring "http://campfire.test".Length
        let! read = test.App.ReadEf(fun db -> (Campfire.Storage.Blob.find db 5L).Value)
        let blob = match read with Ok blob -> blob | Error e -> failwith $"{e}"
        return proxy, disk, Storage.pathFor test.App.Storage blob
    }

/// FR2: the disk service's `show`.
[<Fact>]
let ``FR2 disk show answers whole, ranged, conditional and missing`` () =
    task {
        use! test = bootSeeded "default"
        let! _, disk, path = blobPaths test
        let file = File.ReadAllBytes path
        let size = file.Length

        let! whole = test.Send(get disk)
        Assert.Equal(200, whole.Status)
        Assert.Equal(Some "image/jpeg", whole.Header "content-type")
        Assert.True((whole.Header "content-disposition" |> Option.map (fun d -> d.StartsWith "inline;")) = Some true)
        Assert.Equal(Some "max-age=3600, public", whole.Header "cache-control")
        Assert.Equal(Some(string size), whole.Header "content-length")
        let lastModified = (whole.Header "last-modified").Value
        Assert.Equal(Some "bytes", whole.Header "accept-ranges")
        Assert.Equal<byte[]>(file, whole.Body)

        let! head = test.Send(request "HEAD" disk)
        Assert.Equal(200, head.Status)
        Assert.Equal(Some(string size), head.Header "content-length")
        Assert.Empty head.Body

        let! single = test.Send((get disk).With("range", "bytes=10-19"))
        Assert.Equal(206, single.Status)
        Assert.Equal(Some $"bytes 10-19/{size}", single.Header "content-range")
        Assert.Equal<byte[]>(file[10..19], single.Body)

        let! past = test.Send((get disk).With("range", $"bytes={size + 5}-"))
        Assert.Equal(416, past.Status)
        Assert.Equal(Some $"bytes */{size}", past.Header "content-range")

        let! fresh = test.Send((get disk).With("if-modified-since", lastModified))
        Assert.Equal(304, fresh.Status)
        Assert.Empty fresh.Body

        File.Delete path
        let! missing = test.Send(get disk)
        Assert.Equal(404, missing.Status)

        let! bad = test.Send(get (Text.RegularExpressions.Regex("--").Replace(disk, "--0", 1)))
        Assert.Equal(404, bad.Status)
    }

/// FR3: the proxy with a range.
[<Fact>]
let ``FR3 proxy ranges answer with rails headers`` () =
    task {
        use! test = bootSeeded "default"
        let! proxy, _, path = blobPaths test
        let file = File.ReadAllBytes path
        let ranged (range: string) = (get proxy).With("range", range)

        let! single = test.Send(ranged "bytes=0-9")
        Assert.Equal(206, single.Status)
        Assert.Equal(Some $"bytes 0-9/{file.Length}", single.Header "content-range")
        Assert.Equal(Some "binary", single.Header "content-transfer-encoding")
        Assert.True((single.Header "content-disposition" |> Option.map (fun d -> d.StartsWith "inline;")) = Some true)
        Assert.Equal<byte[]>(file[0..9], single.Body)

        // C3 (a): several ranges are the whole file.
        let! several = test.Send(ranged "bytes=0-9,20-29")
        Assert.Equal(200, several.Status)
        Assert.Equal<byte[]>(file, several.Body)

        // C3 (c): a header Rack's lenient parsing read (and the proxy answered 416 for one Rack could not read) is the whole file.
        let! unreadable = test.Send(ranged "bytes=a-b")
        Assert.Equal(200, unreadable.Status)
        Assert.Equal<byte[]>(file, unreadable.Body)
    }

/// FR4: a blob whose file has no bytes.
[<Fact>]
let ``FR4 an empty file is served whole and refuses a range`` () =
    task {
        use! test = bootSeeded "default"
        let! proxy, _, path = blobPaths test
        File.WriteAllBytes(path, [||])
        let! whole = test.Send(get proxy)
        Assert.Equal(200, whole.Status)
        Assert.Empty whole.Body
        let! ranged = test.Send((get proxy).With("range", "bytes=0-"))
        Assert.Equal(416, ranged.Status)
    }

/// Security review of Phase 9 (plans/design-close.md section 11): the deleted `FileServer` fact "the type and disposition are the ones given"
/// guarded what the framework's file result is now handed. A blob whose type Rails serves as binary (`text/html`, here) goes out as
/// `application/octet-stream` and `attachment` from the proxy (whole and ranged) and the disk service, whatever the query asks.
[<Fact>]
let ``FR5 a blob typed text/html is served as application/octet-stream and attachment, whole, ranged and from the disk service`` () =
    task {
        use! test = bootSeeded "default"
        do! test.Execute("UPDATE active_storage_blobs SET content_type = 'text/html' WHERE id = 5", [])
        let! proxy, _, _ = blobPaths test
        let check (name: string) (reply: Reply) =
            Assert.True(reply.Status = 200 || reply.Status = 206, $"{name}: {reply.Status}")
            Assert.Equal(Some "application/octet-stream", reply.Header "content-type")
            let disposition = reply.Header "content-disposition"
            Assert.True((disposition |> Option.map (fun d -> d.StartsWith "attachment;")) = Some true, $"{name}: {disposition}")
        let! whole = test.Send(get $"{proxy}?disposition=inline")
        check "proxy" whole
        let! ranged = test.Send((get proxy).With("range", "bytes=0-9"))
        check "proxy ranged" ranged
        let _, blobs, _ = sessionVectors ()
        let! redirect = test.Send(get $"{blobs[0]}?disposition=inline")
        let location = (redirect.Header "location").Value
        let! disk = test.Send(get (location.Substring "http://campfire.test".Length))
        check "disk" disk
    }
