// Port of rust/crates/views/templates/messages/room_not_found.html (reference/app/views/messages/room_not_found.html.erb)
/// `messages/room_not_found.html.erb`: the composer's frame when the room was deleted.
module Campfire.Views.Templates.Messages.RoomNotFound

open Falco.Markup

let render: XmlNode =
    Elem.create
        "turbo-frame"
        [ Attr.id "composer-frame" ]
        [ Elem.span
              [ Attr.class' "composer__input input input--actor shake margin-block-end txt-negative txt-align-center"
                Attr.create "style" "--input-border-color: var(--color-negative)" ]
              [ Elem.span [] [ Text.raw "This room was deleted." ] ] ]
