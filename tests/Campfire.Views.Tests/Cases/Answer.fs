/// What an operation answers: the oracle's `out`, and the optional `text` and `fragments` fields of the frozen answers.
module Campfire.Views.Differential.Answer

open System.Text
open Campfire.Views

type Answer =
    { Body: string
      Text: string option
      Fragments: (int * int) list option }

let text (out: string) : Answer = { Body = out; Text = None; Fragments = None }

/// A recorded page's text without its fragments, and the offsets in it where the fragments go: the parts joined, and their ends.
let recordedText (page: HtmlOutput) : string = Encoding.UTF8.GetString(Array.concat page.Parts)

let recordedOffsets (page: HtmlOutput) : (int * int) list =
    let mutable offset = 0
    [ for i in 0 .. page.Fragments.Length - 1 do
          offset <- offset + page.Parts[i].Length
          yield offset, page.Fragments[i].Length ]
