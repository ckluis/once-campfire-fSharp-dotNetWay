// Port of the `Error` enum in rust/crates/storage/src/lib.rs
namespace Campfire.Storage

open System
open System.IO

/// What can go wrong in storage. Named `StorageError` because `Campfire.RailsCompat.Error` has
/// cases of the same names (`InvalidSignature`).
[<RequireQualifiedAccess>]
type StorageError =
    /// "file not found"
    | FileNotFound
    /// "checksum mismatch"
    | Integrity
    /// "invalid signature"
    | InvalidSignature
    /// "invalid variation: {0}"
    | InvalidVariation of string
    /// "can't transform blob with content_type={0}"
    | Invariable of string
    /// "no previewer found for content_type={0}"
    | Unpreviewable of string
    /// "no previewer found and can't transform blob with content_type={0}"
    | Unrepresentable of string
    /// "libvips: {0}"
    | Vips of string
    | Preview of string
    | Analyze of string
    /// `std::io::Error`
    | Io of exn

module StorageError =
    /// The error's `Display`.
    let message (error: StorageError) : string =
        match error with
        | StorageError.FileNotFound -> "file not found"
        | StorageError.Integrity -> "checksum mismatch"
        | StorageError.InvalidSignature -> "invalid signature"
        | StorageError.InvalidVariation message -> $"invalid variation: {message}"
        | StorageError.Invariable contentType -> $"can't transform blob with content_type={contentType}"
        | StorageError.Unpreviewable contentType -> $"no previewer found for content_type={contentType}"
        | StorageError.Unrepresentable contentType -> $"no previewer found and can't transform blob with content_type={contentType}"
        | StorageError.Vips message -> $"libvips: {message}"
        | StorageError.Preview message -> message
        | StorageError.Analyze message -> $"analysis failed: {message}"
        | StorageError.Io e -> e.Message

type StorageResult<'a> = Result<'a, StorageError>

module internal Io =
    /// The I/O failures Rust's `io::Result` carries, as `Error::Io`.
    let attempt (f: unit -> 'a) : StorageResult<'a> =
        try
            Ok(f ())
        with
        | :? IOException as e -> Error(StorageError.Io e)
        | :? UnauthorizedAccessException as e -> Error(StorageError.Io e)

    /// `fs::File::open` and friends with `not_found`: a missing file is `FileNotFound`.
    let attemptFound (f: unit -> 'a) : StorageResult<'a> =
        try
            Ok(f ())
        with
        | :? FileNotFoundException
        | :? DirectoryNotFoundException -> Error StorageError.FileNotFound
        | :? IOException as e -> Error(StorageError.Io e)
        | :? UnauthorizedAccessException as e -> Error(StorageError.Io e)
