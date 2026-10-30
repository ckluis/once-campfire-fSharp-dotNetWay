// Port of rust/crates/views/templates/rooms/show/_invitation.html (reference/app/views/rooms/show/_invitation.html.erb)
/// `rooms/show/_invitation.html.erb`: the welcome message with the join link, shown when the caller decided it applies.
module Campfire.Views.Templates.Rooms.Show._Invitation

open Falco.Markup
open Campfire.Views
open Campfire.Views.Helpers
open Campfire.Views.Markup

let render (ctx: ViewContext) (joinCode: string) : XmlNode =
    Elem.div
        [ Attr.id "system_welcome"; Attr.class' "message message--formatted txt-align-center center" ]
        [ Elem.div
              [ Attr.class' "message__body center" ]
              [ Elem.div
                    [ Attr.class' "message__body-content position-relative" ]
                    [ Markup.Users.accountLogoTag ctx (Some "center margin-block-end txt-large")
                      Elem.div
                          [ Attr.class' "flex align-center gap" ]
                          [ Elem.div
                                [ Attr.class' "system-welcome--translation" ]
                                [ Translations.translationButton ctx "invite_message" ]
                            Elem.p
                                []
                                [ Elem.strong [] [ Text.raw "Welcome to Campfire" ]
                                  Elem.br []
                                  Html.sp
                                  Text.raw "To invite people to chat, share the join link below." ] ]
                      Templates.Accounts._Invite.render ctx joinCode ] ] ]
