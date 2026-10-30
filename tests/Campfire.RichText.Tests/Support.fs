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

/// The tests don't run in parallel in this assembly, and `CpuTimer` measures the calling thread's own CPU time: the process's would take in
/// the runtime's background threads (tiered compilation, the concurrent collector) and whatever else the process is running, which
/// inflated a 300 ms bound to 343 ms under a load of 19 (unit 8.4's L3, unit 8.7's checkpoint).
[<assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)>]
do ()

module private Native =
    [<Struct; System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)>]
    type Timespec =
        val mutable Seconds: int64
        val mutable Nanoseconds: int64

    [<System.Runtime.InteropServices.DllImport("libSystem.dylib", EntryPoint = "clock_gettime")>]
    extern int private clockGettimeMac(int clock, Timespec& time)

    [<System.Runtime.InteropServices.DllImport("libc.so.6", EntryPoint = "clock_gettime")>]
    extern int private clockGettimeLinux(int clock, Timespec& time)

    /// `CLOCK_THREAD_CPUTIME_ID`: 16 on macOS, 3 on Linux. `ValueNone` where the call is not there (Windows).
    let threadCpu () : TimeSpan voption =
        try
            let mutable time = Timespec()
            let status =
                if OperatingSystem.IsMacOS() then clockGettimeMac (16, &time)
                elif OperatingSystem.IsLinux() then clockGettimeLinux (3, &time)
                else -1
            if status = 0 then ValueSome(TimeSpan.FromTicks(time.Seconds * TimeSpan.TicksPerSecond + time.Nanoseconds / 100L)) else ValueNone
        with _ ->
            ValueNone

/// How much CPU time the calling thread has used since the timer started (the process's, where the platform has no thread clock). Read
/// from the thread that made it: the timed work is synchronous.
type CpuTimer() =
    let processStart = Process.GetCurrentProcess().TotalProcessorTime
    let threadStart = Native.threadCpu ()

    member _.Elapsed: TimeSpan =
        match threadStart, Native.threadCpu () with
        | ValueSome start, ValueSome now -> now - start
        | _ -> Process.GetCurrentProcess().TotalProcessorTime - processStart
