// Port of the functions of rust/crates/views/src/users.rs that render templates into the fragment cache
/// `users/sidebars/rooms/_direct` for a membership, whose body is `cache membership` (and which `users/sidebars/show` renders
/// with `cached: true`): the first rendering of a membership version is what later renders reuse. Fetched up front with `await`
/// (plans/design-views.md V5).
module Campfire.Views.UsersCached

open System.Threading.Tasks
open Campfire.RailsCompat
open Microsoft.Extensions.Caching.Hybrid
open Campfire.Views.Users
open Campfire.Views.Templates.Users.Sidebars.Rooms

/// The fragment of membership `membershipId` at `updatedAt`. `view` builds the locals and runs on a miss only: `cache
/// membership` wraps the whole partial, so on a hit Rails loads none of its members.
let fetch
    (cache: HybridCache)
    (ctx: ViewContext)
    (membershipId: int64)
    (updatedAt: Timestamp)
    (view: unit -> SidebarDirect)
    : ValueTask<CachedFragment> =
    Fragments.fetch
        cache
        (Fragments.key "users/sidebars/rooms/_direct" "memberships" membershipId updatedAt None)
        (fun _ -> ValueTask<CachedFragment>(Render.fragment (fun w -> _Direct.render w ctx (view ()))))

/// `direct_room(ctx, membership)`.
let directRoom (cache: HybridCache) (ctx: ViewContext) (membership: SidebarDirect) : ValueTask<CachedFragment> =
    fetch cache ctx membership.MembershipId membership.MembershipUpdatedAt (fun () -> membership)

/// The direct rooms of a sidebar, in order.
let directRooms (cache: HybridCache) (ctx: ViewContext) (views: SidebarDirect list) : ValueTask<CachedFragment[]> =
    ValueTask<CachedFragment[]>(
        task {
            let fragments = ResizeArray<CachedFragment>()
            for view in views do
                let! fragment = directRoom cache ctx view
                fragments.Add fragment
            return fragments.ToArray()
        }
    )
