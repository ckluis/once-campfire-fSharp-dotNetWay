#!/usr/bin/env python3
"""Picks a sample of the cases and the Rust views crate's answers to them, as lines of
{"case": ..., "rust": ...} (after the `shared` line) for tests/Campfire.Views.Tests/oracle.jsonl.
A few of each operation, more of the small ones; the every-code-point cases are sampled evenly. On top of that every operation
gets a hostile quota (HOSTILE_QUOTA): cases whose inputs hold one of the strings that break markup or escaping when a template writes
it unescaped (a script tag, quotes, an apostrophe, an ampersand reference, a lone angle bracket, the slot marker U+E000 ...), so the
frozen sample covers every template with hostile text (plans/design-views.md V11, risk 15). A table of cases per operation, hostile
ones counted, goes to stderr.

  golden.py CASES RUST_OUTPUT
"""
import collections
import json
import sys

cases_path, rust_path = sys.argv[1], sys.argv[2]
QUOTA = {
    "layouts/application_wrapper": 6, "welcome/show": 6, "recorded/page": 12, "helpers/users": 40, "helpers/rooms": 20,
    "helpers/application": 24, "pwa/_browser_settings": 24, "pwa/_install_instructions": 24, "pwa/_system_settings": 24,
    "users/_mention": 16, "fragment_cache/keys": 24, "messages/epoch_ms": 8, "messages/iso8601": 4, "messages/json_time": 4,
    "rooms/show": 6, "users/sidebars/show": 4, "searches/index": 4, "messages/index": 4, "messages/_message": 16,
    "rooms/opens/new": 4, "rooms/opens/edit": 4, "rooms/closeds/new": 4, "rooms/closeds/edit": 4, "rooms/directs/new": 4, "rooms/directs/edit": 4,
    "messages/_actions": 8, "messages/show": 6, "messages/edit": 8, "rooms/refreshes/show": 6, "messages/message_cached": 4,
    "helpers/application:to_lowercase": 100,
    # unit 4.3: pages carry the whole layout (~50 KB), so a couple of each, a framed one among them
    "accounts/edit": 2, "accounts/bots/index": 2, "accounts/bots/new": 2, "accounts/bots/edit": 3, "accounts/custom_styles/edit": 2,
    "first_runs/show": 2, "sessions/new": 2, "sessions/incompatible_browser": 2, "sessions/transfers/show": 2, "users/new": 2,
    "users/show": 4, "users/profiles/show": 3, "users/push_subscriptions/index": 2, "users/_ban_button": 6, "users/avatars/show": 6,
    "users/profiles/_membership": 6, "users/profiles/_transfer": 6, "accounts/users/_user": 12, "pwa/manifest": 12,
}
DEFAULT = 8
# A page's answer can be hundreds of kilobytes (a room with 40 messages): the sample keeps one such answer per kind of case,
# which is what shows recorded fragments, and otherwise takes the smaller ones.
BIG = 60_000
# an account page of a crowd of hundreds is nearly a megabyte (twice, with its recorded text); bin/views-differential covers it live
HUGE = 400_000
EVEN = ("capitalize_each", "initials_each")

# The strings of generate.py's HOSTILE list that a template can get wrong (the rest are about Unicode tables, whitespace and length).
MARKERS = [
    "<script>alert(1)</script>", '"quoted"', "it's", "a<b>c", "&amp; already", "&lt;", "a -> b", "back\\slash", "o'Brien", "\ue000",
    "a { color: red }\n</style>", "</textarea>", "</textarea><b>",
]
HOSTILE_QUOTA = 6          # per operation, in addition to the base quota
HOSTILE_QUOTA_PAGES = 3    # operations whose answers run to tens of kilobytes (whole pages)
HOSTILE_MAX = 120_000      # a hostile answer bigger than this is left out (the base sample keeps its one big page)

def hostile(case):
    """Does the case's input hold a marker string?"""
    text = json.dumps(case, ensure_ascii=False)
    return any(json.dumps(marker, ensure_ascii=False)[1:-1] in text for marker in MARKERS)


def special(case):
    """A weaker test for operations whose generated inputs rarely drew a marker: any string holding a markup or escaping character."""
    def walk(value):
        if isinstance(value, str):
            return any(c in value for c in "<>\"'&\ue000\\")
        if isinstance(value, dict):
            return any(walk(v) for v in value.values())
        if isinstance(value, list):
            return any(walk(v) for v in value)
        return False
    return walk(case.get("args", {}))


answers = {}
for line in open(rust_path):
    answer = json.loads(line)
    answers[answer["id"]] = answer
rows = []
for line in open(cases_path):
    case = json.loads(line)
    if "shared" in case:
        print(line.strip())
        continue
    rows.append((case, answers[case["id"]]))

taken = collections.Counter()
kept = collections.Counter()
total = collections.Counter()
hostile_kept = collections.Counter()
hostile_total = collections.Counter()
strong = collections.Counter()
for case, _ in rows:
    strong[case["op"] + (":to_lowercase" if case.get("args", {}).get("name") == "to_lowercase" else "")] += hostile(case)
even = [(c, r) for c, r in rows if c.get("args", {}).get("name") in EVEN]
step = max(1, len(even) // 10)
chosen = {c["id"] for c, _ in even[::step]}
for case, answer in rows:
    op = case["op"]
    if case.get("args", {}).get("name") == "to_lowercase":
        op += ":to_lowercase"
    if case.get("args", {}).get("name") in EVEN:
        keep = case["id"] in chosen
    else:
        size = len(answer.get("out", ""))
        if size > HUGE:
            keep = False
        elif size > BIG:
            keep = taken[op + " (big)"] < 1 and taken[op] < QUOTA.get(op, DEFAULT)
            taken[op + " (big)"] += keep
        else:
            keep = taken[op] < QUOTA.get(op, DEFAULT)
        taken[op] += keep
    # an operation none of whose cases drew a marker string takes cases with any markup character in an argument instead
    is_hostile = hostile(case) or (strong[op] == 0 and special(case))
    total[op] += 1
    hostile_total[op] += is_hostile
    if not keep and is_hostile and len(answer.get("out", "")) <= HOSTILE_MAX:
        quota = HOSTILE_QUOTA_PAGES if len(answer.get("out", "")) > 20_000 else HOSTILE_QUOTA
        keep = hostile_kept[op] < quota
    if keep:
        hostile_kept[op] += is_hostile
        kept[op] += 1
        # the replay compares `out` and `fragments`; `text` (a recorded page without its fragments) is `out` again for most pages
        answer = {key: value for key, value in answer.items() if key != "text"}
        print(json.dumps({"case": case, "rust": answer}, ensure_ascii=False))

print(f"{'operation':50} {'cases':>6} {'kept':>5} {'hostile cases':>14} {'hostile kept':>13}", file=sys.stderr)
for op in sorted(total):
    print(f"{op:50} {total[op]:>6} {kept[op]:>5} {hostile_total[op]:>14} {hostile_kept[op]:>13}", file=sys.stderr)
print(f"{'total':50} {sum(total.values()):>6} {sum(kept.values()):>5} {sum(hostile_total.values()):>14} {sum(hostile_kept.values()):>13}", file=sys.stderr)
