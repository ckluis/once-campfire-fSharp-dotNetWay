#!/usr/bin/env bash
# VM-side: harness clone, loadgen, seed, workspace clones (no worktrees in ~/Desktop/projects).
set -euo pipefail
B=/var/tmp/dnw-p10
mkdir -p $B/ws $B/out
cd $B
rm -rf once-campfire-verification
git clone -q /Users/clank/Desktop/projects/once-campfire-verification once-campfire-verification
cd once-campfire-verification && git checkout -q oncrack-adapter
mkdir -p loadgen/target/release
cp /Users/clank/Desktop/projects/upstream-campfire/tools/loadgen-1.98.1-static/loadgen loadgen/target/release/loadgen
ruby bin/seed --image campfire-reference:app > $B/seed.log 2>&1
chmod -R g+w fixtures/default/db fixtures/default/storage
cd $B/ws
[ -d once-campfire ] || { git clone -q /Users/clank/Desktop/projects/upstream-campfire/once-campfire once-campfire && git -C once-campfire checkout -q ca9b963; }
[ -d once-campfire-fsharp ] || { git clone -q /Users/clank/Desktop/projects/once-campfire-fsharp once-campfire-fsharp && git -C once-campfire-fsharp checkout -q 9954ad0; }
rm -rf once-campfire-dotnetway; git clone -q --no-checkout /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway once-campfire-dotnetway && git -C once-campfire-dotnetway checkout -q ${DNW_COMMIT:-f9f4637} -- . 2>/dev/null || git -C once-campfire-dotnetway checkout -q ${DNW_COMMIT:-f9f4637}
for d in $B/ws/*; do echo "$(basename $d) $(git -C $d rev-parse --short HEAD)"; done
echo "seed $(sha256sum $B/once-campfire-verification/fixtures/default/db/production.sqlite3 | cut -c1-16)"
echo SETUP-DONE
