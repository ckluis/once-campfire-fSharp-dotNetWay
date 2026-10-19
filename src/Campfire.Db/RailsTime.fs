// Design: plans/design-data-ef.md E4. The one time type of the data layer (the old Timestamp struct is gone).
namespace Campfire.Db

open System
open System.Globalization
open Microsoft.EntityFrameworkCore.Storage.ValueConversion

/// Active Record's text for a `datetime(6)` column: UTC, `YYYY-MM-DD HH:MM:SS`, then `.ffffff` only when the microseconds are
/// non-zero (`ActiveRecord::ConnectionAdapters::Quoting#quoted_date`). Values are truncated, not rounded, to microseconds
/// (`ActiveModel::Type::Helpers::TimeValue`). The entities carry plain `DateTime`s (always UTC); only these functions know the text.
module RailsTime =

    let private ticksPerMicrosecond = 10L

    /// To microseconds (Rails truncates), `Kind = Utc`. A local time is converted; an unspecified one is taken as UTC.
    let truncate (d: DateTime) : DateTime =
        let utc = if d.Kind = DateTimeKind.Local then d.ToUniversalTime() else d
        DateTime(utc.Ticks - utc.Ticks % ticksPerMicrosecond, DateTimeKind.Utc)

    /// `Time.now.utc` truncated, from the app's clock.
    let now (time: TimeProvider) : DateTime = truncate (time.GetUtcNow().UtcDateTime)

    /// The exact text Active Record writes to SQLite.
    let toDb (d: DateTime) : string =
        let d = truncate d
        let microseconds = int (d.Ticks % TimeSpan.TicksPerSecond / ticksPerMicrosecond)
        let whole = d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
        if microseconds = 0 then whole else whole + "." + microseconds.ToString("D6", CultureInfo.InvariantCulture)

    let private isDigit (c: char) = uint (int c - 48) <= 9u

    /// `YYYY-MM-DD HH:MM:SS` with 0-9 fraction digits, the forms Rails and SQLite's `STRFTIME` write; the rest (a `T` separator, a
    /// trailing `Z` or ` UTC`) goes the long way. Fraction digits past the sixth are cut (truncation).
    let private parseStrict (s: string) : DateTime voption =
        let n = s.Length
        if n < 19 || n = 20 || n > 29 || s[4] <> '-' || s[7] <> '-' || s[10] <> ' ' || s[13] <> ':' || s[16] <> ':' then
            ValueNone
        else
            let mutable ok = true
            for i in [ 0; 1; 2; 3; 5; 6; 8; 9; 11; 12; 14; 15; 17; 18 ] do
                if not (isDigit s[i]) then ok <- false
            if ok && n > 19 then
                if s[19] <> '.' then ok <- false
                for i in 20 .. n - 1 do
                    if not (isDigit s[i]) then ok <- false
            if not ok then
                ValueNone
            else
                let two i = (int s[i] - 48) * 10 + (int s[i + 1] - 48)
                let y = two 0 * 100 + two 2
                let mo, d, h, mi, sec = two 5, two 8, two 11, two 14, two 17
                if y < 1 || mo < 1 || mo > 12 || d < 1 || d > DateTime.DaysInMonth(y, mo) || h > 23 || mi > 59 || sec > 59 then
                    ValueNone
                else
                    let mutable microseconds = 0
                    let mutable scale = 100_000
                    for i in 20 .. min (n - 1) 25 do
                        microseconds <- microseconds + (int s[i] - 48) * scale
                        scale <- scale / 10
                    ValueSome(DateTime(y, mo, d, h, mi, sec, DateTimeKind.Utc).AddTicks(int64 microseconds * ticksPerMicrosecond))

    /// Reads what Rails or SQLite wrote; anything unreadable is a `FormatException` (a column holding garbage is a bug, not a value).
    let ofDb (s: string) : DateTime =
        match parseStrict s with
        | ValueSome d -> d
        | ValueNone ->
            let text = s.Trim()
            let text =
                if text.EndsWith(" UTC", StringComparison.Ordinal) then text.Substring(0, text.Length - 4)
                elif text.EndsWith("Z", StringComparison.Ordinal) then text.Substring(0, text.Length - 1)
                else text
            // "FFFFFFF" accepts a bare trailing dot; Rails never writes one.
            if text.EndsWith('.') then raise (FormatException $"not a Rails timestamp: '{s}'")
            match DateTime.TryParseExact(text, [| "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF"; "yyyy-MM-dd HH:mm:ss.FFFFFFF" |], CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal ||| DateTimeStyles.AdjustToUniversal) with
            | true, d -> truncate d
            | _ -> raise (FormatException $"not a Rails timestamp: '{s}'")

    /// `Time#to_fs(:usec)` (`%Y%m%d%H%M%S%6N`), what `cache_version` uses.
    let toUsec (d: DateTime) : string =
        let d = truncate d
        d.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture)
        + (d.Ticks % TimeSpan.TicksPerSecond / ticksPerMicrosecond).ToString("D6", CultureInfo.InvariantCulture)

    /// `Time#to_fs(:epoch)`: whole seconds since 1970.
    let toEpoch (d: DateTime) : int64 = (truncate d - DateTime.UnixEpoch).Ticks / TimeSpan.TicksPerSecond

/// Applied to every `DateTime` and `Nullable<DateTime>` property by `CampfireDb.ConfigureConventions`.
type RailsTimeConverter() =
    inherit ValueConverter<DateTime, string>((fun d -> RailsTime.toDb d), (fun s -> RailsTime.ofDb s))
