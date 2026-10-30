// plans/design-kit-auth.md K5 (unit 2.4): `verify_authenticity_token` over the framework's `ICsrfProtection`.
//
// .NET 11's `DefaultCsrfProtection` is token-less: `Sec-Fetch-Site` first (`same-origin` and `none` pass, `cross-site`
// and `same-site` do not), then CORS-trusted origins, then `Origin` against the request's scheme and host; a request with
// neither header (not a browser) passes, and so does a safe method. It is called from the before-action chain, where
// Rails runs `verify_authenticity_token`, not from a middleware: the framework's own middleware only records a verdict for
// endpoints that carry antiforgery metadata, which would leave JSON and body-less writes unchecked and turn a denied form
// read into a 500. A denial is Rails' `InvalidAuthenticityToken` (422, `public/422.html`). The scheme it compares a browser's
// `Origin` with is `Request.Scheme`, which `UseForwardedHeaders` and the `assume_ssl` middleware (unit 2.6) have set.
namespace Campfire.App.Web

open System.Threading.Tasks
open Microsoft.AspNetCore.Antiforgery
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection

module Csrf =
    /// Why a request was refused, for the log line the refusal writes.
    let private reason (http: HttpContext) : string =
        match http.Request.Headers["Sec-Fetch-Site"].ToString() with
        | "" ->
            let origin = http.Request.Headers.Origin.ToString()
            $"HTTP Origin header ({origin}) didn't match the request's scheme and host"
        | site -> $"Sec-Fetch-Site header ({site}) indicates a cross-site request"

    /// `verify_authenticity_token`: GET and HEAD pass; every other method is the framework's verdict.
    let verifyAuthenticityToken (http: HttpContext) : Task<Result<unit, Campfire.App.Web.Error>> =
        let request = http.Request
        if HttpMethods.IsGet request.Method || HttpMethods.IsHead request.Method then
            Task.FromResult(Ok())
        else
            task {
                let csrf = http.RequestServices.GetRequiredService<ICsrfProtection>()
                let! verdict = csrf.ValidateAsync(http).AsTask()
                return if verdict.IsAllowed then Ok() else Result.Error(Campfire.App.Web.Error.InvalidAuthenticityToken(reason http))
            }
