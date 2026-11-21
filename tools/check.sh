#!/bin/bash
# Compile-only check of the mod with Roslyn, against the game's own DLLs. Writes tools/out/VanityWardrobe.dll and deploys nothing.
# (`dotnet build` cannot build this old-style project: it does not resolve JotunnLib. The real build is Visual Studio's.)
# Usage: tools/check.sh [max lines of errors to show]
HERE="$(cd "$(dirname "$0")" && pwd)"
V="D:/SteamLibrary/steamapps/common/Valheim"
M="$V/valheim_Data/Managed"
P="$M/publicized_assemblies"
R=()
for f in "$M"/*.dll; do
  b=$(basename "$f")
  case "$b" in assembly_valheim.dll|mscorlib.dll|System*.dll|Mono.*.dll) continue;; esac
  [ -f "$P/$b" ] && { R+=("-r:$P/$b"); continue; }   # (the game's own copy is left out, the publicized one is used)
  R+=("-r:$f")
done
R+=("-r:$P/assembly_valheim_publicized.dll")
for f in "$V"/BepInEx/core/{BepInEx,0Harmony}.dll "$V/BepInEx/plugins/Jotunn.dll"; do R+=("-r:$f"); done
FW="C:/Program Files (x86)/Reference Assemblies/Microsoft/Framework/.NETFramework/v4.8.1"
for n in mscorlib System System.Core System.Xml System.Xml.Linq System.Data System.IO.Compression System.Numerics System.Drawing; do R+=("-r:$FW/$n.dll"); done
mkdir -p "$HERE/out"
cd "$HERE/../VanityWardrobe"
FILES=$(find . ../Shared -name "*.cs" -not -path "./obj/*" -not -path "./bin/*" | sort -u)
dotnet "C:/Program Files/dotnet/sdk/11.0.100/Roslyn/bincore/csc.dll" -nologo -target:library -langversion:12 -nostdlib -noconfig -nullable:annotations \
  -out:"$HERE/out/VanityWardrobe.dll" "${R[@]}" -resource:Assets/Translations/English/VanityWardrobe.json,translations/English.json -resource:Assets/hanger.png,icons/hanger.png $FILES 2>&1 | grep -E "error" | head -${1:-30}
echo "check done (no error lines above means it compiled)"
