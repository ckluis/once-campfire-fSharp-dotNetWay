// Port of rust/crates/views/templates/messages/_unrenderable.html (reference/app/views/messages/_unrenderable.html.erb)
/// `messages/_unrenderable.html.erb`: what `message_tag` renders in place of a message whose content raised.
module Campfire.Views.Templates.Messages._Unrenderable

open Falco.Markup

let render: XmlNode =
    Elem.div
        [ Attr.class' "message message--formatted message--failed center" ]
        [ Elem.div
              [ Attr.class' "message__body" ]
              [ Elem.div [ Attr.class' "message__body-content txt-align-center" ] [ Text.raw "Failed to load message content" ] ] ]
