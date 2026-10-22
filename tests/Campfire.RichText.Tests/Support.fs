/// Helpers the richtext tests share.
module Campfire.RichText.Tests.Support

open System
open System.Diagnostics
open Campfire.RichText

/// The value of an `Ok`, or a failed assertion saying what the `Error` was.
let ok (r: Result<'a, 'e>) : 'a =
    match r with
    | Ok v -> v
    | Error e -> failwithf "expected Ok, got Error %A" e

let okR (r: Result<'a, RenderError>) : 'a =
    match r with
    | Ok v -> v
    | Error e -> failwithf "expected Ok, got Error %s" e.Message

let okP (r: Result<'a, ParseError>) : 'a =
    match r with
    | Ok v -> v
    | Error e -> failwithf "expected Ok, got Error %s" e.Message

let isError (r: Result<'a, 'e>) : bool =
    match r with
    | Error _ -> true
    | Ok _ -> false

/// A resolver that finds no record at all.
type NoRecords() =
    interface IAttachableResolver with
        member _.LocateSigned _ = SignedLookup.Invalid
        member _.FindGid _ = GidLookup.NotFound

let render (resolver: IAttachableResolver) (host: string option) : RenderContext = { Resolver = resolver; RequestHost = host }

/// Every element and attribute name in `html` as a browser would parse it.
let parsedMarkup (html: string) : string list =
    let root = okP (Html5.parseBody html)
    [ for node in Html5.descendants root do
          match node with
          | :? AngleSharp.Dom.IElement as element ->
              let name = element.LocalName
              // the layout's wrapper
              if not (name = "div" && Html5.attr element "class" = ValueSome "lexxy-content") then
                  yield name
                  for a in element.Attributes do
                      yield $"{name}[{Html5.qualifiedName a}]"
          | _ -> () ]

let private isDebugBuild =
#if DEBUG
    true
#else
    false
#endif

/// The bound a timing assertion allows: generous in a debug build, tight in release.
let bound (debug: TimeSpan) (release: TimeSpan) : TimeSpan = if isDebugBuild then debug else release

/// The tests don't run in parallel in this assembly: the timing tests below measure this process's
/// CPU time, which is only theirs when nothing else in it is running (and, unlike the clock, isn't
/// stretched when other test assemblies share the machine).
[<assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)>]
do ()

/// How much CPU time the process has used since the timer started.
type CpuTimer() =
    let started = Process.GetCurrentProcess().TotalProcessorTime
    member _.Elapsed : TimeSpan = Process.GetCurrentProcess().TotalProcessorTime - started
