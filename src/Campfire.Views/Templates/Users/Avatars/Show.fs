// Port of rust/crates/views/templates/users/avatars/show.svg (reference/app/views/users/avatars/show.svg.erb)
/// `users/avatars/show.svg.erb`: the initials avatar for users without an uploaded one, as Falco.Markup elements (plans/design-views.md V10).
module Campfire.Views.Templates.Users.Avatars.Show

open Falco.Markup
open Campfire.Views
open Campfire.Views.Helpers
open Campfire.Views.Markup

/// `userId` is the user's id and `initials` `User#initials`. The initials are encoded as text (numeric references are valid XML).
let render (userId: int64) (initials: string) : XmlNode =
    // `initials.chars().count() >= 3`
    let mutable characters = 0
    for _ in initials.EnumerateRunes() do
        characters <- characters + 1
    Elem.create
        "svg"
        [ Attr.create "version" "1.1"
          Attr.create "xmlns" "http://www.w3.org/2000/svg"
          Attr.create "xmlns:xlink" "http://www.w3.org/1999/xlink"
          Attr.create "viewBox" "0 0 512 512"
          Attr.class' "avatar"
          Attr.create "aria-hidden" "true" ]
        [ Elem.create
              "defs"
              []
              [ Elem.create
                    "clipPath"
                    [ Attr.create "id" "porthole" ]
                    [ Elem.create "circle" [ Attr.create "cx" "50%"; Attr.create "cy" "50%"; Attr.create "r" "50%" ] [] ] ]
          Elem.create
              "g"
              []
              [ Elem.create
                    "rect"
                    [ Attr.create "width" "100%"
                      Attr.create "height" "100%"
                      Attr.create "rx" "50"
                      Html.attr "fill" (UsersHelper.avatarBackgroundColor userId) ]
                    []
                Elem.create
                    "text"
                    [ Attr.create "x" "50%"
                      Attr.create "y" "50%"
                      Attr.create "fill" "#FFFFFF"
                      Attr.create "text-anchor" "middle"
                      Attr.create "dy" "0.35em"
                      if characters >= 3 then
                          Attr.create "textLength" "85%"
                          Attr.create "lengthAdjust" "spacingAndGlyphs"
                      Attr.create "font-family" "-apple-system, BlinkMacSystemFont, Segoe UI, Roboto, Helvetica, Arial, sans-serif"
                      Attr.create "font-size" "230"
                      Attr.create "font-weight" "800"
                      Attr.create "letter-spacing" "-5" ]
                    [ // The ERB keeps its indentation around the initials; WebKit centres the text with it (a pixel gate difference without it).
                      Html.text ("\n      " + initials + "\n    ") ] ] ]
