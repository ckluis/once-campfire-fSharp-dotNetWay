// The accounts templates on Falco.Markup (plans/design-views.md unit 4.8): the hostile values the goldens do not carry (T1 for the
// custom styles textarea, attribute and text encoding in the bot, invite and people templates).
module Campfire.Views.Tests.AccountsViewsTests

open AngleSharp.Html.Parser
open Xunit
open Campfire.Views
open Campfire.Views.Tests.Facts

module Inputs = Campfire.Views.Differential.Inputs

let private nonNull (value: 'a | null) : 'a =
    match value with
    | null -> failwith "unexpected null"
    | value -> value

let private hostile = "\n/* </textarea> &lt;b&gt; */ a > b { content: \"x\" }"

let private parse (html: string) = HtmlParser().ParseDocument html

/// T1: the custom styles `textarea` keeps a value that starts with a newline and holds `</textarea>`, an entity and the slot marker.
[<Fact>]
let ``T1 the custom styles textarea survives a newline, a closing textarea and an entity`` () =
    let name = "custom_styles_edit"
    let ctx = context name Request.none
    let html = (Legacy.inLayout ctx false (Templates.Accounts.CustomStyles.Edit.rendered ctx (Some hostile))).ToString()
    let area =
        match (parse html).QuerySelector "textarea#account_custom_styles" with
        | :? AngleSharp.Html.Dom.IHtmlTextAreaElement as t -> t
        | _ -> failwith "no custom styles textarea"
    Assert.Equal(hostile, area.Value.Replace("\r\n", "\n"))
    Assert.Equal("account[custom_styles]", area.GetAttribute "name")
    // The empty value is the Rails one: no text at all.
    let empty = (Legacy.inLayout ctx false (Templates.Accounts.CustomStyles.Edit.rendered ctx None)).ToString()
    Assert.Equal("", (nonNull ((parse empty).QuerySelector "textarea#account_custom_styles")).TextContent.TrimStart('\n'))

let private hostileName = "Bo<b>\"t\" & 'x'"

/// A bot's name and webhook go into attribute values and text as data.
[<Fact>]
let ``the bot form encodes the name and webhook as data`` () =
    let name = "bots_edit"
    let ctx = context name Request.none
    let form: Accounts.BotForm =
        { Name = Some hostileName; WebhookUrl = Some "https://x.test/?a=1&b=\"2\""; AvatarAttachmentUrl = None }
    let html = (Legacy.inLayout ctx false (Templates.Accounts.Bots.Edit.rendered ctx 7L form)).ToString()
    let document = parse html
    Assert.Equal(hostileName, (nonNull (document.QuerySelector "input#user_name")).GetAttribute "value")
    Assert.Equal("https://x.test/?a=1&b=\"2\"", (nonNull (document.QuerySelector "input#user_webhook_url")).GetAttribute "value")
    Assert.Equal("user[avatar]", (nonNull (document.QuerySelector "input[type=file]")).GetAttribute "name")
    Assert.Equal("multipart/form-data", (nonNull (document.QuerySelector "form.flex-column")).GetAttribute "enctype")
    Assert.Null(document.QuerySelector "section.panel b")

/// A person on the account page: the name is text, and the delete label too.
[<Fact>]
let ``a person's name is text on the account page`` () =
    let name = "account_edit_admin"
    let ctx = context name Request.none
    let david = Inputs.userSummary (user name "David")
    let other = { david with Id = david.Id + 1L; Name = "<img src=x onerror=alert(1)>" }
    let html = (HtmlOutput.ofNode (Templates.Accounts.Users._User.render ctx other) []).ToString()
    let document = parse html
    Assert.Null(document.QuerySelector "img[onerror]")
    Assert.Equal("Delete <img src=x onerror=alert(1)>", (nonNull (document.QuerySelector "button .for-screen-reader")).TextContent)
