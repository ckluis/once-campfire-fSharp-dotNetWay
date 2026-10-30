# Unit 9.6 evidence: every class A and B line of the sources, with the README line that carries it

Sources: section 8 (section 9 in `design-data-ef.md`, section 7 in `design-kit-auth.md`) of the nine design notes, and every Phase log
"Differences recorded" line. README means `README.md`, section "Known differences" (class A is the one list under "Class A").
"Dropped" rows carry the reason, which the Phase log repeats.

## design-data-ef (section 9)

| Source line | README line |
|---|---|
| Class A: none visible | none to carry |
| WAL checkpoint returns to SQLite's auto-checkpoint | Data: "The WAL checkpoint" |
| the port's "SQLite index" difference stays | Data: "Boot adds `index_messages_on_room_id_and_created_at`" |
| (Phase log H.2) member ids of direct rooms ordered by membership | Data: "Hot read paths are Dapper" |

## design-kit-auth (section 7)

| Source line | README line |
|---|---|
| CSRF rewritten (B) | Application: "Forgery protection" |
| Rails session: flash and return-to (B) | Application: "The flash and `return_to_after_authenticating`" |
| Method override (B) | Application: "The method override" |
| Remote IP, trusted ranges, no `Client-Ip` (B) | Application: "Proxy headers" |
| Malformed multipart (B) | Application: "A malformed multipart body" |
| Login throttle on wall-clock time (B) | Application: "Sign-in throttling" |
| Byte-level (A): cookie attribute order and case, no body ETag, gzip/Brotli bytes, Cache-Control order | Class A: "Cookies and headers" |

## design-cable (section 8)

| Source line | README line |
|---|---|
| WebSocket compression none, as Rails | Cable: "No WebSocket compression, as Rails" |
| Slow clients cut (B) | Cable: "Slow clients are cut, not buffered" |
| Origin check (B) | Cable: "Origin check" |
| Byte-level (A): JSON escaping, key order, the 101 headers, HTTP/2 WebSockets, heartbeat at boot | Class A: "Cable frames"; Cable: "The `101 Switching Protocols`" |
| Unchanged: limits, remote disconnects after commit, re-check after registration | Cable: "Limits", "Authentication is the page cookie scheme" |
| (Phase log 3.x) memory bound of 16 KB per connection | Cable: "Memory" |

## design-views (section 8)

| Source line | README line |
|---|---|
| Byte-level (A), one entry | Class A: "Markup bytes" |
| Internal: fragment cache is HybridCache | Views: "The fragment cache" (kept: it is visible to an operator through `CAMPFIRE_FRAGMENT_CACHE_MB`) |
| No class B entry | Views: the opening paragraph |
| (Phase log 4.x) custom CSS `</style` is written `<\/style` | Views: "Custom CSS" |
| (Phase log 4.x) `capitalize`, `downcase` tables; DateTimeOffset precision | Views: the two bullets |
| (Phase log 4.9) manifest escapes | Views: "The web app manifest" |
| (Phase log 4.x) empty `rooms/refreshes/show` body; frame layout indentation | Class A: "Markup bytes" |

## design-richtext (section 8)

| Source line | README line |
|---|---|
| Class A: `<` and `>` raw in attribute values | Class A: "Rich text" |
| B: `select` contents (2025 standard), `noscript` fuzz body | Rich text: "The 2025 `select` model" |
| B: limits counted on created elements | Rich text: "Depth and attribute limits" |
| B: caps (10,000 elements, 40,000 tokens, 400 attributes) | Rich text: "Caps Gumbo does not have" |
| B: unfurl URLs on `System.Uri` | Rich text: "Opengraph URLs" |
| Kept: `name` attributes dropped, no link in an attribute, missing attachable renders a box, attachments 8 deep | Carried over: "Rich text" |
| (Phase log 5.x) tokenizer lowercasing, svg/math raw text, table parts, `</body>` comments, fuzz 398 | Rich text: the four bullets |
| (Phase log 5.x) parser-throws refusal and the `xmp` in `select` exception | Rich text: "Markup AngleSharp's tree builder throws on" |
| (Phase log 5.x) report to AngleSharp | Rich text: the last bullet |

