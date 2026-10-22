// Port of rust/crates/richtext/tests/reference_tests.rs
//
// Ports of the reference app's rich text tests: test/helpers/content_filters_test.rb,
// messages_helper_test.rb, rich_text_helper_test.rb, test/lib/rails_ext/*_test.rb,
// test/models/action_text_attachment_test.rb and the plain-text parts of the message tests.
module Campfire.RichText.Tests.ReferenceTests

open System
open System.Text
open Xunit
open Campfire.RailsCompat
open Campfire.Ruby
open Campfire.RichText
open Campfire.RichText.Tests.Support

// --- Both pipelines (unit 5.5) -----------------------------------------------------------------------------------------------
// Every test of this file runs twice, on the old pipeline and on `Next` (the tests do not run in parallel in this assembly).

let mutable private usingNext = false

/// `ActionText` for the pipeline under test.
module private ActionText =
    let messagePresentation body ctx =
        if usingNext then Next.ActionText.messagePresentation body ctx else Campfire.RichText.ActionText.messagePresentation body ctx
    let toPlainText body ctx =
        if usingNext then Next.ActionText.toPlainText body ctx else Campfire.RichText.ActionText.toPlainText body ctx
    let editableValue body ctx =
        if usingNext then Next.ActionText.editableValue body ctx else Campfire.RichText.ActionText.editableValue body ctx
    let mentionedUsers body ctx =
        if usingNext then Next.ActionText.mentionedUsers body ctx else Campfire.RichText.ActionText.mentionedUsers body ctx
    let withoutRecipientMentions plain name =
        if usingNext then Next.ActionText.withoutRecipientMentions plain name else Campfire.RichText.ActionText.withoutRecipientMentions plain name

let private davidSgid =
    "eyJfcmFpbHMiOnsiZGF0YSI6ImdpZDovL2NhbXBmaXJlL1VzZXIvMT9leHBpcmVzX2luIiwicHVyIjoiYXR0YWNoYWJsZSJ9fQ==--f7d8e8773314d3310320f3cdd08e5597bb51ca1a"

/// A Room's attachable SGID, as `rooms(:pets).to_sgid(expires_in: nil, for: "attachable")` mints it.
let private roomSgidMessage =
    "eyJfcmFpbHMiOnsiZGF0YSI6ImdpZDovL2NhbXBmaXJlL1Jvb20vMT9leHBpcmVzX2luIiwicHVyIjoiYXR0YWNoYWJsZSJ9fQ=="

let private david : MentionUser =
    { Id = 1L
      Name = "David"
      Title = "David – Founder"
      AttachableSgid = davidSgid
      UserPath = "/users/1"
      AvatarPath = "/users/1/avatar?v=1" }

type private Fixtures() =
    interface IAttachableResolver with
        member _.LocateSigned sgid = if sgid = davidSgid then SignedLookup.User david else SignedLookup.Invalid

        member _.FindGid gid =
            match (gid.Split '?').[0] with
            | "gid://campfire/User/1" -> GidLookup.User david
            | "gid://campfire/Room/1" -> GidLookup.OtherModel
            | _ -> GidLookup.NotFound

let private ctx = render (Fixtures()) (Some "once.campfire.test")

/// test_helper.rb's `mention_attachment_for(:david)`
let private mentionAttachmentForDavid () : string =
    let content = (Attachables.renderMention david).Replace("\"", "&quot;")
    $"<action-text-attachment sgid=\"{davidSgid}\" content-type=\"application/vnd.campfire.mention\" content=\"{content}\"></action-text-attachment>"

let private filtered (body: string) : string =
    if usingNext then
        let content = okR (Next.Content.load body ctx)
        Next.Content.toHtml (okR (Next.Filters.apply content ctx))
    else
        let content = okR (Content.load body ctx)
        Content.toHtml (okR (Filters.apply content ctx))

