// Port of rust/crates/views/templates/pwa/_browser_settings.html (reference/app/views/pwa/_browser_settings.html.erb)
/// `pwa/_browser_settings.html.erb`: where to allow notifications in the browser.
module Campfire.Views.Templates.Pwa._BrowserSettings

open Falco.Markup
open Campfire.Views
open Campfire.Views.Markup

let private hidden: XmlAttribute = Attr.create "aria-hidden" "true"

/// `image_tag(source, alt:, size:)`.
let private icon (ctx: ViewContext) (source: string) (size: int) (alt: string) : XmlNode =
    Assets.imageSized ctx source size [ Html.attr "alt" alt ]

let private switch (ctx: ViewContext) (alt: string) : XmlNode = icon ctx "external/switch.svg" 22 alt

let private heading (children: XmlNode list) : XmlNode =
    Elem.h2 [ Attr.class' "txt-normal txt-medium margin-block-start" ] children

let private browserName (ctx: ViewContext) : XmlNode =
    Html.text (Campfire.Views.Helpers.Application.capitalize ctx.Platform.Browser)

/// "Turn on notifications for <browser>": the system's steps, on Windows or a Mac.
let private systemSteps (ctx: ViewContext) (windowsAlt: string) : XmlNode =
    Elem.ol
        []
        (if ctx.Platform.Windows then
             [ Elem.li [] [ Text.raw "Click "; Elem.em [] [ Text.raw "Start" ]; Text.raw ", then "; Elem.em [] [ Text.raw "Settings" ]; Text.raw "." ]
               Elem.li [] [ Text.raw "Go to "; Elem.em [] [ Text.raw "System &gt; Notification" ]; Text.raw "." ]
               Elem.li
                   []
                   [ Text.raw "Click "
                     Elem.em [] [ switch ctx windowsAlt ]
                     Html.sp
                     Elem.em [] [ Text.raw "ON" ]
                     Text.raw " for "
                     browserName ctx
                     Text.raw "." ] ]
         else
             [ Elem.li [] [ Text.raw "Click "; Elem.em [ Attr.create "aria-label" "the Apple menu" ] [ Text.raw "\uF8FF" ]; Text.raw " in the top left." ]
               Elem.li [] [ Text.raw "Click "; Elem.em [] [ Text.raw "System Settings…" ]; Text.raw "." ]
               Elem.li [] [ Text.raw "Click "; Elem.em [] [ Text.raw "Notifications" ]; Text.raw "." ]
               Elem.li [] [ Text.raw "Click "; Elem.em [] [ browserName ctx ]; Text.raw "." ]
               Elem.li
                   []
                   [ Text.raw "Click "
                     Elem.em [] [ switch ctx "the switch" ]
                     Text.raw " to "
                     Elem.em [] [ Text.raw "Allow notifications" ]
                     Text.raw "." ] ])

let private forThisWebsite: XmlNode = heading [ Text.raw "Turn on notifications for this website." ]

let private forBrowser (ctx: ViewContext) : XmlNode =
    heading [ Text.raw "Turn on notifications for "; browserName ctx; Text.raw "." ]

let private steps (ctx: ViewContext) : XmlNode list =
    let platform = ctx.Platform
    if platform.Firefox && platform.Android then
        [ Elem.ol
              []
              [ Elem.li
                    []
                    [ Text.raw "Tap "
                      Elem.em [] [ icon ctx "lock.svg" 20 "the View site information button" ]
                      Text.raw " in the address bar." ]
                Elem.li
                    []
                    [ Text.raw "Tap "
                      Elem.em [] [ Text.raw "Notification" ]
                      Text.raw " to change to "
                      Elem.em [] [ Text.raw "Allowed" ]
                      Text.raw "." ] ] ]
    elif platform.Edge && platform.Desktop then
        [ forThisWebsite
          Elem.ol
              []
              [ Elem.li
                    []
                    [ Text.raw "Click "
                      Elem.em [] [ icon ctx "lock.svg" 20 "the View site information button" ]
                      Text.raw " left of the address bar." ]
                Elem.li
                    []
                    [ Text.raw "Under "
                      Elem.em [] [ Text.raw "Permissions for this site &gt; Notifications" ]
                      Text.raw ", choose "
                      Elem.em [] [ Text.raw "Allow" ]
                      Text.raw "." ] ]
          forBrowser ctx
          systemSteps ctx "the switch" ]
    elif platform.Firefox && platform.Desktop then
        [ forThisWebsite
          Elem.ol
              []
              [ Elem.li [] [ Text.raw "Click "; Elem.em [] [ browserName ctx ]; Text.raw " in the top left." ]
                Elem.li [] [ Text.raw "Click "; Elem.em [] [ Text.raw "Settings…" ]; Text.raw "." ]
                Elem.li [] [ Text.raw "Click "; Elem.em [] [ Text.raw "Privacy & Security" ]; Text.raw " in the sidebar." ]
                Elem.li [] [ Text.raw "Scroll down to "; Elem.em [] [ Text.raw "Permissions" ]; Text.raw "." ]
                Elem.li
                    []
                    [ Text.raw "Click "
                      Elem.em [] [ Text.raw "Settings" ]
                      Text.raw " next to "
                      Elem.em [] [ Text.raw "Notifications" ]
                      Text.raw "." ]
                Elem.li
                    []
                    [ Text.raw "Select "
                      Elem.em [] [ Text.raw "Allow" ]
                      Text.raw " next to "
                      Elem.em [] [ Html.text (ctx.Url "/") ]
                      Text.raw "." ] ]
          forBrowser ctx
          systemSteps ctx "the toggle button" ]
    elif platform.Chrome && platform.Desktop then
        [ forThisWebsite
          Elem.ol
              []
              [ Elem.li
                    []
                    [ Text.raw "Click the "
                      Elem.em [] [ icon ctx "external/sliders.svg" 20 "View site information" ]
                      Text.raw " icon in the address bar." ]
                Elem.li [] [ Text.raw "Click "; Elem.em [] [ Text.raw "Site Settings" ]; Text.raw "." ]
                Elem.li [] [ Text.raw "Ensure notifications are "; Elem.em [] [ Text.raw "Allowed" ]; Text.raw "." ] ]
          forBrowser ctx
          systemSteps ctx "the switch" ]
    elif platform.Chrome && platform.Android then
        [ Elem.ol
              []
              [ Elem.li
                    []
                    [ Text.raw "Tap the "
                      Elem.em [] [ icon ctx "menu-dots-vertical.svg" 16 "More options" ]
                      Text.raw " menu button." ]
                Elem.li [] [ Text.raw "Tap "; Elem.em [] [ Text.raw "Settings" ]; Text.raw "." ]
                Elem.li [] [ Text.raw "Tap "; Elem.em [] [ Text.raw "Notifications" ]; Text.raw "." ]
                Elem.li
                    []
                    [ Text.raw "Tap "
                      Elem.em [] [ switch ctx "the switch" ]
                      Text.raw " to "
                      Elem.em [] [ Text.raw "Allow "; browserName ctx; Text.raw " notifications" ]
                      Text.raw "." ]
                Elem.li
                    []
                    [ Text.raw "Tap "
                      Elem.em [] [ switch ctx "the switch" ]
                      Text.raw " next to "
                      Elem.em [] [ Text.raw "Web apps" ]
                      Text.raw "." ]
                Elem.li
                    []
                    [ Text.raw "Tap "
                      Elem.em [] [ icon ctx "notification-bell-alert.svg" 16 "the notification bell" ]
                      Text.raw " and select "
                      Elem.em [] [ Text.raw "Allow" ]
                      Text.raw "." ] ] ]
    elif platform.Safari && platform.Desktop then
        [ Elem.ol
              []
              [ Elem.li [] [ Text.raw "Click "; Elem.em [] [ browserName ctx ]; Text.raw " in the top left." ]
                Elem.li [] [ Text.raw "Click "; Elem.em [] [ Text.raw "Settings…" ]; Text.raw "." ]
                Elem.li [] [ Text.raw "Click the "; Elem.em [] [ Text.raw "Websites" ]; Text.raw " tab." ]
                Elem.li [] [ Text.raw "Click "; Elem.em [] [ Text.raw "Notifications" ]; Text.raw " in the sidebar." ]
                Elem.li [] [ Text.raw "Click "; Elem.em [] [ Html.text (ctx.Url "/") ]; Text.raw " in the list." ]
                Elem.li [] [ Text.raw "Select "; Elem.em [] [ Text.raw "Allow" ]; Text.raw "." ] ] ]
    else
        [ Elem.p
              []
              [ Text.raw "Ensure notifications are enabled for "
                Elem.em [] [ Html.text (ctx.Url "/") ]
                Text.raw " in your web browser settings." ] ]

let render (ctx: ViewContext) : XmlNode list =
    let platform = ctx.Platform
    if (platform.Safari || platform.Chrome) && platform.Ios then
        []
    else
        [ Elem.details
              [ Attr.class' "notifications-help"; Attr.create "data-notifications-target" "details" ]
              (Elem.summary
                  [ Attr.class' "btn" ]
                  [ Assets.imageSized ctx "external/web.svg" 20 [ hidden ]
                    Html.sp
                    Elem.strong [] [ Text.raw "Check your "; browserName ctx; Text.raw " settings" ]
                    Html.sp
                    Assets.imageSized ctx "disclosure.svg" 10 [ hidden; Attr.class' "disclosure" ] ]
               :: steps ctx) ]
