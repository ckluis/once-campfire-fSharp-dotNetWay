#!/usr/bin/env bash
# VM-side: build.sh TAG — publish /var/tmp/dnw-p10/spike-src like the Dockerfile and layer it over campfire-dotnetway:app
set -euo pipefail
TAG=$1; SRC=/var/tmp/dnw-p10/spike-src
docker run --rm -v "$SRC:/src" -v /Users/clank/Desktop/projects/once-campfire-fsharp-thedotnetway/reference:/src/reference:ro -v campfire-fsharp-nuget:/root/.nuget/packages \
  -e DOTNET_NOLOGO=1 -e DOTNET_CLI_TELEMETRY_OPTOUT=1 -w /src campfire-fsharp-toolchain:latest bash -c "
    set -e; rm -rf /src/out
    dotnet publish src/Campfire.App/Campfire.App.fsproj -c Release -r linux-arm64 --self-contained true \
      -p:PublishReadyToRun=true -p:DebugType=None -p:DebugSymbols=false -o /src/out -v quiet -nologo
    chmod -R a+rwX /src/out /src/src"
printf 'FROM campfire-dotnetway:app\nCOPY out/ /opt/campfire/\n' | docker build -q -t campfire-dotnetway:$TAG -f - $SRC
echo BUILT $TAG
