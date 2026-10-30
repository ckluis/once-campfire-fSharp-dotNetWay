// Port of rust/crates/views/src/helpers/turbo.rs
/// turbo-rails' `dom_id`.
module Campfire.Views.Helpers.Turbo

/// `"prefix_model_"` (or `"model_"`), made once per distinct pair: the pairs are a template's literal
/// prefix and a model's param key, so the tables are as small as the templates are.
let private prefixed =
    System.Collections.Concurrent.ConcurrentDictionary<struct (string * string), string>()

let private unprefixed = System.Collections.Concurrent.ConcurrentDictionary<string, string>()

let private domPrefix (model: string) (prefix: string option) : string =
    // No prefix is not the empty prefix: `dom_id(x, "")` is "_x_1", `dom_id(x)` is "x_1".
    match prefix with
    | Some prefix ->
        let key = struct (prefix, model)
        match prefixed.TryGetValue key with
        | true, found -> found
        | _ ->
            let made = $"{prefix}_{model}_"
            prefixed[key] <- made
            made
    | None ->
        match unprefixed.TryGetValue model with
        | true, found -> found
        | _ ->
            let made = $"{model}_"
            unprefixed[model] <- made
            made

/// `dom_id(record, prefix)`: "prefix_model_id".
let domId (model: string) (id: 'a) (prefix: string option) : string = domPrefix model prefix + string id
