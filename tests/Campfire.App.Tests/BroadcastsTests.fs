// Port of rust/crates/campfire/src/channels/tests/broadcasts_test.rs
//
// The broadcasts' streams, targets and markup, as delivered to subscribers.
module Campfire.App.Tests.BroadcastsTests

open System
open System.Text.Json
open System.Threading.Tasks
open Xunit
open Campfire.App.Channels
open Campfire.Cable
open Campfire.Db
open Campfire.RailsCompat
open Campfire.App.Tests.ChannelsSupport
open Campfire.Tests.JsonFrames

let private obj' (entries: (string * Value) list) : Value = Value.Object entries

/// The `<turbo-stream>` a delivery frame carries.
let private turboStream (frame: string) : string =
    use document = JsonDocument.Parse frame
    match document.RootElement.GetProperty("message").GetString() with
    | null -> failwith "a Turbo Stream string"
    | text -> text

let private roomMessages (app: TestApp) (client: Client) (room: string) : Task<string> =
    task {
        let! room = app.Room room
        let signed = app.SignedStreamName [ GlobalId.toParam (Gid.roomGid room); "messages" ]
        let channel = identifier (obj' [ "channel", Value.String "RoomMessagesChannel"; "signed_stream_name", Value.String signed ])
        do! client.Confirm channel
        return channel
    }

let private turbo (app: TestApp) (client: Client) (streamables: string list) : Task<string> =
    task {
        let channel =
            identifier (obj' [ "channel", Value.String "Turbo::StreamsChannel"; "signed_stream_name", Value.String(app.SignedStreamName streamables) ])
        do! client.Confirm channel
        return channel
    }

let private ok (result: Result<'T, DbError>) : 'T =
    match result with
    | Ok value -> value
    | Error error -> failwith (DbError.display error)

[<Fact>]
let ``message broadcasts`` () =
    task {
        use! app = start ()
        use! kevin = app.Connect "kevin"
        let! _ = roomMessages app kevin "designers"
        let unreads = identifier (obj' [ "channel", Value.String "UnreadRoomsChannel" ])
        do! kevin.Confirm unreads

        let! designers = app.Room "designers"
        let! message = app.Message "second"

        app.ReadEf(fun db -> app.Broadcasts.MessageCreate(db, designers, message, fakePartials))
        let! frame = kevin.NextText()
        Assert.Equal(
            $"""<turbo-stream action="append" target="messages_rooms_closed_{designers.Id}"><template><div id="message_0002">message {message.Id}</div></template></turbo-stream>""",
            turboStream frame
        )
        let! frame = kevin.NextText()
        assertSameFrame (delivery unreads $"""{{"roomId":{designers.Id}}}""") frame

        app.Broadcasts.MessageReplace(designers, message, fakePartials)
        let! frame = kevin.NextText()
        Assert.Equal(
            $"""<turbo-stream maintain_scroll="true" action="replace" target="presentation_message_0002"><template><div>presentation {message.Id} & more</div></template></turbo-stream>""",
            turboStream frame
        )

        app.Broadcasts.MessageRemove(designers, message)
        let! frame = kevin.NextText()
        Assert.Equal("""<turbo-stream action="remove" target="message_0002"></turbo-stream>""", turboStream frame)
        do! kevin.AssertSilent()
    }

[<Fact>]
let ``boost broadcasts`` () =
    task {
        use! app = start ()
        use! kevin = app.Connect "kevin"
        let! _ = roomMessages app kevin "designers"
        let! designers = app.Room "designers"
        let! message = app.Message "first"
        let! boost = app.Boost "first"

        app.Broadcasts.BoostCreate(designers, message, boost, fakePartials)
        let! frame = kevin.NextText()
        Assert.Equal(
            $"""<turbo-stream maintain_scroll="true" action="append" target="boosts_message_0001"><template><div>boost {boost.Id}</div></template></turbo-stream>""",
            turboStream frame
        )

        app.Broadcasts.BoostRemove(designers, boost)
        let! frame = kevin.NextText()
        Assert.Equal($"""<turbo-stream action="remove" target="boost_{boost.Id}"></turbo-stream>""", turboStream frame)
    }

[<Fact>]
let ``room list broadcasts`` () =
    task {
        use! app = start ()
        use! jz = app.Connect "jz"
        let! _ = turbo app jz [ "rooms" ]
        let ownRooms = GlobalId.toParam (Gid.userGid (id "jz"))
        let! _ = turbo app jz [ ownRooms; "rooms" ]

        let! hq = app.Room "hq"
        app.Broadcasts.OpenRoomCreate(hq, fakePartials)
        let! frame = jz.NextText()
        Assert.Equal(
            $"""<turbo-stream action="prepend" target="shared_rooms"><template><li>shared {hq.Id}</li></template></turbo-stream>""",
            turboStream frame
        )

        app.Broadcasts.OpenRoomUpdate(hq, fakePartials)
        let! frame = jz.NextText()
        Assert.Equal(
            $"""<turbo-stream action="replace" target="list_rooms_open_{hq.Id}"><template><li>shared {hq.Id}</li></template></turbo-stream>""",
            turboStream frame
        )

        app.Broadcasts.RoomRemove hq
        let! frame = jz.NextText()
        Assert.Equal($"""<turbo-stream action="remove" target="list_rooms_open_{hq.Id}"></turbo-stream>""", turboStream frame)

        // Closed rooms go to each member's own stream: jz is in designers, not the watercooler.
        let! designers = app.Room "designers"
        let! watercooler = app.Room "watercooler"
        app.ReadEf(fun db ->
            app.Broadcasts.ClosedRoomCreate(db, watercooler, fakePartials)
            app.Broadcasts.ClosedRoomCreate(db, designers, fakePartials)
            app.Broadcasts.ClosedRoomUpdate(db, designers, fakePartials))
        let! frame = jz.NextText()
        Assert.Equal(
            $"""<turbo-stream action="prepend" target="shared_rooms"><template><li>shared {designers.Id}</li></template></turbo-stream>""",
            turboStream frame
        )
        let! frame = jz.NextText()
        Assert.Equal(
            $"""<turbo-stream action="replace" target="list_rooms_closed_{designers.Id}"><template><li>shared {designers.Id}</li></template></turbo-stream>""",
            turboStream frame
        )
        do! jz.AssertSilent()
    }

[<Fact>]
let ``direct room and involvement broadcasts`` () =
    task {
        use! app = start ()
        use! kevin = app.Connect "kevin"
        let ownRooms = GlobalId.toParam (Gid.userGid (id "kevin"))
        let! _ = turbo app kevin [ ownRooms; "rooms" ]

        let! direct = app.Room "bender_and_kevin"
        app.ReadEf(fun db -> app.Broadcasts.DirectRoomCreate(db, direct, fakePartials))
        let! membership = app.Membership("bender_and_kevin", "kevin")
        let membership = membership.Value
        let! frame = kevin.NextText()
        Assert.Equal(
            $"""<turbo-stream action="prepend" target="direct_rooms"><template><li>direct {membership.Id}</li></template></turbo-stream>""",
            turboStream frame
        )
        do! kevin.AssertSilent()

        let! designers = app.Room "designers"
        let! membership = app.Membership("designers", "kevin")
        let membership = membership.Value

        // Direct rooms never change the list.
        Assert.True((app.Broadcasts.InvolvementChange(direct, membership, Some Involvement.Invisible, fakePartials)).IsOk)
        do! kevin.AssertSilent()

        let membership = { membership with Involvement = Nullable Involvement.Invisible }
        Assert.True((app.Broadcasts.InvolvementChange(designers, membership, Some Involvement.Mentions, fakePartials)).IsOk)
        let! frame = kevin.NextText()
        Assert.Equal($"""<turbo-stream action="remove" target="list_rooms_closed_{designers.Id}"></turbo-stream>""", turboStream frame)

        let membership = { membership with Involvement = Nullable Involvement.Everything }
        Assert.True((app.Broadcasts.InvolvementChange(designers, membership, Some Involvement.Invisible, fakePartials)).IsOk)
        let! frame = kevin.NextText()
        Assert.Equal(
            $"""<turbo-stream action="prepend" target="shared_rooms"><template><li>shared {designers.Id}</li></template></turbo-stream>""",
            turboStream frame
        )

        Assert.True((app.Broadcasts.InvolvementChange(designers, membership, Some Involvement.Mentions, fakePartials)).IsOk)
        do! kevin.AssertSilent()
        Assert.True((app.Broadcasts.InvolvementChange(designers, membership, None, fakePartials)).IsError)
    }

[<Fact>]
let ``broadcast frames decode to active support json`` () =
    task {
        use! app = start ()
        use! kevin = app.Connect "kevin"
        let! channel = roomMessages app kevin "designers"
        let! designers = app.Room "designers"
        let! message = app.Message "first"
        app.Broadcasts.MessageReplace(designers, message, fakePartials)
        let! frame = kevin.NextText()
        // Rails escapes `<`, `>`, `&` as \u003c...; a frame that writes them raw decodes to the same JSON (C15).
        assertSameFrame
            (delivery
                channel
                (htmlJson
                    $"""<turbo-stream maintain_scroll="true" action="replace" target="presentation_message_0001"><template><div>presentation {message.Id} & more</div></template></turbo-stream>"""))
            frame
    }