let private loaded (body: string) : string =
    if usingNext then Next.Content.toHtml (okR (Next.Content.load body ctx)) else Content.toHtml (okR (Content.load body ctx))

let private presentation (body: string) : string = okR (ActionText.messagePresentation body ctx)

let private basecampUnfurl =
    "<action-text-attachment content-type=\"application/vnd.actiontext.opengraph-embed\" url=\"https://basecamp.com/assets/general/opengraph.png\" href=\"https://basecamp.com/\" filename=\"Project management software, online collaboration\" caption=\"Trusted by millions.\"></action-text-attachment>"

let private twitterUnfurl =
    "<action-text-attachment content-type=\"application/vnd.actiontext.opengraph-embed\" url=\"https://pbs.twimg.com/ext_tw_video_thumb/1752476502791503873/pu/img/WEAqUgarUxWjPNHD.jpg\" href=\"https://twitter.com/dhh/status/1752476663303323939\" filename=\"DHH (@dhh)\" caption=\"We're playing.\"></action-text-attachment>"

let private contains (haystack: string) (needle: string) : bool = haystack.Contains(needle, StringComparison.Ordinal)

// --- test/helpers/content_filters_test.rb --------------------------------------------------------

[<Theory; InlineData(false); InlineData(true)>]
let ``entire_message_contains_an_unfurled_url`` (next: bool) =
    usingNext <- next
    let body = $"<div>https://basecamp.com/{basecampUnfurl}</div>"
    let result = filtered body
    Assert.True((loaded body <> result), result)
    Assert.True(contains result "<div><action-text-attachment")

[<Theory; InlineData(false); InlineData(true)>]
let ``entire_message_contains_an_unfurled_url_in_a_lexxy_body`` (next: bool) =
    usingNext <- next
    let body = $"<p><a href=\"https://basecamp.com/\">https://basecamp.com/</a></p>{basecampUnfurl}"
    let result = filtered body
    Assert.False(contains result ">https://basecamp.com/</a>")
    Assert.True(contains result "<action-text-attachment")

[<Theory; InlineData(false); InlineData(true)>]
let ``message_includes_additional_text_besides_an_unfurled_url`` (next: bool) =
    usingNext <- next
    let body = $"<div>Hello https://basecamp.com/{basecampUnfurl}</div>"
    let result = filtered body
    Assert.Equal(loaded body, result)
    Assert.True(contains result "<div>Hello https://basecamp.com/<action-text-attachment")

[<Theory; InlineData(false); InlineData(true)>]
let ``unfurled_tweet_with_an_avatar_image_gets_the_twitter_avatar_treatment`` (next: bool) =
    usingNext <- next
    let body =
        "<div>https://twitter.com/37signals/status/1750290547908952568<action-text-attachment content-type=\"application/vnd.actiontext.opengraph-embed\" url=\"https://pbs.twimg.com/profile_images/1671940407633010689/9P5gi6LF_200x200.jpg\" href=\"https://twitter.com/37signals/status/1750290547908952568\" filename=\"37signals (@37signals)\" caption=\"We're back up on all apps, everyone.\"></action-text-attachment></div>"
    Assert.True(contains (presentation body) "og-embed--twitter-avatar")

[<Theory; InlineData(false); InlineData(true)>]
let ``unfurled_tweet_with_an_avatar_image_in_a_lexxy_body_gets_the_twitter_avatar_treatment`` (next: bool) =
    usingNext <- next
    let content =
        "<actiontext-opengraph-embed><div class=\"og-embed gap\"><div class=\"og-embed__content\"><div class=\"og-embed__title\"><a href=\"https://twitter.com/x/status/1\">Tweet</a></div><div class=\"og-embed__description\">desc</div></div><div class=\"og-embed__image\"><img src=\"https://pbs.twimg.com/profile_images/x.jpg\" class=\"image center\" alt=\"\" /></div></div></actiontext-opengraph-embed>"
    let body =
        $"<p><a href=\"https://twitter.com/x/status/1\">https://twitter.com/x/status/1</a></p><action-text-attachment content-type=\"application/vnd.actiontext.opengraph-embed\" content=\"{Erb.htmlEscape content}\"></action-text-attachment>"
    Assert.True(contains (presentation body) "og-embed--twitter-avatar")

