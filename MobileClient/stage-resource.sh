#!/bin/sh
# Stages an installed client's resource folder into MobileClient/resource
# so it ships inside the APK, and writes the one sidecar per file that
# stops Godot from eating it.
#
#   ./stage-resource.sh /path/to/Meridian-104/resource
#
# WHY the sidecars. Godot IMPORTS .ogg, .wav, .png, .jpg and friends: the
# export then contains its own converted copy under .godot/imported/ plus
# a small `X.ogg.import`, and the ORIGINAL X.ogg is not in the pack at
# all. This was established by exporting a pack and reading its file
# list, not by reasoning about it:
#
#   Storing File: res://.godot/imported/AMBCave.ogg-<md5>.oggvorbisstr
#   Storing File: res://resource/AMBCave.ogg.import
#
# and DirAccess.GetFiles("res://resource") in the exported build then
# lists "AMBCave.ogg.import" and no "AMBCave.ogg". The library reads
# sound and music as .ogg files off the disk (ResourceManager.cs:583 and
# :595), so without this the phone has NO SOUND AND NO MUSIC, and
# nothing anywhere says so.
#
# An `importer="keep"` sidecar - exactly what MobileClient/sky/ has
# carried since the skybox went in, and for the same reason - makes Godot
# pass the file through untouched. The same export then stores
# `res://resource/AMBCave.ogg` itself and no sidecar at all, which is
# what M59Paths.UnpackIfNeeded copies out.
#
# A `.gdignore` in the folder does NOT work as a shortcut: it takes the
# whole folder out of the export, include_filter and all. Tried, measured,
# rejected - the pack came out with no resource folder in it.
set -e

src="$1"
if [ -z "$src" ] || [ ! -d "$src" ]; then
    echo "usage: $0 <path to an installed client's resource folder>" >&2
    exit 2
fi

here=$(cd "$(dirname "$0")" && pwd)
dest="$here/resource"

echo "staging $src -> $dest"
mkdir -p "$dest"
# Subfolders included: the game's resource folder has rooms/, sounds/,
# music/, mails/, strings/, bgfobjects/ and bgftextures/ in it, and
# M59Paths walks the pack recursively now, so anything under here
# reaches the device.
cp -R "$src/." "$dest/"

# Every extension Godot has an importer for. A sidecar on a .roo or a
# .bgf would be harmless but pointless - Godot has no importer for those
# and passes them through already.
kept=0
find "$dest" -type f \( \
       -iname '*.ogg' -o -iname '*.wav' -o -iname '*.mp3' \
    -o -iname '*.png' -o -iname '*.jpg' -o -iname '*.jpeg' \
    -o -iname '*.bmp' -o -iname '*.tga' -o -iname '*.webp' \
    -o -iname '*.svg' -o -iname '*.ttf' -o -iname '*.otf' \
    -o -iname '*.obj' -o -iname '*.gltf' -o -iname '*.glb' \
    -o -iname '*.csv' -o -iname '*.json' \
  \) -print | while read -r f; do
    printf '[remap]\n\nimporter="keep"\n' > "$f.import"
    kept=$((kept + 1))
done

echo "staged $(find "$dest" -type f ! -name '*.import' | wc -l) files"
echo "kept   $(find "$dest" -type f -name '*.import' | wc -l) sidecars"
echo
echo "Now bump application/config/version in project.godot (and version/code"
echo "in export_presets.cfg) so devices holding the old data replace it."
