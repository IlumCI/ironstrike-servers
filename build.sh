#!/usr/bin/env bash
# Build IronstrikeServers and optionally deploy to the game's BepInEx/plugins.
#
#   ./build.sh                        # build against locally generated interop
#   ./build.sh /mnt/ironstrike        # build + deploy to an SMB-mounted game dir
#
# Prefer the game's own BepInEx/interop when it exists: those are the assemblies
# the runtime actually generated, so they are guaranteed to match the live game.
set -euo pipefail

export PATH="$PATH:$HOME/.dotnet/tools"
export DOTNET_ROLL_FORWARD=LatestMajor

HERE="$(cd "$(dirname "$0")" && pwd)"
GAME_DIR="${1:-}"
LOCAL_INTEROP="${IRONSTRIKE_INTEROP:-$HERE/refs}"

if [[ -n "$GAME_DIR" && -f "$GAME_DIR/BepInEx/interop/GameAssembly.dll" ]]; then
  INTEROP="$GAME_DIR/BepInEx/interop"
  echo "==> interop: game-generated ($INTEROP)"
else
  INTEROP="$LOCAL_INTEROP"
  echo "==> refs: committed refs/ ($INTEROP)"
fi

if [[ ! -f "$INTEROP/GameAssembly.dll" ]]; then
  echo "ERROR: no interop assemblies at $INTEROP" >&2
  echo "       refs/ is committed; if it is missing, run ./regen-interop.sh." >&2
  exit 1
fi

dotnet build "$HERE/IronstrikeServers/IronstrikeServers.csproj" \
  -c Release -p:InteropDir="$INTEROP" --nologo

DLL="$HERE/IronstrikeServers/bin/Release/net6.0/IronstrikeServers.dll"

if [[ -n "$GAME_DIR" ]]; then
  DEST="$GAME_DIR/BepInEx/plugins"
  mkdir -p "$DEST"
  cp -v "$DLL" "$DEST/"
  echo "==> deployed. Tail the log with:"
  echo "    tail -f '$GAME_DIR/BepInEx/LogOutput.log'"
else
  echo "==> built: $DLL"
fi