[<Theory; InlineData(false); InlineData(true)>]
let ``unfurled_tweet_with_a_content_image_is_not_styled_as_an_avatar`` (next: bool) =
    usingNext <- next
    let body =
        "<div>https://twitter.com/dhh/status/1748445489648050505<action-text-attachment content-type=\"application/vnd.actiontext.opengraph-embed\" url=\"https://pbs.twimg.com/media/GEO5l04bsAA9f6H.jpg\" href=\"https://twitter.com/dhh/status/1748445489648050505\" filename=\"DHH (@dhh)\" caption=\"MIT\"></action-text-attachment></div>"
    Assert.False(contains (presentation body) "og-embed--twitter-avatar")

[<Theory; InlineData(false); InlineData(true)>]
let ``entire_message_contains_an_unfurled_url_from_x_com_but_unfurls_to_twitter_com`` (next: bool) =
    usingNext <- next
    for text in [ "https://x.com/dhh/status/1752476663303323939"; "https://x.com/dhh/status/1752476663303323939?s=20" ] do
        let body = $"<div>{text}{twitterUnfurl}</div>"
        let result = filtered body
        Assert.True((loaded body <> result), result)
        Assert.True(contains result "<div><action-text-attachment")

[<Theory; InlineData(false); InlineData(true)>]
let ``message_keeps_strikethrough_underline_and_code_block_formatting`` (next: bool) =
    usingNext <- next
    let html = presentation "<p>Hello <s>struck</s> <u>under</u> <mark>marked</mark></p><pre data-language=\"ruby\">def x<br>end</pre>"
    Assert.True(contains html "<s>struck</s>")
    Assert.True(contains html "<u>under</u>")
    Assert.True(contains html "<mark>marked</mark>")
    Assert.True(contains html "<pre data-language=\"ruby\">")

[<Theory; InlineData(false); InlineData(true)>]
let ``message_contains_a_forbidden_tag`` (next: bool) =
    usingNext <- next
    Assert.Equal("Hello World", filtered "Hello <img src=\"https://ssecurityrise.com/tests/billionlaughs-cache.svg\">World")

[<Theory; InlineData(false); InlineData(true)>]
let ``message_with_a_link_using_an_unsafe_uri_scheme`` (next: bool) =
    usingNext <- next
    let result = filtered "<div><a href=\"javascript:alert(1)\">x</a></div>"
    Assert.False(contains result "javascript:")
    Assert.True(contains result "<a>x</a>")

[<Theory; InlineData(false); InlineData(true)>]
let ``message_with_an_event_handler_attribute_on_an_allowed_tag`` (next: bool) =
    usingNext <- next
    let result = filtered "<div><a href=\"/x\" onmouseover=\"alert(1)\">x</a> <span onclick=\"alert(2)\">y</span></div>"
    Assert.False(contains result "onmouseover")
    Assert.False(contains result "onclick")
    Assert.True(contains result "<a href=\"/x\">x</a>")
    Assert.True(contains result "<span>y</span>")

[<Theory; InlineData(false); InlineData(true)>]
let ``message_with_a_data_uri_link`` (next: bool) =
    usingNext <- next
    let result = filtered "<div><a href=\"data:text/html,pwned\">x</a></div>"
    Assert.False(contains result "data:")
    Assert.True(contains result "<a>x</a>")

