// Port of rust/crates/views/templates/pwa/manifest.json (reference/app/views/pwa/manifest.json.erb)
/// `pwa/manifest.json.erb`, written with `Utf8JsonWriter` (plans/design-views.md V10), the port's key order. ERB HTML-escapes the
/// values into the JSON, so an account named `a\b` or `"a"` made the manifest invalid and the logo URL came out as
/// `?size=small&amp;v=...`; the values are JSON strings here (README, Known differences). The answer is equal to Rails' as JSON,
/// not as text (class A: indentation, and the escapes the relaxed encoder chooses).
module Campfire.Views.Templates.Pwa.Manifest

open System.Buffers
open System.Text.Encodings.Web
open System.Text.Json

let private options =
    JsonWriterOptions(Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping)

/// `accountName` is `Current.account&.name`, `logoPathSmall` `fresh_account_logo_path(size: :small)`, `logoPath`
/// `fresh_account_logo_path`, `baseUrl` `request.base_url` (for `image_url`) and `assetPath` the asset resolver.
let render (accountName: string option) (logoPathSmall: string) (logoPath: string) (baseUrl: string) (assetPath: string -> string) : byte[] =
    let buffer = ArrayBufferWriter<byte>(2048)
    use w = new Utf8JsonWriter(buffer, options)
    let imageUrl (source: string) = baseUrl + assetPath source
    let icon (src: string) (sizes: string) (purpose: string | null) =
        w.WriteStartObject()
        w.WriteString("src", src)
        w.WriteString("type", "image/png")
        w.WriteString("sizes", sizes)
        match purpose with
        | null -> ()
        | purpose -> w.WriteString("purpose", purpose)
        w.WriteEndObject()
    let shortcut (name: string) (description: string) (url: string) (iconSrc: string) =
        w.WriteStartObject()
        w.WriteString("name", name)
        w.WriteString("description", description)
        w.WriteString("url", url)
        w.WriteStartArray "icons"
        w.WriteStartObject()
        w.WriteString("src", iconSrc)
        w.WriteString("sizes", "any")
        w.WriteEndObject()
        w.WriteEndArray()
        w.WriteEndObject()
    let screenshot (source: string) (label: string) =
        w.WriteStartObject()
        w.WriteString("src", imageUrl source)
        w.WriteString("sizes", "1080x2400")
        w.WriteString("form_factor", "narrow")
        w.WriteString("label", label)
        w.WriteEndObject()

    w.WriteStartObject()
    w.WriteString("name", defaultArg accountName "Campfire")
    w.WriteStartArray "icons"
    icon logoPathSmall "192x192" null
    icon logoPath "512x512" null
    icon logoPath "512x512" "maskable"
    w.WriteEndArray()
    w.WriteString("start_url", "/")
    w.WriteString("display", "standalone")
    w.WriteString("scope", "/")
    w.WriteString("description", "A chat app from the makers of Basecamp and HEY.")
    w.WriteStartArray "categories"
    w.WriteStringValue "social"
    w.WriteStringValue "business"
    w.WriteStringValue "productivity"
    w.WriteEndArray()
    w.WriteString("theme_color", "#ffffff")
    w.WriteString("background_color", "#ffffff")
    w.WriteStartArray "shortcuts"
    shortcut "New chat room" "Open Campfire and start a new chat room" "rooms/opens/new" (imageUrl "add.svg")
    shortcut "My profile" "Open Campfire and view your profile" "/users/me/profile" (imageUrl "person.svg")
    w.WriteEndArray()
    w.WriteStartArray "screenshots"
    screenshot "screenshots/android-chat.png" "Campfire is an installable, self-hosted group chat system."
    screenshot "screenshots/android-sidebar.png" "Easily invite people. Make rooms. @mentions, DMs, and mobile support."
    screenshot "screenshots/android-dark-mode.png" "Full support for dark mode, customizable to your brand."
    w.WriteEndArray()
    w.WriteEndObject()
    w.Flush()
    buffer.WrittenSpan.ToArray()
