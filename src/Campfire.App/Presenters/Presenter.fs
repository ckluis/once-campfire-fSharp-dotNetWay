// Port of rust/crates/campfire/src/controllers/presenters.rs, all but what `Common.fs` has (the avatar
// paths, `user_summary`, `epoch_string`, `storage_error`).
//
// Maps database rows into the view models `Campfire.Views` renders: what the Rails views read off the
// records (`message.creator`, `room_display_name`, `message_presentation`, the Jbuilder partials)
// computed up front.
namespace Campfire.App.Presenters

open System
open System.Collections.Generic
open System.Text.RegularExpressions
open System.Threading.Tasks
open Campfire.App
open Campfire.Db
open Campfire.RailsCompat
open Campfire.RichText
open Campfire.Routes
open Campfire.Storage
open Campfire.Views
open Campfire.Views.Messages
open Campfire.Views.MessagesJson
open Campfire.Views.Rooms

module PresenterSupport =
    /// `Message::THUMBNAIL_MAX_WIDTH` / `THUMBNAIL_MAX_HEIGHT`.
    [<Literal>]
    let ThumbnailMaxWidth = 1200L

    [<Literal>]
    let ThumbnailMaxHeight = 800L

    /// `String#all_emoji?` (reference/lib/rails_ext/string.rb):
    /// `\A(\p{Emoji_Presentation}|\p{Extended_Pictographic}|️)+\z`.
    let allEmoji (text: string) : bool =
        let ranges = EmojiRanges.ranges
        let isEmoji (codePoint: int) =
            let mutable lo = 0
            let mutable hi = ranges.Length / 2 - 1
            let mutable found = false
            while not found && lo <= hi do
                let mid = (lo + hi) / 2
                if ranges[mid * 2 + 1] < codePoint then lo <- mid + 1
                elif ranges[mid * 2] > codePoint then hi <- mid - 1
                else found <- true
            found
        text.Length > 0 && (text.EnumerateRunes() |> Seq.forall (fun rune -> isEmoji rune.Value))

    let roomKind (roomType: RoomType) : RoomKind =
        match roomType with
        | RoomType.Open -> RoomKind.Open
        | RoomType.Closed -> RoomKind.Closed
        | _ -> RoomKind.Direct

    let userView (secrets: Secrets) (user: User) : UserView =
        { Id = user.Id
          Name = user.Name
          Title = User.title user
          AvatarUrl = Common.avatarPath secrets user }

    /// `users/_user.json.jbuilder`.
    let userJson (secrets: Secrets) (baseUrl: string) (user: User) : UserJson =
        { Id = user.Id
          Name = user.Name
          Role = Role.name user.Role
          AvatarUrl = baseUrl + Common.avatarPath secrets user }

    /// `attachment.metadata[:width]`: an Integer for images, a Float for videos.
    let dimension (blob: Campfire.Storage.Blob) (name: string) : MessagesSupport.RubyNumber option =
        match blob.Metadata.TryGet name with
        | Some(Value.Int value) -> Some(MessagesSupport.RubyNumber.Int value)
        | Some(Value.Float value) -> Some(MessagesSupport.RubyNumber.Float value)
        | _ -> None

open PresenterSupport

