// Port of rust/crates/kit/src/deflater/splice.rs, without its gzip pieces (unit 2.8: compression is the framework's)
//
// ETags for pages made mostly of cached fragments (a room's messages), without rehashing the whole page on every request.
//
// A page arrives in parts that cover it end to end: its cached fragments (each with the few bytes of text before it,
// when that follows another fragment) and the text in between (the layout). The template recorded where each fragment
// went as it rendered, so the page's bytes are never joined into one buffer, nor searched for its fragments: a plain
// response sends the parts one after another. The ETag is a hash of the parts' digests instead of the whole body; those
// digests are remembered, so pages don't hash their parts on every request: a fragment's with the fragment, a text's by
// the text's bytes.
//
// Where Rust keys a fragment's SHA-256 by the address of its `Arc<String>` in a bounded map, a .NET object can't be found
// by address, so it hangs off the `Fragment` itself and lives as long as the fragment does.
namespace Campfire.Kit

open System
open System.Buffers
open System.Buffers.Binary
open System.Collections.Generic
open System.Security.Cryptography
open System.Threading
open Campfire.Kit

module internal SpliceLimits =
    /// Smaller fragments aren't worth a part of their own; they stay in the text around them.
    [<Literal>]
    let MinFragment = 1024

    /// At most this much text between two fragments travels with the second; more is a text part.
    [<Literal>]
    let MaxGlue = 256

    /// A bound on the bytes of texts remembered with their SHA-256. A room page's texts (the layout
    /// before and after its messages) are ~34 KB for each person and room, so a generation holds those
    /// of ~240 rooms as people see them: every page in use at a small install. A text that isn't
    /// remembered is simply hashed again.
    [<Literal>]
    let MaxTextBytes = 16777216

    /// Larger texts are hashed every time, for the same reason larger pieces aren't stored.
    [<Literal>]
    let MaxStoredText = 262144

    /// How many of the most recent texts are kept at hand (a power of two), and the largest one kept
    /// there: at most 2 MB beyond what `MaxTextBytes` allows.
    [<Literal>]
    let RecentSlots = 64

    [<Literal>]
    let MaxRecentText = 32768

// --- What's remembered -------------------------------------------------------------------------

module internal Digests =
    let inline same (a: BodyDigest) (b: BodyDigest) : bool = a.A = b.A && a.B = b.B && a.C = b.C && a.D = b.D

/// A fragment seen in a page: its SHA-256.
[<Sealed; AllowNullLiteral>]
type internal KnownFragment(sha: BodyDigest) =
    member _.Sha = sha

/// A cached HTML fragment (the rendered bytes of one message, say) as the fragment cache hands it out. Its identity is
/// what its remembered SHA-256 hangs off, so a fragment rendered again into a new `Fragment` hashes afresh.
[<Sealed; AllowNullLiteral>]
type Fragment(bytes: byte[]) =
    [<VolatileField>]
    let mutable known: KnownFragment = null

    new(text: string) = Fragment(Text.Encoding.UTF8.GetBytes text)

    member _.Bytes: byte[] = bytes
    member _.Memory: ReadOnlyMemory<byte> = ReadOnlyMemory<byte> bytes
    member _.Length = bytes.Length

    /// What the fragment keeps alive besides its bytes: its SHA-256, as the fragment cache counts it against its budget.
    member _.HeldBytes: int =
        match known with
        | null -> 0
        | _ -> 32 + 64

    member internal _.Known
        with get () = known
        and set (value: KnownFragment) = known <- value

    /// Remembers `value` unless something is remembered already (another request may have hashed the
    /// fragment meanwhile; either answer is the same).
    member internal _.Remember(value: KnownFragment) : unit =
        Interlocked.CompareExchange(&known, value, null) |> ignore

