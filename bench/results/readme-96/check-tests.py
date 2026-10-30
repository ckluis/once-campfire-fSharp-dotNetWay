#!/usr/bin/env python3
"""Throwaway check for unit 9.6: every test the README's "Known differences" section names exists under tests/.

Three kinds of reference are pulled from the section:
  - an ID such as FR1, LS5, HS12, SR-J1 or `OC1-OC9` (a range is expanded): it must appear as a whole word in a test name or a comment in tests/;
  - a backticked module such as `HttpLogTests`: a `module` or file of that name must exist;
  - a quoted fact name that follows a module ("..." right after `XTests`): it must be a case-insensitive substring of some test name.
Usage: python3 check-tests.py README.md tests
"""
import re, sys, pathlib

readme = pathlib.Path(sys.argv[1]).read_text()
tests_dir = pathlib.Path(sys.argv[2])
start = readme.index("## Known differences")
end = readme.index("## How this was built") if "## How this was built" in readme else readme.index("## License")
sec = re.sub(r"\s+", " ", readme[start:end])

src = {p: p.read_text(errors="replace") for p in tests_dir.rglob("*.fs")}
alltext = "\n".join(src.values())
names = re.findall(r"let\s+(?:private\s+)?``([^`]+)``", alltext)
names += re.findall(r'\[<Fact[^\]]*DisplayName\s*=\s*"([^"]+)"', alltext)
lname = [n.lower() for n in names]

missing = []
ids = set()
for m in re.finditer(r"\b([A-Z]{1,4}(?:-[A-Z]\d?)?)(\d+)([a-z]?)\s*-\s*(?:([A-Z]{1,4}(?:-[A-Z])?)?)(\d+)\b", sec):
    pre, a, suf, pre2, b = m.group(1), int(m.group(2)), m.group(3), m.group(4), int(m.group(5))
    if pre2 in (None, "", pre) and a < b < 40:
        for i in range(a, b + 1):
            ids.add(f"{pre}{i}")
for m in re.finditer(r"\b([A-Z]{1,4}(?:-J)?\d{1,2}[a-z]?)\b", sec):
    ids.add(m.group(1))
# tokens that look like IDs but are not tests
not_tests = {"A1", "B1", "C1", "S3", "R1", "R3b"}  # placeholders kept empty by default; see below
idlist = sorted(ids)
for i in idlist:
    if not re.search(r"(?<![A-Za-z0-9])" + re.escape(i) + r"(?![A-Za-z0-9])", alltext):
        missing.append(("ID", i))

mods = sorted(set(re.findall(r"`([A-Z][A-Za-z0-9]*Tests)`", sec)))
for mname in mods:
    ok = any(re.search(r"\bmodule\s+(?:[\w.]+\.)?" + mname + r"\b", t) or p.stem == mname for p, t in src.items())
    if not ok:
        missing.append(("module", mname))

facts = re.findall(r'`[A-Z][A-Za-z0-9]*Tests`\s*\(?"([^"]+)"', sec)
facts += re.findall(r'(?:fact|facts)\s+"([^"]+)"', sec)
facts = sorted(set(facts))
for f in facts:
    if f.lower() not in "\n".join(lname):
        missing.append(("fact", f))

for b in sorted(set(re.findall(r"`(bin/[a-z-]+)", sec))):
    if not pathlib.Path(b).exists():
        missing.append(("tool", b))
print(f"ids checked: {len(idlist)}; modules: {len(mods)}; quoted facts: {len(facts)}")
for k, v in missing:
    print("MISSING", k, v)
print("0 missing" if not missing else f"{len(missing)} missing")
sys.exit(1 if missing else 0)
