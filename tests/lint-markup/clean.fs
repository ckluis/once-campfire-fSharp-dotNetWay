// A clean file: literals, the encoding module, Falco's zero-argument values, and the forbidden spellings only in comments and strings.
// Attr.href url, Text.raw body, Elem.create tag, TextNode v, Trusted.ofSanitized body, cache.GetOrCreateAsync
(* Attr.title ("Hi " + v) and Text.rawf "%s" v in a block comment (* nested Text.raw x *) *)
module Clean

open System.Text
open Falco.Markup
open Campfire.Views.Markup
module Strings = System.String

let note = "Attr.href url, Text.raw body, Elem.create \"img\", Text.strong name"
let verbatim = @"Attr.title (x + y)"
let triple = """Text.raw body and "quotes" """
let quote = '"'

let page (title: string) (url: string) (body: string) =
    Elem.div
        [ Attr.class' "box"
          Attr.create "data-controller" "lightbox"
          Attr.methodPost
          Attr.disabled
          Attr.merge [] []
          Html.attr "href" url
          Html.attr "title" $"Hi {title}" ]
        [ Text.raw "<b>static</b>"
          Text.strong "literal"
          Text.enc body
          Html.text body
          Text.empty
          Elem.create "turbo-frame" [ Html.attr "id" "x" ] []
          Elem.createSelfClosing "meta" [ Attr.name "viewport" ]
          Elem.img [ Attr.src "/a.png" ]
          Html.sp
          Html.nl ]

let ignored (_ctx: int) (x: int option) =
    match x with
    | Some _ -> Templates.Layouts.render
    | _ -> Campfire.Views.Templates.Rooms.ShowPage.rendered
