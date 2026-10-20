// plans/design-kit-auth.md K1 (Web/TicketFormat.fs): the `session_token` cookie's bytes are Rails' signed cookie.
//
// ASP.NET Core's cookie authentication does the plumbing (read, `SignInAsync`, `SignOutAsync`, attributes); this
// is the only part that is Rails': `cookies.signed[:session_token]` (HMAC-SHA1 over the PBKDF2 "signed cookie" key,
// the `_rails` envelope with purpose `cookie.session_token` and an `exp`), through RailsCompat's verifier.
namespace Campfire.App.Web

open System
open System.Security.Claims
open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Authentication.Cookies
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.Features
open Campfire.RailsCompat

module Schemes =
    /// The `session_token` cookie scheme.
    [<Literal>]
    let Session = "Session"

    /// The bot-key scheme (K4).
    [<Literal>]
    let BotKey = "BotKey"

    /// The cookie's name.
    [<Literal>]
    let SessionCookie = "session_token"

    /// The claim that carries the session's token, so a renewal can sign it again.
    [<Literal>]
    let TokenClaim = "session_token"

type RailsSignedTicketFormat(secrets: Secrets, time: TimeProvider) =
    let protect (ticket: AuthenticationTicket) : string =
        match ticket.Principal.FindFirst Schemes.TokenClaim with
        | null -> ""
        | claim ->
            let expires = ticket.Properties.ExpiresUtc |> Option.ofNullable
            Cookies.sign secrets Schemes.SessionCookie claim.Value expires

    // The verifier already enforced `exp`, so the ticket carries no `ExpiresUtc` of its own.
    let unprotect (text: string | null) : AuthenticationTicket | null =
        match text with
        | null -> null
        | text ->
            match Cookies.verifySigned secrets Schemes.SessionCookie text (time.GetUtcNow()) with
            | None -> null
            | Some token ->
                let identity = ClaimsIdentity([ Claim(Schemes.TokenClaim, token) ], Schemes.Session)
                AuthenticationTicket(ClaimsPrincipal identity, AuthenticationProperties(), Schemes.Session)

    interface ISecureDataFormat<AuthenticationTicket> with
        member _.Protect(data: AuthenticationTicket) : string = protect data
        member _.Protect(data: AuthenticationTicket, _purpose: string | null) : string = protect data
        member _.Unprotect(protectedText: string | null) : AuthenticationTicket | null = unprotect protectedText
        member _.Unprotect(protectedText: string | null, _purpose: string | null) : AuthenticationTicket | null = unprotect protectedText
