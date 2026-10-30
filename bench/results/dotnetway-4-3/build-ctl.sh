#!/usr/bin/env bash
# The control image: the base commit 6f69c4b (the head of 4.2) built from a context made of its tracked files, as parity/bin/candidate build
# makes it (the build context is only src, the props and the parts of reference/ the asset build embeds).
cd /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway
D=bench/results/dotnetway-4-3
refrev=$(git -C reference rev-parse HEAD)
colima ssh -- bash -c "cd $PWD && docker build -t campfire-dotnetway:u43ctl -f target/u43ctx/Dockerfile --build-arg APP_VERSION=parity --build-arg GIT_REVISION=$refrev --build-arg SOURCE_REVISION=6f69c4b --label org.opencontainers.image.revision=6f69c4bd63415df1c03612bf4dc2d85fd8c4d930 target/u43ctx" > $D/build-ctl.log 2>&1
echo "build-ctl rc=$?" > $D/STATUS-build-ctl
bin/clean-docker > $D/clean-docker-ctl.log 2>&1
