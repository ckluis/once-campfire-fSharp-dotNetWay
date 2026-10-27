// Port of rust/crates/richtext/src/attachables.rs
//
// Resolving `<action-text-attachment>` nodes to what they attach, and rendering each attachable's
// partial, as Action Text, Lexxy and Campfire's extensions do.
namespace Campfire.RichText

open System
open System.Text
open Campfire.Rails
open Campfire.Rails.Ruby
open Campfire.RichText.RubyExt
open Campfire.RichText.Unicode

/// What the app knows about a user, for rendering `users/_mention.html.erb`. Plain text and
/// mentions only read `Id` and `Name`; the other fields are only rendered.
type MentionUser =
    { Id: int64
      Name: string
      /// `User#title`: name and bio joined with " – ".
      Title: string
      /// `user.attachable_sgid`: a freshly minted SGID for the "attachable" purpose.
      AttachableSgid: string
      /// `user_path(user)`
      UserPath: string
      /// `fresh_user_avatar_path(user)`
      AvatarPath: string }

/// A record `GlobalID.find` located.
[<RequireQualifiedAccess>]
type GidLookup =
    | User of MentionUser
    /// Found, but not a `User`: the invalid-signature fallback ignores it.
    | OtherModel
    /// `GlobalID.find` returned nil or raised `ActiveRecord::RecordNotFound`.
    | NotFound
    /// `GlobalID.find` raised anything else (an unknown model constant, say).
    | Raises

/// What a signed GlobalID verified for the "attachable" purpose points at.
[<RequireQualifiedAccess>]
type SignedLookup =
    /// The signature verified and the user exists.
    | User of MentionUser
    /// The signature verified (`SignedGlobalID.parse` succeeds) but the record is gone.
    | MissingRecord of modelName: string
    /// Bad signature, wrong purpose, expired, or not an SGID at all.
    | Invalid

/// The app's access to its records. RailsCompat verifies signatures; this crate only needs the
/// two lookups Action Text performs.
type IAttachableResolver =
    /// `GlobalID::Locator.locate_signed(sgid, for: "attachable")` (and, when that finds nothing,
    /// whether `SignedGlobalID.parse(sgid, for: "attachable")` still verifies). Campfire's only
    /// attachables are users (mentions).
    abstract LocateSigned: sgid: string -> SignedLookup

    /// `GlobalID.find(gid)` for a `gid://` URI string, with no signature involved.
    abstract FindGid: gid: string -> GidLookup

/// Everything rendering needs from the request and the app.
type RenderContext =
    { Resolver: IAttachableResolver
      /// `Current.request_host`
      RequestHost: string option }

/// `ActionText::Attachment::OpengraphEmbed` (reference/lib/rails_ext/actiontext_opengraph_embeds.rb)
type OpengraphEmbed =
    { Href: string option
      Url: string option
      Filename: string option
      Description: string option }

[<RequireQualifiedAccess>]
type Attachable =
    | User of MentionUser
    | OpengraphEmbed of OpengraphEmbed
    /// `ActionText::Attachables::ContentAttachment`
    | Content of content: string
    /// `ActionText::Attachables::RemoteImage`
    | RemoteImage of url: string * width: string option * height: string option
    /// Lexxy's `ActionText::Attachables::RemoteVideo`
    | RemoteVideo of url: string * contentType: string * width: string option * height: string option * filename: string option
    /// `ActionText::Attachables::MissingAttachable`, remembering the model a still-valid SGID named
    | Missing of signedModel: string option

/// The attachment node's attributes an attachment reads, plus its resolved attachable.
type Attachment =
    { Attachable: Attachable
      Caption: string option }

/// A plain-text representation replaces the attachment node: strings are parsed as markup in the
/// node's parent, while a content attachment's fragment is moved in as is.
[<RequireQualifiedAccess>]
type PlainTextRepresentation =
    | Markup of string
    | Fragment of string

