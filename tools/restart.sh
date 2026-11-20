#!/bin/bash
# Closes Valheim, deploys tools/out/VanityWardrobe.dll to the game, relaunches it and enters the first world on the list.
# The game locks the DLL while it runs, so it has to be closed first. Waits are fixed times that suit this machine.
HERE="$(cd "$(dirname "$0")" && pwd)"
V="/d/SteamLibrary/steamapps/common/Valheim"
G="$(cygpath -w "$HERE/gameinput.ps1")"
taskkill //IM valheim.exe //F >/dev/null 2>&1; sleep 6
mkdir -p "$V/BepInEx/plugins/VanityWardrobe"
cp "$HERE/out/VanityWardrobe.dll" "$V/BepInEx/plugins/VanityWardrobe/VanityWardrobe.dll" || { echo "copy failed"; exit 1; }
for l in "$HERE"/../VanityWardrobe/Assets/Translations/*/; do
  n=$(basename "$l"); mkdir -p "$V/BepInEx/config/VanityWardrobe/Translations/$n"
  cp "$l"VanityWardrobe.json "$V/BepInEx/config/VanityWardrobe/Translations/$n/"
done
start "" "steam://rungameid/892970"
sleep 50
powershell -NoProfile -ExecutionPolicy Bypass -File "$G" -Action click -Arg1 620 -Arg2 819   # Start on the Select World screen
sleep 45
echo "in world (check with a screenshot)"
