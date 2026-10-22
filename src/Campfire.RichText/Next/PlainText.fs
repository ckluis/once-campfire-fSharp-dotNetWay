// plans/design-richtext.md (Phase 5, unit 5.3): `ActionText::PlainTextConversion` over AngleSharp nodes. The same reduction as
// ../PlainText.fs (a port of rust/crates/richtext/src/plain_text.rs), keyed on each node's Nokogiri name.
module Campfire.RichText.Next.PlainText

open System
open AngleSharp.Dom
open Campfire.RichText
open Campfire.RichText.RubyExt

let private isList (name: string) = name = "ul" || name = "ol"

/// Nokogiri's `Node#name`: an element's local name, "text" and "comment" otherwise.
let private nameOf (node: INode) : string =
    match node with
    | :? IElement as element -> element.LocalName
    | :? IText -> "text"
    | :? IComment -> "comment"
    | _ -> "#other"

let private ancestors (node: INode) : INode seq =
    Seq.unfold (fun (n: INode) -> match n.Parent with null -> None | p -> Some(p, p)) node

let private listDepth (node: INode) : int = ancestors node |> Seq.filter (fun a -> isList (nameOf a)) |> Seq.length

let private bulletForLi (node: INode) : string =
    let list = ancestors node |> Seq.map nameOf |> Seq.tryFind isList
    if list = Some "ol" then
        let index =
            match node.Parent with
            | :? IElement as parent ->
                parent.Children |> Seq.tryFindIndex (fun c -> obj.ReferenceEquals(c, node)) |> Option.defaultValue 0
            | _ -> 0
        string (index + 1) + "."
    else
        "•"

let private plainTextForBlock (childValues: string list) : string = chompNewlines (String.Concat childValues) + "\n\n"

let private isSpace (c: char) = c = ' ' || c = '\t' || c = '\n' || c = '\u000b' || c = '\u000c' || c = '\r'

let rec private plainTextFor (node: INode) : string =
    let childValues () = [ for child in node.ChildNodes -> plainTextFor child ]
    match nameOf node with
    | "script"
    | "style"
    | "unsupported" -> ""
    | "h1"
    | "p" -> plainTextForBlock (childValues ())
    | "ul"
    | "ol" ->
        let text = plainTextForBlock (childValues ())
        if listDepth node > 0 then "\n" + text else text
    | "br" -> "\n"
    // Text nodes, and elements that happen to be named "text" (SVG's), use `node.text`
    | "text" -> chompNewlines (Html5.textContent node)
    | "div" -> chompNewlines (String.Concat(childValues ())) + "\n"
    | "figcaption" -> "[" + chompNewlines (String.Concat(childValues ())) + "]"
    | "blockquote" ->
        let text = plainTextForBlock (childValues ())
        if isBlank text then
            "“”"
        else
            // `text.insert(text.rindex(/\S/) + 1, "”")`, then `text.index(/\S/)` for "“"
            let mutable last = text.Length - 1
            while last >= 0 && isSpace text[last] do
                last <- last - 1
            let mutable first = 0
            while first < text.Length && isSpace text[first] do
                first <- first + 1
            let closed = if last >= 0 then text.Insert(last + 1, "”") else text
            if first < closed.Length then closed.Insert(first, "“") else closed
    | "li" ->
        let bullet = bulletForLi node
        let text = chompNewlines (String.Concat(childValues ()))
        let depth = listDepth node
        let indentation = if depth > 1 then String.replicate (depth - 1) "  " else ""
        indentation + bullet + " " + text + "\n"
    | _ -> String.Concat(childValues ())

/// `PlainTextConversion.node_to_plain_text`: a bottom-up reduction keyed on each node's name.
let nodeToPlainText (node: INode) : string = chompNewlines (plainTextFor node)
