// Ports of the #[cfg(test)] modules in rust/crates/ruby/src/{erb,string,uri,integer,rack,float}.rs
module Campfire.Ruby.Tests.UnitTests

open System
open Xunit
open Campfire.Ruby
open Campfire.Ruby.Integer

// erb.rs

[<Fact>]
let ``erb escapes like erb util`` () =
    Assert.Equal("&lt;&amp;&gt;&quot;&#39;x é", Erb.htmlEscape "<&>\"'x é")

// string.rs

[<Fact>]
let ``strips nul and ascii whitespace`` () =
    Assert.Equal("x", Ruby.strip "\000 \t\u000b x \000\n")
    Assert.Equal("\u00a0x\u00a0", Ruby.strip " \u00a0x\u00a0 ")

// uri.rs

[<Fact>]
let ``uri escapes like ruby`` () =
    Assert.Equal("a%2A~+b-._%2B%C3%A9", Ruby.cgiEscape "a*~ b-._+é")
    Assert.Equal("a%2A~%20b-._%2B%C3%A9", Ruby.urlEncode "a*~ b-._+é")

// integer.rs

[<Fact>]
let ``to_i like ruby`` () =
    // `String#to_i` in the reference.
    Assert.Equal(1717243200000L, toI "1717243200000")
    Assert.Equal(12L, toI " +12abc")
    Assert.Equal(7L, toI "\t\n\u000b\u000c\r 7")
    Assert.Equal(0L, toI "\u00a05")
    Assert.Equal(0L, toI "abc")
    Assert.Equal(-5L, toI "-5")
    Assert.Equal(0L, toI "--5")
    Assert.Equal(56L, toI "5_6")
    Assert.Equal(5L, toI "5__6")
    Assert.Equal(0L, toI "_5")
    Assert.Equal(0L, toI "0__5")
    Assert.Equal(-5L, toI "-0d5")
    Assert.Equal(0L, toI "0d_5")
    Assert.Equal(0L, toI "0x5")
    Assert.Equal(Int64.MaxValue, toI "99999999999999999999")
    Assert.Equal(Int64.MinValue, toI "-99999999999999999999")
    Assert.Equal(None, toIChecked "99999999999999999999")
    Assert.Equal(Int128.Parse "99999999999999999999", toI128 "99999999999999999999")

[<Fact>]
let ``casts like active record`` () =
    // `Room.type_for_attribute(:id).serialize(s)` in the reference; a RangeError is None.
    let cases: (string * int64 option) list =
        [ "12", Some 12L
          "12abc", Some 12L
          " -3", Some -3L
          " +12abc", Some 12L
          "\t\n\u000b\u000c\r 7", Some 7L
          "\u000b-0d7", Some -7L
          "0d12", Some 12L
          "0D12", Some 12L
          "0d", Some 0L
          "0d_5", Some 0L
          "0d0_5", Some 5L
          "5_6", Some 56L
          "5__6", Some 5L
          "1_000", Some 1000L
          "0x5", Some 0L
          "-0", Some 0L
          "5\000", Some 5L
          "3000000000", Some 3000000000L
          "9223372036854775807", Some Int64.MaxValue
          "-9223372036854775808", Some Int64.MinValue
          "9223372036854775808", None
          "-9223372036854775809", None
          "99999999999999999999", None
          "\u00a05", None
          "\000 5", None
          "_5", None
          "--5", None
          "+-5", None
          "+", None
          "abc", None
          "", None ]
    for (value, id) in cases do
        Assert.True((integerCast value = id), $"{value}")

// float.rs


[<Fact>]
let ``float_to_s like ruby 3.4`` () =
    // `Float#to_s` in the reference (Ruby 3.4.10): from 1e15, the exponent form unless there
    // are digits after the decimal point.
    let cases: (float * string) list =
        [ 100.0, "100.0"
          0.1, "0.1"
          0.0001, "0.0001"
          0.00012345, "0.00012345"
          1e-5, "1.0e-05"
          1.5e-7, "1.5e-07"
          5e-324, "5.0e-324"
          1e14, "100000000000000.0"
          123456789012345.6, "123456789012345.6"
          999999999999999.0, "999999999999999.0"
          999999999999999.9, "999999999999999.9"
          -999999999999999.0, "-999999999999999.0"
          1e15, "1.0e+15"
          -1e15, "-1.0e+15"
          1.5e15, "1.5e+15"
          1234567890123456.0, "1.234567890123456e+15"
          9007199254740992.0, "9.007199254740992e+15"
          1000000000000001.0, "1.000000000000001e+15"
          1963684456584958.8, "1963684456584958.8"
          1000000000000000.1, "1000000000000000.1"
          2251799813685248.5, "2251799813685248.5"
          -2551800308696183.5, "-2551800308696183.5"
          1e16, "1.0e+16"
          1e20, "1.0e+20"
          Double.MaxValue, "1.7976931348623157e+308" ]
    for (f, s) in cases do
        Assert.True((Ruby.floatToS f = s), $"{f:E}: {Ruby.floatToS f} vs {s}")

[<Fact>]
let ``float_to_s breaks ties to even like ruby`` () =
    // `Float#to_s` in the reference: of two shortest forms equally close, the even one, as long
    // as it reads back. Doubles are twice as dense just below 2^-24, so its even form
    // (5.960464477539062e-08) reads back as a different double.
    let cases: (float * string) list =
        [ 667020902720176.0 + 0.25, "667020902720176.2"
          667020902720176.0 + 0.75, "667020902720176.8"
          1000000000000000.2, "1000000000000000.2"
          1125899906842624.0 + 0.25, "1125899906842624.2"
          -(2074704973491874.0 + 0.25), "-2074704973491874.2"
          24603114260468.0 + 0.0625, "24603114260468.062"
          210745403561986.0 + 0.125, "210745403561986.12"
          Math.Pow(2.0, -24.0), "5.960464477539063e-08"
          Math.Pow(2.0, -25.0), "2.9802322387695312e-08"
          0.1 + 0.2, "0.30000000000000004"
          1.0 / 3.0, "0.3333333333333333" ]
    for (f, s) in cases do
        Assert.True((Ruby.floatToS f = s), $"{f:E}: {Ruby.floatToS f} vs {s}")


[<Fact>]
let ``to_i without 128-bit arithmetic gives the answers of the general one`` () =
    let random = Random 20261006
    let alphabet = " +-0123456789_dD\tx"
    let clamp (n: Int128) =
        if n < Int128.CreateChecked Int64.MinValue then Int64.MinValue
        elif n > Int128.CreateChecked Int64.MaxValue then Int64.MaxValue
        else Int64.CreateChecked n
    for _ in 1..200000 do
        let length = random.Next(0, 24)
        let text = String(Array.init length (fun _ -> alphabet[random.Next alphabet.Length]))
        Assert.True((toI text = clamp (toI128Slowly text)), $"to_i of {text}")
        Assert.True((toI128 text = toI128Slowly text), $"to_i of {text}")
    for text in [ "0"; "-0"; "007"; "0d12"; "-0d5"; "1_000"; "999999999999999999"; "1000000000000000000"; " \n12abc"; "+7"; "" ] do
        Assert.Equal(clamp (toI128Slowly text), toI text)