[<Theory; InlineData(false); InlineData(true)>]
let ``message_with_a_safe_link_and_formatting_is_preserved`` (next: bool) =
    usingNext <- next
    let result =
        filtered
            "<div><a href=\"https://example.com\">example</a> <strong>bold</strong> <code>code</code><ul><li>one</li><li>two</li></ul></div>"
    Assert.True(contains result "<a href=\"https://example.com\">example</a>")
    Assert.True(contains result "<strong>bold</strong>")
    Assert.True(contains result "<code>code</code>")
    Assert.True(contains result "<ul><li>one</li><li>two</li></ul>")

[<Theory; InlineData(false); InlineData(true)>]
let ``sanitize_attributes_neutralizes_unsafe_input_and_preserves_benign_content`` (next: bool) =
    usingNext <- next
    let body =
        $"<div><a href=\"javascript:alert(1)\" onclick=\"x()\">link</a> <a href=\"data:text/html,pwned\">data</a> <span class=\"cf-twitter-avatar\" onmouseover=\"y()\">avatar</span> <img src=\"https://evil.example/x.svg\"> Hey {mentionAttachmentForDavid ()}</div>"
    let result =
        if usingNext then
            Next.Content.toHtml (okR (Next.Filters.sanitizeAttributes (okR (Next.Content.load body ctx))))
        else
            Content.toHtml (okR (Filters.sanitizeAttributes (okR (Content.load body ctx))))
    for forbidden in [ "javascript:"; "data:text/html"; "onclick"; "onmouseover"; "evil.example" ] do
        Assert.False(contains result forbidden, $"{forbidden} in {result}")
    Assert.True(contains result "<span class=\"cf-twitter-avatar\">avatar</span>")
    Assert.True(contains result ">link<")
    Assert.True(contains result $"<action-text-attachment sgid=\"{davidSgid}\"")

[<Theory; InlineData(false); InlineData(true)>]
let ``message_with_formatting_saved_under_trix_renders_unchanged`` (next: bool) =
    usingNext <- next
    let body =
        "<div>Hello <strong>bold</strong> <em>it</em> <del>gone</del> <a href=\"https://example.com/\">link</a><br>second line</div><h1>Heading</h1><blockquote>quoted</blockquote><pre>line 1\nline 2</pre><ul><li>one</li></ul><ol><li>first</li></ol>"
    Assert.Equal(body, filtered body)
    Assert.True(contains (presentation body) body)

[<Theory; InlineData(false); InlineData(true)>]
let ``message_with_a_table_keeps_the_table`` (next: bool) =
    usingNext <- next
    let body =
        "<figure class=\"lexxy-content__table-wrapper\"><table><tbody><tr><th><p>Name</p></th></tr><tr><td><p>Jason</p></td></tr></tbody></table></figure>"
    Assert.Equal(body, filtered body)
    let html = presentation body
    let table = html.IndexOf("<table>", StringComparison.Ordinal)
    Assert.True(table >= 0)
    let fromTable = html.Substring table
    Assert.True(contains fromTable "<th><p>Name</p></th>" && contains fromTable "<td><p>Jason</p></td>")

[<Theory; InlineData(false); InlineData(true)>]
let ``message_with_a_mention_attachment`` (next: bool) =
    usingNext <- next
    let result = filtered $"<div>Hey {mentionAttachmentForDavid ()}</div>"
    Assert.True(
        contains result $"<action-text-attachment sgid=\"{davidSgid}\" content-type=\"application/vnd.campfire.mention\" content=\""
    )

// --- test/helpers/messages_helper_test.rb --------------------------------------------------------

[<Theory; InlineData(false); InlineData(true)>]
let ``message_presentation_neutralizes_unsafe_uri_schemes_in_links`` (next: bool) =
    usingNext <- next
    let html = presentation "<div><a href=\"javascript:alert(1)\">x</a></div>"
    Assert.False(contains html "javascript:")
    Assert.True(contains html "<a>x</a>")

