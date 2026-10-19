// Random tokens, as Rails' SecureRandom makes them (the part of the old Sql.fs the models still use).
namespace Campfire.Db

open System
open System.Security.Cryptography

module Tokens =
    let private pick (chars: string) (n: int) : string =
        let out = Array.zeroCreate<char> n
        for i in 0 .. n - 1 do
            out[i] <- chars[RandomNumberGenerator.GetInt32 chars.Length]
        String out

    /// `SecureRandom.alphanumeric(n)`
    let alphanumeric (n: int) : string = pick "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789" n

    /// `SecureRandom.base58(n)`, as `has_secure_token` uses it.
    let base58 (n: int) : string = pick "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz" n

    /// `SecureRandom.uuid` / `Random.uuid`
    let uuid () : string = Guid.NewGuid().ToString()
