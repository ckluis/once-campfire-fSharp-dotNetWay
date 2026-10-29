#!/usr/bin/env bash
# Inside colima: tmpfs harness for the six-ways bench (as upstream-campfire/scripts/setup-r32.sh, workspace = clean worktrees in ~/cfsix-ws).
set -euo pipefail
U=/Users/clank/Desktop/projects/upstream-campfire
sudo mkdir -p /mnt/cfv
mountpoint -q /mnt/cfv || sudo mount -t tmpfs -o size=3g,mode=0775 tmpfs /mnt/cfv
sudo chown "$(id -u)":1000 /mnt/cfv
cd /mnt/cfv
rm -rf once-campfire-verification ws
git clone -q /Users/clank/Desktop/projects/once-campfire-verification once-campfire-verification
cd once-campfire-verification
git checkout -q oncrack-adapter
mkdir -p loadgen/target/release
cp "$U/tools/loadgen-1.98.1-static/loadgen" loadgen/target/release/loadgen
ruby bin/seed --image campfire-reference:app > /dev/null 2>&1
chmod -R g+w fixtures/default/db fixtures/default/storage
mkdir -p /mnt/cfv/ws /mnt/cfv/out
for n in once-campfire once-campfire-c once-campfire-rust once-campfire-fsharp once-campfire-oncrack once-campfire-dotnetway; do ln -sfn /Users/clank/cfsix-ws/$n /mnt/cfv/ws/$n; done
echo "harness $(git log --oneline -1)"
echo "loadgen $(sha256sum loadgen/target/release/loadgen | cut -c1-16)"
echo "seed    $(sha256sum fixtures/default/db/production.sqlite3 | cut -c1-16)"
for d in /mnt/cfv/ws/*; do echo "$(basename "$d") $(git -C "$d" rev-parse --short HEAD) dirty=$(git -C "$d" status --porcelain --untracked-files=no | wc -l)"; done