## design-assets (section 8)

| Source line | README line |
|---|---|
| A: fingerprint URL form; CSS `url()` and `sourceMappingURL` not rewritten; ETag and Last-Modified; precompressed text; import map JSON; 15 versus 16 preload links | Class A: "Assets" |
| B: cache headers; `/robots.txt` and error pages `no-cache` with an ETag | Assets: "Cache headers" |
| B: extensionless `/404` etc. | Assets: "An extensionless `/404`..." |
| B: `/assets/.manifest.json` not served | Assets: "`/assets/.manifest.json` is not served" |
| B: plain `/assets/<logical path>` served | Assets: "Cache headers" |
| B: several byte ranges | Storage: "Files are served with the framework's file result" (the Assets entry's own line moved there, one statement) |
| B: 405 on a non-GET asset | Assets: "A method other than GET or HEAD" |
| B: `lexxy.js.gz` and `.br` | Assets: "The vendored `lexxy.js.gz`" |
| (Phase log 6.x) letter case; trailing slash | Assets: "Asset and public-file routes ignore letter case" |
| (Phase log 6.x) `Accept-Ranges`, uncompressed images | Class A: "Assets" |
| (Phase log 6.x) no `x-request-id` or `x-runtime` on assets | Assets: "Asset responses carry neither..." |

## design-front (section 8)

| Source line | README line |
|---|---|
| TLS-ALPN-01 not answered (AC11) | Front server: "Certificates" |
| ordered at boot, renewed `min(30 days, lifetime / 3)`, first handshake waits, cached name not served (AC4, AC6) | Front server: "Certificates", "TLS" |
| HTTP port 421 / redirect (HS2) | Front server: "TLS" |
| no h2c; `HTTP_WRITE_TIMEOUT` unread; minimum data rate (HS8) | Front server: "Listeners and timeouts" |
| `GZIP_COMPRESSION_*`, `DEBUG` unread; no jitter (CZ3) | Application: "Compression"; Front server: "Listeners and timeouts" |
| request log is the HTTP logging line (LG1-LG3) | Front server: "The request log" |
| `X-Cache` gone, `Age` on hits, cookie responses not cached, `Authorization` bypass (OC1, OC7) | Front server: "The response cache" |
| shutdown grace one 10 s budget (HS11) | Front server: "`MAX_REQUEST_BODY`" |
| cached avatar, logo, QR served to anyone with the URL (OC1) | Class A: "Front server" |
| Class A: `Vary`, images, `X-Request-Start`, `X-Forwarded-*`, hit's own `X-Request-Id` | Class A: "Cookies and headers", "Front server" |
| Kept Kestrel lines: `Date`, head limits, three parser rules | Front server: the first three bullets |
| (Phase log 7.1) query values `[FILTERED]`; paths as sent | Front server: "The request log" |
| (Phase log 7.4a) `FORWARD_HEADERS`, `MAX_REQUEST_BODY`, `force_ssl` redirect gone, HSTS kept | Front server: the three bullets |
| (Phase log 7.1) zstd after br and gzip | Application: "Compression" |
| Operator step (Let's Encrypt staging) | not a difference; kept in AGENTS.md |

## design-jobs (section 8)

| Source line | README line |
|---|---|
| B: .NET's request headers, header order, `Connection`, `Accept-Encoding` | Integrations: "Webhooks" |
| B: trickling reply cut at 60 s, 7 s to the head | Integrations: "Webhooks" |
| B: 16 unfurls at once | Integrations: "Link unfurls" |
| B: 14 malformed-page probes (OG3) | Integrations: "Malformed pages" |
| B: caps 256 and 100,000 (OG1, OG2) | Integrations: "Caps" |
| B: `mailto:` 204 | Integrations: "`mailto:`" |
| B: bot key and webhook URL logged as HMAC hashes (RD1-RD3) | Application: "Redaction in the logs" |
| B: push 429 not retried (WP4) | Integrations: "Web Push" |
| A: request target normalization | Class A: "Integrations" |
| A: push record size, padding, JWT bytes (WP1, WP2) | Class A: "Integrations" |
| A: pooled push connections; no `Accept`/`User-Agent` on push | Class A: "Integrations" |
| Kept: user agent and QR code, job model, push limits, unfurl and webhook limits | Carried over: "Jobs", "Push", "Webhook and unfurl limits" |
| (Phase log 8.5) 400 "UnauthorizedRegistration" logged as `ResponseError`; VAPID ECDSA random nonce; `exp` | Integrations: "Web Push" |
| (Phase log 8.4/8.x) tweet host case | Integrations: "Tweet hosts" |
| (Phase log 8.x) deadline closes the connection under the read | Integrations: "Link unfurls" |
| (Phase log 8.x) `VAPID_SUBJECT` default | Application: "`VAPID_SUBJECT`" |

## design-close (section 8)

| Source line | README line |
|---|---|
| B: the five `Range` kinds, 416 body, `If-Modified-Since` (with README's wording, `bytes= -5`, not `bytes= 1-2`) | Storage: "Files are served with the framework's file result" |
| B: request log skips assets, avatars, `/up` (LS2, LS3) | Front server: "The request log skips three route families" |
| A: `Accept-Ranges: bytes` on the disk service | Class A: "Storage" |
| Retired vector groups (`to_f`, `byte_ranges`) | not differences; the Phase log (9.1, 9.2) records them |
| Kept: JSON text, `Content-Disposition`, URL escaping, temp files, job and push behaviour | no difference to state |

## Phase log lines with no row above (Application and earlier phases)

| Phase log line | README line |
|---|---|
| 2.7 params and formats | Application: "Params", "Format negotiation" |
| 2.8 compression; empty errors compressed; `Vary` to a client with no `Accept-Encoding` | Application: "Compression"; Class A |
| 2.8 `send_file` ranges; `fresh_when` ETag text | Application: "Conditional responses" |
| 2.9 headers of the HttpContext controllers; throttle | Application: "Headers of the controllers", "Sign-in throttling" |
| 2.10 JSON `null` param; Live controllers' headers | Application: "A JSON `null` param", "Headers of the controllers" |
| 2.11 mention list hash filter | Application: "The mention list" |
| 2.12 bot API raw body, `null` attachment, `involvement` 500 | Application: "A bot API request" |
| 2.13 route 404 without `Allow`; HEAD from POST; non-ASCII header bytes | Application: three bullets |
| 2.6 head limits, body cap, method override | Front server; Application |
| `Pages.json` floats; refresh `since` clamp; route recognition; log lines; GC options | Application: the matching bullets |

## Dropped, with the reason (repeated in the Phase log)

| Old README line | Why it is not in the new README |
|---|---|
| "A file an upload spooled is deleted when the response completes, as Rails' `Rack::TempfileReaper` does. In Rust a `TempPath` goes when the `UploadedFile` drops" | A difference from Rust, not from Rails: Rails does the same. The new README lists differences from Rails only. |
| "`FILES_AND_MEDIA_URL_REGEX` is .NET's regex engine (non-backtracking) where Rust uses the `regex` crate" | A difference from Rust on characters that are only marks or letter numbers; no test pins it (`git grep` finds none), so it cannot be a class B line with a test. |
| Rust "Passwords" (bcrypt outside the database writer) and "Caching" (bot JSON per base URL) | Rust internals that do not exist here: there is no writer thread, and the bot API's JSON is not cached (design-views section 8). Stated in the README's "Not carried" bullet. |
| Stale test names: `ControllersATests`, `ControllersBTests`, `SeamTests`, `ConfigTests` | Not dropped: renamed. 9.5 renamed the first two to `SessionControllerTests` and `AccountControllerTests`, folded `SeamTests` into `RouteTableTests` and `AuthenticationTests`, and the vapid subject test is `OptionsTests` OP1. |
| The Rust README pointer ("see its README") | Removed on purpose: the new list restates Rust's still-applying entries under "Carried over from the Rust port". |
