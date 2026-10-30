// Port of rust/crates/views/src/helpers/assets.rs
/// Asset resolution (`AssetTagHelper`, `AssetUrlHelper`).
module Campfire.Views.Helpers.Assets

open System
open Campfire.Views

let private isScheme (scheme: ReadOnlySpan<char>) : bool =
    let mutable all = scheme.Length > 0
    for c in scheme do
        if not (Char.IsAsciiLetter c || c = '-') then all <- false
    all

/// `asset_path(source)`: URLs and absolute paths pass through; logical asset paths are digested.
let assetPath (ctx: ViewContext) (source: string) : string =
    let isUrl =
        source.StartsWith('/')
        || source.StartsWith("data:", StringComparison.Ordinal)
        || source.StartsWith("cid:", StringComparison.Ordinal)
        || (match source.IndexOf("://", StringComparison.Ordinal) with
            | -1 -> false
            | at -> isScheme (source.AsSpan(0, at)))
    if isUrl then source else ctx.Asset source
