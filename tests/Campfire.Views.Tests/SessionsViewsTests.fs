// The sessions, first run, PWA and welcome templates on Falco.Markup (plans/design-views.md unit 4.9): the hostile values the goldens
// do not carry, the block-less transfer form, the manifest as JSON and the platform branches of the PWA partials.
module Campfire.Views.Tests.SessionsViewsTests

open System.Text.Json
open AngleSharp.Html.Parser
open Xunit
open Campfire.Views
open Campfire.Views.Tests.Facts

let private nonNull (value: 'a | null) : 'a =
    match value with
    | null -> failwith "unexpected null"
    | value -> value

let private parse (html: string) = HtmlParser().ParseDocument html

let private hostile = "x\"><script>alert(1)</script>&amp; 'y' "

let private helpContact (name: string) : Accounts.HelpContact option =
    let david = user name "David"
    Some { Name = nonNull (david.GetProperty("name").GetString()); EmailAddress = nonNull (david.GetProperty("email_address").GetString()) }

/// The sign-in form writes the email the user typed as data, in the attribute and nowhere else.
[<Fact>]
let ``the sign in email is attribute data`` () =
    let name = "sessions_new_email"
    let ctx = context name Request.none
    let html = (Layouts.page ctx false (Templates.Sessions.New.rendered ctx (Some hostile) (helpContact name))).ToString()
    let document = parse html
    Assert.Equal(hostile, (nonNull (document.QuerySelector "input#email_address")).GetAttribute "value")
    Assert.Equal("email_address", (nonNull (document.QuerySelector "input#email_address")).GetAttribute "name")
    Assert.Equal("log_in", (nonNull (document.QuerySelector "button[type=submit]")).GetAttribute "name")
    Assert.Null(document.QuerySelector "section script")
    // The `shake` class is the flash alert's.
    Assert.Equal("panel", (nonNull (document.QuerySelector "section > div")).ClassName)
    let rejected = context "sessions_new_rejected" { Request.none with FlashAlert = Some "No" }
    let shaken = (Layouts.page rejected false (Templates.Sessions.New.rendered rejected None (helpContact name))).ToString()
    Assert.Equal("panel shake", (nonNull ((parse shaken).QuerySelector "section > div")).ClassName)

/// The transfer form is the block-less `form_with`: its start tag and hidden field, no `</form>`, and the action as data.
[<Fact>]
let ``the transfer form is left open and encodes its action`` () =
    let action = "/session/transfers/a\"b<c>&d"
    let page = Templates.Sessions.Transfers.Show.rendered action
    let html = (HtmlOutput.ofNodes page.Regions.Content []).ToString()
    Assert.StartsWith("<form data-controller=\"auto-submit\" action=\"", html)
    Assert.DoesNotContain("</form>", html)
    Assert.Contains("<input type=\"hidden\" name=\"_method\" value=\"put\" />", html)
    let form = nonNull ((parse html).QuerySelector "form")
    Assert.Equal(action, form.GetAttribute "action")
    Assert.Equal("put", (nonNull (form.QuerySelector "input[name=_method]")).GetAttribute "value")
    // In the layout the footer is the form's descendant, as in the reference's DOM (the loose DOM compares it).
    let ctx = context "sessions_transfer" Request.none
    let document = parse ((Layouts.page ctx false page).ToString())
    Assert.NotNull(document.QuerySelector "main > form > footer")

[<Fact>]
let ``unclosed refuses what is not the element it was told`` () =
    Assert.Throws<System.InvalidOperationException>(fun () ->
        Campfire.Views.Markup.Html.unclosed "form" (Falco.Markup.Elem.div [] []) |> ignore)
    |> ignore

/// The welcome page writes the user's name as text.
[<Fact>]
let ``the welcome page encodes the user's name`` () =
    let name = "welcome"
    let ctx = context name Request.none
    let html = (Layouts.page ctx false (Templates.Welcome.Show.rendered ctx "<img src=x onerror=alert(1)>")).ToString()
    let document = parse html
    Assert.Equal("<img src=x onerror=alert(1)>", (nonNull (document.QuerySelector "#message-area .for-screen-reader")).TextContent)
    Assert.Null(document.QuerySelector "#message-area img[onerror]")
    Assert.Equal("sidebar", (nonNull (nonNull (document.QuerySelector "body")).ClassName).Split(' ')[0])

/// The title is Rails': "Campfire" in Apple's in-app browser, "Unsupported browser" everywhere else.
[<Fact>]
let ``the incompatible browser page is titled for the platform`` () =
    for name, title in [ "incompatible_browser", "Unsupported browser"; "incompatible_browser_apple_messages", "Campfire" ] do
        let ctx = context name Request.none
        let page = Templates.Sessions.IncompatibleBrowser.rendered ctx
        Assert.Equal(Some title, page.Regions.Title)
        let document = parse ((Layouts.page ctx false page).ToString())
        Assert.Equal(4, document.QuerySelectorAll(".browser-list .browser").Length)

/// The first run form posts a file with the multipart encoding and names its fields after the user.
[<Fact>]
let ``the first run form is multipart and model scoped`` () =
    let ctx = context "first_run" Request.none
    let html = (Layouts.page ctx false (Templates.FirstRuns.Show.rendered ctx)).ToString()
    let document = parse html
    let form = nonNull (document.QuerySelector "form.center.max-width")
    Assert.Equal("multipart/form-data", form.GetAttribute "enctype")
    Assert.Equal("/first_run", form.GetAttribute "action")
    for field in [ "user[avatar]"; "user[name]"; "user[email_address]"; "user[password]" ] do
        Assert.NotNull(form.QuerySelector $"input[name='{field}']")
    Assert.Equal("signup", (nonNull (nonNull (document.QuerySelector "body")).ClassName).Split(' ')[0])

/// The browser settings partial is empty on iOS's Safari and Chrome (they have no browser settings for notifications), and the others
/// say where the platform keeps them; a name with markup in it is text.
[<Fact>]
let ``the pwa partials follow the platform`` () =
    let ios = context "browser_settings_safari_ios" Request.none
    Assert.Empty(Templates.Pwa._BrowserSettings.render ios)
    let chrome = context "install_instructions_chrome_mac" Request.none
    Assert.Empty(Templates.Pwa._InstallInstructions.render chrome)
    let odd = { ios with Platform = { ios.Platform with Browser = "<b>x</b>"; Ios = false; Safari = false; Chrome = false } }
    let html = (HtmlOutput.ofNodes (Templates.Pwa._BrowserSettings.render odd) []).ToString()
    Assert.Contains("Check your &lt;b&gt;x&lt;/b&gt; settings", html)
    Assert.DoesNotContain("<b>", html)

/// The manifest: the port's key order, JSON whatever the account is called, and `<`, `&` and non-ASCII text left alone by the relaxed
/// encoder (a JSON response is no page).
[<Fact>]
let ``the manifest keeps its key order and escapes only what JSON needs`` () =
    let manifest (name: string option) (logo: string) =
        System.Text.Encoding.UTF8.GetString(Templates.Pwa.Manifest.render name logo "/b" "http://h.test" (fun logical -> "/assets/" + logical))
    let json = manifest (Some "Fire <&> \"x\" \u00e9 \U0001F525") "/a?x=1&y=2"
    use document = JsonDocument.Parse json
    let root = document.RootElement
    Assert.Equal<string list>(
        [ "name"; "icons"; "start_url"; "display"; "scope"; "description"; "categories"; "theme_color"; "background_color"; "shortcuts"; "screenshots" ],
        [ for p in root.EnumerateObject() -> p.Name ]
    )
    Assert.Equal("Fire <&> \"x\" \u00e9 \U0001F525", root.GetProperty("name").GetString())
    Assert.Contains("Fire <&> \\\"x\\\" \u00e9", json)
    let icons = root.GetProperty "icons"
    let first, last = icons[0], icons[2]
    Assert.Equal("/a?x=1&y=2", first.GetProperty("src").GetString())
    Assert.Equal(3, icons.GetArrayLength())
    Assert.Equal("maskable", last.GetProperty("purpose").GetString())
    let shortcut = (root.GetProperty "shortcuts")[0]
    let shortcutIcon = (shortcut.GetProperty "icons")[0]
    Assert.Equal("http://h.test/assets/add.svg", shortcutIcon.GetProperty("src").GetString())
    use unnamed = JsonDocument.Parse(manifest None "/a")
    Assert.Equal("Campfire", unnamed.RootElement.GetProperty("name").GetString())
