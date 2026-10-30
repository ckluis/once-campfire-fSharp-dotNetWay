// `form_with`'s id and name sanitizing (ActionView's `sanitize_to_id`, `FormBuilder`'s object name). The form helpers
// themselves are Falco.Markup functions in `Markup/Forms.fs`.
module Campfire.Views.Helpers.Forms

open System

/// Each character (Unicode scalar value) that isn't one of `allowed` becomes one "_": Rust's `chars()`.
let private replaceOthers (text: string) (allowed: Text.Rune -> bool) : string =
    let out = Text.StringBuilder(text.Length)
    for rune in text.EnumerateRunes() do
        out.Append(if allowed rune then rune.ToString() else "_") |> ignore
    out.ToString()

/// `object_name.gsub(/\]\[|[^-a-zA-Z0-9:.]/, "_").delete_suffix("_")`.
let sanitizeObjectName (name: string) : string =
    let replaced = name.Replace("][", "_")
    let isAllowed (rune: Text.Rune) =
        rune.IsAscii && (Char.IsAsciiLetterOrDigit(char rune.Value) || rune.Value = int '-' || rune.Value = int ':' || rune.Value = int '.')
    let sanitized = replaceOthers replaced isAllowed
    if sanitized.EndsWith '_' then sanitized.Substring(0, sanitized.Length - 1) else sanitized

/// `sanitize_to_id`: `]` removed, other non-id characters become "_".
let sanitizeToId (name: string) : string =
    let isAllowed (rune: Text.Rune) =
        rune.IsAscii
        && (Char.IsAsciiLetterOrDigit(char rune.Value) || rune.Value = int '-' || rune.Value = int '_' || rune.Value = int ':' || rune.Value = int '.')
    replaceOthers (name.Replace("]", "")) isAllowed
