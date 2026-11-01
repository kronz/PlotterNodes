#!/usr/bin/env bash
# Compile check for PlotterNodes.Canvas in the Linux sandbox (see README.md).
# Usage: check.sh <root of extracted dotnet-sdk-8.0 / glslang-tools .deb files>
set -euo pipefail
TOOLS_ROOT="${1:-/tmp/tools/root}"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
SRC="$REPO/src/PlotterNodes.Canvas"
WORK="$(mktemp -d /tmp/canvascheck.XXXXXX)"

export DOTNET_ROOT="$TOOLS_ROOT/usr/lib/dotnet"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

echo "== C# against API stubs"
cp -r "$HERE/." "$WORK/"
"$DOTNET_ROOT/dotnet" build "$WORK/CanvasCompileCheck.csproj" -c Release -nologo -v:minimal \
    -p:CanvasSourceDir="$SRC" -p:RestoreIgnoreFailedSources=true

echo "== HLSL through glslang's HLSL front end"
GLSLANG="$TOOLS_ROOT/usr/bin/glslangValidator"
SH="$WORK/shaders"
mkdir -p "$SH"
cp "$SRC"/Shaders/Helix/*.hlsl "$SRC"/Shaders/*.hlsl "$SRC"/Shaders/*.hlsli "$SH/"
status=0
for pair in "vsCanvasMesh.hlsl vert" "psCanvasMesh.hlsl frag" "psGround.hlsl frag" "vsSkyDepth.hlsl vert" "psSkyDepth.hlsl frag" "vsCanvasPointLine.hlsl vert" "psCanvasLine.hlsl frag"; do
    set -- $pair
    if out=$("$GLSLANG" -D -V -e main -S "$2" -I"$SH" "$SH/$1" -o "$SH/$1.spv" 2>&1); then
        echo "ok   $1"
    else
        echo "FAIL $1"; echo "$out" | grep -v "struct-member initializers ignored"; status=1
    fi
done
rm -rf "$WORK"
exit $status