[<Theory; InlineData(false); InlineData(true)>]
let ``message_presentation_strips_event_handler_attributes_from_allowed_tags`` (next: bool) =
    usingNext <- next
    let html = presentation "<div><a href=\"/x\" onmouseover=\"alert(1)\">x</a></div>"
    Assert.False(contains html "onmouseover")
    Assert.True(contains html "<a href=\"/x\">x</a>")

[<Theory; InlineData(false); InlineData(true)>]
let ``message_presentation_preserves_safe_links_and_formatting`` (next: bool) =
    usingNext <- next
    let html = presentation "<div><a href=\"https://example.com\">example</a> <strong>bold</strong></div>"
    Assert.True(contains html "<a href=\"https://example.com\">example</a>")
    Assert.True(contains html "<strong>bold</strong>")

// --- test/helpers/rich_text_helper_test.rb -------------------------------------------------------

let private editableAttachment (body: string) : string * string =
    let value = (okR (ActionText.editableValue body ctx)).Value
    let dom = Dom()
    let root = okP (dom.ParseFragment value)
    let node = dom.Descendants root |> Seq.find (fun n -> dom.IsNamed(n, "action-text-attachment"))
    (dom.Attr(node, "content-type")).Value, (dom.Attr(node, "content")).Value

[<Theory; InlineData(false); InlineData(true)>]
let ``editable_body_renders_legacy_opengraph_embeds_into_the_content_attribute`` (next: bool) =
    usingNext <- next
    let (_, content) =
        editableAttachment
            "<div>https://example.com/ <action-text-attachment content-type=\"application/vnd.actiontext.opengraph-embed\" url=\"https://example.com/image.png\" href=\"https://example.com/\" filename=\"Example title\" caption=\"Example description\"></action-text-attachment></div>"
    // A Trix-era embed has a url, so Lexxy leaves the content as the rendered partial
    Assert.True(contains content "<a rel=\"noreferrer\" target=\"_blank\" href=\"https://example.com/\">Example title</a>")
    Assert.True(contains content "<div class=\"og-embed__description\">Example description</div>")
    Assert.True(contains content "<img src=\"https://example.com/image.png\"")

[<Theory; InlineData(false); InlineData(true)>]
let ``editable_body_rebuilds_a_hand_written_embed_from_its_validated_details`` (next: bool) =
    usingNext <- next
    let content =
        "<actiontext-opengraph-embed data-controller=\"pwn\" data-action=\"click->pwn#run\"> <div class=\"og-embed\"><div class=\"og-embed__title\"><a href=\"/rooms/1\">Free cookies</a></div> <div class=\"og-embed__image\"><img src=\"/rooms/1/avatar\" data-action=\"load->pwn#run\"></div></div> </actiontext-opengraph-embed>"
    let body =
        $"<p><action-text-attachment content-type=\"application/vnd.actiontext.opengraph-embed\" url=\"https://example.com/image.png\" content=\"{Erb.htmlEscape content}\"></action-text-attachment></p>"
    let (_, rebuilt) = editableAttachment body
    Assert.True(contains rebuilt "Free cookies")
    Assert.False(contains rebuilt "rooms/1")
    Assert.False(contains rebuilt "data-")
    Assert.True(not (contains rebuilt "<a ") && not (contains rebuilt "<img"))

[<Theory; InlineData(false); InlineData(true)>]
let ``editable_body_restores_the_content_type_of_a_mention_edited_under_trix`` (next: bool) =
    usingNext <- next
    let (contentType, content) =
        editableAttachment
            $"<div>Hey <action-text-attachment sgid=\"{davidSgid}\" content-type=\"application/octet-stream\"></action-text-attachment></div>"
    Assert.Equal("application/vnd.campfire.mention", contentType)
    Assert.True(contains content "David")

[<Theory; InlineData(false); InlineData(true)>]
let ``editable_body_leaves_bodies_without_attachments_unchanged`` (next: bool) =
    usingNext <- next
    Assert.Equal(Some "<p>Plain text</p>", okR (ActionText.editableValue "<p>Plain text</p>" ctx))

