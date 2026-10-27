// plans/design-front.md F8 (unit 7.1): the request log is the framework's `UseHttpLogging`, one combined line per request, replacing
// the front server's own writer (Thruster's line). No headers and no bodies, but the user agent, and the client address that
// Thruster's line had: operators grep for both. Query values Rails' `filter_parameters` name are `[FILTERED]` (section 11, R1).
namespace Campfire.App.Web

open System
open System.Text.RegularExpressions
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

    /// Rails' `filter_parameters` (`reference/config/initializers/filter_parameter_logging.rb`) as `ActiveSupport::ParameterFilter`
    /// compiles them: case-insensitive, anywhere in the key (`message.body` is the nested key's form).
    let private filteredKey =
        Regex(
            @"passw|email|secret|token|_key|crypt|salt|certificate|otp|ssn|cvv|cvc|endpoint|message\.body",
            RegexOptions.IgnoreCase ||| RegexOptions.CultureInvariant ||| RegexOptions.Compiled
        )

    /// `ActionDispatch::Http::FilterParameters`' `PAIR_RE`.
    let private queryPair = Regex(@"([^&;=]+)=([^&;=]+)", RegexOptions.CultureInvariant ||| RegexOptions.Compiled)

    /// `filtered_query_string` (what Rails' request line logs): each `key=value` pair whose raw key a filter matches has its value
    /// replaced by `[FILTERED]`. ValueNone when nothing is filtered, so the query is logged as sent.
    let filterQuery (query: string) : string voption =
        if not (filteredKey.IsMatch query) then
            ValueNone
        else
            let filtered =
                queryPair.Replace(
                    query,
                    MatchEvaluator(fun pair ->
                        let key = pair.Groups[1].Value
                        if filteredKey.IsMatch key then key + "=[FILTERED]" else pair.Value)
                )
            if filtered = query then ValueNone else ValueSome filtered

    /// Adds `remote_addr` (as the forwarded-headers middleware left it, an IPv4-mapped address written as IPv4) and `user_agent`,
    /// and logs the query with Rails' filtered values in place of `RequestQuery` when it has any (plans/design-front.md section 11,
    /// R1: a token or password in a query string reached the line in clear, where Rails' own request line filters it).
    type Interceptor() =
        interface IHttpLoggingInterceptor with
            member _.OnRequestAsync(context: HttpLoggingInterceptorContext) : ValueTask =
                let query = context.HttpContext.Request.QueryString
                if query.HasValue then
                    match filterQuery (nonNull query.Value) with
                    | ValueSome filtered ->
                        context.Disable HttpLoggingFields.RequestQuery
                        context.AddParameter("QueryString", filtered)
                    | ValueNone -> ()
                ValueTask.CompletedTask

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
