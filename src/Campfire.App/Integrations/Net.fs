// Port of rust/crates/campfire/src/integrations/net.rs
//
// What the named clients share (`Clients.fs`): name resolution and connecting (to a pinned address when a policy
// requires one). Everything else, TLS and the HTTP exchange, is `SocketsHttpHandler`'s; none of the clients uses a proxy.
namespace Campfire.App.Integrations

open System
open System.Net
open System.Net.Sockets
open System.Threading
open System.Threading.Tasks

/// Name resolution. The system one is `getaddrinfo` (Ruby's `Resolv.getaddresses` reads /etc/hosts,
/// then DNS); tests substitute fixed answers. `cancellation` is the caller's deadline: a name server that never answers must not
/// hold an unfurl past it (`Guard.resolvePublicIps` also stops waiting at it, whatever the resolver does).
type IResolver =
    abstract Lookup: host: string * cancellation: CancellationToken -> Task<Result<IPAddress list, exn>>

/// Opens the TCP connection to an address. Tests redirect fake public addresses to a local server;
/// everything else connects for real. A failure is the `SocketException` the connect raised.
type IDialer =
    abstract Connect: address: IPEndPoint * cancellation: CancellationToken -> Task<Socket>

[<Sealed>]
type SystemResolver() =
    interface IResolver with
        member _.Lookup(host: string, cancellation: CancellationToken) : Task<Result<IPAddress list, exn>> =
            task {
                try
                    let! addresses = Dns.GetHostAddressesAsync(host, cancellation)
                    return Ok(List.ofArray addresses)
                with e ->
                    return Error e
            }

[<Sealed>]
type TcpDialer() =
    interface IDialer with
        member _.Connect(address: IPEndPoint, cancellation: CancellationToken) : Task<Socket> =
            task {
                let socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
                try
                    socket.NoDelay <- true
                    do! socket.ConnectAsync(address, cancellation)
                    return socket
                with e ->
                    socket.Dispose()
                    return raise e
            }
