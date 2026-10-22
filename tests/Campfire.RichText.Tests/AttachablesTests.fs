// Port of the tests in rust/crates/richtext/src/attachables.rs
module Campfire.RichText.Tests.AttachablesTests

open System
open Xunit
open Campfire.RailsCompat
open Campfire.RichText
open Campfire.RichText.Tests.Support
open Campfire.Tests

/// `GlobalID.find` over the reference's records the vectors name: users 1 and 2 exist, and the
/// default locator ignores the GID's app.
type private VectorRecords() =
    interface IAttachableResolver with
        member _.LocateSigned _ = SignedLookup.Invalid

        member _.FindGid gid =
            let user id =
                GidLookup.User { Id = id; Name = ""; Title = ""; AttachableSgid = ""; UserPath = ""; AvatarPath = "" }
            if not (gid.StartsWith "gid://") then
                GidLookup.NotFound
            else
                let rest = ((gid.Substring 6).Split '?').[0]
                match rest.IndexOf '/' with
                | -1 -> GidLookup.NotFound
                | slash ->
                    let path = rest.Substring(slash + 1)
                    if path.StartsWith "User/" then
                        match path.Substring 5 with
                        | "1" -> user 1L
                        | "2" -> user 2L
                        | _ -> GidLookup.NotFound
                    else
                        GidLookup.OtherModel

/// The reference's answers for SGIDs whose signature doesn't verify (`unverified_sgids` in
/// `vectors/rails_compat.json`, from `reference-tools/rails_compat_vectors.rb`).
[<Fact>]
let ``possibly_expired_sgids_find_users_like_rails`` () =
    use vectors = Repo.vector "rails_compat"
    let cases = vectors.RootElement.GetProperty("unverified_sgids").EnumerateArray() |> Seq.toArray
    // Every vector case is run: a case that silently went missing would fail here.
    Assert.Equal(23, cases.Length)
    let ctx = render (VectorRecords()) None
    let mutable run = 0
    for case in cases do
        let label = case.GetProperty("case").GetString()
        let sgid =
            match case.GetProperty("sgid").ValueKind with
            | Text.Json.JsonValueKind.String -> ValueSome(case.GetProperty("sgid").GetString() |> nonNull)
            | _ -> ValueNone
        let found = Attachables.attachableFromPossiblyExpiredSgid sgid ctx |> Result.map (Option.map (fun u -> u.Id))
        let expected = case.GetProperty "expected"
        match expected.ValueKind with
        | Text.Json.JsonValueKind.Null -> Assert.True((found = Ok None), label)
        | Text.Json.JsonValueKind.String ->
            let gid = expected.GetString() |> nonNull
            let id = Int64.Parse(gid.Substring(gid.LastIndexOf '/' + 1))
            Assert.True((found = Ok(Some id)), label)
        | _ -> Assert.True(isError found, $"{label} should raise like Rails ({expected})")
        run <- run + 1
    Assert.Equal(23, run)

// --- web_url on System.Uri behind the RFC 3986 character check (R8) -----------------------------

[<Fact>]
let ``web_url on System.Uri keeps Ruby's verdicts on the character rules`` () =
    let webUrl (value: string) = okR (Attachables.webUrl (ValueSome value) "once.campfire.test")
    // the query is exempt, as in Ruby's parser: a link to `?x=<y>` is a link; a `%` with two non-hex characters is not
    Assert.Equal(Some "https://example.com/i.png?x=<y>", webUrl "https://example.com/i.png?x=<y>")
    Assert.Equal(Some "https://example.com/p?q=%z", webUrl "https://example.com/p?q=%z")
    Assert.Equal(None, webUrl "https://example.com/p?q=%zz")
    // outside the query: printable ASCII only, no "<>\^`{|}, one `#`, `%` + two hex digits
    for bad in [ "https://example.com/a b"; "https://example.com/<x>"; "https://example.com/a|b"; "https://example.com/a%zzb"; "https://example.com/#a#b"; "https://exämple.com/"; " https://example.com/" ] do
        Assert.True((webUrl bad).IsNone, bad)
    // this Campfire's own host, a numeric host, a scheme that is not http(s)
    Assert.Equal(None, webUrl "https://once.campfire.test/a")
    Assert.Equal(None, webUrl "http://127.0.0.1/a")
    Assert.Equal(None, webUrl "ftp://example.com/a")
    // (the six hand-made URLs where Ruby raises or accepts and System.Uri refuses are known-differences.json's web_url entries)