/// A map key that borrows the bytes it was made from, compared and hashed by content: how a page's
/// text finds the SHA-256 remembered for it. Stored keys own a copy.
[<Struct; CustomEquality; NoComparison>]
type internal TextKey =
    { Memory: ReadOnlyMemory<byte> }

    override this.Equals(other: obj | null) =
        match other with
        | :? TextKey as other -> this.Memory.Span.SequenceEqual other.Memory.Span
        | _ -> false

    override this.GetHashCode() = TextKey.Hash this.Memory.Span

    /// A fast, non-cryptographic hash of tens of KB (SHA-256 is what stored pieces are found by; this
    /// only finds the text that was hashed): the CRC-32C instructions on four interleaved lanes where the
    /// CPU has them, four lanes of multiply-and-fold, a word at a time, where it doesn't.
    static member Hash(bytes: ReadOnlySpan<byte>) : int =
        let mutable i = 0
        let mutable tail = 0UL
        let mutable shift = 0
        let mutable result = 0UL
        if System.Runtime.Intrinsics.Arm.Crc32.Arm64.IsSupported then
            let mutable c0 = uint32 bytes.Length
            let mutable c1 = 0x9E3779B9u
            let mutable c2 = 0x85EBCA6Bu
            let mutable c3 = 0xC2B2AE35u
            while i + 32 <= bytes.Length do
                c0 <- System.Runtime.Intrinsics.Arm.Crc32.Arm64.ComputeCrc32C(c0, BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice i))
                c1 <- System.Runtime.Intrinsics.Arm.Crc32.Arm64.ComputeCrc32C(c1, BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(i + 8)))
                c2 <- System.Runtime.Intrinsics.Arm.Crc32.Arm64.ComputeCrc32C(c2, BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(i + 16)))
                c3 <- System.Runtime.Intrinsics.Arm.Crc32.Arm64.ComputeCrc32C(c3, BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(i + 24)))
                i <- i + 32
            while i + 8 <= bytes.Length do
                c0 <- System.Runtime.Intrinsics.Arm.Crc32.Arm64.ComputeCrc32C(c0, BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice i))
                i <- i + 8
            while i < bytes.Length do
                c1 <- System.Runtime.Intrinsics.Arm.Crc32.ComputeCrc32C(c1, bytes[i])
                i <- i + 1
            result <- (uint64 (c0 ^^^ (c2 <<< 13 ||| c2 >>> 19)) <<< 32) ||| uint64 (c1 ^^^ (c3 <<< 7 ||| c3 >>> 25))
        elif System.Runtime.Intrinsics.X86.Sse42.X64.IsSupported then
            let mutable c0 = uint64 bytes.Length
            let mutable c1 = 0x9E3779B9UL
            let mutable c2 = 0x85EBCA6BUL
            let mutable c3 = 0xC2B2AE35UL
            while i + 32 <= bytes.Length do
                c0 <- System.Runtime.Intrinsics.X86.Sse42.X64.Crc32(c0, BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice i))
                c1 <- System.Runtime.Intrinsics.X86.Sse42.X64.Crc32(c1, BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(i + 8)))
                c2 <- System.Runtime.Intrinsics.X86.Sse42.X64.Crc32(c2, BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(i + 16)))
                c3 <- System.Runtime.Intrinsics.X86.Sse42.X64.Crc32(c3, BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(i + 24)))
                i <- i + 32
            while i + 8 <= bytes.Length do
                c0 <- System.Runtime.Intrinsics.X86.Sse42.X64.Crc32(c0, BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice i))
                i <- i + 8
            while i < bytes.Length do
                c1 <- uint64 (System.Runtime.Intrinsics.X86.Sse42.Crc32(uint32 c1, bytes[i]))
                i <- i + 1
            let m0, m1, m2, m3 = uint32 c0, uint32 c1, uint32 c2, uint32 c3
            result <- (uint64 (m0 ^^^ (m2 <<< 13 ||| m2 >>> 19)) <<< 32) ||| uint64 (m1 ^^^ (m3 <<< 7 ||| m3 >>> 25))
        else
            let mutable h0 = 0x9E3779B97F4A7C15UL ^^^ uint64 bytes.Length
            let mutable h1 = 0xC2B2AE3D27D4EB4FUL
            let mutable h2 = 0x165667B19E3779F9UL
            let mutable h3 = 0x85EBCA77C2B2AE63UL
            let inline mix (h: uint64) (w: uint64) =
                let m = (h ^^^ w) * 0x9FB21C651E98DF25UL
                m ^^^ (m >>> 29)
            while i + 32 <= bytes.Length do
                h0 <- mix h0 (BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice i))
                h1 <- mix h1 (BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(i + 8)))
                h2 <- mix h2 (BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(i + 16)))
                h3 <- mix h3 (BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(i + 24)))
                i <- i + 32
            while i + 8 <= bytes.Length do
                h0 <- mix h0 (BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice i))
                i <- i + 8
            while i < bytes.Length do
                tail <- tail ||| (uint64 bytes[i] <<< shift)
                shift <- shift + 8
                i <- i + 1
            h0 <- mix h0 tail
            result <- mix (mix h0 h1) (mix h2 h3)
        int (result ^^^ (result >>> 32))

