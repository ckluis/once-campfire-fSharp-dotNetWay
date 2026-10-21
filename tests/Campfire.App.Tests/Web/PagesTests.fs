// plans/design-views.md section 5, P1: `Pages.send` writes a page in parts (`HtmlOutput`), and a page with cached fragments keeps
// its compressed form (V7) by the SHA-256 of its parts and fragments.
module Campfire.App.Tests.Web.PagesTests

open System
open System.IO
open System.IO.Compression
open System.Threading
open System.Threading.Tasks
open Microsoft.AspNetCore.ResponseCompression
open Microsoft.Extensions.Caching.Hybrid
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.DependencyInjection.Extensions
open Xunit
open Campfire.App.Tests.Support
open Campfire.App.Web
open Campfire.Views

/// Counts the compressed streams made: a kept page makes none.
type private CountingEncoder(inner: ICompressionProvider, counter: int ref) =
    interface ICompressionProvider with
        member _.EncodingName = inner.EncodingName
        member _.SupportsFlush = inner.SupportsFlush

        member _.CreateStream(output: Stream) =
            Interlocked.Increment counter |> ignore
            inner.CreateStream output

type private CountingProvider(inner: IResponseCompressionProvider, counter: int ref) =
    interface IResponseCompressionProvider with
        member _.CheckRequestAcceptsCompression http = inner.CheckRequestAcceptsCompression http
        member _.ShouldCompressResponse http = inner.ShouldCompressResponse http

        member _.GetCompressionProvider http =
            match inner.GetCompressionProvider http with
            | null -> null
            | encoder -> CountingEncoder(encoder, counter) :> ICompressionProvider |> Unchecked.nonNull

let private counting (counter: int ref) (services: IServiceCollection) : unit =
    services.Replace(
        ServiceDescriptor.Singleton<IResponseCompressionProvider>(fun sp ->
            let options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<ResponseCompressionOptions>>()
            CountingProvider(Compression.SkipSmall(ResponseCompressionProvider(sp, options)), counter) :> IResponseCompressionProvider)
    )
    |> ignore

let private decode (encoding: string) (bytes: byte[]) : byte[] =
    use input = new MemoryStream(bytes)
    use decoder: Stream =
        if encoding = "br" then new BrotliStream(input, CompressionMode.Decompress) else new GZipStream(input, CompressionMode.Decompress)
    use output = new MemoryStream()
    decoder.CopyTo output
    output.ToArray()

[<Fact>]
let ``P1 a page in parts has its Content-Length, HEAD keeps it, and the compressed page is made once per encoding`` () =
    task {
        let made = ref 0
        use! test = bootSeededServices None ignore (counting made) [] "default"
        let david = david test
        let path = $"/rooms/{ALL_TALK}"
        let page (encoding: string option) =
            let req = get path
            david.Send(match encoding with Some e -> req.With("accept-encoding", e) | None -> req)

        let! plain = page None
        Assert.Equal(200, plain.Status)
        Assert.Equal(0, made.Value)
        Assert.Equal(Some(string plain.Body.Length), plain.Header "content-length")
        Assert.Equal(None, plain.Header "content-encoding")

        let! head = david.Send((request "HEAD" path))
        Assert.Equal(0, head.Body.Length)
        Assert.Equal(plain.Header "content-length", head.Header "content-length")

        // The first gzip request compresses the page; the second compresses nothing and is the same bytes.
        let! first = page (Some "gzip")
        Assert.Equal(1, made.Value)
        Assert.Equal<string list>([ "gzip" ], first.HeaderValues "content-encoding")
        Assert.Contains("Accept-Encoding", String.Join(", ", first.HeaderValues "vary"))
        Assert.Equal(Some(string first.Body.Length), first.Header "content-length")
        Assert.Equal<byte[]>(plain.Body, decode "gzip" first.Body)
        let! second = page (Some "gzip")
        Assert.Equal(1, made.Value)
        Assert.Equal<byte[]>(first.Body, second.Body)

        // Brotli is another page of the cache, made once.
        let! br = page (Some "br")
        Assert.Equal(2, made.Value)
        Assert.Equal<string list>([ "br" ], br.HeaderValues "content-encoding")
        Assert.Equal<byte[]>(plain.Body, decode "br" br.Body)
        let! brAgain = page (Some "br")
        let! gzipAgain = page (Some "gzip")
        Assert.Equal(2, made.Value)
        Assert.Equal<byte[]>(br.Body, brAgain.Body)
        Assert.Equal<byte[]>(first.Body, gzipAgain.Body)
        Assert.NotEqual<byte[]>(first.Body, br.Body)

        // HEAD of a compressing client: the length of the plain page, no body, nothing compressed.
        let! headGzip = david.Send((request "HEAD" path).With("accept-encoding", "gzip"))
        Assert.Equal(0, headGzip.Body.Length)
        Assert.Equal(2, made.Value)
    }

[<Fact>]
let ``P1 the kept page is made once for concurrent requests, and a page with another byte is another page`` () =
    task {
        let services = ServiceCollection()
        Fragments.add services (8L * 1024L * 1024L)
        use provider = services.BuildServiceProvider()
        let cache = provider.GetRequiredService<HybridCache>()
        let fragment = CachedFragment(String('f', 3000))
        let page = HtmlOutput([| Text.Encoding.UTF8.GetBytes "<p>"; Text.Encoding.UTF8.GetBytes "</p>" |], [| fragment |])
        let made = ref 0
        let compress (page: HtmlOutput) : byte[] =
            Interlocked.Increment made |> ignore
            Thread.Sleep 50
            page.ToArray()
        let! results =
            Task.WhenAll [| for _ in 1..16 -> Task.Run<CompressedPage>(fun () -> (Fragments.compressedPage cache "gzip" page compress).AsTask()) |]
        Assert.Equal(1, made.Value)
        Assert.True(results |> Array.forall (fun kept -> obj.ReferenceEquals(kept, results[0])))
        Assert.Equal("gzip", results[0].Encoding)
        // Another fragment with the same length, or other text, is another page.
        let other = HtmlOutput(page.Parts, [| CachedFragment(String('g', 3000)) |])
        let! _ = (Fragments.compressedPage cache "gzip" other compress).AsTask()
        Assert.Equal(2, made.Value)
        let! _ = (Fragments.compressedPage cache "br" page compress).AsTask()
        Assert.Equal(3, made.Value)
    }
