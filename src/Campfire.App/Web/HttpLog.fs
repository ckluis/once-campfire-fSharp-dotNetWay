// plans/design-front.md F8 (unit 7.1): the request log is the framework's `UseHttpLogging`, one combined line per request, replacing
// the front server's own writer (Thruster's line). No headers and no bodies, but the user agent, and the client address that
// Thruster's line had: operators grep for both.
namespace Campfire.App.Web

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.HttpLogging
open Microsoft.Extensions.DependencyInjection

module HttpLog =
    /// The six fields of the line (`CombineLogs`: request and response in one entry).
    let configure (options: HttpLoggingOptions) : unit =
        options.LoggingFields <-
            HttpLoggingFields.RequestMethod
            ||| HttpLoggingFields.RequestPath
            ||| HttpLoggingFields.RequestQuery
            ||| HttpLoggingFields.RequestProtocol
            ||| HttpLoggingFields.ResponseStatusCode
            ||| HttpLoggingFields.Duration
        options.CombineLogs <- true

    /// A client-supplied value on one log line: control characters (a newline would forge a line) become a space.
    let private oneLine (value: string) : string =
        if value.AsSpan().IndexOfAnyInRange('\u0000', '\u001f') < 0 && value.IndexOf '\u007f' < 0 then value
        else String(value.ToCharArray() |> Array.map (fun c -> if Char.IsControl c then ' ' else c))

    /// Adds `remote_addr` (as the forwarded-headers middleware left it, an IPv4-mapped address written as IPv4) and `user_agent`.
    /// It runs when the response is done, after `UseForwardedHeaders` has set the address.
    type Interceptor() =
        interface IHttpLoggingInterceptor with
            member _.OnRequestAsync(_: HttpLoggingInterceptorContext) : ValueTask = ValueTask.CompletedTask

            member _.OnResponseAsync(context: HttpLoggingInterceptorContext) : ValueTask =
                let http = context.HttpContext
                match http.Connection.RemoteIpAddress with
                | null -> ()
                | address ->
                    let address = if address.IsIPv4MappedToIPv6 then address.MapToIPv4() else address
                    context.AddParameter("remote_addr", address.ToString())
                match http.Request.Headers.UserAgent.ToString() with
                | "" -> ()
                | agent -> context.AddParameter("user_agent", oneLine agent)
                ValueTask.CompletedTask

    let add (services: IServiceCollection) : unit =
        services.AddHttpLogging(configure) |> ignore
        services.AddHttpLoggingInterceptor<Interceptor>() |> ignore
