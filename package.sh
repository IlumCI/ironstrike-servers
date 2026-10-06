#!/usr/bin/env bash
# Package IronstrikeServers for distribution.
#
# Produces dist/IronstrikeServers-<version>.zip, laid out so both install paths work:
#   Thunderstore / r2modman / Gale -> manifest.json + icon.png + README.md at the zip root
#   manual                         -> drag BepInEx/ over the game folder
#
# BepInEx already defines the mods folder (BepInEx/plugins), so there is nothing to gain from
# inventing our own. Stdlib only: this has to run on a bare CI runner.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
CSPROJ="$HERE/IronstrikeServers/IronstrikeServers.csproj"
VER="$(grep -oP '<Version>\K[^<]+' "$CSPROJ")"
DLL="$HERE/IronstrikeServers/bin/Release/net6.0/IronstrikeServers.dll"
OUT="$HERE/dist"; STAGE="$OUT/stage"

[[ -f "$DLL" ]] || { echo "ERROR: build first (./build.sh)" >&2; exit 1; }

# The version lives in the csproj; Plugin.cs must agree or the in-game log lies about it.
SRCVER="$(grep -oP 'Version\s*=\s*"\K[^"]+' "$HERE/IronstrikeServers/Plugin.cs" | head -1)"
if [[ "$SRCVER" != "$VER" ]]; then
  echo "ERROR: version drift -- csproj=$VER but Plugin.cs Id.Version=$SRCVER" >&2
  exit 1
fi

DESC="Community server browser and host tools for modded IRONSTRIKE. Modded servers live in their own lobby: invisible and unjoinable without the mod, with mod-set matching, passwords, kick/ban and filters. Public matchmaking is locked while loaded."

rm -rf "$STAGE"; mkdir -p "$STAGE/BepInEx/plugins"
cp "$DLL" "$STAGE/BepInEx/plugins/"
cp "$HERE/README.md" "$STAGE/README.md"
cp "$HERE/icon.png"  "$STAGE/icon.png"

python3 - "$STAGE" "$VER" "$DESC" <<'PYEOF'
import json, os, re, sys
stage, ver, desc = sys.argv[1], sys.argv[2], sys.argv[3]
m = {
    "name": "IronstrikeServers",
    "version_number": ver,
    "website_url": "",
    "description": desc,
    "dependencies": ["BepInEx-BepInExPack_IL2CPP-6.0.755"],
}
errs = []
if not re.fullmatch(r"[A-Za-z0-9_]+", m["name"]):
    errs.append("name must be alphanumeric/underscore only")
if not re.fullmatch(r"\d+\.\d+\.\d+", m["version_number"]):
    errs.append("version_number must be major.minor.patch")
if len(m["description"]) > 250:
    errs.append("description is %d chars, max 250" % len(m["description"]))
for d in m["dependencies"]:
    if not re.fullmatch(r"[A-Za-z0-9_]+-[A-Za-z0-9_]+-\d+\.\d+\.\d+", d):
        errs.append("bad dependency string: " + d)
if errs:
    print("manifest validation FAILED:")
    for e in errs:
        print("  -", e)
    sys.exit(1)
open(os.path.join(stage, "manifest.json"), "w").write(json.dumps(m, indent=2) + "\n")
print("manifest.json ok (description %d/250 chars)" % len(m["description"]))
PYEOF

# Icon must be a 256x256 PNG. Read the IHDR by hand so this needs no imaging library.
python3 - "$STAGE/icon.png" <<'PYEOF'
import struct, sys
d = open(sys.argv[1], "rb").read(24)
if d[:8] != b"\x89PNG\r\n\x1a\n" or d[12:16] != b"IHDR":
    sys.exit("icon.png is not a PNG")
w, h = struct.unpack(">II", d[16:24])
if (w, h) != (256, 256):
    sys.exit("icon must be 256x256, got %dx%d" % (w, h))
print("icon.png ok (PNG %dx%d)" % (w, h))
PYEOF

mkdir -p "$OUT"
ZIP="$OUT/IronstrikeServers-$VER.zip"
rm -f "$ZIP"
( cd "$STAGE" && python3 - "$ZIP" <<'PYEOF'
import os, sys, zipfile
z = zipfile.ZipFile(sys.argv[1], "w", zipfile.ZIP_DEFLATED)
for root, _, files in os.walk("."):
    for f in sorted(files):
        p = os.path.join(root, f)
        z.write(p, os.path.relpath(p, "."))
z.close()
PYEOF
)
rm -rf "$STAGE"

echo
echo "==> $ZIP"
python3 - "$ZIP" <<'PYEOF'
import sys, zipfile
z = zipfile.ZipFile(sys.argv[1])
for i in z.infolist():
    print("    %8d  %s" % (i.file_size, i.filename))
print("    bad zip!" if z.testzip() else "    zip integrity OK")
PYEOF
echo
sha256sum "$ZIP" | sed 's/^/    /'
