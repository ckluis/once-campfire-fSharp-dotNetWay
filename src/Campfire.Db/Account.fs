// Port of rust/crates/db/src/models/account.rs on EF Core (design section 4, unit 1.3).
// `reference/app/models/account.rb` and `account/joinable.rb`
namespace Campfire.Db

open System.Linq
open Microsoft.EntityFrameworkCore
open Campfire.RailsCompat
open Campfire.Db.Ef

/// `has_json :settings, restrict_room_creation_to_administrators: false`
/// (`ActiveModel::SchematizedJson`): the stored hash with the schema defaults merged in.
type AccountSettings = { Data: (string * Value) list }

module AccountSettings =
    [<Literal>]
    let private RestrictRoomCreation = "restrict_room_creation_to_administrators"

    let private set (key: string) (value: Value) (settings: AccountSettings) : AccountSettings =
        if settings.Data |> List.exists (fun (k, _) -> k = key) then
            { Data = settings.Data |> List.map (fun (k, v) -> if k = key then k, value else k, v) }
        else
            { Data = settings.Data @ [ key, value ] }

    let internal fromColumn (raw: string option) : AccountSettings =
        let data =
            raw
            |> Option.bind (fun r -> Json.parse (System.Text.Encoding.UTF8.GetBytes r))
            |> Option.bind (fun v -> v.AsObject)
            |> Option.defaultValue []
        if data |> List.exists (fun (k, _) -> k = RestrictRoomCreation) then
            { Data = data }
        else
            { Data = data @ [ RestrictRoomCreation, Value.Bool false ] }

    let private isPresent (value: Value option) : bool =
        match value with
        | None
        | Some Value.Null
        | Some(Value.Bool false) -> false
        | Some(Value.String s) -> not (System.String.IsNullOrWhiteSpace s)
        | Some(Value.Array a) -> not (List.isEmpty a)
        | Some(Value.Object o) -> not (List.isEmpty o)
        | Some _ -> true

    /// `ActiveModel::Type::Boolean#cast` of a form value: blank is nil, the `FALSE_VALUES` are
    /// false, anything else is true.
    let castBoolean (value: string) : bool option =
        let falseValues = [ "0"; "f"; "F"; "false"; "FALSE"; "off"; "OFF" ]
        if value = "" then None else Some(not (List.contains value falseValues))

    /// `restrict_room_creation_to_administrators?`: `present?` of the stored value.
    let restrictRoomCreationToAdministrators (settings: AccountSettings) : bool =
        isPresent (settings.Data |> List.tryFind (fun (k, _) -> k = RestrictRoomCreation) |> Option.map snd)

    /// `restrict_room_creation_to_administrators = value`, cast as a boolean.
    let setRestrictRoomCreationToAdministrators (value: string) (settings: AccountSettings) : AccountSettings =
        let cast =
            match castBoolean value with
            | Some b -> Value.Bool b
            | None -> Value.Null
        set RestrictRoomCreation cast settings

    /// `assign_data_with_type_casting`: every key must be in the schema.
    let assign (values: (string * string) list) (settings: AccountSettings) : AccountSettings =
        values
        |> List.fold
            (fun settings (key, value) ->
                match key with
                | RestrictRoomCreation -> setRestrictRoomCreationToAdministrators value settings
                | other -> Err.fail (DbError.other $"undefined method '{other}=' for account settings"))
            settings

    let get (key: string) (settings: AccountSettings) : Value option =
        settings.Data |> List.tryFind (fun (k, _) -> k = key) |> Option.map snd

    let toJson (settings: AccountSettings) : string = Json.generate (Value.Object settings.Data)

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Account =
    let find (db: CampfireDb) (id: int64) : Account = db.Accounts.Find id |> Data.found "Account"

    /// `Account.first` (`Current.account`).
    let first (db: CampfireDb) : Account option =
        if FastReads.enabled then FastReads.accountFirst db
        else db.Accounts.FromSql($"SELECT * FROM accounts ORDER BY id LIMIT 1") |> Data.sqlFirst

    let count (db: CampfireDb) : int64 = db.Accounts.LongCount()

    let settings (account: Account) : AccountSettings = AccountSettings.fromColumn (Option.ofObj account.Settings)

    /// `SecureRandom.alphanumeric(12).scan(/.{4}/).join("-")`
    let generateJoinCode () : string =
        let code = Tokens.alphanumeric 12
        $"{code.Substring(0, 4)}-{code.Substring(4, 4)}-{code.Substring(8, 4)}"

    /// `Account.create!(name:)`: a fresh join code, and the settings defaults written out.
    let create (tx: Tx) (name: string) : Account =
        let now = tx.Now()
        Data.insert
            tx.Db
            { Id = 0L
              Name = name
              JoinCode = generateJoinCode ()
              CustomStyles = null
              Settings = AccountSettings.toJson (AccountSettings.fromColumn None)
              SingletonGuard = 0L
              CreatedAt = now
              UpdatedAt = now }

    /// `reset_join_code`
    let resetJoinCode (tx: Tx) (account: Account) : Account =
        Data.update tx.Db account { account with JoinCode = generateJoinCode () }

    /// `update!(name:, custom_styles:, settings:)`. Only changed attributes are written; the settings column is written when its
    /// (defaulted) hash changes.
    let update
        (tx: Tx)
        (account: Account)
        (name: string option)
        (customStyles: string option option)
        (settings: (string * string) list option)
        : Account =
        let mutable updated = account
        match name with
        | Some n -> updated <- { updated with Name = n }
        | None -> ()
        match customStyles with
        | Some styles -> updated <- { updated with CustomStyles = Option.toObj styles }
        | None -> ()
        match settings with
        | Some values ->
            let original = AccountSettings.fromColumn (Option.ofObj updated.Settings)
            let assigned = AccountSettings.assign values original
            if isNull updated.Settings || assigned <> original then
                updated <- { updated with Settings = AccountSettings.toJson assigned }
        | None -> ()
        Data.update tx.Db account updated