/// What a remembered text keeps: its SHA-256, and the key that owns the text's bytes.
type internal TextSha = { Sha: BodyDigest; Owned: TextKey }

/// Text bytes as a map key, compared by content.
[<Sealed>]
type internal TextKeyComparer() =
    interface IEqualityComparer<TextKey> with
        member _.Equals(a, b) = a.Memory.Span.SequenceEqual b.Memory.Span
        member _.GetHashCode key = TextKey.Hash key.Memory.Span

module internal SpliceCaches =
    open SpliceLimits

    let textShasGate = obj ()

    let textShas =
        Generations<TextKey, TextSha>(
            MaxTextBytes,
            (fun key _ -> key.Memory.Length + CacheCosts.EntryOverhead),
            TextKeyComparer(),
            (fun _ stored -> stored.Owned)
        )

    let private readSha (stored: TextSha) : BodyDigest = stored.Sha

    let private sha256 (bytes: ReadOnlySpan<byte>) : BodyDigest =
        let hash = Span<byte>(Array.zeroCreate<byte> 32)
        SHA256.HashData(bytes, hash) |> ignore
        { A = BinaryPrimitives.ReadUInt64LittleEndian(hash.Slice(0, 8))
          B = BinaryPrimitives.ReadUInt64LittleEndian(hash.Slice(8, 8))
          C = BinaryPrimitives.ReadUInt64LittleEndian(hash.Slice(16, 8))
          D = BinaryPrimitives.ReadUInt64LittleEndian(hash.Slice(24, 8)) }

    /// The texts a page used most recently, in slots picked by a handful of their bytes: a page's texts
    /// are the same from one request to the next, so the text in its slot is nearly always the one asked
    /// about, and comparing them (a vectorized compare, no hashing of tens of KB) is all it takes. A slot
    /// is only a shortcut: a text that isn't in its slot is found in (or added to) `shas` as before.
    /// Bounded by `RecentSlots` and `MaxRecentText`.
    let private recent: TextSha[] = Array.zeroCreate RecentSlots

    let private slotOf (text: ReadOnlySpan<byte>) : int =
        if text.Length < 16 then
            text.Length &&& (RecentSlots - 1)
        else
            let head = BinaryPrimitives.ReadUInt64LittleEndian text
            let tail = BinaryPrimitives.ReadUInt64LittleEndian(text.Slice(text.Length - 8))
            let mixed = (uint64 text.Length * 0x9E3779B97F4A7C15UL) ^^^ (head * 0xC2B2AE3D27D4EB4FUL) ^^^ (tail * 0x165667B19E3779F9UL)
            int (mixed >>> 40) &&& (RecentSlots - 1)

    /// The SHA-256 of `text`, remembered by its bytes in `shas`: a page repeats its texts (a room's
    /// layout) from one request to the next, and finding one again costs a compare with the text in its
    /// slot (or, a fast hash and a compare), a small part of hashing it. The compare is what makes this
    /// sound, since the SHA-256 is what stored pieces are found by: two texts whose fast hashes collide
    /// still get their own.
    let textShaIn (gate: obj) (shas: Generations<TextKey, TextSha>) (text: ReadOnlyMemory<byte>) : BodyDigest =
        let slot = if text.Length <= MaxRecentText && obj.ReferenceEquals(shas, textShas) then slotOf text.Span else -1
        let remembered = if slot >= 0 then recent[slot] else Unchecked.defaultof<TextSha>
        if slot >= 0 && not (isNull (box remembered)) && remembered.Owned.Memory.Span.SequenceEqual text.Span then
            remembered.Sha
        else
            let key = { Memory = text }
            let mutable known = ValueNone
            Threading.Monitor.Enter gate
            try
                known <- shas.Get(key, readSha)
            finally
                Threading.Monitor.Exit gate
            match known with
            | ValueSome sha -> sha
            | ValueNone ->
                let sha = sha256 text.Span
                if text.Length <= MaxStoredText then
                    let owned = { Memory = ReadOnlyMemory<byte>(text.ToArray()) }
                    let entry = { Sha = sha; Owned = owned }
                    lock gate (fun () -> shas.Insert(owned, entry))
                    if slot >= 0 then recent[slot] <- entry
                sha

    let textSha (text: ReadOnlyMemory<byte>) : BodyDigest = textShaIn textShasGate textShas text

    let fragmentSha (fragment: Fragment) : BodyDigest =
        match fragment.Known with
        | null ->
            let sha = sha256 (ReadOnlySpan<byte> fragment.Bytes)
            let known = KnownFragment sha
            fragment.Remember known
            sha
        | known -> known.Sha

