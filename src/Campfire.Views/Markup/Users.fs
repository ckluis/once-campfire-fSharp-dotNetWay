// `UsersHelper`, `Users::FilterHelper` and `AccountsHelper` as Falco.Markup functions (plans/design-views.md V13). First part: what
// `rooms/**` needs.
namespace Campfire.Views.Markup

open Falco.Markup
open Campfire.Routes
open Campfire.Views

module Users =
    /// `avatar_tag(user, **options)`: a link to the user around their avatar; the options go to the image, after `aria-hidden`.
    let avatarTag (ctx: ViewContext) (user: Campfire.Views.Helpers.UsersHelper.AvatarUser) (options: XmlAttribute list) : XmlNode =
        Links.linkTo
            (Routes.user user.Id)
            [ Html.attr "title" user.Title; Attr.class' "btn avatar"; Attr.create "data-turbo-frame" "_top" ]
            [ Assets.imageSized ctx user.AvatarPath 48 (Attr.create "aria-hidden" "true" :: options) ]

    /// `account_logo_tag(style:)`. A nil style leaves a trailing space in the class.
    let accountLogoTag (ctx: ViewContext) (style: string option) : XmlNode =
        Elem.figure
            [ Html.attr "class" ("account-logo avatar " + defaultArg style "") ]
            [ Assets.imageSized ctx ctx.Account.LogoUrl 300 [ Attr.create "alt" "Account logo" ] ]

    /// `user_filter_menu_tag { content }`.
    let userFilterMenu (content: XmlNode list) : XmlNode =
        Elem.create
            "menu"
            [ Attr.class' "flex flex-column gap margin-none pad overflow-y constrain-height"
              Attr.create "data-controller" "filter"
              Attr.create "data-filter-active-class" "filter--active"
              Attr.create "data-filter-selected-class" "selected" ]
            content

    /// `user_filter_search_tag`.
    let userFilterSearch: XmlNode =
        Elem.input
            [ Attr.create "type" "search"
              Attr.create "id" "search"
              Attr.create "autocorrect" "off"
              Attr.create "autocomplete" "off"
              Attr.create "data-1p-ignore" "true"
              Attr.class' "input input--transparent full-width"
              Attr.create "placeholder" "Filter…"
              Attr.create "data-action" "input->filter#filter" ]

    /// `sidebar_turbo_frame_tag(src:) { content }`.
    let sidebarFrameWith (src: string option) (content: XmlNode list) : XmlNode =
        Turbo.frameTag
            "user_sidebar"
            src
            (Some "_top")
            [ Attr.create "data-turbo-permanent" "true"
              Attr.create "data-controller" "rooms-list read-rooms turbo-frame"
              Attr.create "data-rooms-list-unread-class" "unread"
              Attr.create
                  "data-action"
                  "presence:present@window->rooms-list#read read-rooms:read->rooms-list#read turbo:frame-load->rooms-list#loaded refresh-room:visible@window->turbo-frame#reload" ]
            content

    /// `sidebar_turbo_frame_tag(src:)` with nothing in it.
    let sidebarFrame (src: string option) : XmlNode = sidebarFrameWith src []
