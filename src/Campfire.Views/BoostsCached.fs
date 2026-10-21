// Port of the functions of rust/crates/views/src/messages.rs that render `messages/boosts/_boost` into the fragment cache
/// `messages/boosts/_boost` for a boost, whose body is `cache boost`. Fetched up front with `await`, never inside a template
/// (plans/design-views.md V5): the callers are presenters, controllers and message factories.
module Campfire.Views.BoostsCached

open System.Threading.Tasks
open Microsoft.Extensions.Caching.Hybrid
open Campfire.Views.Messages
open Campfire.Views.Templates.Messages.Boosts

/// `boost(ctx, boost)`: `views/messages/boosts/_boost/boosts/<id>-<version>`.
let boost (cache: HybridCache) (ctx: ViewContext) (boost: BoostView) : ValueTask<CachedFragment> =
    Fragments.fetch
        cache
        (Fragments.key "messages/boosts/_boost" "boosts" boost.Id boost.UpdatedAt None)
        (fun _ -> ValueTask<CachedFragment>(HtmlOutput.toFragment (HtmlOutput.ofNodes (_Boost.render ctx boost) [])))

/// A message's boosts in order, awaited one after the other.
let boosts (cache: HybridCache) (ctx: ViewContext) (views: BoostView list) : ValueTask<CachedFragment[]> =
    match views with
    | [] -> ValueTask<CachedFragment[]>([||])
    | _ ->
        ValueTask<CachedFragment[]>(
            task {
                let fragments = ResizeArray<CachedFragment>()
                for view in views do
                    let! fragment = boost cache ctx view
                    fragments.Add fragment
                return fragments.ToArray()
            }
        )
