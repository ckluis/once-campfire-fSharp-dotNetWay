// Design: plans/design-data-ef.md E2-E4. One context over the unchanged Rails schema; no navigations, no migrations.
namespace Campfire.Db.Ef

open Microsoft.EntityFrameworkCore
open Microsoft.EntityFrameworkCore.Storage.ValueConversion

type CampfireDb(options: DbContextOptions<CampfireDb>) =
    inherit DbContext(options)

    member this.Users = this.Set<User>()
    member this.Sessions = this.Set<Session>()
    member this.Rooms = this.Set<Room>()
    member this.Memberships = this.Set<Membership>()
    member this.Messages = this.Set<Message>()
    member this.Boosts = this.Set<Boost>()
    member this.RichTexts = this.Set<RichTextRecord>()
    member this.Accounts = this.Set<Account>()
    member this.Searches = this.Set<Search>()
    member this.Bans = this.Set<Ban>()
    member this.Webhooks = this.Set<Webhook>()
    member this.PushSubscriptions = this.Set<PushSubscription>()
    member this.Blobs = this.Set<Blob>()
    member this.Attachments = this.Set<Attachment>()
    member this.Variants = this.Set<VariantRecord>()

    /// Active Record's text for every `DateTime` and `Nullable<DateTime>` (E4).
    override _.ConfigureConventions(builder: ModelConfigurationBuilder) =
        builder.Properties<System.DateTime>().HaveConversion<Campfire.Db.RailsTimeConverter>() |> ignore

    override _.OnModelCreating(builder: ModelBuilder) =
        builder.Entity<User>().ToTable("users") |> ignore
        builder.Entity<Session>().ToTable("sessions") |> ignore
        builder.Entity<Message>().ToTable("messages") |> ignore
        builder.Entity<Boost>().ToTable("boosts") |> ignore
        builder.Entity<RichTextRecord>().ToTable("action_text_rich_texts") |> ignore
        builder.Entity<Account>().ToTable("accounts") |> ignore
        builder.Entity<Search>().ToTable("searches") |> ignore
        builder.Entity<Ban>().ToTable("bans") |> ignore
        builder.Entity<Webhook>().ToTable("webhooks") |> ignore
        builder.Entity<Blob>().ToTable("active_storage_blobs") |> ignore
        builder.Entity<Attachment>().ToTable("active_storage_attachments") |> ignore
        builder.Entity<VariantRecord>().ToTable("active_storage_variant_records") |> ignore
        builder.Entity<PushSubscription>(fun e ->
            e.ToTable("push_subscriptions") |> ignore
            // Spelled out so the convention's treatment of the digits cannot rename the column.
            e.Property(fun p -> p.P256dhKey).HasColumnName("p256dh_key") |> ignore)
        |> ignore
        // One Room entity, not TPH: the STI strings stay byte-identical and EF owns no discriminator (E3).
        builder.Entity<Room>(fun e ->
            e.ToTable("rooms") |> ignore
            e.Property(fun r -> r.Type).HasConversion(ValueConverter<RoomType, string>((fun t -> Names.roomTypeToDb t), (fun s -> Names.roomTypeOfDb s)))
            |> ignore)
        |> ignore
        builder.Entity<Membership>(fun e ->
            e.ToTable("memberships") |> ignore
            e.Property(fun m -> m.Involvement).HasConversion(ValueConverter<Involvement, string>((fun i -> Names.involvementToDb i), (fun s -> Names.involvementOfDb s)))
            |> ignore)
        |> ignore
