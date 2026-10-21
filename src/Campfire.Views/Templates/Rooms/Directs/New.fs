// Port of rust/crates/views/templates/rooms/directs/new.html (reference/app/views/rooms/directs/new.html.erb)
/// `rooms/directs/new.html.erb`: the form that starts a Ping.
module Campfire.Views.Templates.Rooms.Directs.New

open Falco.Markup
open Campfire.Routes
open Campfire.Views
open Campfire.Views.Markup

let private icon (ctx: ViewContext) (name: string) : XmlNode =
    Elem.img [ Attr.create "aria-hidden" "true"; Html.attr "src" (ctx.Asset name) ]

/// The page: no title, no nav, the frame as its content.
let rendered (ctx: ViewContext) : Campfire.Views.Layouts.ViewPage =
    let content =
        Turbo.frameTag
            "direct_rooms_control"
            None
            (Some "_top")
            []
            [ Elem.div
                  [ Attr.class' "directs directs--new flex flex-column gap" ]
                  [ Forms.formWith
                        (Routes.roomsDirects ())
                        "post"
                        [ Attr.class' "flex gap flex-item-grow"
                          Attr.create "data-controller" "form"
                          Attr.create "data-action" "keydown.esc->form#cancel" ]
                        [ Links.linkTo
                              (Routes.userSidebar ())
                              [ Attr.class' "btn flex-item-no-shrink"
                                Attr.create "data-turbo-frame" "user_sidebar"
                                Attr.create "data-form-target" "cancel" ]
                              [ icon ctx "arrow-left.svg"; Html.sp; Application.screenReader "Cancel changes" ]
                          Elem.section
                              [ Attr.class' "autocomplete__container unpad input input--actor" ]
                              [ Elem.div
                                    [ Attr.class' "autocomplete__input input flex flex-wrap position-relative flex-item-grow"
                                      Attr.create "data-controller" "autocomplete"
                                      Html.attr "data-autocomplete-url-value" (Routes.autocompletableUsers ()) ]
                                    [ Elem.select
                                          [ Attr.create "name" "user_ids[]"
                                            Attr.create "data-autocomplete-target" "select"
                                            Attr.create "data-template-id" "autocompletable-user"
                                            Attr.create "multiple" "true"
                                            Attr.create "hidden" ""
                                            Attr.create "required" "" ]
                                          []
                                      Html.sp
                                      Templates.Users.Autocompletables._Template.render ctx
                                      Html.sp
                                      Elem.input
                                          [ Attr.create "autocomplete" "off"
                                            Attr.create "autocorrect" "off"
                                            Attr.create "data-1p-ignore" "true"
                                            Attr.class' "autocomplete__input input flex flex-wrap position-relative"
                                            Attr.create "data-autocomplete-target" "input"
                                            Attr.create "data-action" "input->autocomplete#search keydown->autocomplete#didPressKey"
                                            Attr.create "type" "text"
                                            Attr.create "name" "rooms_direct[user_ids_input]"
                                            Attr.create "id" "rooms_direct_user_ids_input" ] ] ]
                          Forms.buttonTag
                              [ Attr.class' "btn btn--reversed flex-item-no-shrink" ]
                              [ icon ctx "check.svg"; Html.sp; Application.screenReader "Start Ping" ] ]
                    Elem.span
                        [ Attr.class' "txt-small translucent pad-inline-half center" ]
                        [ Text.raw "Type names to ping someone…" ] ] ]
    { Regions = Campfire.Views.Layouts.Regions.content [ content ]
      Fragments = [] }