// --- test/lib/rails_ext/action_text_attachables_test.rb, test/models/action_text_attachment_test.rb

let private attachmentPlainText (sgid: string option) : string =
    let attribute =
        match sgid with
        | Some s -> $" sgid=\"{s}\""
        | None -> ""
    okR (ActionText.toPlainText $"<action-text-attachment{attribute}></action-text-attachment>" ctx)

[<Theory; InlineData(false); InlineData(true)>]
let ``from_node_with_a_valid_sgid`` (next: bool) =
    usingNext <- next
    Assert.Equal("@David", attachmentPlainText (Some davidSgid))

[<Theory; InlineData(false); InlineData(true)>]
let ``from_node_with_a_rails_7_sgid`` (next: bool) =
    usingNext <- next
    // Base64.urlsafe_encode64(Marshal.dump("gid://campfire/User/1"))
    let marshaled =
        Array.concat
            [ [| 0x04uy; 0x08uy; byte 'I'; byte '"'; 0x1auy |]
              Encoding.ASCII.GetBytes "gid://campfire/User/1"
              [| 0x06uy; byte ':'; 0x06uy; byte 'E'; byte 'T' |] ]
    let message = RailsEncoding.urlsafeEncodePadded marshaled
    let payload = $"{{\"_rails\":{{\"message\":\"{message}\",\"exp\":null,\"pur\":\"attachable\"}}}}"
    let sgid = $"{Convert.ToBase64String(Encoding.UTF8.GetBytes payload)}--invalidsignature"
    Assert.Equal("@David", attachmentPlainText (Some sgid))

[<Theory; InlineData(false); InlineData(true)>]
let ``lookup_user_attachable_with_invalid_signature`` (next: bool) =
    usingNext <- next
    let message = (davidSgid.Split "--").[0]
    Assert.Equal("@David", attachmentPlainText (Some $"{message}--invalid"))

[<Theory; InlineData(false); InlineData(true)>]
let ``lookup_invalid_sgid_for_an_attachable_requiring_a_valid_sgid`` (next: bool) =
    usingNext <- next
    // A tampered SGID for any model but User is a missing attachable, which renders as ☒
    for sgid in [ $"{roomSgidMessage}--invalid"; $"{roomSgidMessage}--f7d8e8773314d3310320f3cdd08e5597bb51ca1ainvalid" ] do
        Assert.Equal("", attachmentPlainText (Some sgid))
        let html = presentation $"<p><action-text-attachment sgid=\"{sgid}\"></action-text-attachment></p>"
        Assert.True(contains html "☒", html)
        Assert.False(contains html "mention")

[<Theory; InlineData(false); InlineData(true)>]
let ``lookup_attachable_with_nil_sgid`` (next: bool) =
    usingNext <- next
    let html = presentation "<p><action-text-attachment content-type=\"application/pdf\"></action-text-attachment></p>"
    Assert.True(contains html "☒")

// --- test/lib/rails_ext/actiontext_opengraph_embeds_test.rb --------------------------------------

let private webUrl (value: string) (host: string) : string option = okR (Attachables.webUrl (ValueSome value) host)

[<Theory; InlineData(false); InlineData(true)>]
let ``keeps_absolute_http_and_https_links_and_images`` (next: bool) =
    usingNext <- next
    Assert.Equal(Some "http://example.com/page", webUrl "http://example.com/page" "")
    Assert.Equal(Some "https://example.com/image.png", webUrl "https://example.com/image.png" "")

[<Theory; InlineData(false); InlineData(true)>]
let ``drops_a_link_and_an_image_that_arent_web_urls`` (next: bool) =
    usingNext <- next
    for value in
        [ "javascript:alert(1)"
          "data:text/html,pwned"
          "vbscript:msgbox(1)"
          "//example.com/image.png"
          "/rooms/1"
          "rooms/1"
          ""
          "http://exa mple.com/ "
          "https:/rooms/1"
          "https:rooms/1"
          "http:/rooms/1"
          "https://"
          "http://:80/rooms/1" ] do
        Assert.True((webUrl value "").IsNone, value)

