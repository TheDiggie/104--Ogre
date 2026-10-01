#!/bin/bash
# Writes the synthetic oggs the M59_* sound switches name into a resource
# folder. The client's test dump holds only Rat_awr.ogg and AMBCave.ogg (both
# 2 s of 440 Hz at -21 dB), so a run that needs a loop that is not the
# one-shot, a second track, a splash or a third loop has to bring its own.
# Each file has its OWN level and pitch so the Master bus tells them apart.
#   usage: make-sounds.sh <dir>        (needs ffmpeg with libvorbis)
set -e
d="${1:?usage: make-sounds.sh <dir>}"
mk() { # name freq seconds volume
  ffmpeg -v error -y -f lavfi -i "sine=frequency=$2:duration=$3:sample_rate=44100" \
    -af "volume=$4" -ac 1 -c:a libvorbis "$d/$1.ogg"
}
mk FXLoop1  220 2  3.0   # the split loop
mk FXLoop2  330 2  1.5   # the second loop
mk FXLoop3  550 2  2.0   # owned / ghost loop
mk FXShot   660 10 4.0   # the long one-shot
mk FXRoom2  440 2  3.0   # room 2's ambient
mk FXMusic2 523 2  3.0   # the second music track
mk FXSplash 880 1  4.0   # the wading splash
