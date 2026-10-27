// Port of rust/crates/ruby/src/erb.rs
/// `ERB::Util.html_escape` (and `h`): `& < > " '` become `&amp; &lt; &gt; &quot; &#39;`.
module Campfire.Ruby.Erb

open System.Text

let private replacement (c: char) : string | null =
    match c with
    | '&' -> "&amp;"
    | '<' -> "&lt;"
    | '>' -> "&gt;"
    | '"' -> "&quot;"
    | '\'' -> "&#39;"
    | _ -> null

let htmlEscape (s: string) : string =
    let out = StringBuilder(s.Length)
    let mutable last = 0
    for index in 0 .. s.Length - 1 do
        match replacement s[index] with
        | null -> ()
        | r ->
            out.Append(s, last, index - last).Append r |> ignore
            last <- index + 1
    out.Append(s, last, s.Length - last).ToString()
