// Port of rust/crates/views/src/helpers/users.rs
/// `UsersHelper`, `Users::AvatarsHelper`, `Users::FilterHelper`, `Users::ProfilesHelper`,
/// `Users::SidebarHelper` and `AccountsHelper`.
module Campfire.Views.Helpers.UsersHelper

open System
open System.Text

/// `Users::AvatarsHelper::AVATAR_COLORS`.
let avatarColors: string[] =
    [| "#AF2E1B"; "#CC6324"; "#3B4B59"; "#BFA07A"; "#ED8008"; "#ED3F1C"; "#BF1B1B"; "#736B1E"; "#D07B53"; "#736356"; "#AD1D1D"; "#BF7C2A"
       "#C09C6F"; "#698F9C"; "#7C956B"; "#5D618F"; "#3B3633"; "#67695E" |]

/// CRC-32 (IEEE, as `Zlib.crc32` and the `crc32fast` crate), byte at a time.
let private crcTable =
    Array.init 256 (fun n ->
        let mutable c = uint32 n
        for _ in 0..7 do
            c <- if c &&& 1u <> 0u then 0xEDB88320u ^^^ (c >>> 1) else c >>> 1
        c)

let private crc32 (bytes: ReadOnlySpan<byte>) : uint32 =
    let mutable crc = 0xFFFFFFFFu
    for b in bytes do
        crc <- crcTable[int ((crc ^^^ uint32 b) &&& 0xFFu)] ^^^ (crc >>> 8)
    crc ^^^ 0xFFFFFFFFu

/// `avatar_background_color(user)`: `Zlib.crc32(user.to_param)` picks the color.
let avatarBackgroundColor (userId: int64) : string =
    let crc = crc32 (ReadOnlySpan<byte>(Encoding.UTF8.GetBytes(string userId)))
    avatarColors[int (crc % uint32 avatarColors.Length)]

/// `char::is_alphanumeric`.
let private isAlphanumeric (rune: Rune) : bool =
    let table = UnicodeTables.alphanumeric
    let code = rune.Value
    let mutable low = 0
    let mutable high = table.Length - 1
    let mutable found = false
    while not found && low <= high do
        let middle = (low + high) / 2
        let struct (first, last) = table[middle]
        if code < first then high <- middle - 1
        elif code > last then low <- middle + 1
        else found <- true
    found

/// `User#initials`: `name.scan(/\b\w/).join`. Ruby's `\w` is ASCII-only while `\b` sees
/// Unicode word characters, so "Élodie" contributes nothing.
let initials (name: string) : string =
    let isWord (rune: Rune) = isAlphanumeric rune || rune.Value = int '_'
    let out = StringBuilder()
    let mutable previous: Rune voption = ValueNone
    for c in name.EnumerateRunes() do
        let ascii = c.IsAscii && (Char.IsAsciiLetterOrDigit(char c.Value) || c.Value = int '_')
        let afterWord =
            match previous with
            | ValueSome p -> isWord p
            | ValueNone -> false
        if ascii && not afterWord then out.Append(char c.Value) |> ignore
        previous <- ValueSome c
    out.ToString()

/// `User#title`: `[ name, bio ].compact_blank.join(" – ")`.
let userTitle (name: string) (bio: string option) : string =
    let blank (part: string) = String.IsNullOrWhiteSpace part
    String.Join(" – ", [ Some name; bio ] |> List.choose id |> List.filter (not << blank))

/// What `avatar_tag` needs to know about a user.
type AvatarUser =
    { Id: int64
      /// `User#title`.
      Title: string
      /// `fresh_user_avatar_path(user)`.
      AvatarPath: string }

/// The bot curl commands in `accounts/bots/_bot`.
let curlTextLine (url: string) : string = $"curl -d 'Hello!' {url}"

let curlUploadLine (url: string) : string = $"curl -F \"attachment=@/path/to/file\" {url}"
