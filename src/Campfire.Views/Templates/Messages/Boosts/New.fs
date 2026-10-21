// Port of rust/crates/views/templates/messages/boosts/new.html (reference/app/views/messages/boosts/new.html.erb)
/// `messages/boosts/new.html.erb`: the form that adds a boost; `user` is `Current.user`.
module Campfire.Views.Templates.Messages.Boosts.New

open Falco.Markup
open Campfire.Views
open Campfire.Views.Markup
open Campfire.Views.Messages

/// The inline buttons of the form, each separated by the space the ERB's line breaks leave (V4).
let render (ctx: ViewContext) (message: MessageView) (user: UserView) : XmlNode =
    Elem.create
        "turbo-frame"
        [ Html.attr "id" ("new_boost_message_" + message.ClientMessageId) ]
        [ Elem.div
              [ Attr.class' "boost flex-inline postion--relative max-width fill-white"
                Attr.create "style" "--column-gap: var(--inline-space-half)" ]
              [ Forms.formWith
                    message.BoostsPath
                    "post"
                    [ Attr.class' "boost__form flex align-center gap expanded"
                      Attr.create "data-controller" "form scroll-into-view"
                      Html.attr "data-turbo-frame" ("boosting_message_" + message.ClientMessageId)
                      Attr.create "data-action" "keydown.esc->form#cancel" ]
                    [ Elem.label
                          [ Attr.class' "boost__form-label flex gap"
                            Attr.create "style" "--column-gap: 0.7ch;"
                            Attr.create "role" "button"
                            Attr.create "tabindex" "0"
                            Attr.create "aria-label" "Add a boost" ]
                          [ Elem.figure
                                [ Attr.class' "avatar boost__avatar flex-item-no-shrink" ]
                                [ Links.linkTo
                                      user.Path
                                      [ Html.attr "title" user.Title; Attr.class' "btn avatar"; Attr.create "data-turbo-frame" "_top" ]
                                      [ Elem.img
                                            [ Attr.create "aria-hidden" "true"
                                              Html.attr "src" user.AvatarUrl
                                              Attr.create "width" "48"
                                              Attr.create "height" "48" ] ]
                                  Html.sp
                                  Elem.span [ Attr.class' "for-screen-reader" ] [ Html.text user.Name ] ]
                            Elem.input
                                [ Attr.create "autofocus" "autofocus"
                                  Attr.create "autocomplete" "off"
                                  Attr.create "autocorrect" "off"
                                  Attr.create "maxlength" "16"
                                  Attr.create "required" "required"
                                  Attr.create "pattern" "\\S+.*"
                                  Attr.create "data-boost-form-target" "input"
                                  Attr.class' "input input--boost txt-small"
                                  Attr.create "size" "16"
                                  Attr.create "type" "text"
                                  Attr.create "name" "boost[content]" ] ]
                      Html.sp
                      Forms.buttonTag
                          [ Attr.class' "btn btn--reversed" ]
                          [ Assets.image ctx "check.svg" [ Attr.create "aria-hidden" "true" ]
                            Html.sp
                            Elem.span [ Attr.class' "for-screen-reader" ] [ Text.raw "Submit" ] ]
                      Html.sp
                      Links.linkTo
                          message.BoostsPath
                          [ Html.attr "data-turbo-frame" ("boosts_message_" + message.ClientMessageId)
                            Attr.create "data-form-target" "cancel"
                            Attr.class' "btn btn--negative" ]
                          [ Assets.image ctx "minus.svg" [ Attr.create "aria-hidden" "true" ]
                            Html.sp
                            Elem.span [ Attr.class' "for-screen-reader" ] [ Text.raw "Cancel" ] ] ] ] ]
