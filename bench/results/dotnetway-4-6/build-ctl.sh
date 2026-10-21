#!/usr/bin/env bash
# The control image: the base commit 18aad84 (the head of 4.5) built from a context made of its tracked files (git archive) and the
# reference parts the asset build embeds, as parity/bin/candidate build makes it.
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-4-6
refrev=$(git -C reference rev-parse HEAD)
colima ssh -- bash -c "cd $PWD && docker build -t campfire-dotnetway:u46ctl -f target/u46ctx/Dockerfile --build-arg APP_VERSION=parity --build-arg GIT_REVISION=$refrev --build-arg SOURCE_REVISION=18aad84 --label org.opencontainers.image.revision=$(git rev-parse 18aad84) target/u46ctx" > $D/build-ctl.log 2>&1
echo "build-ctl rc=$?" > $D/STATUS-build-ctl
bin/clean-docker > $D/clean-docker-ctl.log 2>&1