/// Body bytes gathered piece by piece (text, and fragments too small for a part of their own),
/// joined only when there's more than one piece.
[<Struct; NoEquality; NoComparison>]
type internal Gathered =
    val mutable private first: ReadOnlyMemory<byte>
    val mutable private count: int
    val mutable private rest: ResizeArray<ReadOnlyMemory<byte>> | null

    member this.Push(bytes: ReadOnlyMemory<byte>) : unit =
        if not bytes.IsEmpty then
            if this.count = 0 then
                this.first <- bytes
            else
                match this.rest with
                | null ->
                    let list = ResizeArray<ReadOnlyMemory<byte>>(4)
                    list.Add bytes
                    this.rest <- list
                | list -> list.Add bytes
            this.count <- this.count + 1

    member this.Take() : ReadOnlyMemory<byte> =
        let taken =
            match this.count with
            | 0 -> ReadOnlyMemory<byte>.Empty
            | 1 -> this.first
            | _ ->
                let list = nonNull this.rest
                let mutable total = this.first.Length
                for piece in list do
                    total <- total + piece.Length
                let joined = Array.zeroCreate<byte> total
                this.first.Span.CopyTo(Span<byte> joined)
                let mutable at = this.first.Length
                for piece in list do
                    piece.Span.CopyTo(Span<byte>(joined, at, piece.Length))
                    at <- at + piece.Length
                ReadOnlyMemory<byte> joined
        this.first <- ReadOnlyMemory<byte>.Empty
        this.count <- 0
        this.rest <- null
        taken

/// A page's body split at its cached fragments, with each part's identity.
[<Struct; NoEquality; NoComparison>]
type internal SplicePart =
    {
        /// The fragment, or null for a text part.
        Fragment: Fragment
        /// A text part's text, or the text since the previous fragment, which goes out just before this
        /// one.
        Bytes: ReadOnlyMemory<byte>
        Sha: BodyDigest
    }

