// What is left of rust/crates/kit/src/deflater.rs's tests after unit 2.8: the word match and the CRC-32 Front shares, and the
// `Generations` tests of rust/crates/kit/src/deflater/splice.rs. `Rack::Deflater`'s own cases are the framework's now
// (`Campfire.App.Tests.Web.RespondTests`, X4).
module Campfire.Kit.Tests.DeflaterTests

open System
open System.Text
open Xunit
open Campfire.Kit

[<Fact>]
let ``words`` () =
    Assert.True(Deflater.hasWord "no-transform, public" "no-transform")
    Assert.False(Deflater.hasWord "xno-transform" "no-transform")
    Assert.True(Deflater.hasWord "identity" "identity")

[<Fact>]
let ``the crc matches the reference polynomial on every path`` () =
    // The standard check value for "123456789".
    Assert.Equal(0xCBF43926u, Deflater.crc32Update 0u (ReadOnlySpan<byte>(Encoding.ASCII.GetBytes "123456789")))
    // The table version, which runs where the CPU has no CRC instruction, agrees with whichever runs here.
    Assert.Equal(0xCBF43926u, Deflater.crc32Software 0u (ReadOnlySpan<byte>(Encoding.ASCII.GetBytes "123456789")))
    let random = Random 7
    for size in [ 0; 1; 7; 8; 9; 63; 64; 65; 1000; 65537 ] do
        let data = Array.zeroCreate<byte> size
        random.NextBytes data
        Assert.Equal(Deflater.crc32Software 0u (ReadOnlySpan<byte> data), Deflater.crc32Update 0u (ReadOnlySpan<byte> data))
        Assert.Equal(Deflater.crc32Software 12345u (ReadOnlySpan<byte> data), Deflater.crc32Update 12345u (ReadOnlySpan<byte> data))
    // Updating in pieces equals updating at once.
    let data = Array.init 5000 (fun i -> byte (i * 13))
    let once = Deflater.crc32Update 0u (ReadOnlySpan<byte> data)
    let pieces = Deflater.crc32Update (Deflater.crc32Update 0u (ReadOnlySpan<byte>(data, 0, 1234))) (ReadOnlySpan<byte>(data, 1234, 3766))
    Assert.Equal(once, pieces)

// splice.rs

[<Fact>]
let ``generations stay within their budget and keep what is read`` () =
    let budget = 64 * 1024
    let size = 1024
    let map = Generations<byte, int>(budget, (fun _ size -> size))
    for n in 0..255 do
        map.Insert(byte n, size)
        Assert.True((map.Get(0uy, ignore)).IsSome, "what's read stays")
        let held = map.Count * size
        // Each generation may overshoot half the budget by the entry that filled it.
        Assert.True(held <= budget + 2 * size, $"{held} bytes held")
    Assert.True((map.Get(255uy, ignore)).IsSome, "the latest is kept")
    Assert.True((map.Get(1uy, ignore)).IsNone, "what isn't read ages out")

    let map = Generations<byte, int>(budget, (fun _ size -> size))
    map.Insert(1uy, size)
    map.Insert(1uy, size)
    Assert.Equal(size, map.YoungCost)
