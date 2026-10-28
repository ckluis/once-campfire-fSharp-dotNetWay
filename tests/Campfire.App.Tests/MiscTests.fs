// Port of the small test modules of rust/crates/campfire: concerns.rs, rich_text.rs, controllers/sessions.rs,
// active_storage.rs and main.rs.
module Campfire.App.Tests.MiscTests

open System
open System.IO
open Microsoft.Data.Sqlite
open Xunit
open Campfire.App
open Campfire.App.Presenters
open Campfire.App.Controllers
open Campfire.App.Tests.Support
open Campfire.Db
open Campfire.Rails
open Campfire.Storage

// --- rich_text.rs --------------------------------------------------------------------------------------------

/// A context nothing is read from: the bodies here name no record that exists.
let private noDb () : Ef.CampfireDb =
    (Ef.Data.factory (Ef.Data.options ":memory:" TimeProvider.System ignore)).CreateDbContext()

[<Fact>]
let ``a body whose plain text raises has none`` () =
    use loggers = new LogCapture()
    let richText = AppRichText(Secrets.create "test-secret", TimeProvider.System, loggers.Logger "rich_text") :> Campfire.Db.RichText
    use db = noDb ()
    // Rails raises `ArgumentError: invalid base64` reading the mention.
    let html = """Hey <action-text-attachment sgid="!!!" content-type="application/vnd.campfire.mention"></action-text-attachment> there"""
    Assert.Equal("", richText.ToPlainText(db, html))
    Assert.Equal("Hey there", richText.ToPlainText(db, "Hey <b>there</b>"))

/// Rust parses every body for its mentions and logs `mentioned_users raised` where the parse fails (`rich_text.rs`). Skipping the
/// parse for a body with no attachment (unit 7.4) lost that line for the bodies that fail, which are only those past the parse limits (`ParseError`).
let private deepBody = String.replicate 500 "<div>" + "text"
let private manyAttributesBody = "<p " + String.Join(" ", [ for i in 1..450 -> $"a{i}" ]) + ">text</p>"

[<Fact>]
let ``a body without attachments that fails to parse still logs the mentions error`` () =
    use loggers = new LogCapture()
    let richText = AppRichText(Secrets.create "test-secret", TimeProvider.System, loggers.Logger "rich_text") :> Campfire.Db.RichText
    use db = noDb ()
    for body in [ deepBody; manyAttributesBody ] do
        Assert.True(body.Length > AppRichText.ParseSafeLength)
        Assert.Equal<int64 list>([], richText.MentionedUserIds(db, body))
    Assert.Contains("Error rich_text: mentioned_users raised error=Document tree depth limit exceeded", loggers.Text)
    Assert.Contains("Error rich_text: mentioned_users raised error=Attributes per element limit exceeded", loggers.Text)

[<Fact>]
let ``a plain body parses nothing and logs nothing`` () =
    use loggers = new LogCapture()
    let richText = AppRichText(Secrets.create "test-secret", TimeProvider.System, loggers.Logger "rich_text") :> Campfire.Db.RichText
    use db = noDb ()
    for body in [ ""; "hello"; "<div>hi <b>there</b></div>"; String('x', 5000) ] do
        Assert.Equal<int64 list>([], richText.MentionedUserIds(db, body))
    Assert.Equal("", loggers.Text)

/// The skip is exact only while no body of `ParseSafeLength` units or fewer can fail to parse: the worst shapes (the most elements,
/// the most formatting elements the tree builder re-opens, the most attributes) cut to that length all parse.
[<Fact>]
let ``no body of the safe length fails to parse`` () =
    let limit = AppRichText.ParseSafeLength
    let formatting = [ "b"; "i"; "u"; "em"; "strong"; "a"; "code"; "font"; "s"; "small"; "big"; "tt"; "strike"; "nobr" ]
    let opens = String.Join("", formatting |> List.map (fun t -> $"<{t}>"))
    let shapes =
        [ String.replicate 400 "<b>"
          String.replicate 400 "<div>"
          String.replicate 400 "<b><p>"
          String.replicate 400 (opens + "<p>")
          String.replicate 400 (opens + "<div>x")
          String.replicate 200 "<table><td>"
          String.replicate 200 "<svg><g>"
          String.replicate 400 "<p><b>"
          "<p " + String.Join(" ", [ for i in 1..400 -> $"a{i}" ])
          "<p " + String.Join("", [ for i in 1..400 -> $"a{i}=1 " ])
          "<p " + String.Join("", [ for i in 1..400 -> $"{char (97 + i % 26)}{i} " ]) ]
    for shape in shapes do
        let body = shape.Substring(0, min limit shape.Length)
        match Campfire.RichText.Html5.parseBody body with
        | Ok _ -> ()
        | Error e -> failwith $"{e.Message}: {body.Substring(0, 40)}"

// --- active_storage.rs ---------------------------------------------------------------------------------------------

[<Fact>]
let ``json times have milliseconds`` () =
    Assert.Equal("2026-03-02T16:00:00.123Z", ActiveStorage.jsonTime "2026-03-02 16:00:00.123456")
    Assert.Equal("2026-03-02T16:00:00.000Z", ActiveStorage.jsonTime "2026-03-02 16:00:00")

// --- main.rs -------------------------------------------------------------------------------------------------------

[<Fact>]
let ``transparent huge pages are disabled`` () =
    if not (OperatingSystem.IsLinux()) then
        Assert.Skip "transparent huge pages are a Linux setting (bin/linux-tests App runs this in the toolchain image)"
    Program.disableTransparentHugePages ()
    Assert.True(Program.transparentHugePagesDisabled ())
