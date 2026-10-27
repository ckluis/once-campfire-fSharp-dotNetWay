// Port of rust/crates/ruby/src/float.rs
module Campfire.Ruby.Float

open System
open System.Globalization

/// The digits of `1.2345e6` and where the decimal point goes in them (7).
let private scientificDigits (formatted: string) : string * int =
    let e = formatted.IndexOf 'E'
    let mantissa = formatted.Substring(0, e)
    let exponent = Int32.Parse(formatted.Substring(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)
    let digits = String(mantissa.ToCharArray() |> Array.filter Char.IsAsciiDigit)
    (digits, exponent + 1)

/// The shortest digits that round-trip, as `{:e}` writes them in Rust, and where the decimal
/// point goes in them.
let private shortestRoundTrip (magnitude: float) : string * int =
    let r = magnitude.ToString("R", CultureInfo.InvariantCulture)
    let roundTrips (text: string) =
        Double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture) = magnitude
    if roundTrips r then
        let e = r.IndexOf 'E'
        let mantissa, exponent =
            if e < 0 then r, 0
            else r.Substring(0, e), Int32.Parse(r.Substring(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture)
        let dot = mantissa.IndexOf '.'
        let integerLength = if dot < 0 then mantissa.Length else dot
        let raw = mantissa.Replace(".", "")
        let trimmed = raw.TrimStart '0'
        let leadingZeros = raw.Length - trimmed.Length
        (trimmed.TrimEnd '0', integerLength - leadingZeros + exponent)
    else
        // .NET's "R" can write a form that reads back as the neighbour below at a power of two,
        // where the interval is lopsided (2^-25 as 2.980232238769531e-08). Take the first
        // correctly rounded length that does read back.
        let mantissa = (r.Split 'E').[0]
        let significand = mantissa.Replace(".", "").TrimStart '0'
        let candidates =
            seq {
                for n in significand.Length + 1 .. 17 do
                    yield magnitude.ToString("E" + string (n - 1), CultureInfo.InvariantCulture)
            }
        let (d, point) = scientificDigits (candidates |> Seq.find roundTrips)
        (d.TrimEnd '0', point)

/// The shortest digits that read back as `magnitude`, and where the decimal point goes in them.
/// When two such forms are equally close, Ruby's dtoa takes the even one and Rust the upper one:
/// `667020902720176.25.to_s` is "667020902720176.2". Those ties take 16 or 17 digits, and
/// fixed-precision formatting breaks them to even.
let private shortestDigits (magnitude: float) : string * int =
    let (digits, _) as shortest = shortestRoundTrip magnitude
    if digits.Length >= 16 && "13579".Contains digits[digits.Length - 1] then
        let even = magnitude.ToString("E" + string (digits.Length - 1), CultureInfo.InvariantCulture)
        if Double.Parse(even, NumberStyles.Float, CultureInfo.InvariantCulture) = magnitude then
            scientificDigits even
        else
            shortest
    else
        shortest

/// `Float#to_s`: plain decimals from 1e-4 up to (not including) 1e15, and above that while the
/// shortest digits still reach past the decimal point (`1000000000000000.1`); the exponent form
/// otherwise (`flo_to_s` in Ruby 3.4's numeric.c).
let floatToS (f: float) : string =
    if Double.IsNaN f then "NaN"
    elif Double.IsInfinity f then (if f > 0.0 then "Infinity" else "-Infinity")
    elif f = 0.0 then (if Double.IsNegative f then "-0.0" else "0.0")
    else
        let (digits, decpt) = shortestDigits (abs f)
        let sign = if f < 0.0 then "-" else ""
        if decpt < -3 || (decpt > 15 && digits.Length <= decpt) then
            let first = digits.Substring(0, 1)
            let rest = if digits.Length = 1 then "0" else digits.Substring 1
            let e = decpt - 1
            let esign = if e < 0 then '-' else '+'
            $"{sign}{first}.{rest}e{esign}{(abs e):D2}"
        elif decpt <= 0 then
            $"{sign}0.{String('0', -decpt)}{digits}"
        elif decpt >= digits.Length then
            $"{sign}{digits}{String('0', decpt - digits.Length)}.0"
        else
            $"{sign}{digits.Substring(0, decpt)}.{digits.Substring decpt}"
