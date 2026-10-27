// Port of rust/crates/views/templates/searches/_recents.html
/// The recent-searches links repeated in `searches/index.html.erb`'s nav and sidebar.
module Campfire.Views.Templates.Searches._Recents

open Falco.Markup
open Campfire.Rails.Routes
open Campfire.Views
open Campfire.Views.Markup
open Campfire.Views.Searches

let render (ctx: ViewContext) (index: IndexView) : XmlNode list =
    [ for position, search in List.indexed index.RecentSearches do
          if position > 0 then Html.sp
          Links.linkTo
              (searchPath search)
              [ Attr.class' "align-center gap room btn txt-nowrap" ]
              [ Elem.span [ Attr.class' "overflow-ellipsis" ] [ Html.text ("“" + search + "”") ] ]
      if not index.RecentSearches.IsEmpty then
          Forms.buttonTo
              (ctx.Url(Routes.clearSearches ()))
              "delete"
              [ Attr.class' "btn searches__btn"
                Attr.create "data-turbo-confirm" "Are you sure you want to clear your recent searches?" ]
              [ Assets.image ctx "broom.svg" [ Attr.create "aria-hidden" "true" ]
                Html.sp
                Elem.span [ Attr.class' "for-screen-reader" ] [ Text.raw "Clear recent searches" ] ] ]
