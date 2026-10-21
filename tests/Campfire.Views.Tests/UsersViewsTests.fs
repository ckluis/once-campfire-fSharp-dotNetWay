// The users templates on Falco.Markup (plans/design-views.md unit 4.7): the hostile values the goldens do not carry (T1 for the
// profile's bio, the SVG avatar as XML) and the form fields' names.
module Campfire.Views.Tests.UsersViewsTests

open System.Xml
open AngleSharp.Html.Parser
open Xunit
open Campfire.Views
open Campfire.Views.Tests.Facts

module Inputs = Campfire.Views.Differential.Inputs

let private nonNull (value: 'a | null) : 'a =
    match value with
    | null -> failwith "unexpected null"
    | value -> value

let private hostile = "\n<p>a \"quoted\" & it's</p></textarea>&lt;script&gt;"

let private profile (bio: string option) : Users.ProfileShow =
    let name = "profile_chrome_mac"
    { User = { Inputs.userSummary (user name "David") with Bio = bio; Name = "D<b>\"x\"" }
      AvatarAttached = false
      TransferId = "t"
      SharedMemberships = []
      DirectMemberships = [] }

/// T1: the bio's `textarea` keeps a value that starts with a newline and holds `</textarea>`, an entity and the slot marker.
[<Fact>]
let ``T1 the profile bio survives a newline, a closing textarea and an entity`` () =
    let name = "profile_chrome_mac"
    let ctx = context name Request.none
    let page = Templates.Users.Profiles.Show.rendered ctx (profile (Some hostile))
    let html = (Layouts.page ctx false page).ToString()
    let document = HtmlParser().ParseDocument html
    let area =
        match document.QuerySelector "textarea#user_bio" with
        | :? AngleSharp.Html.Dom.IHtmlTextAreaElement as t -> t
        | _ -> failwith "no bio textarea"
    Assert.Equal(hostile, area.Value.Replace("\r\n", "\n"))
    Assert.Equal("user[bio]", area.GetAttribute "name")
    Assert.Null(document.QuerySelector "section.panel script")

/// The avatar fields: the file inputs share the id "file" as Rails writes them, the form is multipart, the name goes into the
/// text field's value as data.
[<Fact>]
let ``the profile forms name their fields and encode the user's name`` () =
    let name = "profile_chrome_mac"
    let ctx = context name Request.none
    let html = (Layouts.page ctx false (Templates.Users.Profiles.Show.rendered ctx (profile None))).ToString()
    let document = HtmlParser().ParseDocument html
    let files = document.QuerySelectorAll "input[type=file]"
    Assert.Equal(2, files.Length)
    for file in files do
        Assert.Equal("user[avatar]", file.GetAttribute "name")
        Assert.Equal("file", file.GetAttribute "id")
        Assert.Equal("multipart/form-data", (nonNull (file.Closest "form")).GetAttribute "enctype")
    Assert.Equal("D<b>\"x\"", (nonNull (document.QuerySelector "input#user_name")).GetAttribute "value")
    Assert.Null(document.QuerySelector "form#user_name b")
    Assert.Equal("72", (nonNull (document.QuerySelector "input#user_password")).GetAttribute "size")

/// The initials are text in an XML document (numeric references for non-ASCII, escaped markup).
[<Fact>]
let ``the avatar SVG is well-formed XML for hostile initials`` () =
    for initials in [ "JZ"; "A<B"; "\"&'"; "é😀"; "ABC" ] do
        let svg = (HtmlOutput.ofNode (Templates.Users.Avatars.Show.render 7L initials) []).ToString()
        let document = XmlDocument()
        document.LoadXml svg
        let text = nonNull (document.GetElementsByTagName("text").[0])
        Assert.Equal(initials, text.InnerText)
        let attributes = nonNull text.Attributes
        let stretched = not (isNull (attributes.GetNamedItem "textLength"))
        Assert.Equal(Seq.length (initials.EnumerateRunes()) >= 3, stretched)
