// plans/design-views.md V5: a rendered HTML fragment as the fragment caches hand it out.
namespace Campfire.Views

open System.Security.Cryptography
open System.Text
open System.Threading

/// A rendered HTML fragment: exactly its UTF-8 bytes (no spare capacity), their SHA-256 (computed once, here) and a
/// process-unique `Id`, which is what a page in parts finds the fragment by (`Html.fragment`'s slot) and what the kept
/// compressed page's digest is made of. Immutable, so a cache can hand out the one instance: a fragment rendered again
/// is a new object with its own `Id`.
[<Sealed; AllowNullLiteral; System.ComponentModel.ImmutableObject(true)>]
type CachedFragment(bytes: byte[]) =
    static let mutable counter = 0L

    let id = Interlocked.Increment &counter
    let digest = SHA256.HashData bytes

    new(text: string) = CachedFragment(Encoding.UTF8.GetBytes text)

    member _.Id: int64 = id
    member _.Bytes: byte[] = bytes
    member _.Digest: byte[] = digest
    member _.Length: int = bytes.Length
    member _.Memory: System.ReadOnlyMemory<byte> = System.ReadOnlyMemory<byte> bytes
    member _.Span: System.ReadOnlySpan<byte> = System.ReadOnlySpan<byte> bytes

    override _.ToString() = Encoding.UTF8.GetString bytes
