// Rails' asset helpers as Falco.Markup functions (plans/design-views.md V13). First part: what `messages/**` needs.
namespace Campfire.Views.Markup

open Falco.Markup
open Campfire.Views

module Assets =
    /// `image_tag(source, options)`: the options in order, then `src`.
    let image (ctx: ViewContext) (source: string) (options: XmlAttribute list) : XmlNode =
        Elem.img (options @ [ Html.attr "src" (Campfire.Views.Helpers.Assets.assetPath ctx source) ])

    /// `image_tag(source, size: n, options)`: the options in order, then `src`, `width` and `height`.
    let imageSized (ctx: ViewContext) (source: string) (size: int) (options: XmlAttribute list) : XmlNode =
        Elem.img (
            options
            @ [ Html.attr "src" (Campfire.Views.Helpers.Assets.assetPath ctx source)
                Html.attrInt "width" (int64 size)
                Html.attrInt "height" (int64 size) ]
        )
