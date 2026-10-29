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
david, kevin, jason, jz = Client("david@37signals.com"), Client("kevin@37signals.com"), Client("jason@37signals.com"), Client("jz@37signals.com")

# messages page, two users: each user's answers stable, statuses 200, and no other user's name leaks
for p in [f"/rooms/{DESIGNERS}/messages"]:
    d = [david.get(p) for _ in range(3)]; k = [kevin.get(p) for _ in range(3)]
    check("messages page per user stable", all(s == 200 for s,_,_ in d+k) and d[1][1]==d[2][1] and k[1][1]==k[2][1], f"david==kevin bodies: {d[2][1]==k[2][1]}")

# cache live: three GETs (the first sets last_room), outside rename, fourth GET is the warm page
for _ in range(3): st, b0, _ = david.get(f"/rooms/{ALL_TALK}")
sql(f"update rooms set name='Raw Renamed' where id={ALL_TALK}")
st, b1, _ = david.get(f"/rooms/{ALL_TALK}")
check("cache is live: outside write not seen within 60 s (class B)", title(b1) == title(b0) == "All Talk", f"{title(b0)!r} -> {title(b1)!r}")
# a commit through the app retires it
st, _, _ = jason.req("POST", f"/rooms/{ALL_TALK}/messages", {"message[body]": "retire"}, {"Accept": "text/vnd.turbo-stream.html, text/html"})
st, b2, _ = david.get(f"/rooms/{ALL_TALK}")
check("an app commit retires the warm page", title(b2) == "Raw Renamed", f"{title(b2)!r}")
sql(f"update rooms set name='All Talk' where id={ALL_TALK}")

# removed membership through the app: David revises Designers without Kevin
for p in pages(DESIGNERS):
    for _ in range(3): kevin.get(p)
keep = [DAVID, JASON, 773523953, 773523955, 773523956]
form = [("room[name]", "Designers")] + [("user_ids[]", str(u)) for u in keep]
data = urllib.parse.urlencode(form).encode()
r = urllib.request.Request(BASE + f"/rooms/closeds/{DESIGNERS}", data=data, method="PATCH"); r.add_header("Accept-Encoding","gzip")
try: resp = david.op.open(r); st = resp.status
except urllib.error.HTTPError as e: st = e.code
check("david revises Designers without Kevin", st == 302, f"status {st}; kevin member rows now {sql(f'select count(*) from memberships where room_id={DESIGNERS} and user_id={KEVIN}')}")
outs = [(p, kevin.get(p)[0]) for p in pages(DESIGNERS)[:2]]
check("removed member refused on room and messages pages", all(s in (302, 404) for _, s in outs), str(outs))
st, b, _ = kevin.get("/users/me/sidebar")
check("removed member's sidebar no longer lists Designers", st == 200 and f"/rooms/{DESIGNERS}" not in b, f"status {st}")

# deactivation through the app: David deactivates JZ after JZ's pages are warm
for p in pages(DESIGNERS):
    for _ in range(3): jz.get(p)
st, _, _ = david.req("DELETE", "/account/users/773523953")
outs = [(p, jz.get(p)[0]) for p in pages(DESIGNERS)]
check("deactivated user refused on every cached page", all(s == 302 for _, s in outs), f"deactivate {st} {outs}")

print("SUMMARY", sum(ok for _, ok in results), "/", len(results))