/// Everything a page of messages needs, with the rows it looks up along the way remembered
/// (Rails preloads them with `with_creator`, `with_boosts` and friends). One per read: it holds the
/// request's context (or a leased one), so it can't outlive it.
[<Sealed; NoComparison; NoEquality>]
type Presenter(db: Ef.CampfireDb, app: AppState, requestHost: string option) =
    let secrets = app.Secrets
    let storage = app.Storage
    let richText = app.Env.RichText
    let now = app.Time.GetUtcNow()
    let users = Dictionary<int64, User>()
    let roomNames = Dictionary<int64, Room * string>()

    member _.Db = db
    member _.Secrets = secrets

    member _.Resolver: DbResolver = DbResolver(db, secrets, now)

    member _.User(id: int64) : User =
        match users.TryGetValue id with
        | true, user -> user
        | _ ->
            let user = User.find db id
            users[id] <- user
            user

    member this.UserView(id: int64) : UserView = userView secrets (this.User id)

    /// `room_display_name(room, for_user:)`.
    member _.RoomDisplayName(room: Room, forUser: User option) : string =
        let names =
            if Room.isDirect room then
                Room.users db room
                |> List.filter (fun user ->
                    match forUser with
                    | Some forUser -> forUser.Id <> user.Id
                    | None -> true)
                |> List.map (fun user -> user.Name)
            else
                []
        Campfire.Views.Rooms.roomDisplayName (Option.ofObj room.Name) (Room.isDirect room) names (forUser |> Option.map (fun user -> user.Name))

    member this.RoomView(room: Room, forUser: User) : RoomView =
        { Id = room.Id
          Kind = roomKind room.Type
          Name = Option.ofObj room.Name
          DisplayName = this.RoomDisplayName(room, Some forUser) }

    /// `message.room` with `room_display_name(message.room, for_user: nil)`.
    member private this.RoomAndName(roomId: int64) : Room * string =
        match roomNames.TryGetValue roomId with
        | true, entry -> entry
        | _ ->
            let room = Room.find db roomId
            let name = this.RoomDisplayName(room, None)
            roomNames[roomId] <- (room, name)
            room, name

    member _.PlainTextBody(message: Message) : string = Message.plainTextBody db richText message

    /// `render @messages`: each message's fragment, fetched up front (`cache [ message, "presentation-v3" ]` wraps the whole
    /// partial, so Rails evaluates none of it on a hit: the view is built on a miss only). One after the other, since the
    /// context is used by a miss.
    member this.Messages(messages: Message list) : Task<MessageItem list> =
        task {
            let items = ResizeArray<MessageItem>()
            for message in messages do
                let! item = this.MessageItem message
                items.Add item
            return List.ofSeq items
        }

    /// `render message`, as `Messages` does it.
    member this.MessageItem(message: Message) : Task<MessageItem> =
        task {
            let! html = this.MessageFragment message
            return
                { ClientMessageId = message.ClientMessageId
                  RoomId = message.RoomId
                  Html = html }
        }

    /// The `messages/_message` fragment of this message version.
    member this.MessageFragment(message: Message) : ValueTask<CachedFragment> =
        MessagesCached.fetch app.Fragments (Page.fragmentContext app) message.Id (DateTimeOffset message.UpdatedAt) (fun () -> this.Message message)

    /// A message as `messages/_message` shows it.
    member this.Message(message: Message) : MessageView =
        let _, roomName = this.RoomAndName message.RoomId
        try
            this.RenderableMessage(message, roomName)
        with DbException(RecordNotFound _) ->
            // `message_tag` rescues whatever its block raises, e.g. `avatar_tag message.creator`
            // for a creator that's gone (nil), and renders `messages/_unrenderable` instead.
            { Id = message.Id
              ClientMessageId = message.ClientMessageId
              RoomId = message.RoomId
              RoomName = roomName
              Creator =
                { Id = message.CreatorId
                  Name = ""
                  Title = ""
                  AvatarUrl = "" }
              CreatedAt = DateTimeOffset(message.CreatedAt)
              UpdatedAt = DateTimeOffset(message.UpdatedAt)
              AllEmoji = false
              Content = MessageContent.Unrenderable
              Boosts = [] }

    member private this.RenderableMessage(message: Message, roomName: string) : MessageView =
        let plainText = this.PlainTextBody message
        { Id = message.Id
          ClientMessageId = message.ClientMessageId
          RoomId = message.RoomId
          RoomName = roomName
          Creator = this.UserView message.CreatorId
          CreatedAt = DateTimeOffset(message.CreatedAt)
          UpdatedAt = DateTimeOffset(message.UpdatedAt)
          AllEmoji = allEmoji plainText
          Content = this.Content(message, plainText)
          Boosts = this.Boosts message }

    /// `message.boosts.ordered`.
    member this.Boosts(message: Message) : BoostView list =
        Boost.forMessageOrdered db message.Id |> List.map this.Boost

    member this.Boost(boost: Boost) : BoostView =
        { Id = boost.Id
          UpdatedAt = DateTimeOffset(boost.UpdatedAt)
          MessageId = boost.MessageId
          Content = boost.Content
          AllEmoji = allEmoji boost.Content
          Booster = this.UserView boost.BoosterId }

    /// `message.content_type`, with what `message_presentation` shows for it.
    member private this.Content(message: Message, plainText: string) : MessageContent =
        let body = defaultArg (Message.bodyHtml db message) ""
        let ctx = this.Resolver.RenderContext requestHost
        // `message_tag` evaluates `message.plain_text_body` first; where that raises, it rescues
        // and renders `messages/_unrenderable`, unless logging the exception raises again (a
        // message that isn't UTF-8): then the page fails (verified against the reference).
        match ActionText.toPlainText body ctx with
        | Error(RenderError.Unrenderable error) ->
            Err.fail (DbError.other $"message_tag's rescue raised logging {error}")
        | Error _ -> MessageContent.Unrenderable
        | Ok _ ->
            match this.Attachment message with
            | Some attachment -> MessageContent.Attachment attachment
            | None ->
                match Message.soundIn plainText with
                | Some sound ->
                    MessageContent.Sound
                        { Url = Campfire.App.Web.StaticAssets.path (Sound.assetPath sound)
                          Image =
                            sound.Image
                            |> Option.map (fun image ->
                                { Src = Campfire.App.Web.StaticAssets.path (SoundImage.assetPath image)
                                  Width = uint32 image.Width
                                  Height = uint32 image.Height })
                          Text = sound.Text }
                | None ->
                    match ActionText.presentMessage body ctx with
                    | Presentation.Html html -> MessageContent.Text(Campfire.Views.Markup.Html.Trusted.ofSanitized html)
                    | Presentation.Unrenderable -> MessageContent.Unrenderable

    /// `message.attachment` as `Messages::AttachmentPresentation` needs it.
    member private this.Attachment(message: Message) : AttachmentView option =
        match Attachments.attachedBlob db "Message" message.Id "attachment" with
        | None -> None
        | Some blob ->
            let verifier = storage.Verifier
            let preview =
                if Campfire.Storage.Blob.isPreviewable blob || Campfire.Storage.Blob.isVariable blob then
                    if Campfire.Storage.Blob.isVideo blob then
                        // `attachment.preview(format: :webp, resize_to_limit: [...])`
                        let poster =
                            Variation.create
                                [ "format", Marshal.Value.Symbol "webp"
                                  "resize_to_limit", Marshal.Value.Array [ Marshal.Value.Int ThumbnailMaxWidth; Marshal.Value.Int ThumbnailMaxHeight ] ]
                        AttachmentPreview.Video(Paths.representationRedirectPath verifier blob poster)
                    else
                        AttachmentPreview.Image(this.ThumbPath blob)
                else
                    AttachmentPreview.File
            let (Filename filename) = blob.Filename
            Some
                { Filename = filename
                  BlobPath = Paths.blobRedirectPath verifier blob None
                  DownloadPath = Paths.blobRedirectPath verifier blob (Some "attachment")
                  Preview = preview
                  Width = dimension blob "width"
                  Height = dimension blob "height" }

    /// `polymorphic_url(attachment.representation(:thumb), only_path: true)`.
    member private _.ThumbPath(blob: Campfire.Storage.Blob) : string =
        let thumb = Variation.resizeToLimit ThumbnailMaxWidth ThumbnailMaxHeight None
        let variation =
            if Campfire.Storage.Blob.isPreviewable blob then thumb else Common.raiseStorage (Storage.variationFor blob thumb)
        Paths.representationRedirectPath storage.Verifier blob variation

    /// `message.body.to_s`: the stored rich text rendered inside its layout.
    member this.BodyHtml(message: Message) : string =
        match Message.bodyHtml db message with
        | None -> ""
        | Some body ->
            let ctx = this.Resolver.RenderContext requestHost
            match Content.load body ctx |> Result.bind (fun content -> Content.toRenderedHtmlWithLayout content ctx) with
            | Ok html -> html
            | Error _ -> ""

    /// `editable_body(message)` as the editor's `value`.
    member this.EditableBody(message: Message) : string =
        let body = defaultArg (Message.bodyHtml db message) ""
        let ctx = this.Resolver.RenderContext requestHost
        // An `Error` is where the edit page raises in Rails (a missing attachment, say).
        match ActionText.editableValue body ctx with
        | Ok value -> defaultArg value ""
        | Error error -> Err.fail (DbError.other $"editable_body raised: {error.Message}")

    /// `messages/_message.json.jbuilder`: rendered each time (the bot API is no workload, and a Jbuilder cache is keyed by version).
    member this.MessageJson(message: Message, baseUrl: string) : MessageJson =
        { Id = message.Id
          CreatedAt = MessagesSupport.jsonTime (DateTimeOffset(message.CreatedAt))
          Body =
            { PlainText = this.PlainTextBody message
              Html = this.BodyHtml message }
          Creator = userJson secrets baseUrl (this.User message.CreatorId)
          Room = { Id = message.RoomId }
          Url = baseUrl + Routes.roomMessage message.RoomId message.Id }

    /// `messages/boosts/_boost.json.jbuilder`.
    member this.BoostJson(boost: Boost, message: Message, baseUrl: string) : BoostJson =
        { Id = boost.Id
          Content = boost.Content
          CreatedAt = MessagesSupport.jsonTime (DateTimeOffset(boost.CreatedAt))
          Booster = userJson secrets baseUrl (this.User boost.BoosterId)
          Message =
            { Id = boost.MessageId
              Url = baseUrl + Routes.roomMessage message.RoomId message.Id } }

    /// `users/sidebars/rooms/_shared` locals.
    member _.SidebarRoom(room: Room) : Campfire.Views.Users.SidebarRoom =
        { Id = room.Id
          ParamKey = RoomKind.paramKey (roomKind room.Type)
          Name = defaultArg (Option.ofObj room.Name) ""
          Unread = false }

    /// `users/sidebars/rooms/_direct` locals for `membership`.
    member this.SidebarDirect(membership: Membership) : Campfire.Views.Users.SidebarDirect =
        let room = Room.find db membership.RoomId
        let members =
            match Room.users db room |> List.filter (fun user -> user.Id <> membership.UserId) with
            | [] -> [ this.User membership.UserId ]
            | others -> others
        { RoomId = room.Id
          Unread = Membership.unread membership
          UpdatedAtEpoch = Common.epochStringOf room.UpdatedAt
          Members = members |> List.map (Common.userSummary secrets)
          MembershipId = membership.Id
          MembershipUpdatedAt = DateTimeOffset(membership.UpdatedAt) }

    member _.UserSummary(user: User) : Campfire.Views.Users.UserSummary = Common.userSummary secrets user
