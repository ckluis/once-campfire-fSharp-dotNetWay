// Port of rust/crates/campfire/src/channels/connection.rs
//
// `ApplicationCable::Connection` (reference/app/channels/application_cable/connection.rb):
// `current_user` from the signed `session_token` cookie (`Authentication::SessionLookup`), or
// `reject_unauthorized_connection`.
namespace Campfire.App.Channels

open System.Threading.Tasks
open Microsoft.Extensions.Logging
open Campfire.Cable
open Campfire.Db
open Campfire.RailsCompat
open Campfire.RailsCompat.Clock

type SessionAuthenticator(factory: Microsoft.EntityFrameworkCore.IDbContextFactory<Campfire.Db.Ef.CampfireDb>, secrets: Secrets, clock: SharedClock, logger: ILogger) =
    /// `cookies.signed[:session_token]`.
    member private _.SessionToken(request: ConnectRequest) : string | null =
        let headers =
            [ for value in request.Headers["Cookie"] do
                  match value with
                  | null -> ()
                  | value -> value ]
        // The cookie header's first `session_token`, percent-decoded as the framework's request cookies are.
        let raw =
            Microsoft.Net.Http.Headers.CookieHeaderValue.ParseList(ResizeArray<string>(headers))
            |> Seq.tryFind (fun cookie -> cookie.Name.Equals("session_token", System.StringComparison.Ordinal))
            |> Option.map (fun cookie -> System.Uri.UnescapeDataString((cookie.Value.Value |> nonNull).Replace('+', ' ')))
        match raw with
        | None -> null
        | Some raw ->
            match Cookies.verifySigned secrets "session_token" raw (clock.Now()) with
            | Some value -> value
            | None -> null

    member this.Connect(request: ConnectRequest) : Task<CableUser option> =
        task {
            match this.SessionToken request with
            | null -> return None
            | token ->
                let user =
                    use db = factory.CreateDbContext()
                    Campfire.Db.Ef.Reads.run db (fun db ->
                        match Session.findByToken db token with
                        | Some session -> User.findById db session.UserId
                        | None -> None)
                match user with
                | Ok user -> return user |> Option.map (fun user -> { CableUser.Id = user.Id; Name = user.Name })
                | Error error ->
                    logger.LogError("Could not look up the cable session error={Error}", DbError.display error)
                    return None
        }
