// Port of rust/crates/richtext/src/autolink.rs
//
// rails_autolink 1.1.8's matchers, by hand: `AUTO_LINK_RE` (a URL: scheme or `www.`, then what a URL is made of, trimmed of trailing
// punctuation and unbalanced brackets) and the e-mail address pattern. Rust's regular expressions were matched by hand in the port, as
// .NET's differ in what `\w`, `\b` and `(?i)` mean, and each one below is small enough to say exactly. `Autolink` (Autolink.fs) runs
// them over a text node's escaped form and builds the nodes.
module internal Campfire.RichText.AutolinkMatch

open System
open Campfire.RichText.RubyExt
open Campfire.RichText.Unicode

/// What Rust's `(?i)` folds an ASCII letter with: its other case, and for s and k the long s and the
/// Kelvin sign, which Unicode case folding makes equal to them.
let private fold (c: char) : char =
    if c >= 'A' && c <= 'Z' then char (int c + 32)
    elif c = 'ſ' then 's'
    elif c = 'K' then 'k'
    else c

let private schemes =
    [| "ed2k"; "ftp"; "http"; "https"; "irc"; "mailto"; "news"; "gopher"; "nntp"; "telnet"; "webcal"; "xmpp"; "callto"; "feed"; "svn"
       "urn"; "aim"; "rsync"; "tag"; "ssh"; "sftp"; "rtsp"; "afs"; "file" |]

/// `[^ \t\r\n\x0B\x0C<\u{A0}"]`: what a URL is made of after its start.
let private isUrlChar (c: char) : bool =
    not (c = ' ' || c = '\t' || c = '\r' || c = '\n' || c = '\u000b' || c = '\u000c' || c = '<' || c = ' ' || c = '"')

/// One match of `AUTO_LINK_RE`: where it starts and ends, and whether a scheme started it (the
/// regular expression's capture group).
[<Struct>]
type internal UrlMatch = { Start: int; End: int; HasScheme: bool }

let private isAsciiWordClass (c: char) = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c = '_'

/// The match of `AUTO_LINK_RE` that starts at `i`, if any:
/// `(?:(?i:((?:ed2k|ftp|...|file):))//|(?i:www)\.[a-zA-Z0-9_])[^ \t\r\n\x0B\x0C<\u{A0}"]+`.
let internal urlMatchAt (text: string) (i: int) : UrlMatch voption =
    let n = text.Length
    let tail (from: int) =
        let mutable e = from
        while e < n && isUrlChar text[e] do
            e <- e + 1
        e
    let mutable result = ValueNone
    // A scheme, then "://"
    let mutable s = 0
    while result.IsNone && s < schemes.Length do
        let name = schemes[s]
        if i + name.Length + 2 < n && text[i + name.Length] = ':' && text[i + name.Length + 1] = '/' && text[i + name.Length + 2] = '/' then
            let mutable same = true
            for k in 0 .. name.Length - 1 do
                if fold text[i + k] <> name[k] then same <- false
            if same then
                let from = i + name.Length + 3
                let e = tail from
                // The tail needs at least one character
                if e > from then result <- ValueSome { Start = i; End = e; HasScheme = true }
        s <- s + 1
    if result.IsNone && i + 5 < n && fold text[i] = 'w' && fold text[i + 1] = 'w' && fold text[i + 2] = 'w' && text[i + 3] = '.' && isAsciiWordClass text[i + 4] then
        let e = tail (i + 5)
        if e > i + 5 then result <- ValueSome { Start = i; End = e; HasScheme = false }
    result

/// `AUTO_EMAIL_RE` without its lookbehind, which is checked separately:
/// `\A[a-zA-Z0-9_.!#$%+-]\.?[a-zA-Z0-9_.!#$%&'*/=?^`{|}~+-]*@[a-zA-Z0-9_-]+(?:\.[a-zA-Z0-9_-]+)+`.
/// Returns where the match, anchored at `start`, ends.
let internal emailMatchEnd (text: string) (start: int) : int voption =
    let n = text.Length
    let firstClass (c: char) = isAsciiWordClass c || ".!#$%+-".IndexOf c >= 0
    let localClass (c: char) = isAsciiWordClass c || ".!#$%&'*/=?^`{|}~+-".IndexOf c >= 0
    let labelClass (c: char) = isAsciiWordClass c || c = '-'
    if start >= n || not (firstClass text[start]) then
        ValueNone
    else
        let mutable i = start + 1
        if i < n && text[i] = '.' then i <- i + 1
        while i < n && localClass text[i] do
            i <- i + 1
        if i >= n || text[i] <> '@' then
            ValueNone
        else
            i <- i + 1
            let labelStart = i
            while i < n && labelClass text[i] do
                i <- i + 1
            if i = labelStart then
                ValueNone
            else
                let mutable groups = 0
                let mutable fin = false
                while not fin do
                    if i < n && text[i] = '.' then
                        let mutable e = i + 1
                        while e < n && labelClass text[e] do
                            e <- e + 1
                        if e > i + 1 then
                            i <- e
                            groups <- groups + 1
                        else
                            fin <- true
                    else
                        fin <- true
                if groups > 0 then ValueSome i else ValueNone

let internal isEmailLocalChar (c: char) : bool = Char.IsAsciiLetterOrDigit c || "_.!#$%&'*/=?^`{|}~+-".IndexOf c >= 0

/// Brackets whose closing half may end a URL when the URL opened it.
let internal openingBracket (closing: char) : char voption =
    match closing with
    | ']' -> ValueSome '['
    | ')' -> ValueSome '('
    | '}' -> ValueSome '{'
    | _ -> ValueNone

