// Port of rust/crates/views/src/helpers/rooms.rs
/// The parts of `RoomsHelper` and `Rooms::InvolvementsHelper` the sidebar and the profile's
/// memberships use; the rest of the rooms helpers are in `Campfire.Views.Rooms`.
module Campfire.Views.Helpers.RoomsHelper

/// `HUMANIZE_INVOLVEMENT`.
let humanizeInvolvement (involvement: string) : string =
    match involvement with
    | "mentions" -> "Notifying about @ mentions"
    | "everything" -> "Notifying about all messages"
    | "nothing" -> "Notifications are off"
    | "invisible" -> "Notifications are off and room invisible in sidebar"
    | _ -> ""

/// `next_involvement_for(room, involvement:)`.
let nextInvolvement (direct: bool) (involvement: string) : string =
    let order = if direct then [| "everything"; "nothing" |] else [| "mentions"; "everything"; "nothing"; "invisible" |]
    match Array.tryFindIndex ((=) involvement) order with
    | Some index when index + 1 < order.Length -> order[index + 1]
    | _ -> order[0]

/// A room as the involvement helpers see it.
type InvolvementRoom =
    { Id: int64
      /// `model_name.param_key` of the room's class: "rooms_open", "rooms_closed" or "rooms_direct".
      ParamKey: string
      Direct: bool }
