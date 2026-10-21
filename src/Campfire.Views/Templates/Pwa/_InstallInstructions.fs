// Port of rust/crates/views/templates/pwa/_install_instructions.html (reference/app/views/pwa/_install_instructions.html.erb)
/// `pwa/_install_instructions.html.erb`: how to install Campfire as a web app on the platform.
module Campfire.Views.Templates.Pwa._InstallInstructions

open Falco.Markup
open Campfire.Views
open Campfire.Views.Markup

let private hidden: XmlAttribute = Attr.create "aria-hidden" "true"

let private icon (ctx: ViewContext) (source: string) (size: int) (alt: string) : XmlNode =
    Assets.imageSized ctx source size [ Html.attr "alt" alt ]

let private steps (ctx: ViewContext) : XmlNode list =
    let platform = ctx.Platform
    if platform.Edge then
        [ Elem.ol
              []
              [ Elem.li
                    []
                    [ Text.raw "Click "
                      Elem.em [] [ icon ctx "install-edge.svg" 16 "the app available - install Campfire chat button" ]
                      Text.raw "in the address bar." ]
                Elem.li [] [ Text.raw "Click "; Elem.em [] [ Text.raw "Install" ]; Text.raw "." ] ] ]
    elif platform.Chrome && platform.Android then
        [ Elem.ol
              []
              [ Elem.li [] [ Text.raw "Tap the "; Elem.em [] [ icon ctx "menu-dots-vertical.svg" 16 "More options" ]; Text.raw " menu button." ]
                Elem.li [] [ Text.raw "Tap "; Elem.em [] [ Text.raw "Install app" ]; Text.raw " in the menu." ] ] ]
    elif platform.Firefox && platform.Android then
        [ Elem.ol
              []
              [ Elem.li [] [ Text.raw "Tap the "; Elem.em [] [ icon ctx "menu-dots-vertical.svg" 16 "More options" ]; Text.raw " menu button." ]
                Elem.li [] [ Text.raw "Tap "; Elem.em [] [ Text.raw "Install" ]; Text.raw " in the menu." ] ] ]
    elif platform.Safari && platform.Desktop then
        [ Elem.ol
              []
              [ Elem.li [] [ Text.raw "Click "; Elem.em [] [ Text.raw "File" ]; Text.raw " in the top left." ]
                Elem.li [] [ Text.raw "Click "; Elem.em [] [ Text.raw "Add to Dock…" ]; Text.raw "." ] ] ]
    elif (platform.Safari || platform.Chrome) && platform.Ios then
        [ Elem.p
              []
              [ Text.raw "To receive push notifications in "
                Html.text (Campfire.Views.Helpers.Application.capitalize platform.Browser)
                Text.raw " for "
                Html.text platform.OperatingSystem
                Text.raw ", you must install Campfire as a web app." ]
          Elem.ol
              []
              [ Elem.li [] [ Text.raw "Tap "; Elem.em [] [ icon ctx "external/share.svg" 20 "the share button" ] ]
                Elem.li [] [ Text.raw "Tap "; Elem.em [] [ Text.raw "Add to Home Screen" ]; Text.raw "." ] ] ]
    else
        [ Elem.p [] [ Text.raw "Some platforms require you to install Campfire as a web app to receive push notifications." ] ]

let render (ctx: ViewContext) : XmlNode list =
    let platform = ctx.Platform
    if platform.Chrome || (platform.Firefox && not platform.Android) then
        []
    else
        let summary =
            Elem.summary
                [ Attr.class' "btn" ]
                [ Assets.imageSized ctx "external/install.svg" 20 [ hidden ]
                  Html.sp
                  Elem.strong [] [ Text.raw "Install Campfire as a web app." ]
                  Html.sp
                  Assets.imageSized ctx "disclosure.svg" 10 [ hidden; Attr.class' "disclosure" ] ]
        let installer =
            Elem.div
                [ Attr.class' "margin-block-start txt-align-center pwa__installer" ]
                [ Elem.hr [ Attr.class' "separator margin-block" ]
                  Elem.button
                      [ Attr.class' "btn btn--reversed center"; Attr.create "data-action" "pwa-install#promptInstall" ]
                      [ Assets.image ctx "external/install.svg" [ hidden ]; Html.sp; Text.raw "Install now" ] ]
        [ Elem.details
              [ Attr.class' "notifications-help pwa__instructions hide-in-pwa"
                Attr.create "data-controller" "pwa-install"
                Attr.create "data-pwa-install-prompting-class" "pwa--can-install"
                Attr.create "data-notifications-target" "details" ]
              ([ summary ] @ steps ctx @ [ installer ]) ]
