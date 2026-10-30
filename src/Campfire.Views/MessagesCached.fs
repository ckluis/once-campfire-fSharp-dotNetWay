// Port of the functions of rust/crates/views/src/messages.rs that render `messages/_message` into the fragment cache
/// `messages/_message` for a message, whose body is `cache [ message, "presentation-v3" ]` (and whose collection renders are
/// `cached: true`), so a message version renders once. Fetched up front with `await` (plans/design-views.md V5).
module Campfire.Views.MessagesCached

open System.Threading.Tasks
open Campfire.Rails
open Microsoft.Extensions.Caching.Hybrid
open Campfire.Views.Messages
open Campfire.Views.Templates.Messages

/// The fragment of message `id` at `updatedAt`. `view` builds the `MessageView` and runs on a miss only, as Rails evaluates the
/// block of `cache` only then; the message's factory awaits its boosts' fragments first, as Rails nests `cache boost` inside
/// `cache [ message, "presentation-v3" ]`.
let fetch
    (cache: HybridCache)
    (ctx: ViewContext)
    (id: int64)
    (updatedAt: Timestamp)
    (view: unit -> MessageView)
    : ValueTask<CachedFragment> =
    Fragments.fetch
        cache
        (Fragments.key "messages/_message" "messages" id updatedAt (Some "presentation-v3"))
        (fun _ ->
            ValueTask<CachedFragment>(
                task {
                    let view = view ()
                    let! boosts = BoostsCached.boosts cache ctx view.Boosts
                    let boosts = if view.IsUnrenderable then [||] else boosts
                    return HtmlOutput.toFragment (HtmlOutput.ofNode (_Message.render ctx view boosts) boosts)
                }
            ))

/// `message(ctx, message)`.
let message (cache: HybridCache) (ctx: ViewContext) (message: MessageView) : ValueTask<CachedFragment> =
    fetch cache ctx message.Id message.UpdatedAt (fun () -> message)

/// `render @messages` over views already built, in order.
let messages (cache: HybridCache) (ctx: ViewContext) (views: MessageView list) : ValueTask<CachedFragment[]> =
    ValueTask<CachedFragment[]>(
        task {
            let fragments = ResizeArray<CachedFragment>()
            for view in views do
                let! fragment = message cache ctx view
                fragments.Add fragment
            return fragments.ToArray()
        }
    )

/// `message` as the item a page or a turbo stream takes.
let item (cache: HybridCache) (ctx: ViewContext) (view: MessageView) : ValueTask<MessageItem> =
    ValueTask<MessageItem>(
        task {
            let! html = message cache ctx view
            return { ClientMessageId = view.ClientMessageId; RoomId = view.RoomId; Html = html }
        }
    )
