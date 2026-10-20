// The two helpers Front shares: a word match for header lists and the CRC-32 its gzip writer keeps.
//
// `Rack::Deflater` itself is gone (unit 2.8): response compression is the framework's `UseResponseCompression`
// (plans/design-kit-auth.md K11), and a page of cached fragments is no longer gzipped from stored pieces.
namespace Campfire.Kit

open System
open System.Buffers.Binary

module Deflater =
    /// `/\bword\b/`
    let internal hasWord (haystack: string) (word: string) : bool =
        let isWord (c: char) = Char.IsAsciiLetterOrDigit c || c = '_'
        let mutable found = false
        let mutable from = 0
        while not found && from <= haystack.Length - word.Length do
            match haystack.IndexOf(word, from, StringComparison.Ordinal) with
            | -1 -> from <- haystack.Length
            | i ->
                let beforeOk = i = 0 || not (isWord haystack[i - 1])
                let afterOk = i + word.Length >= haystack.Length || not (isWord haystack[i + word.Length])
                if beforeOk && afterOk then found <- true
                from <- i + 1
        found

    // --- gzip -----------------------------------------------------------------------------------

    let private slicingTables: uint32[][] =
        let tables = Array.init 8 (fun _ -> Array.zeroCreate<uint32> 256)
        for n in 0..255 do
            let mutable c = uint32 n
            for _ in 0..7 do
                c <- if c &&& 1u <> 0u then 0xEDB88320u ^^^ (c >>> 1) else c >>> 1
            tables[0][n] <- c
        for n in 0..255 do
            let mutable c = tables[0][n]
            for k in 1..7 do
                c <- tables[0][int (c &&& 0xFFu)] ^^^ (c >>> 8)
                tables[k][n] <- c
        tables

    /// The running CRC-32 (IEEE, as zlib's `crc32`) of `data` after what `crc` covers, by slicing-by-8
    /// tables: what runs where the CPU has no CRC instruction (and what the tests check the other against).
    let internal crc32Software (crc: uint32) (data: ReadOnlySpan<byte>) : uint32 =
        let t = slicingTables
        let mutable c = ~~~crc
        let mutable i = 0
        while i + 8 <= data.Length do
            let lo = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice i) ^^^ c
            let hi = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(i + 4))
            c <-
                t[7][int (lo &&& 0xFFu)]
                ^^^ t[6][int ((lo >>> 8) &&& 0xFFu)]
                ^^^ t[5][int ((lo >>> 16) &&& 0xFFu)]
                ^^^ t[4][int (lo >>> 24)]
                ^^^ t[3][int (hi &&& 0xFFu)]
                ^^^ t[2][int ((hi >>> 8) &&& 0xFFu)]
                ^^^ t[1][int ((hi >>> 16) &&& 0xFFu)]
                ^^^ t[0][int (hi >>> 24)]
            i <- i + 8
        while i < data.Length do
            c <- t[0][int ((c ^^^ uint32 data[i]) &&& 0xFFu)] ^^^ (c >>> 8)
            i <- i + 1
        ~~~c

    /// The same by the ARM CRC-32 instructions.
    let private crc32Arm (crc: uint32) (data: ReadOnlySpan<byte>) : uint32 =
        let mutable c = ~~~crc
        let mutable i = 0
        while i + 8 <= data.Length do
            c <- System.Runtime.Intrinsics.Arm.Crc32.Arm64.ComputeCrc32(c, BinaryPrimitives.ReadUInt64LittleEndian(data.Slice i))
            i <- i + 8
        while i < data.Length do
            c <- System.Runtime.Intrinsics.Arm.Crc32.ComputeCrc32(c, data[i])
            i <- i + 1
        ~~~c

    /// The running CRC-32 (IEEE, as zlib's `crc32`) of `data` after what `crc` covers.
    let internal crc32Update (crc: uint32) (data: ReadOnlySpan<byte>) : uint32 =
        if System.Runtime.Intrinsics.Arm.Crc32.Arm64.IsSupported then crc32Arm crc data else crc32Software crc data
