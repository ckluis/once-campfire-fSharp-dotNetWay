// turbo-rails' helpers as Falco.Markup functions (plans/design-views.md V13).
namespace Campfire.Views.Markup

open Falco.Markup

module Turbo =
    /// `turbo_frame_tag(id, src:, target:, **attributes) { content }`: the given attributes first, then `id`, `src` and `target`
    /// (the ones that are `None` dropped).
    let frameTag (id: string) (src: string option) (target: string option) (attributes: XmlAttribute list) (content: XmlNode list) : XmlNode =
        Elem.create
            "turbo-frame"
            (attributes
             @ [ Html.attr "id" id
                 match src with
                 | Some src -> Html.attr "src" src
                 | None -> ()
                 match target with
                 | Some target -> Html.attr "target" target
                 | None -> () ])
            content

    /// `turbo_stream_from(...)` with Turbo's own channel; the signed stream name comes from the caller.
    let streamFrom (signedStreamName: string) : XmlNode =
        Elem.create
            "turbo-cable-stream-source"
            [ Attr.create "channel" "Turbo::StreamsChannel"; Html.attr "signed-stream-name" signedStreamName ]
            []

    /// `turbo_stream.<action>(target) { content }`: the content goes in a `template`.
    let stream (action: string) (target: string) (content: XmlNode list) : XmlNode =
        Elem.create "turbo-stream" [ Html.attr "action" action; Html.attr "target" target ] [ Elem.template [] content ]
