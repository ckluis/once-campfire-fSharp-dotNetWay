// The encoding module of the Falco.Markup views (plans/design-views.md V2). Falco.Markup 1.4.0 writes attribute values and
// `Text.raw` unescaped, so a value that is not a literal reaches the page only through here; `bin/lint-markup` enforces it
// (this is the one file it does not read).
namespace Campfire.Views.Markup

open System.Globalization
open System.Net
open Falco.Markup
open Campfire.Views

module Html =
    /// HTML that is already safe to write as it is. The constructor is private; the makers below are the only ways in, and the
    /// lint allows each one in a single file.
    [<Struct; NoComparison; NoEquality>]
    type Trusted = private Trusted of string

    module Trusted =
        /// U+E000 is the slot marker's second character (`Html.fragment`). Text and attribute values reach the page through
        /// `WebUtility.HtmlEncode`, which escapes the `<` before it, but trusted HTML is written as it is: a `<` U+E000 digits `>`
        /// inside it would read as a slot, and `HtmlOutput` raises on a slot it was not given (every page holding it a 500). The
        /// makers that take text a user wrote write the character another way, with the same meaning in their context.
        let private withoutMarker (replacement: string) (text: string) : string =
            if text.Contains '\uE000' then text.Replace("\uE000", replacement) else text

        /// Rich text the sanitizer produced (allowlisted: Presenters/Presenter.fs, `MessageContent.Text`). U+E000 becomes its
        /// character reference: the sanitizer keeps no `script` or `style`, so it is in text or an attribute value, where the
        /// reference parses to the same character.
        let ofSanitized (html: string) : Trusted = Trusted(withoutMarker "&#57344;" html)
        /// The import map and stylesheet tags `Campfire.Assets` builds from digests (allowlisted: where the ViewContext is built).
        let ofAssetTags (html: string) : Trusted = Trusted html
        /// The account's custom CSS, which Rails writes `html_safe` (allowlisted: Markup/Application.fs).
        /// U+E000 becomes a CSS escape (`\E000 `, the same character in an identifier or a string): the styles are a `style`
        /// element's raw text, where a character reference would not be decoded.
        let ofAccountStyles (css: string) : Trusted = Trusted(withoutMarker "\\E000 " css)
        /// What a writer template rendered (the migration seam, `Legacy.node`; deleted with it in unit 4.11).
        let internal ofLegacy (html: string) : Trusted = Trusted html
        let value (Trusted html) : string = html

    /// An attribute whose value is data: encoded with the encoder `Text.enc` uses, so the project has one escaping rule.
    let attr (name: string) (value: string) : XmlAttribute = Attr.create name (WebUtility.HtmlEncode value)

    let attrInt (name: string) (value: int64) : XmlAttribute =
        Attr.create name (value.ToString(CultureInfo.InvariantCulture))

    /// Text that is data (`Text.enc`).
    let text (value: string) : XmlNode = Text.enc value

    let int (value: int64) : XmlNode = Text.raw (value.ToString(CultureInfo.InvariantCulture))

    let trusted (html: Trusted) : XmlNode = Text.raw (Trusted.value html)

    /// A cached fragment's slot: `HtmlOutput` finds it by `Id` after Falco has rendered the page (V6). Both text encoders escape `<`,
    /// so no text or attribute value can hold a `<` followed by U+E000.
    let fragment (fragment: CachedFragment) : XmlNode =
        Text.raw ("<\uE000" + fragment.Id.ToString(CultureInfo.InvariantCulture) + ">")

    /// The space the loose DOM rule keeps between inline-level siblings (V4).
    let sp: XmlNode = Text.raw " "

    /// The newline Rails' `text_area` writes right after `<textarea>` (V4).
    let nl: XmlNode = Text.raw "\n"
