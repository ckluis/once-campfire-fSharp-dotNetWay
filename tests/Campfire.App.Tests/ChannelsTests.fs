// Port of rust/crates/campfire/src/channels/tests/channels_test.rs, and the unit tests of channels/room.rs and
// channels/room_messages.rs: ports of reference/test/channels/**, plus the channels the reference doesn't test
// directly.
module Campfire.App.Tests.ChannelsTests

open System
open System.Threading.Tasks
open Xunit
open Campfire.App.Channels
open Campfire.Cable
open Campfire.Db
open Campfire.RailsCompat
open Campfire.App.Tests.ChannelsSupport
open Campfire.Tests.JsonFrames

let private obj' (entries: (string * Value) list) : Value = Value.Object entries

let private channelId (name: string) : string = identifier (obj' [ "channel", Value.String name ])

// ApplicationCable::Connection

[<Fact>]
let ``connects with a valid session cookie`` () =
    task {
        use! app = start ()
        use! client = app.Connect "david"
        do! client.Confirm(channelId "HeartbeatChannel")
    }

[<Fact>]
let ``rejects a connection without or with a bad session cookie`` () =
    task {
        use! app = start ()
        let unauthorized = """{"type":"disconnect","reason":"unauthorized","reconnect":false}"""
        for cookie in [ None; Some(app.CookieWithToken "-1"); Some "session_token=forged--0000" ] do
            use! client = app.ConnectWithCookie cookie
            let! frames = client.UntilClosed()
            Assert.Equal<string list>([ unauthorized ], frames)
    }

[<Fact>]
let ``rejects a session cookie once it expires`` () =
    task {
        use! app = start ()
        let userId = id "david"
        let! session = app.WriteEf(fun tx -> Session.start tx userId (Some "test") (Some "8.8.8.8"))
        let session =
            match session with
            | Ok session -> session
            | Error error -> failwith (DbError.display error)
        let expiresAt = app.NowOffset() + TimeSpan.FromSeconds 60.0
        let cookie = app.CookieWithTokenExpiring(session.Token, Some expiresAt)

        use! client = app.ConnectWithCookie(Some cookie)
        let! welcome = client.NextText()
        Assert.Equal("""{"type":"welcome"}""", welcome)

        app.Time.Advance(TimeSpan.FromSeconds 61.0)
        use! client = app.ConnectWithCookie(Some cookie)
        let! frames = client.UntilClosed()
        Assert.Equal<string list>([ """{"type":"disconnect","reason":"unauthorized","reconnect":false}""" ], frames)
    }

// HeartbeatChannel and ApplicationCable::Channel

[<Fact>]
let ``heartbeat and base channels confirm`` () =
    task {
        use! app = start ()
        use! client = app.Connect "jz"
        do! client.Confirm(channelId "HeartbeatChannel")
        do! client.Confirm(channelId "ApplicationCable::Channel")
        do! client.AssertSilent()
    }

// RoomChannel

[<Fact>]
let ``room channel streams for member rooms only`` () =
    task {
        use! app = start ()
        use! client = app.Connect "kevin"
        let! designers = app.Room "designers"

        let member' = roomIdentifier "RoomChannel" designers.Id
        do! client.Confirm member'
        do! client.Reject(roomIdentifier "RoomChannel" (id "watercooler"))
        do! client.Reject(roomIdentifier "RoomChannel" -1L)
        do! client.Reject(channelId "RoomChannel")
        // Params are cast like Active Record casts an id.
        let byString =
            identifier (obj' [ "channel", Value.String "RoomChannel"; "room_id", Value.String(string designers.Id) ])
        do! client.Confirm byString

        // Both subscriptions stream the room; a connection polls its subscriptions in no set order.
        let stream = $"room:{GlobalId.toParam (Gid.roomGid designers)}"
        app.Server.Broadcast(stream, Payload.ofValue {| hello = 1 |}) |> ignore
        let! first = client.NextText()
        let! second = client.NextText()
        assertSameFramesUnordered [ delivery member' """{"hello":1}"""; delivery byString """{"hello":1}""" ] [ first; second ]
        do! client.AssertSilent()
    }

// PresenceChannel (reference/test/channels/presence_channel_test.rb)

[<Fact>]
let ``presence subscribes and marks the membership connected`` () =
    task {
        use! app = start ()
        use! client = app.Connect "david"
        let reads = channelId "ReadRoomsChannel"
        do! client.Confirm reads

        let! membership = app.Membership("designers", "david")
        let membership = membership.Value
        Assert.False(Membership.isConnected membership (app.Utc()))
        // A member's unread room gets cleared by `present`.
        do! app.Execute("UPDATE memberships SET unread_at = '2024-01-01 00:00:00' WHERE id = ?", box membership.Id)

        let presence = roomIdentifier "PresenceChannel" (id "designers")
        do! client.Confirm presence
        let! read = client.NextText()
        Assert.Equal(delivery reads $"""{{"room_id":{id "designers"}}}""", read)

        let! membership = app.Membership("designers", "david")
        let membership = membership.Value
        Assert.True(Membership.isConnected membership (app.Utc()))
        Assert.Equal(1L, membership.Connections)
        Assert.False(membership.UnreadAt.HasValue)

        do! client.Unsubscribe presence
        do!
            eventually (fun () ->
                task {
                    let! membership = app.Membership("designers", "david")
                    return not (Membership.isConnected membership.Value (app.Utc()))
                })
        let! membership = app.Membership("designers", "david")
        let membership = membership.Value
        Assert.Equal((0L, false), (membership.Connections, membership.ConnectedAt.HasValue))
    }

[<Fact>]
let ``presence counts connections and refreshes`` () =
    task {
        use! app = start ()
        let presence = roomIdentifier "PresenceChannel" (id "designers")
        use! first = app.Connect "david"
        use! second = app.Connect "david"
        do! first.Confirm presence
        do! second.Confirm presence
        let! membership = app.Membership("designers", "david")
        Assert.Equal(2L, membership.Value.Connections)

        // `refresh` reconnects a membership that timed out.
        app.Time.Advance(TimeSpan.FromSeconds 61.0)
        let! membership = app.Membership("designers", "david")
        Assert.False(Membership.isConnected membership.Value (app.Utc()))
        do! first.Perform(presence, obj' [ "action", Value.String "refresh" ])
        do!
            eventually (fun () ->
                task {
                    let! membership = app.Membership("designers", "david")
                    return Membership.isConnected membership.Value (app.Utc())
                })
        let! membership = app.Membership("designers", "david")
        Assert.Equal(1L, membership.Value.Connections)

        do! second.Unsubscribe presence
        do!
            eventually (fun () ->
                task {
                    let! membership = app.Membership("designers", "david")
                    return not membership.Value.ConnectedAt.HasValue
                })
    }

[<Fact>]
let ``presence rejects rooms the user is not in without touching memberships`` () =
    task {
        use! app = start ()
        use! client = app.Connect "david"
        let! before = app.Membership("bender_and_kevin", "kevin")

        do! client.Reject(roomIdentifier "PresenceChannel" (id "bender_and_kevin"))
        do! client.Reject(roomIdentifier "PresenceChannel" -1L)
        do! client.AssertSilent()
        let! after = app.Membership("bender_and_kevin", "kevin")
        Assert.Equal(before, after)
    }

// ReadRoomsChannel and UnreadRoomsChannel (reference/test/channels/unread_rooms_channel_test.rb)

[<Fact>]
let ``unread rooms streams only the subscribers own stream`` () =
    task {
        use! app = start ()
        let! direct = app.Room "bender_and_kevin"
        let unreads = channelId "UnreadRoomsChannel"

        use! outsider = app.Connect "jz"
        use! member' = app.Connect "kevin"
        do! outsider.Confirm unreads
        do! member'.Confirm unreads

        let! message = app.Message "first"
        app.ReadEf(fun db -> app.Broadcasts.MessageCreate(db, direct, message, fakePartials))

        let! frame = member'.NextText()
        assertSameFrame (delivery unreads $"""{{"roomId":{direct.Id}}}""") frame
        do! member'.AssertSilent()
        do! outsider.AssertSilent()
    }

[<Fact>]
let ``read rooms streams the users reads`` () =
    task {
        use! app = start ()
        let reads = channelId "ReadRoomsChannel"
        use! client = app.Connect "jason"
        do! client.Confirm reads
        BroadcastNames.readRoom app.Server (id "jason") 7L |> ignore
        BroadcastNames.readRoom app.Server (id "david") 8L |> ignore
        let! frame = client.NextText()
        assertSameFrame (delivery reads """{"room_id":7}""") frame
        do! client.AssertSilent()
    }

// TypingNotificationsChannel

let private typingStreamName (room: Room) : string =
    Naming.broadcastingFor "TypingNotificationsChannel" [ GlobalId.toParam (Gid.roomGid room) ]

[<Fact>]
let ``typing notifications broadcast start and stop to the room`` () =
    task {
        use! app = start ()
        let typing = roomIdentifier "TypingNotificationsChannel" (id "designers")
        use! typist = app.Connect "jz"
        use! reader = app.Connect "kevin"
        do! typist.Confirm typing
        do! reader.Confirm typing

        do! typist.Perform(typing, obj' [ "action", Value.String "start" ])
        let start' = $"""{{"action":"start","user":{{"id":{id "jz"},"name":"JZ"}}}}"""
        let! frame = reader.NextText()
        assertSameFrame (delivery typing start') frame
        let! frame = typist.NextText()
        assertSameFrame (delivery typing start') frame

        do! typist.Perform(typing, obj' [ "action", Value.String "stop" ])
        let stop = $"""{{"action":"stop","user":{{"id":{id "jz"},"name":"JZ"}}}}"""
        let! frame = reader.NextText()
        assertSameFrame (delivery typing stop) frame

        // Not an action: nothing is sent.
        do! typist.Perform(typing, obj' [ "action", Value.String "dance" ])
        do! reader.AssertSilent()

        let! designers = app.Room "designers"
        Assert.Equal($"typing_notifications:{GlobalId.toParam (Gid.roomGid designers)}", typingStreamName designers)
    }

/// `safe_constantize` resolves "::TypingNotificationsChannel" to the class, whose broadcastings are named after
/// the class alone, so both spellings share the room's typing stream.
[<Fact>]
let ``typing notifications reach the room however the channel is spelled`` () =
    task {
        use! app = start ()
        let prefixed = roomIdentifier "::TypingNotificationsChannel" (id "designers")
        let plain = roomIdentifier "TypingNotificationsChannel" (id "designers")
        use! typist = app.Connect "jz"
        use! reader = app.Connect "kevin"
        do! typist.Confirm prefixed
        do! reader.Confirm plain

        do! typist.Perform(prefixed, obj' [ "action", Value.String "start" ])
        let start' = $"""{{"action":"start","user":{{"id":{id "jz"},"name":"JZ"}}}}"""
        let! frame = reader.NextText()
        assertSameFrame (delivery plain start') frame
        let! frame = typist.NextText()
        assertSameFrame (delivery prefixed start') frame

        do! reader.Perform(plain, obj' [ "action", Value.String "stop" ])
        let stop = $"""{{"action":"stop","user":{{"id":{id "kevin"},"name":"Kevin"}}}}"""
        let! frame = typist.NextText()
        assertSameFrame (delivery prefixed stop) frame
        let! frame = reader.NextText()
        assertSameFrame (delivery plain stop) frame
    }

/// A subscription whose `subscribed` failed has no room: typing there is an error, not a crash that takes the
/// whole connection down.
[<Fact>]
let ``typing on a failed subscription leaves the connection up`` () =
    task {
        use! app = start ()
        let rename (from: string) (to': string) =
            task {
                do! app.Execute $"ALTER TABLE {from} RENAME TO {to'}"
            }
        use! client = app.Connect "jz"
        let typing = roomIdentifier "TypingNotificationsChannel" (id "designers")

        do! rename "rooms" "rooms_away"
        do! client.Subscribe typing
        do! client.Confirm(channelId "HeartbeatChannel")
        do! rename "rooms_away" "rooms"

        do! client.Perform(typing, obj' [ "action", Value.String "start" ])
        do! client.Confirm(channelId "ApplicationCable::Channel")
    }

[<Fact>]
let ``typing notifications reject non members`` () =
    task {
        use! app = start ()
        use! client = app.Connect "jz"
        do! client.Reject(roomIdentifier "TypingNotificationsChannel" (id "watercooler"))
    }

// RoomMessagesChannel (reference/test/channels/room_messages_channel_test.rb)

[<Fact>]
let ``a member may subscribe to a rooms message stream`` () =
    task {
        use! app = start ()
        let! designers = app.Room "designers"
        let signed = app.SignedStreamName [ GlobalId.toParam (Gid.roomGid designers); "messages" ]
        let channel = identifier (obj' [ "channel", Value.String "RoomMessagesChannel"; "signed_stream_name", Value.String signed ])

        use! kevin = app.Connect "kevin"
        do! kevin.Confirm channel

        let! message = app.Message "first"
        app.Broadcasts.MessageRemove(designers, message)
        let! frame = kevin.NextText()
        assertSameFrame (delivery channel (htmlJson """<turbo-stream action="remove" target="message_0001"></turbo-stream>""")) frame
    }

[<Fact>]
let ``room message streams are rejected for everyone else`` () =
    task {
        use! app = start ()
        let! designers = app.Room "designers"
        let! hq = app.Room "hq"
        let signed = app.SignedStreamName [ GlobalId.toParam (Gid.roomGid designers); "messages" ]
        let subscribe (signed: Value) =
            identifier (obj' [ "channel", Value.String "RoomMessagesChannel"; "signed_stream_name", signed ])

        // A user who was never a member.
        use! bender = app.Connect "bender"
        do! bender.Reject(subscribe (Value.String signed))
        // Another room the user isn't in.
        do! bender.Reject(subscribe (Value.String(app.SignedStreamName [ GlobalId.toParam (Gid.roomGid hq); "messages" ])))

        use! kevin = app.Connect "kevin"
        // An unsigned stream name.
        do! kevin.Reject(subscribe (Value.String $"{GlobalId.toParam (Gid.roomGid designers)}:messages"))
        // A missing stream name.
        do! kevin.Reject(channelId "RoomMessagesChannel")
        // A signed name that isn't a room's message stream.
        do! kevin.Reject(subscribe (Value.String(app.SignedStreamName [ "rooms" ])))
        // A room whose type changed since the name was signed (`Rooms::Open.find` of a closed room).
        let stale = { designers with Type = RoomType.Open }
        do! kevin.Reject(subscribe (Value.String(app.SignedStreamName [ GlobalId.toParam (Gid.roomGid stale); "messages" ])))
        // A user GID in place of a room.
        do! kevin.Reject(subscribe (Value.String(app.SignedStreamName [ GlobalId.toParam (Gid.userGid (id "kevin")); "messages" ])))
    }

[<Fact>]
let ``a revoked member may not resubscribe with a harvested stream name`` () =
    task {
        use! app = start ()
        let! designers = app.Room "designers"
        let signed = app.SignedStreamName [ GlobalId.toParam (Gid.roomGid designers); "messages" ]
        let channel = identifier (obj' [ "channel", Value.String "RoomMessagesChannel"; "signed_stream_name", Value.String signed ])

        use! kevin = app.Connect "kevin"
        do! kevin.Confirm channel

        let! revoked = app.WriteEf(fun tx -> Room.revokeFrom tx designers [ id "kevin" ])
        Assert.True(revoked.IsOk)
        let! _ = kevin.UntilClosed()

        use! kevin = app.Connect "kevin"
        do! kevin.Reject channel
    }

// Turbo::StreamsChannel with RoomStreamsAreAuthorized

[<Fact>]
let ``the stock turbo channel refuses room message streams but serves the room list`` () =
    task {
        use! app = start ()
        let! designers = app.Room "designers"
        let turbo (signed: string) =
            identifier (obj' [ "channel", Value.String "Turbo::StreamsChannel"; "signed_stream_name", Value.String signed ])
        use! kevin = app.Connect "kevin"

        do! kevin.Reject(turbo (app.SignedStreamName [ GlobalId.toParam (Gid.roomGid designers); "messages" ]))
        do! kevin.Reject(turbo "forged--0000")
        do! kevin.Reject(channelId "Turbo::StreamsChannel")

        let rooms = turbo (app.SignedStreamName [ "rooms" ])
        do! kevin.Confirm rooms
        app.Broadcasts.RoomRemove designers
        let! frame = kevin.NextText()
        assertSameFrame
            (delivery
                rooms
                (htmlJson $"""<turbo-stream action="remove" target="list_rooms_closed_{designers.Id}"></turbo-stream>"""))
            frame
    }

// Public `subscribed` is an action in Ruby: performing it streams again.

[<Fact>]
let ``performing subscribed streams twice like ruby`` () =
    task {
        use! app = start ()
        let unreads = channelId "UnreadRoomsChannel"
        use! client = app.Connect "kevin"
        do! client.Confirm unreads
        do! client.Perform(unreads, obj' [ "action", Value.String "subscribed" ])
        do! client.AssertSilent()

        let! room = app.Room "bender_and_kevin"
        app.ReadEf(fun db -> app.Broadcasts.UnreadRoom(db, room))
        let expected = delivery unreads $"""{{"roomId":{id "bender_and_kevin"}}}"""
        let! frame = client.NextText()
        assertSameFrame expected frame
        let! frame = client.NextText()
        assertSameFrame expected frame
    }

// channels/room.rs

[<Fact>]
let ``casts ids like active record`` () =
    let cast (json: string) =
        use document = System.Text.Json.JsonDocument.Parse json
        RoomChannel.castId document.RootElement
    let text (s: string) = System.Text.Json.JsonSerializer.Serialize s
    Assert.Equal(Some 5L, cast "5")
    Assert.Equal(Some 5L, cast "5.9")
    Assert.Equal(Some 5L, cast (text "5"))
    Assert.Equal(Some 12L, cast (text " 12abc"))
    Assert.Equal(Some 1000L, cast (text "1_000"))
    Assert.Equal(Some -1L, cast (text "-1"))
    Assert.Equal(None, cast (text "abc"))
    Assert.Equal(None, cast (text ""))
    Assert.Equal(Some 1L, cast "true")
    Assert.Equal(Some 0L, cast "false")
    Assert.Equal(None, cast "null")
    Assert.Equal(None, cast (text "99999999999999999999"))
    // A number past int64 is truncated as a float when it fits, and nothing otherwise.
    Assert.Equal(None, cast "1e30")
    Assert.Equal(Some 5L, cast "5.0")
    // Probed in the reference: Ruby's whitespace includes \v, and to_i reads a `0d` prefix.
    Assert.Equal(Some 5L, cast (text "\u000b5"))
    Assert.Equal(Some 12L, cast (text "0d12"))
    Assert.Equal(None, cast (text "\u00a05"))
    Assert.Equal(Some Int64.MinValue, cast (text "-9223372036854775808"))

// channels/room_messages.rs

[<Fact>]
let ``guards only message streams`` () =
    Assert.True(RoomMessages.guardedStream "Z2lkOi8vY2FtcGZpcmUvUm9vbXM6Ok9wZW4vMQ:messages")
    Assert.False(RoomMessages.guardedStream "rooms")
    Assert.False(RoomMessages.guardedStream "Z2lk:rooms")
    Assert.False(RoomMessages.guardedStream "Z2lk:messages:more")
    Assert.False(RoomMessages.guardedStream "")
    Assert.True(RoomMessages.guardedStream ":messages")
