// O1 (plans/design-views.md section 5). Replays what the Rust views crate rendered (oracle.jsonl, written once by
// `bin/views-differential golden` in unit 4.1 and frozen) for a sample of each kind of case, hostile text included: every
// template, the view helpers and the view-model helpers (the fragment cache keys and eviction retired in unit 4.3: the cache is HybridCache's, tested by F1-F3). Markup answers must be equal as the loose DOM
// (`Campfire.Tests.LooseDom`: the pixel harness's own rule, so a template that passes here passes the loose layer in the
// browser); the answers that are not markup (URLs, JSON, numbers, keys, scripts, the SVG avatar) must be equal as text.
module Campfire.Views.Tests.DifferentialTests

open System
open System.IO
open System.Text.Json
open Xunit
open Campfire.Tests
open Campfire.Views.Differential

/// The operations whose answer is text, not markup: compared exactly.
let private textOps =
    set [ "helpers/url"; "helpers/translations"; "helpers/asset_path"; "helpers/application:to_lowercase"
          "messages/epoch_ms"; "messages/iso8601"; "messages/json_time"; "messages/ruby_number"
          "messages/json_by_bots_index"; "messages/json_by_bots_show"; "messages/json_boosts_by_bots_show"
          "autocompletable/users_index_json"; "pwa/manifest"; "pwa/service_worker"; "searches/search_path"; "rooms/mention_prompt_src"
          "rooms/room_display_name" ]

let private sameAnswer (op: string) (expected: string) (ours: string) : string option =
    if ours = expected then None
    elif textOps.Contains op then Some "text differs"
    else LooseDom.diff (LooseDom.normalize expected) (LooseDom.normalize ours) |> Option.map (fun report -> "loose DOM differs\n" + report)

[<Fact>]
let ``the views render as the rust views did`` () =
    let lines = File.ReadAllLines(Repo.path "tests/Campfire.Views.Tests/oracle.jsonl")
    let mutable shared = JsonElement()
    let counts = Collections.Generic.Dictionary<string, int>()
    let failures = ResizeArray<string>()
    let mutable replayed = 0
    for line in lines do
        let document = JsonDocument.Parse line
        let root = document.RootElement
        match root.TryGetProperty "shared" with
        | true, value -> shared <- value
        | _ ->
            let case = root.GetProperty "case"
            let rust = root.GetProperty "rust"
            let op = Inputs.str (Inputs.get case "op")
            let op = if Inputs.str (Inputs.get (Inputs.get case "args") "name") = "to_lowercase" then op + ":to_lowercase" else op
            counts[op] <- (match counts.TryGetValue op with | true, n -> n | _ -> 0) + 1
            replayed <- replayed + 1
            let ours = Operations.run case shared
            let expected = Inputs.str (Inputs.get rust "out")
            match sameAnswer op expected ours.Out with
            | None -> ()
            | Some why ->
                let at = Seq.zip ours.Out expected |> Seq.tryFindIndex (fun (a, b) -> a <> b) |> Option.defaultValue (min ours.Out.Length expected.Length)
                let around (s: string) = s.Substring(max 0 (at - 30), min 90 (s.Length - max 0 (at - 30)))
                failures.Add $"{op}: {why}\n  first byte difference at {at}\n  rust: {around expected}\n  ours: {around ours.Out}"
            match ours.Fragments, Inputs.get rust "fragments" with
            | Some fragments, parts when parts.ValueKind = JsonValueKind.Array ->
                // Where the fragments go in the page's text. Their lengths are not compared: a message's fragment is Falco's markup now
                // (unit 4.4), a few percent shorter than the writer's (class A: no whitespace where it does not render), and equal
                // to Rust's as the loose DOM (the case's answer above).
                let theirs = [ for p in parts.EnumerateArray() -> Inputs.intOf (Inputs.at p 0) ]
                let ours = fragments |> List.map fst
                if ours <> theirs then failures.Add $"{op}: fragment offsets {ours} against rust's {theirs}"
            | _ -> ()
    // The sample covers every kind of case, so that a case dropped from the file fails.
    let required =
        [ "layouts/application_wrapper"; "layouts/_lightbox"; "layouts/turbo_rails/frame"; "recorded/page";
          "accounts/_help_contact"; "accounts/_invite"; "pwa/_install_instructions"; "pwa/_browser_settings";
          "pwa/_system_settings"; "users/_mention"; "users/autocompletables/_template"; "users/sidebars/rooms/_direct";
          "users/sidebars/rooms/_shared"; "users/sidebars/rooms/_direct_placeholder"; "users/direct_room"; "welcome/show";
          "helpers/tag"; "helpers/form"; "helpers/button"; "helpers/link"; "helpers/image_tag"; "helpers/application";
          "helpers/users"; "helpers/rooms"; "helpers/translations"; "helpers/url"; "helpers/turbo";
          "messages/presentation"; "messages/epoch_ms"; "messages/ruby_number";
          "messages/json_by_bots_index"; "helpers/application:to_lowercase"; "messages/_message"; "messages/message_cached";
          "messages/_actions"; "messages/_presentation"; "messages/_unrenderable"; "messages/_template"; "messages/index";
          "messages/show"; "messages/edit"; "messages/create_turbo_stream"; "messages/destroy_turbo_stream";
          "messages/room_not_found"; "messages/boosts/_boost"; "messages/boosts/_boosts"; "messages/boosts/index";
          "messages/boosts/new"; "rooms/show"; "rooms/involvements/show"; "rooms/refreshes/show"; "rooms/opens/new";
          "rooms/opens/edit"; "rooms/closeds/new"; "rooms/closeds/edit"; "rooms/directs/new"; "rooms/directs/edit";
          "rooms/layouts/_form"; "searches/index"; "autocompletable/users/index"; "autocompletable/users/_prompt_item";
          "users/sidebars/show"; "accounts/edit"; "accounts/users/_user"; "accounts/users/_next_page_container";
          "accounts/users/index_turbo_stream"; "accounts/bots/_bot"; "accounts/bots/_form"; "accounts/bots/index";
          "accounts/bots/new"; "accounts/bots/edit"; "accounts/custom_styles/edit"; "first_runs/show"; "sessions/new";
          "sessions/incompatible_browser"; "sessions/transfers/show"; "users/new"; "users/show"; "users/_ban_button";
          "users/profiles/show"; "users/profiles/_membership"; "users/profiles/_transfer"; "users/push_subscriptions/index";
          "users/push_subscriptions/_push_subscription"; "users/avatars/show"; "pwa/manifest"; "pwa/service_worker";
          "autocompletable/users_index_json"; "helpers/asset_path"; "messages/iso8601"; "messages/json_boosts_by_bots_show";
          "messages/json_by_bots_show"; "messages/json_time"; "rooms/button_to_delete_room"; "rooms/mention_prompt_src";
          "rooms/room_display_name"; "searches/search_path" ]
    for op in required do
        Assert.True(counts.ContainsKey op, $"no {op} case in oracle.jsonl")
    // And the list covers the sample, so an operation added to the generator is added here too.
    let listed = Collections.Generic.HashSet<string>(required)
    for op in counts.Keys do
        Assert.True(listed.Contains op, $"{op} is in oracle.jsonl but not in the list of operations the sample must cover")
    let report = String.Join("\n", failures |> Seq.truncate 10)
    Assert.True(failures.Count = 0, $"{failures.Count} of {replayed} differ:\n{report}")
