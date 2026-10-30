// Port of rust/crates/views/templates/pwa/_system_settings.html (reference/app/views/pwa/_system_settings.html.erb)
/// `pwa/_system_settings.html.erb`: where to allow notifications in the system settings.
module Campfire.Views.Templates.Pwa._SystemSettings

open Falco.Markup
open Campfire.Views
open Campfire.Views.Markup

let private hidden: XmlAttribute = Attr.create "aria-hidden" "true"

let private icon (ctx: ViewContext) (source: string) (size: int) (alt: string) : XmlNode =
    Assets.imageSized ctx source size [ Html.attr "alt" alt ]

let private startThenSettings: XmlNode =
    Elem.li [] [ Text.raw "Click "; Elem.em [] [ Text.raw "Start" ]; Text.raw ", then "; Elem.em [] [ Text.raw "Settings" ]; Text.raw "." ]

let private systemNotification: XmlNode =
    Elem.li [] [ Text.raw "Go to "; Elem.em [] [ Text.raw "System &gt; Notification" ]; Text.raw "." ]

let private windowsOn (ctx: ViewContext) (alt: string) : XmlNode =
    Elem.li
        []
        [ Text.raw "Click "
          Elem.em [] [ icon ctx "external/switch.svg" 22 alt ]
          Html.sp
          Elem.em [] [ Text.raw "ON" ]
          Text.raw " for Campfire." ]

let private appleMenu: XmlNode =
    Elem.li [] [ Text.raw "Click "; Elem.em [ Attr.create "aria-label" "the Apple menu" ] [ Text.raw "\uF8FF" ]; Text.raw " in the top left." ]

let private macSteps (ctx: ViewContext) (alt: string) : XmlNode list =
    [ appleMenu
      Elem.li [] [ Text.raw "Click "; Elem.em [] [ Text.raw "System Settings…" ]; Text.raw "." ]
      Elem.li [] [ Text.raw "Click "; Elem.em [] [ Text.raw "Notifications" ]; Text.raw "." ]
      Elem.li [] [ Text.raw "Click "; Elem.em [] [ Text.raw "Campfire" ]; Text.raw "." ]
      Elem.li
          []
          [ Text.raw "Click "
            Elem.em [] [ icon ctx "external/switch.svg" 22 alt ]
            Text.raw " to "
            Elem.em [] [ Text.raw "Allow notifications" ]
            Text.raw "." ] ]

let private settingsApp (ctx: ViewContext) : XmlNode list =
    [ Text.raw "Open the "
      Elem.em [] [ Assets.imageSized ctx "external/gear.svg" 20 [ hidden ] ]
      Text.raw " Settings app." ]

let private steps (ctx: ViewContext) : XmlNode =
    let platform = ctx.Platform
    if platform.Firefox && platform.Android then
        Elem.ol
            []
            [ Elem.li [] [ Text.raw "Tap the "; Elem.em [] [ icon ctx "menu-dots-vertical.svg" 16 "More options" ]; Text.raw " menu button." ]
              Elem.li [] [ Text.raw "Tap "; Elem.em [] [ Text.raw "Settings" ]; Text.raw "." ]
              Elem.li [] [ Text.raw "Tap "; Elem.em [] [ Text.raw "Notifications" ]; Text.raw "." ]
              Elem.li
                  []
                  [ Text.raw "Tap "
                    Elem.em [] [ icon ctx "external/switch.svg" 22 "the toggle button" ]
                    Text.raw " to "
                    Elem.em [] [ Text.raw "Allow "; Html.text (Campfire.Views.Helpers.Application.capitalize platform.Browser); Text.raw " notifications" ]
                    Text.raw "." ] ]
    elif platform.Edge && platform.Desktop then
        Elem.ol [] [ startThenSettings; systemNotification; windowsOn ctx "the toggle button" ]
    elif (platform.Firefox || platform.Chrome) && platform.Desktop then
        Elem.ol
            []
            (if platform.Windows then
                 [ startThenSettings; systemNotification; windowsOn ctx "the toggle button" ]
             else
                 macSteps ctx "the allow notifications switch")
    elif platform.Safari && platform.Desktop then
        Elem.ol [] (macSteps ctx "the allow notifications switch")
    elif (platform.Safari || platform.Chrome) && platform.Ios then
        Elem.ol
            []
            [ Elem.li [] (settingsApp ctx)
              Elem.li [] [ Text.raw "Scroll to and tap "; Elem.em [] [ Text.raw "Campfire" ]; Text.raw "." ]
              Elem.li [] [ Text.raw "Tap "; Elem.em [] [ Text.raw "Notifications" ]; Text.raw "." ]
              Elem.li
                  []
                  [ Text.raw "Tap "
                    Elem.em [] [ icon ctx "external/switch.svg" 22 "the allow notifications switch button" ]
                    Text.raw " to "
                    Elem.em [] [ Text.raw "Allow Notifications" ]
                    Text.raw "." ] ]
    elif platform.Chrome && platform.Android then
        Elem.ol
            []
            [ Elem.li [] (settingsApp ctx)
              Elem.li [] [ Text.raw "Tap "; Elem.em [] [ Text.raw "Notifications" ]; Text.raw "." ]
              Elem.li [] [ Text.raw "Tap "; Elem.em [] [ Text.raw "App notifications" ]; Text.raw "." ]
              Elem.li [] [ Text.raw "Scroll to "; Elem.em [] [ Text.raw "Campfire" ]; Text.raw "." ]
              Elem.li
                  []
                  [ Text.raw "Tap "
                    Elem.em [] [ icon ctx "external/switch.svg" 22 "the switch" ]
                    Text.raw " to "
                    Elem.em [] [ Text.raw "Allow Notifications" ]
                    Text.raw "." ] ]
    else
        Elem.p
            []
            [ Text.raw "Ensure notifications are allowed for "
              Html.text (Campfire.Views.Helpers.Application.capitalize platform.Browser)
              Text.raw " in your system settings." ]

let render (ctx: ViewContext) : XmlNode =
    Elem.details
        [ Attr.class' "notifications-help hide-in-browser"; Attr.create "data-notifications-target" "details" ]
        [ Elem.summary
              [ Attr.class' "btn" ]
              [ Assets.imageSized ctx "external/gear.svg" 20 [ hidden ]
                Html.sp
                Elem.strong [] [ Text.raw "Check your "; Html.text ctx.Platform.OperatingSystem; Text.raw " settings" ]
                Html.sp
                Assets.imageSized ctx "disclosure.svg" 10 [ hidden; Attr.class' "disclosure" ] ]
          steps ctx ]
