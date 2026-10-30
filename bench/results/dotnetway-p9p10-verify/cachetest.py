#!/usr/bin/env python3
# Live checks of theDotNetWay's page cache against a running candidate (port 4312, seed default).
import gzip, http.cookiejar, re, subprocess, sys, time, urllib.parse, urllib.request

BASE = "http://127.0.0.1:4312"
DB = "/Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/parity/.seed/.candidates/4312/db/production.sqlite3"
DESIGNERS, ALL_TALK = 654632876, 486777696
DAVID, KEVIN, JASON = 127326141, 712064548, 149087659
results = []


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *a, **k):
        return None


class Client:
    def __init__(self, email):
        self.jar = http.cookiejar.CookieJar()
        self.op = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(self.jar), NoRedirect)
        st, _, h = self.req("POST", "/session", {"email_address": email, "password": "secret123456"})
        assert st == 302, (email, st)

    def req(self, method, path, form=None, headers=None):
        data = urllib.parse.urlencode(form).encode() if form is not None else None
        r = urllib.request.Request(BASE + path, data=data, method=method)
        r.add_header("Accept-Encoding", "gzip")
        r.add_header("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 Version/18.0 Safari/605.1.15")
        for k, v in (headers or {}).items():
            r.add_header(k, v)
        try:
            resp = self.op.open(r)
        except urllib.error.HTTPError as e:
            resp = e
        body = resp.read()
        if resp.headers.get("Content-Encoding") == "gzip":
            body = gzip.decompress(body)
        return resp.status, body.decode("utf-8", "replace"), resp.headers

    def get(self, path):
        return self.req("GET", path)


def sql(stmt):
    out = subprocess.run(["colima", "ssh", "--", "sqlite3", "-cmd", ".timeout 5000", DB, stmt], capture_output=True, text=True)
    if out.returncode != 0:
        raise SystemExit("sql failed: " + out.stderr)
    return out.stdout.strip()


def check(name, ok, detail=""):
    results.append((name, ok))
    print(("PASS " if ok else "FAIL ") + name + (" -- " + detail if detail else ""), flush=True)


def uid(body):
    m = re.search(r'name="current-user-id" content="(\d+)"', body)
    return int(m.group(1)) if m else None


def title(body):
    m = re.search(r"<title>([^<]*)", body)
    return m.group(1).strip() if m else None


pages = lambda room: [f"/rooms/{room}", f"/rooms/{room}/messages", "/users/me/sidebar", "/searches?q=the"]

david1, david2, kevin, jason = Client("david@37signals.com"), Client("david@37signals.com"), Client("kevin@37signals.com"), Client("jason@37signals.com")

# 1. Two users, same URLs: each gets their own page, twice.
for p in pages(DESIGNERS):
    d = [david1.get(p) for _ in range(2)]
    k = [kevin.get(p) for _ in range(2)]
    ok = all(s == 200 for s, _, _ in d + k) and d[0][1] == d[1][1] and k[0][1] == k[1][1] and (d[0][1] != k[0][1] or p.endswith("/messages"))
    ids = {uid(b) for _, b, _ in d}, {uid(b) for _, b, _ in k}
    if p == f"/rooms/{DESIGNERS}":
        ok = ok and ids == ({DAVID}, {KEVIN})
    else:
        ok = ok and DAVID not in ids[1] and KEVIN not in ids[0]
    check(f"two users {p}", ok, f"statuses {[x[0] for x in d + k]} ids {ids}")

# 2. Caching is live: a write outside the app (raw SQL, no epoch bump) is not seen on a warm page (class B, 60 s).
for _ in range(3):  # the first GET changes last_room, which is in the key
    st, b0, _ = david1.get(f"/rooms/{ALL_TALK}")