/// A page's body split at its cached fragments, with each part's identity.
[<Sealed>]
type PageParts internal (length: int, parts: SplicePart[]) =
    static let isFragment (part: SplicePart) = not (isNull (box part.Fragment))

    [<ThreadStatic; DefaultValue>]
    static val mutable private hashBuffer: byte[] | null

    /// A SHA-256's worth of room for the thread to hash into.
    static member private HashBuffer: byte[] =
        match PageParts.hashBuffer with
        | null ->
            let created = Array.zeroCreate<byte> 32
            PageParts.hashBuffer <- created
            created
        | existing -> existing

    /// The body `text` makes with each of `fragments` (cached HTML, in order) spliced in at its byte
    /// offset in `text`, split into parts; or that body whole (`Error`), when no fragment is big enough
    /// for a part of its own. Raises `ArgumentOutOfRangeException` if the offsets go backwards or past
    /// the end of `text`.
    static member Splice(text: ReadOnlyMemory<byte>, fragments: IReadOnlyList<struct (int * Fragment)>) : Result<PageParts, ReadOnlyMemory<byte>> =
        let count = fragments.Count
        let mutable length = text.Length
        for i in 0 .. count - 1 do
            let struct (_, fragment) = fragments[i]
            length <- length + fragment.Length
        // The text before each fragment big enough for a part, and the fragments themselves.
        let gaps = Array.zeroCreate<ReadOnlyMemory<byte>> count
        let bigs = Array.zeroCreate<Fragment> count
        let mutable runs = 0
        let mutable between = Gathered()
        let mutable position = 0
        for i in 0 .. count - 1 do
            let struct (offset, fragment) = fragments[i]
            between.Push(text.Slice(position, offset - position))
            position <- offset
            if fragment.Length < SpliceLimits.MinFragment then
                between.Push fragment.Memory
            else
                gaps[runs] <- between.Take()
                bigs[runs] <- fragment
                runs <- runs + 1
        between.Push(text.Slice position)
        let rest = between.Take()
        if runs = 0 then
            Error rest
        else
            // A gap is a text part of its own unless it follows a fragment and is short enough to travel
            // with the next: the first gap never follows one.
            let mutable total = runs + (if rest.IsEmpty then 0 else 1)
            for i in 0 .. runs - 1 do
                let gap = gaps[i]
                if not gap.IsEmpty && (i = 0 || gap.Length > SpliceLimits.MaxGlue) then total <- total + 1
            let parts = Array.zeroCreate<SplicePart> total
            let mutable made = 0
            for i in 0 .. runs - 1 do
                let gap = gaps[i]
                let fragment = bigs[i]
                let sha = SpliceCaches.fragmentSha fragment
                let glue =
                    if i > 0 && gap.Length <= SpliceLimits.MaxGlue then
                        gap
                    else
                        if not gap.IsEmpty then
                            parts[made] <- PageParts.TextPart gap
                            made <- made + 1
                        ReadOnlyMemory<byte>.Empty
                parts[made] <- { Fragment = fragment; Bytes = glue; Sha = sha }
                made <- made + 1
            if not rest.IsEmpty then parts[made] <- PageParts.TextPart rest
            Ok(PageParts(length, parts))

    static member private TextPart(text: ReadOnlyMemory<byte>) : SplicePart =
        { Fragment = null
          Bytes = text
          Sha = SpliceCaches.textSha text }

    /// The body's length.
    member _.BodyLength = length

    /// The weak ETag's value: 32 hex digits of a SHA-256 over the parts (the same body split the same
    /// way always gets the same one), as `Rack::ETag`'s is of the body.
    member _.Etag() : string =
        let mutable size = 0
        for i in 0 .. parts.Length - 1 do
            let part = &parts[i]
            size <- size + (if isFragment part then 1 + 8 + part.Bytes.Length + 8 + 32 else 1 + 8 + 32)
        let buffer = ArrayPool<byte>.Shared.Rent size
        try
            let mutable at = 0
            for i in 0 .. parts.Length - 1 do
                let part = &parts[i]
                let fragment = part.Fragment
                buffer[at] <- (if isNull (box fragment) then byte 'T' else byte 'F')
                at <- at + 1
                BinaryPrimitives.WriteUInt64LittleEndian(Span<byte>(buffer, at, 8), uint64 part.Bytes.Length)
                at <- at + 8
                if not (isNull (box fragment)) then
                    part.Bytes.Span.CopyTo(Span<byte>(buffer, at, part.Bytes.Length))
                    at <- at + part.Bytes.Length
                    BinaryPrimitives.WriteUInt64LittleEndian(Span<byte>(buffer, at, 8), uint64 fragment.Length)
                    at <- at + 8
                let sha = part.Sha
                BinaryPrimitives.WriteUInt64LittleEndian(Span<byte>(buffer, at, 8), sha.A)
                BinaryPrimitives.WriteUInt64LittleEndian(Span<byte>(buffer, at + 8, 8), sha.B)
                BinaryPrimitives.WriteUInt64LittleEndian(Span<byte>(buffer, at + 16, 8), sha.C)
                BinaryPrimitives.WriteUInt64LittleEndian(Span<byte>(buffer, at + 24, 8), sha.D)
                at <- at + 32
            let hash = PageParts.HashBuffer
            SHA256.HashData(ReadOnlySpan<byte>(buffer, 0, at), Span<byte>(hash)) |> ignore
            Convert.ToHexStringLower(ReadOnlySpan<byte>(hash, 0, 16))
        finally
            ArrayPool<byte>.Shared.Return buffer

    /// The body, part after part, never joined into one buffer.
    member _.WritePlain(writer: IBufferWriter<byte>) : unit =
        for i in 0 .. parts.Length - 1 do
            let part = &parts[i]
            match part.Fragment with
            | null -> writer.Write part.Bytes.Span
            | fragment ->
                if not part.Bytes.IsEmpty then writer.Write part.Bytes.Span
                writer.Write(ReadOnlySpan<byte> fragment.Bytes)

    /// The number of parts, for tests.
    member internal _.PartCount = parts.Length

    /// What a part's piece is stored under, for tests: whether it is a fragment, its glue and its text.
    member internal _.Part(i: int) : SplicePart = parts[i]

    interface IPageParts with
        member this.BodyLength = this.BodyLength
        member this.Etag() = this.Etag()
        member this.WritePlain writer = this.WritePlain writer