module Attachables =
    [<Literal>]
    let MentionContentType = "application/vnd.campfire.mention"

    [<Literal>]
    let OpengraphEmbedContentType = "application/vnd.actiontext.opengraph-embed"

    [<Literal>]
    let private TwitterAvatarUrlPrefix = "https://pbs.twimg.com/profile_images"

    /// `attachable_content_type`, which only some attachables define.
    let attachableContentType (attachable: Attachable) : Result<string, RenderError> =
        match attachable with
        | Attachable.User _ -> Ok MentionContentType
        | Attachable.OpengraphEmbed _ -> Ok OpengraphEmbedContentType
        | _ -> Error(Raised "NoMethodError: attachable_content_type")

    // --- Resolution --------------------------------------------------------------------------

    /// `(?m)^<prefix>(/.+|$)`: some line is the prefix alone, or the prefix and a slash and something.
    let private matchesMediaType (prefix: string) (s: string) : bool =
        let mutable found = false
        let mutable lineStart = 0
        while not found && lineStart <= s.Length do
            if String.CompareOrdinal(s, lineStart, prefix, 0, prefix.Length) = 0 && lineStart + prefix.Length <= s.Length then
                let idx = lineStart + prefix.Length
                if idx = s.Length || s[idx] = '\n' then found <- true
                elif s[idx] = '/' && idx + 1 < s.Length && s[idx + 1] <> '\n' then found <- true
            if not found then
                match s.IndexOf('\n', lineStart) with
                | -1 -> lineStart <- s.Length + 1
                | nl -> lineStart <- nl + 1
        found

    /// `/application\/vnd.actiontext.opengraph-embed/`, where the dots are any character but a newline.
    let private isOpengraphContentType (s: string) : bool =
        let pattern = "application/vnd.actiontext.opengraph-embed"
        let matchAt (start: int) =
            let mutable i = start
            let mutable ok = true
            let mutable p = 0
            while ok && p < pattern.Length do
                if i >= s.Length then
                    ok <- false
                elif pattern[p] = '.' then
                    if s[i] = '\n' then ok <- false
                    else i <- i + (if Char.IsHighSurrogate s[i] && i + 1 < s.Length && Char.IsLowSurrogate s[i + 1] then 2 else 1)
                elif s[i] = pattern[p] then
                    i <- i + 1
                else
                    ok <- false
                p <- p + 1
            ok
        let mutable found = false
        let mutable start = 0
        while not found && start < s.Length do
            if matchAt start then found <- true
            start <- start + 1
        found

    /// `(gid://campfire/[^/]+/[0-9]+)` over bytes: the first place it matches.
    let private findMarshaledGid (bytes: byte[]) : string option =
        let prefix = Encoding.ASCII.GetBytes "gid://campfire/"
        let mutable result = None
        let mutable start = 0
        while result.IsNone && start + prefix.Length < bytes.Length do
            if bytes.AsSpan(start, prefix.Length).SequenceEqual(ReadOnlySpan prefix) then
                let mutable i = start + prefix.Length
                while i < bytes.Length && bytes[i] <> byte '/' do
                    i <- i + 1
                // [^/]+ needs a byte, then a slash and at least one digit
                if i > start + prefix.Length && i < bytes.Length then
                    let digits = i + 1
                    let mutable e = digits
                    while e < bytes.Length && bytes[e] >= byte '0' && bytes[e] <= byte '9' do
                        e <- e + 1
                    if e > digits then result <- Some(Encoding.UTF8.GetString(bytes, start, e - start))
            start <- start + 1
        result

    /// `Base64.strict_decode64(message) rescue Base64.urlsafe_decode64(message)`
    let private decodeBase64 (message: string) : Result<byte[], RenderError> =
        match RailsEncoding.strictDecode message |> Option.orElse (RailsEncoding.urlsafeDecode message) with
        | Some bytes -> Ok bytes
        | None -> Error(Raised "ArgumentError: invalid base64")

    /// `attachable_from_possibly_expired_sgid`: reads the GlobalID out of an SGID without checking its
    /// signature, and only ever returns a User.
    let attachableFromPossiblyExpiredSgid (sgid: string voption) (ctx: RenderContext) : Result<MentionUser option, RenderError> =
        match sgid with
        | ValueNone -> Ok None
        | ValueSome sgid ->
            // `sgid.split("--").first`: Ruby drops trailing empty fields, so "" and "--" have no first
            let fields = sgid.Split "--"
            if fields |> Array.forall (fun f -> f.Length = 0) then
                Ok None
            else
                let message = fields[0]
                result {
                    let! decoded = decodeBase64 message
                    // Ruby's JSON parser takes the bytes as UTF-8 without validating what's inside strings
                    let validUtf8 =
                        try
                            UTF8Encoding(false, true).GetString decoded |> ignore
                            true
                        with :? DecoderFallbackException ->
                            false
                    let! json =
                        match jsonParse (UTF8Encoding(false, false).GetString decoded) with
                        | Some json -> Ok json
                        | None when validUtf8 -> Error(Raised "JSON::ParserError")
                        // The parser error quotes the invalid bytes, and logging that message raises in turn
                        | None -> Error(Unrenderable "JSON::ParserError")
                    let! rails =
                        match json with
                        | Value.Object _ ->
                            match json.TryGet "_rails" with
                            | None
                            | Some Value.Null -> Ok None
                            | Some(Value.Object _ as o) -> Ok(Some o)
                            | Some _ -> Error(Raised "TypeError: dig")
                        | _ -> Error(Raised "NoMethodError: dig")
                    let truthy (v: Value option) =
                        match v with
                        | Some Value.Null
                        | Some(Value.Bool false) -> None
                        | other -> other
                    let railsGet (key: string) = rails |> Option.bind (fun r -> r.TryGet key)
                    let! gid =
                        match truthy (railsGet "data") with
                        // GlobalID.find of anything but a string finds nothing
                        | Some data -> Ok data.AsString
                        | None ->
                            match truthy (railsGet "message") with
                            | Some(Value.String message) ->
                                // Rails 7 Marshal-dumped the GID. The signature isn't verified, so the dump
                                // can't be safely loaded; the GID is matched out of its bytes instead.
                                decodeBase64 message |> Result.map findMarshaledGid
                            | Some _ -> Error(Raised "NoMethodError: unpack1")
                            | None -> Ok None
                    match gid with
                    | None -> return None
                    | Some gid ->
                        match ctx.Resolver.FindGid gid with
                        | GidLookup.User user -> return Some user
                        | GidLookup.OtherModel
                        | GidLookup.NotFound -> return None
                        | GidLookup.Raises -> return! Error(Raised "GlobalID.find")
                }

    /// `ActionText::Attachable.from_node`, with Lexxy's RemoteVideo fallback for missing attachables.
    /// `Content#attachables` (and so `Message#mentionees`) uses this directly, without Campfire's
    /// invalid-signature fallback.
    let actionTextAttachableFrom (attrs: Attrs) (ctx: RenderContext) : Attachable =
        let signed =
            match attrs "sgid" with
            | ValueSome sgid -> ctx.Resolver.LocateSigned sgid
            | ValueNone -> SignedLookup.Invalid
        match signed with
        | SignedLookup.User user -> Attachable.User user
        | _ ->
            let contentType = attrs "content-type"
            let isContent =
                match attrs "content", contentType with
                | ValueSome content, ValueSome t when t.Contains "html" && not (isBlank content) -> Some content
                | _ -> None
            match isContent with
            | Some content -> Attachable.Content content
            | None ->
                let opt (v: string voption) = match v with ValueSome s -> Some s | ValueNone -> None
                let fromUrl =
                    match attrs "url" with
                    | ValueSome url ->
                        let t = defaultValueArg contentType ""
                        if matchesMediaType "image" t then
                            Some(Attachable.RemoteImage(url, opt (attrs "width"), opt (attrs "height")))
                        elif matchesMediaType "video" t then
                            Some(
                                Attachable.RemoteVideo(
                                    url,
                                    t,
                                    opt (attrs "width"),
                                    opt (attrs "height"),
                                    opt (attrs "filename")
                                )
                            )
                        else
                            None
                    | ValueNone -> None
                match fromUrl with
                | Some attachable -> attachable
                | None ->
                    Attachable.Missing(
                        match signed with
                        | SignedLookup.MissingRecord modelName -> Some modelName
                        | _ -> None
                    )

    // --- Opengraph embeds --------------------------------------------------------------------

    let private hasClass (node: AngleSharp.Dom.INode) (cls: string) : bool =
        match Html5.attr node "class" with
        | ValueSome c -> c.Split([| ' '; '\t'; '\n'; '\r' |]) |> Array.exists (fun token -> token = cls)
        | ValueNone -> false

    let private isAsciiAlphabetic (c: char) = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')

    let private namedHost (host: string) : Result<bool, RenderError> =
        if isBlank host || host.Contains '%' || not (host.Contains '.') then
            Ok false
        else
            // `host.split(".").last`: Ruby drops trailing empty labels, and nil.match? raises
            let trimmed = host.TrimEnd '.'
            if trimmed.Length = 0 then
                Error(Raised "NoMethodError: match?")
            else
                let label = trimmed.Substring(trimmed.LastIndexOf '.' + 1)
                Ok(label |> Seq.exists isAsciiAlphabetic && not (label.ToLowerInvariant().StartsWith("0x", StringComparison.Ordinal)))

    let private canonicalHost (host: string) : string =
        let lower = Chars.toLowercase host
        if lower.EndsWith '.' then lower.Substring(0, lower.Length - 1) else lower

    let private elsewhere (host: string option) (requestHost: string) : Result<bool, RenderError> =
        match host with
        | None -> Ok false
        | Some host ->
            result {
                let! named = namedHost host
                if not named then return false else return canonicalHost host <> canonicalHost requestHost
            }

    /// RFC 3986's character rules (plans/design-richtext.md R8): printable ASCII only, none of `"<>\\^`{|}`, at most one `#`,
    /// every `%` followed by two hex digits. What Ruby's `URI.parse` refuses outright (`URI::InvalidURIError`). The query
    /// (from the first `?` to the first `#`) is exempt, as in Ruby's parser (`[^#]*`): a link to `?x=<y>` is a link.
    let private isHex (c: char) = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')

    /// Where the query is: from the first `?` to the first `#` (start -1 when there is none).
    let private querySpan (value: string) : struct (int * int) =
        let hash = value.IndexOf '#'
        let question = value.IndexOf '?'
        let start = if question >= 0 && (hash < 0 || question < hash) then question else -1
        struct (start, (if start < 0 then -1 elif hash < 0 then value.Length else hash))

    /// `URI::Generic#query=` refuses (`InvalidURIError`, so no link) a `%` followed by two non-hex characters.
    let private queryEscapeRaises (value: string) : bool =
        let struct (start, finish) = querySpan value
        if start < 0 then
            false
        else
            let query = value.Substring(start + 1, finish - start - 1).Replace("\t", "").Replace("\r", "").Replace("\n", "")
            let mutable bad = false
            for w in 0 .. query.Length - 3 do
                if query[w] = '%' && not (isHex query[w + 1]) && not (isHex query[w + 2]) then bad <- true
            bad

    let rfc3986Characters (value: string) : bool =
        let struct (queryStart, queryEnd) = querySpan value
        let mutable ok = true
        let mutable hashes = 0
        let mutable i = 0
        while ok && i < value.Length do
            if i = queryStart then
                i <- queryEnd
            else
                let c = value[i]
                if c < '\u0021' || c > '\u007e' || "\"<>\\^`{|}".IndexOf c >= 0 then
                    ok <- false
                elif c = '#' then
                    hashes <- hashes + 1
                    if hashes > 1 then ok <- false
                elif c = '%' && not (i + 2 < value.Length && isHex value[i + 1] && isHex value[i + 2]) then
                    ok <- false
                i <- i + 1
        ok

    /// An absolute `System.Uri` (R8) for a string that passes the character check.
    let tryAbsoluteUri (value: string) : Uri voption =
        if not (rfc3986Characters value) then
            ValueNone
        else
            match Uri.TryCreate(value, UriKind.Absolute) with
            | true, uri -> ValueSome(nonNull uri)
            | _ -> ValueNone

    /// `web_url`: an absolute http(s) URL on a named host other than this Campfire's. `System.Uri` behind the character
    /// check (R8); Ruby's `InvalidComponentError` and the few hand-made URLs it accepts and `System.Uri` refuses are class B.
    let webUrl (value: string voption) (requestHost: string) : Result<string option, RenderError> =
        match value with
        | ValueSome value when not (isBlank value) ->
            match tryAbsoluteUri value with
            | ValueNone -> Ok None
            | ValueSome _ when queryEscapeRaises value -> Ok None
            | ValueSome parsed ->
                if parsed.Scheme = Uri.UriSchemeHttp || parsed.Scheme = Uri.UriSchemeHttps then
                    elsewhere (Some parsed.IdnHost) requestHost
                    |> Result.map (fun other -> if other then Some value else None)
                else
                    Ok None
        | _ -> Ok None

    let private opt (v: string voption) : string option =
        match v with
        | ValueSome s -> Some s
        | ValueNone -> None

    let rec private hasClassAncestor (node: AngleSharp.Dom.INode) (cls: string) : bool =
        match node.Parent with
        | null -> false
        | parent -> hasClass parent cls || hasClassAncestor parent cls

    /// `attributes_from_content`: the details Lexxy serializes as the embed's content markup.
    let private embedFromContent (content: string) (host: string) : Result<OpengraphEmbed, RenderError> =
        result {
            let! root = Html5.parseBody content |> Result.mapError Parse
            let all = Html5.descendants root
            let withClass (cls: string) = all |> List.tryFind (fun n -> hasClass n cls)
            let title = withClass "og-embed__title"
            let link =
                title
                |> Option.bind (fun t -> Html5.descendants t |> List.tryFind (fun n -> Html5.isNamed n "a"))
            let image =
                all
                |> List.tryFind (fun n ->
                    Html5.isNamed n "img"
                    && hasClassAncestor n "og-embed__image")
            let description = withClass "og-embed__description"
            let! href = webUrl (link |> Option.map (fun l -> Html5.attr l "href") |> Option.defaultValue ValueNone) host
            let! url = webUrl (image |> Option.map (fun i -> Html5.attr i "src") |> Option.defaultValue ValueNone) host
            return
                { Href = href
                  Url = url
                  Filename = (match link with Some l -> Some l | None -> title) |> Option.map (fun n -> RubyString.strip (Html5.textContent n))
                  Description = description |> Option.map (fun n -> RubyString.strip (Html5.textContent n)) }
        }

    /// `ActionText::Attachment::OpengraphEmbed.from_node`
    let opengraphEmbedFrom (attrs: Attrs) (ctx: RenderContext) : Result<OpengraphEmbed option, RenderError> =
        match attrs "content-type" with
        | ValueNone -> Ok None
        | ValueSome contentType when not (isOpengraphContentType contentType) -> Ok None
        | ValueSome _ ->
            let host = defaultArg ctx.RequestHost ""
            result {
                if (presence (attrs "filename")).IsSome then
                    let! href = webUrl (attrs "href") host
                    let! url = webUrl (attrs "url") host
                    return
                        Some
                            { Href = href
                              Url = url
                              Filename = opt (attrs "filename")
                              Description = opt (attrs "caption") }
                else
                    let! embed = embedFromContent (defaultValueArg (attrs "content") "") host
                    return Some embed
            }

    let twitterAvatar (embed: OpengraphEmbed) : bool =
        (defaultArg embed.Url "").StartsWith(TwitterAvatarUrlPrefix, StringComparison.Ordinal)

    let private attachableFrom (attrs: Attrs) (ctx: RenderContext) : Result<Attachable, RenderError> =
        result {
            match! opengraphEmbedFrom attrs ctx with
            | Some embed -> return Attachable.OpengraphEmbed embed
            | None ->
                match! attachableFromPossiblyExpiredSgid (attrs "sgid") ctx with
                | Some user -> return Attachable.User user
                | None -> return actionTextAttachableFrom attrs ctx
        }

    /// Campfire's `ActionText::Attachment.from_node` (reference/lib/rails_ext/action_text_attachables.rb):
    /// an opengraph embed, else a User found through a possibly invalid SGID, else Action Text's own
    /// lookup (as extended by Lexxy).
    let attachmentFrom (attrs: Attrs) (ctx: RenderContext) : Result<Attachment, RenderError> =
        attachableFrom attrs ctx
        |> Result.map (fun attachable ->
            { Attachable = attachable
              Caption = presence (attrs "caption") |> opt })

    // --- Partials ----------------------------------------------------------------------------

    /// reference/app/views/users/_mention.html.erb, with `avatar_tag` (users/avatars_helper.rb).
    let renderMention (user: MentionUser) : string =
        let h = Erb.htmlEscape
        "<span class=\"mention\" sgid=\""
        + h user.AttachableSgid
        + "\"><a title=\""
        + h user.Title
        + "\" class=\"btn avatar\" data-turbo-frame=\"_top\" href=\""
        + h user.UserPath
        + "\"><img aria-hidden=\"true\" src=\""
        + h user.AvatarPath
        + "\" width=\"48\" height=\"48\" /></a> "
        + h user.Name
        + "</span>\n"

    /// reference/app/views/action_text/attachables/_opengraph_embed.html.erb
    let renderOpengraphEmbed (embed: OpengraphEmbed) : string =
        let h = Erb.htmlEscape
        let title =
            match embed.Href, embed.Filename with
            | Some href, filename ->
                let text =
                    match filename with
                    | Some f -> h (truncate f 280 "…")
                    | None -> h href
                "<a rel=\"noreferrer\" target=\"_blank\" href=\"" + h href + "\">" + text + "</a>"
            | None, Some f -> h (truncate f 280 "…")
            | None, None -> ""
        let html = StringBuilder()
        html
            .Append("<figure class=\"attachment attachment--content attachment--og\">\n  <actiontext-opengraph-embed>\n    <div class=\"og-embed gap ")
            .Append(if twitterAvatar embed then "og-embed--twitter-avatar" else "")
            .Append("\">\n      <div class=\"og-embed__content\">\n        <div class=\"og-embed__title\">\n          ")
            .Append(title)
            .Append("\n        </div>\n        <div class=\"og-embed__description\">")
            .Append(h (truncate (defaultArg embed.Description "") 560 "…"))
            .Append("</div>\n      </div>\n")
        |> ignore
        match embed.Url with
        | Some url ->
            html
                .Append("        <div class=\"og-embed__image\">\n          <img src=\"")
                .Append(h url)
                .Append("\" class=\"image center\" alt=\"\">\n        </div>\n")
            |> ignore
        | None -> ()
        html.Append("    </div>\n  </actiontext-opengraph-embed>\n</figure>\n") |> ignore
        html.ToString()

    /// `(?mi)^[-a-z]+://|^(?:cid|data):|^//`: a line that starts like an asset URL.
    let private isAssetUri (s: string) : bool =
        // What Rust's (?i) folds: the letters, with the long s and the Kelvin sign standing for s and k.
        let fold (c: char) =
            if c >= 'A' && c <= 'Z' then char (int c + 32)
            elif c = 'ſ' then 's'
            elif c = 'K' then 'k'
            else c
        let matchesLine (start: int) =
            // [-a-z]+://
            let mutable i = start
            while i < s.Length && (s[i] = '-' || (let f = fold s[i] in f >= 'a' && f <= 'z')) do
                i <- i + 1
            let scheme = i > start && i + 2 < s.Length && s[i] = ':' && s[i + 1] = '/' && s[i + 2] = '/'
            // The run is greedy, and a slash can't be part of it, so no shorter run could do better.
            let cidOrData =
                let lower (n: int) = if start + n < s.Length then fold s[start + n] else '\000'
                (lower 0 = 'c' && lower 1 = 'i' && lower 2 = 'd' && lower 3 = ':')
                || (lower 0 = 'd' && lower 1 = 'a' && lower 2 = 't' && lower 3 = 'a' && lower 4 = ':')
            let slashes = start + 1 < s.Length && s[start] = '/' && s[start + 1] = '/'
            scheme || cidOrData || slashes
        let mutable found = false
        let mutable lineStart = 0
        while not found && lineStart <= s.Length do
            if matchesLine lineStart then found <- true
            else
                match s.IndexOf('\n', lineStart) with
                | -1 -> lineStart <- s.Length + 1
                | nl -> lineStart <- nl + 1
        found

    /// `image_tag(url, width:, height:)` for a remote image. Sources that aren't URLs go through the
    /// asset pipeline, which raises for anything it doesn't know; a rooted path passes through.
    let private imageTag (url: string) (width: string option) (height: string option) : Result<string, RenderError> =
        let src =
            if isBlank url then Ok ""
            elif isAssetUri url || url.StartsWith '/' then Ok url
            else Error(Raised "Propshaft::MissingAssetError")
        src
        |> Result.map (fun src ->
            let html = StringBuilder "<img"
            for (name, value) in [ "width", width; "height", height ] do
                match value with
                | Some v -> html.Append(' ').Append(name).Append("=\"").Append(Erb.htmlEscape v).Append('"') |> ignore
                | None -> ()
            html.Append(" src=\"").Append(Erb.htmlEscape src).Append("\" />") |> ignore
            html.ToString())

    /// `render_action_text_attachment(attachment)`: the attachable's partial, chomped. `renderContent`
    /// renders a nested content attachment's own content (`ContentAttachment#to_html`).
    let renderAttachment (attachment: Attachment) (renderContent: string -> Result<string, RenderError>) : Result<string, RenderError> =
        let caption () =
            match attachment.Caption with
            | Some caption ->
                "    <figcaption class=\"attachment__caption\">\n      " + Erb.htmlEscape caption + "\n    </figcaption>\n"
            | None -> ""
        let html =
            match attachment.Attachable with
            | Attachable.User user -> Ok(renderMention user)
            | Attachable.OpengraphEmbed embed -> Ok(renderOpengraphEmbed embed)
            // Rails asks the SGID's model for its missing partial, which only models that include
            // ActionText::Attachable as a concern have. User doesn't, so a mention of a deleted user
            // raised and blanked the whole message; every missing attachable is Action Text's ☒ here.
            | Attachable.Missing _ -> Ok "☒"
            | Attachable.Content content ->
                renderContent content
                |> Result.map (fun rendered -> "<figure class=\"attachment attachment--content\">\n  " + rendered + "\n</figure>\n")
            | Attachable.RemoteImage(url, width, height) ->
                imageTag url width height
                |> Result.map (fun tag -> "<figure class=\"attachment attachment--preview\">\n  " + tag + "\n" + caption () + "</figure>\n")
            | Attachable.RemoteVideo(url, contentType, width, height, _) ->
                let html = StringBuilder "<figure class=\"attachment attachment--preview attachment--video\">\n  <video controls=\"controls\""
                for (name, value) in [ "width", width; "height", height ] do
                    match value with
                    | Some v -> html.Append(' ').Append(name).Append("=\"").Append(Erb.htmlEscape v).Append('"') |> ignore
                    | None -> ()
                html
                    .Append(">\n    <source src=\"")
                    .Append(Erb.htmlEscape url)
                    .Append("\" type=\"")
                    .Append(Erb.htmlEscape contentType)
                    .Append("\">\n</video>")
                    .Append(caption ())
                    .Append("</figure>\n")
                |> ignore
                Ok(html.ToString())
        html |> Result.map chomp

    /// `Attachment#to_plain_text`
    let attachmentPlainText (attachment: Attachment) : PlainTextRepresentation =
        let caption = attachment.Caption
        match attachment.Attachable with
        | Attachable.User user -> PlainTextRepresentation.Markup("@" + user.Name)
        | Attachable.OpengraphEmbed _ -> PlainTextRepresentation.Markup ""
        | Attachable.Content content -> PlainTextRepresentation.Fragment content
        | Attachable.RemoteImage _ -> PlainTextRepresentation.Markup("[" + defaultArg caption "Image" + "]")
        | Attachable.RemoteVideo(_, _, _, _, filename) ->
            PlainTextRepresentation.Markup("[" + (caption |> Option.orElse filename |> Option.defaultValue "Video") + "]")
        | Attachable.Missing _ -> PlainTextRepresentation.Markup(defaultArg caption "")
