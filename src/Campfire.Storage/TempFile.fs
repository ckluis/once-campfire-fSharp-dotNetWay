namespace Campfire.Storage

open System
open System.IO

/// A file in a directory of its own under the temp path (mode 0700), both deleted on dispose. The suffix picks the output
/// format of libvips and ffmpeg, and the framework's own temporary file names cannot take one.
[<Sealed>]
type TempFile private (directory: DirectoryInfo, path: string) =
    member _.Path : string = path

    /// Creates `<temp dir>/<prefix><random>/file<suffix>`.
    static member Create(prefix: string, suffix: string) : TempFile =
        let directory = Directory.CreateTempSubdirectory prefix
        let path = Path.Combine(directory.FullName, "file" + suffix)
        try
            let options = FileStreamOptions(Mode = FileMode.CreateNew, Access = FileAccess.Write, UnixCreateMode = (UnixFileMode.UserRead ||| UnixFileMode.UserWrite))
            (new FileStream(path, options)).Dispose()
        with _ ->
            directory.Delete true
            reraise ()
        new TempFile(directory, path)

    interface IDisposable with
        member _.Dispose() =
            try
                directory.Delete true
            with :? IOException | :? UnauthorizedAccessException ->
                ()
