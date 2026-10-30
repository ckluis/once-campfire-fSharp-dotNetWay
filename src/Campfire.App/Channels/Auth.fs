// Phase 3 (plans/design-cable.md C9, C10): `ApplicationCable::Connection` (reference/app/channels/application_cable/connection.rb).
// `current_user` is the Phase 2 cookie scheme's answer for the upgrade request (`AuthenticateAsync Schemes.Session`: the signed
// `session_token`, then the `sessions` row, then the user: `Authentication::SessionLookup`), or `reject_unauthorized_connection`.
//
// The scheme only authenticates: `Session#resume` (the hourly `sessions` write) and the cookie's re-issue are
// `Authentication.restore`'s, which a Cable connection never runs, so an upgrade writes nothing and sets no cookie (A2).
module Campfire.App.Channels.CableAuth

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Campfire.App.Web
open Campfire.Db

/// The connection's identity, looked up once (`identified_by :current_user`).
let authenticate (ctx: HttpContext) : Task<CableUser option> =
    task {
        let! result = ctx.AuthenticateAsync Schemes.Session
        let current = Current.get ctx
        match current.LookupError with
        | Some _ ->
            // The scheme can only reject; a database failure is logged as the port always has.
            let logger = ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger "campfire.cable"
            logger.LogError("Could not look up the cable session")
            return None
        | None ->
            match current.User with
            | Some user when result.Succeeded -> return Some { CableUser.Id = user.Id; Name = user.Name }
            | _ -> return None
    }

/// C10: the same lookup once more, now that the connection is registered and a remote disconnect would reach it. Not a second
/// `AuthenticateAsync` (the handler answers a repeat with its first result): the token comes from that first ticket, and the
/// session and user are read again in `services`, a scope of its own, through `SessionCookie.lookup`.
let recheck (ctx: HttpContext) (services: IServiceProvider) : Task<bool> =
    task {
        let! result = ctx.AuthenticateAsync Schemes.Session
        match result.Ticket with
        | null -> return false
        | ticket ->
            match ticket.Principal.FindFirst Schemes.TokenClaim with
            | null -> return false
            | claim ->
                let db = services.GetRequiredService<Campfire.Db.Ef.CampfireDb>()
                match SessionCookie.lookup db claim.Value with
                | Ok(Some _) -> return true
                | Ok None -> return false
                | Error error ->
                    let logger = services.GetRequiredService<ILoggerFactory>().CreateLogger "campfire.cable"
                    logger.LogError("Could not look up the cable session error={Error}", DbError.display error)
                    return false
    }
