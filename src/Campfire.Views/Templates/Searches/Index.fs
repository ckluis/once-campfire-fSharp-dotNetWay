// Port of rust/crates/views/templates/searches/index.html (reference/app/views/searches/index.html.erb)
/// `searches/index.html.erb`, with `SearchesHelper#search_results_tag`: the search page. A page module returns its regions and the
/// fragments it places.
module Campfire.Views.Templates.Searches.Index

open Falco.Markup
open Campfire.Routes
open Campfire.Views
open Campfire.Views.Layouts
open Campfire.Views.Markup
open Campfire.Views.Searches

let private hidden: XmlAttribute = Attr.create "aria-hidden" "true"

/// `content_for :nav`.
let private nav (ctx: ViewContext) (index: IndexView) : XmlNode list =
    [ match index.Query with
      | Some query ->
          Elem.div
              [ Attr.class' "searches__query flex align-center gap pad-block-start-half" ]
              [ Elem.div
                    [ Attr.class' "btn btn--reversed btn--faux align-center gap txt-nowrap" ]
                    [ Elem.span [ Attr.class' "overflow-ellipsis" ] [ Html.text ("“" + query + "”") ]
                      Html.sp
                      Elem.span [ Attr.class' "flex-item-no-shrink" ] [ Html.int (int64 index.Messages.Length) ] ] ]
      | None -> ()
      Elem.div
          [ Attr.class' "searches__recents align-center gap pad-block-half overflow-y overflow-hide-scrollbar" ]
          (_Recents.render ctx index) ]

/// `content_for :sidebar`.
let private sidebar (ctx: ViewContext) (index: IndexView) : XmlNode list =
    [ Elem.div
          [ Attr.class' "rooms position-relative flex flex-column gap overflow-y overflow-hide-scrollbar" ]
          (_Recents.render ctx index) ]

/// The page itself.
let private content (ctx: ViewContext) (index: IndexView) : XmlNode list =
    [ Elem.div
          [ Attr.id "message-area"; Attr.class' "message-area" ]
          [ Elem.div
                [ Attr.class' "message-area--empty min-width center" ]
                [ Elem.figure
                      [ Attr.class' "center pad" ]
                      [ Assets.image ctx "search.svg" [ hidden; Attr.class' "colorize--black translucent" ] ] ]
            Elem.div
                [ Attr.id "search-results"
                  Attr.class' "messages searches__results"
                  Attr.create "data-controller" "search-results"
                  Attr.create "data-search-results-target" "messages"
                  Attr.create "data-search-results-me-class" "message--me"
                  Attr.create "data-search-results-threaded-class" "message--threaded"
                  Attr.create "data-search-results-mentioned-class" "message--mentioned"
                  Attr.create "data-search-results-formatted-class" "message--formatted" ]
                [ for message in index.Messages -> Html.fragment message.Html ] ] ]

/// `content_for :footer`.
let private footer (ctx: ViewContext) (index: IndexView) : XmlNode list =
    [ Elem.div
          [ Attr.class' "composer flex align-end gap" ]
          [ Links.linkTo
                (Routes.room index.ReturnToRoomId)
                [ Attr.class' "btn flex-item-no-shrink margin-block-end"
                  Attr.create "style" "view-transition-name: input-switcher; --btn-border-radius: 0.5em" ]
                [ Assets.image ctx "arrow-left.svg" [ hidden ]
                  Html.sp
                  Elem.span [ Attr.class' "for-screen-reader" ] [ Text.raw "Exit search " ] ]
            Forms.formWith
                (Routes.searches ())
                "post"
                [ Attr.class' "margin-block flex-item-grow contain flex align-center gap"
                  Attr.create "data-controller" "form"
                  Attr.create "data-action" "keydown.esc->form#cancel" ]
                [ Elem.div
                      [ Attr.class' "composer__input flex align-center flex-item-grow gap full-width input input--actor min-width" ]
                      [ Assets.imageSized
                            ctx
                            "search.svg"
                            20
                            [ hidden
                              Attr.class' "composer__input-hint colorize--black"
                              Attr.create "style" "view-transition-name: input-btn;" ]
                        Html.sp
                        Elem.input
                            [ match index.Q with
                              | Some q -> Html.attr "value" q
                              | None -> ()
                              Attr.class' "searches__input input flex-item-grow"
                              Attr.create "role" "searchbox"
                              Attr.create "aria-label" "search"
                              Attr.create "autofocus" "autofocus"
                              Attr.create "required" "required"
                              Attr.create "type" "text"
                              Attr.create "name" "q"
                              Attr.create "id" "q" ]
                        Html.sp
                        Links.linkTo
                            (Routes.searches ())
                            [ Attr.create "data-form-target" "cancel"; Attr.create "role" "button"; Attr.class' "searches__reset" ]
                            [ Assets.imageSized ctx "remove.svg" 14 [ hidden; Attr.class' "colorize--black" ]
                              Html.sp
                              Elem.span [ Attr.class' "for-screen-reader" ] [ Text.raw "Clear search field" ] ]
                        Html.sp
                        Forms.buttonTag
                            [ Attr.class' "btn btn--reversed flex-item-no-shrink txt-small"
                              Attr.create "style" "--btn-border-radius: 0.5em" ]
                            [ Assets.image ctx "arrow-up.svg" [ hidden ]
                              Html.sp
                              Elem.span [ Attr.class' "for-screen-reader" ] [ Text.raw "Search" ] ] ] ] ] ]

/// The page's regions: `@page_title`, `@body_class` and the `content_for` blocks.
let regions (ctx: ViewContext) (index: IndexView) : Regions =
    { Title = Some "Search"
      BodyClass = Some "sidebar searches"
      Head = []
      Nav = nav ctx index
      Content = content ctx index
      Footer = footer ctx index
      Sidebar = sidebar ctx index }

/// The page, with the messages' fragments it places.
let rendered (ctx: ViewContext) (index: IndexView) : ViewPage =
    { Regions = regions ctx index
      Fragments = [ for message in index.Messages -> message.Html ] }