sql(f"update rooms set name='Raw Renamed' where id={ALL_TALK}")
st, b1, _ = david1.get(f"/rooms/{ALL_TALK}")
check("hit serves the warm page after an outside write (cache is live)", title(b1) == title(b0) == "All Talk", f"titles {title(b0)!r} -> {title(b1)!r}")
sql(f"update rooms set name='All Talk' where id={ALL_TALK}")

# 3. A fresh message through the app retires every page.
token = f"verifier fresh {int(time.time())}"
for p in [f"/rooms/{ALL_TALK}", f"/rooms/{ALL_TALK}/messages"]:
    david2.get(p); david2.get(p)
st, _, h = jason.req("POST", f"/rooms/{ALL_TALK}/messages", {"message[body]": token}, {"Accept": "text/vnd.turbo-stream.html, text/html"})
check("jason posts", st in (200, 201, 302), f"status {st}")
for p in [f"/rooms/{ALL_TALK}", f"/rooms/{ALL_TALK}/messages"]:
    st, b, _ = david2.get(p)
    check(f"fresh message shows on {p}", st == 200 and token in b, f"status {st}")
st, b, _ = david2.get("/searches?q=" + urllib.parse.quote("verifier fresh"))
check("fresh message shows in search", st == 200 and token in b, f"status {st}")

# 4. A removed membership (raw SQL, no epoch bump) is refused on a warm page.
for p in [f"/rooms/{DESIGNERS}", f"/rooms/{DESIGNERS}/messages"]:
    s1, _, _ = kevin.get(p); s2, _, _ = kevin.get(p)
    assert s1 == s2 == 200
sql(f"delete from memberships where room_id={DESIGNERS} and user_id={KEVIN}")
for p in [f"/rooms/{DESIGNERS}", f"/rooms/{DESIGNERS}/messages"]:
    st, b, h = kevin.get(p)
    check(f"removed membership refused on {p}", st in (302, 404) and "Designers" not in title(b or "") if b else True, f"status {st} location {h.get('Location')}")
st, b, _ = kevin.get("/users/me/sidebar")
check("sidebar after raw membership delete (warm entry, outside write)", st == 200, f"Designers in sidebar: {'Designers' in b} (class B within 60 s)")

# 5. A revoked session (raw SQL delete, no epoch bump) is refused on a warm page; the other session still works.
for p in pages(DESIGNERS):
    david1.get(p); david1.get(p)
tok1 = next(c.value for c in david1.jar if c.name == "session_token")
rows = sql(f"select id from sessions where user_id={DAVID} order by id desc limit 2").split()
# david2 signed in after david1, so david1 is the older of the last two... confirm by deleting and probing
sql(f"delete from sessions where id={rows[1]}")
outs = [(p, david1.get(p)[0]) for p in pages(DESIGNERS)]
check("revoked session refused on every cached page", all(s == 302 for _, s in outs), str(outs))
outs2 = [(p, david2.get(p)[0]) for p in pages(DESIGNERS)]
check("the other session still served", all(s == 200 for _, s in outs2), str(outs2))

# 6. Sign-out through the app, then the old cookie.
st, _, _ = david2.req("DELETE", "/session")
outs3 = [(p, david2.get(p)[0]) for p in pages(DESIGNERS)]
check("signed-out cookie refused", all(s == 302 for _, s in outs3), f"signout {st} {outs3}")

# 7. Deactivated user (raw SQL, no bump) on a warm page.
for p in pages(DESIGNERS):
    jason.get(p); jason.get(p)
sql(f"update users set status=1 where id={JASON}")
outs4 = [(p, jason.get(p)[0]) for p in pages(DESIGNERS)]
# Rails' restore_authentication does not read users.status (User#deactivate deletes the sessions), so a raw status change
# without deleting sessions is let in by Rails too: expected 200 here, the same with and without the cache.
check("raw status-only deactivation behaves as Rails (sessions kept, so signed in)", all(s == 200 for _, s in outs4), str(outs4))

print("SUMMARY", sum(ok for _, ok in results), "/", len(results))