[<Theory; InlineData(false); InlineData(true)>]
let ``drops_a_link_and_an_image_on_this_campfires_own_host_however_it_is_spelled`` (next: bool) =
    usingNext <- next
    for value in
        [ "https://once.campfire.test/rooms/1"
          "http://once.campfire.test/rooms/1"
          "https://ONCE.Campfire.Test/rooms/1"
          "https://once.campfire.test./rooms/1"
          "https://%6fnce.campfire.test/rooms/1"
          "https://%77ww.example.com/x.png" ] do
        Assert.True((webUrl value "once.campfire.test").IsNone, value)
    Assert.True((webUrl "https://example.com/page" "once.campfire.test").IsSome)

[<Theory; InlineData(false); InlineData(true)>]
let ``drops_a_link_and_an_image_on_a_bare_address_rather_than_a_domain_name`` (next: bool) =
    usingNext <- next
    for value in
        [ "http://127.0.0.1/rooms/1"
          "http://2130706433/rooms/1"
          "http://0177.0.0.1/rooms/1"
          "http://0x7f.0.0.1/rooms/1"
          "http://1.2.3.0xff/rooms/1"
          "http://[::1]/rooms/1"
          "http://localhost/rooms/1"
          "https://203.0.113.10/image.png" ] do
        Assert.True((webUrl value "").IsNone, value)

[<Theory; InlineData(false); InlineData(true)>]
let ``keeps_an_internationalized_domain_written_in_punycode`` (next: bool) =
    usingNext <- next
    Assert.True((webUrl "https://xn--80aswg.xn--p1ai/page" "").IsSome)

[<Theory; InlineData(false); InlineData(true)>]
let ``renders_the_title_and_the_description_as_text`` (next: bool) =
    usingNext <- next
    let html =
        presentation
            "<action-text-attachment content-type=\"application/vnd.actiontext.opengraph-embed\" href=\"https://example.com/page\" url=\"https://example.com/image.png\" filename=\"&lt;b&gt;Title&lt;/b&gt;\" caption=\"&lt;img src=x onerror=alert(1)&gt;\"></action-text-attachment>"
    Assert.False(contains html "<b>")
    Assert.False(contains html "<img src=x")
    Assert.True(contains html "&lt;b&gt;Title&lt;/b&gt;")

// --- Plain text: test/models/message_test.rb, message/searchable_test.rb, webhook_test.rb ---------

[<Theory; InlineData(false); InlineData(true)>]
let ``rich_text_body_is_converted_to_plain_text_for_indexing`` (next: bool) =
    usingNext <- next
    Assert.Equal("My hovercraft is full of eels", okR (ActionText.toPlainText "<span>My hovercraft is full of eels</span>" ctx))
    Assert.Equal("First post!", okR (ActionText.toPlainText "First post!" ctx))

[<Theory; InlineData(false); InlineData(true)>]
let ``mentionees_are_the_mentioned_users_once_each`` (next: bool) =
    usingNext <- next
    let body = $"<div>Hey {mentionAttachmentForDavid ()} {mentionAttachmentForDavid ()}</div>"
    let users = okR (ActionText.mentionedUsers body ctx)
    Assert.Equal<int64 list>([ 1L ], users |> List.map (fun u -> u.Id))

[<Theory; InlineData(false); InlineData(true)>]
let ``webhook_plain_body_drops_the_recipients_mentions`` (next: bool) =
    usingNext <- next
    let plain = okR (ActionText.toPlainText $"<p>{mentionAttachmentForDavid ()} hello</p>" ctx)
    Assert.Equal("@David hello", plain)
    Assert.Equal("hello", ActionText.withoutRecipientMentions plain "David")
