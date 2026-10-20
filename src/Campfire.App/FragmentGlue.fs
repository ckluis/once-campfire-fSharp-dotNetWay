// What `Campfire.App` does between the views and the page parts: a page rendered with `Render.page` is a text and the
// fragments it recorded, `PageParts.Splice` takes those, and the SHA for each fragment hangs off the view fragment and
// is counted by the fragment cache. `Campfire.Views` doesn't reference the app's `Web` layer; this is where they meet.
namespace Campfire.App

open System
open Campfire.App.Web
open Campfire.Views

/// The kit's side of a view fragment: what `Fragment.Attach` holds, found again for every page.
[<Sealed>]
type PartsLink(fragment: Campfire.App.Web.Fragment) =
    member _.Fragment = fragment

    interface IFragmentAttachment with
        member _.HeldBytes = fragment.HeldBytes

module FragmentGlue =
    /// The page-parts fragment of a view fragment, made the first time a page splices it.
    let kitFragment (fragment: Campfire.Views.Fragment) : Campfire.App.Web.Fragment =
        match fragment.Attachment with
        | :? PartsLink as link -> link.Fragment
        | _ ->
            match fragment.Attach(PartsLink(Campfire.App.Web.Fragment fragment.Bytes)) with
            | :? PartsLink as link -> link.Fragment
            | _ -> failwith "only the app attaches to a fragment"

    /// The page's body split at its fragments, or that body whole when none is big enough for a part.
    let splice (page: RecordedPage) : Result<PageParts, ReadOnlyMemory<byte>> =
        let fragments =
            page.Fragments |> Array.map (fun struct (offset, fragment) -> struct (offset, kitFragment fragment))
        PageParts.Splice(ReadOnlyMemory<byte> page.Text, fragments)
