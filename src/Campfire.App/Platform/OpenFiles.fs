// plans/design-front.md F13: the open-file limit, moved unchanged from the deleted front's `Server.fs`. The framework has no
// equivalent and every WebSocket is a descriptor.
namespace Campfire.App

open System
open System.Runtime.InteropServices

[<Struct; StructLayout(LayoutKind.Sequential)>]
type internal RLimit =
    val mutable Cur: uint64
    val mutable Max: uint64

module internal LibC =
    [<DllImport("libc.so.6", EntryPoint = "getrlimit")>]
    extern int getrlimitLinux(int resource, RLimit& limit)

    [<DllImport("libc.so.6", EntryPoint = "setrlimit")>]
    extern int setrlimitLinux(int resource, RLimit& limit)

    [<DllImport("libSystem.dylib", EntryPoint = "getrlimit")>]
    extern int getrlimitMac(int resource, RLimit& limit)

    [<DllImport("libSystem.dylib", EntryPoint = "setrlimit")>]
    extern int setrlimitMac(int resource, RLimit& limit)

module OpenFiles =
    /// RLIMIT_NOFILE
    let private resource = if OperatingSystem.IsMacOS() then 8 else 7

    let private getLimit (limit: byref<RLimit>) : int =
        if OperatingSystem.IsMacOS() then LibC.getrlimitMac (resource, &limit) else LibC.getrlimitLinux (resource, &limit)

    let private setLimit (limit: byref<RLimit>) : int =
        if OperatingSystem.IsMacOS() then LibC.setrlimitMac (resource, &limit) else LibC.setrlimitLinux (resource, &limit)

    /// Raises the soft limit on open files to the hard limit, and returns the new limit. Every
    /// WebSocket is a file descriptor, and containers commonly start processes with a soft limit
    /// (65,536 in Docker's default, 1,024 elsewhere) far below the hard one, which would cap the
    /// number of connected clients however little memory each takes.
    let raiseOpenFileLimit () : uint64 voption =
        if not (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) then
            ValueNone
        else
            let mutable limit = RLimit()
            if getLimit &limit <> 0 then
                ValueNone
            else
                if limit.Cur < limit.Max then
                    let mutable raised = RLimit()
                    raised.Cur <- limit.Max
                    raised.Max <- limit.Max
                    if setLimit &raised = 0 then limit <- raised
                ValueSome limit.Cur

    /// The soft and hard open-file limits as the OS reports them now, for tests.
    let internal openFileLimits () : struct (uint64 * uint64) voption =
        let mutable limit = RLimit()
        if getLimit &limit = 0 then ValueSome(struct (limit.Cur, limit.Max)) else ValueNone
