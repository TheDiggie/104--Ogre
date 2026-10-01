#!/bin/bash
# A scripted pass/fail for the two name cases, from what the client itself
# logs with M59SOUNDLOG=1 (no client changes, no audio device).
#
#   miscase  the fixture names Rat_awr.ogg and AMBCave.ogg in the wrong case
#            (M59_SNDNAME=miscase). Each of the one-shot, the loop and the
#            music MUST be played, and no "no such file" / "no file for" /
#            "unreadable" line may appear. This is the regression guard for
#            the case-sensitive-filesystem bug (238 of 520 names in a real
#            dump differ from the file on disk).
#   missing  the fixture names files that do not exist (M59_SNDNAME=missing).
#            Nothing may play for them, the client must say so, the music
#            already running must not be replaced, and the room's own
#            sounds must have played.
#
#   usage: sound-guard.sh <godot-binary> <client-project-dir> <resource-dir> [port]
#   The resource dir must hold Rat_awr.ogg and AMBCave.ogg; it is COPIED
#   (symlinks), because the server rewrites rsc0000.rsb in whatever it is given.
# Needs: dotnet, xvfb-run. Exit status 0 = both pass.
set -u
GODOT="${1:?godot binary}"; PROJ="${2:?client project dir (MobileClient)}"; SRC="${3:?resource dir}"
PORT="${4:-16777}"
HERE="$(cd "$(dirname "$0")" && pwd)"
# M59_FAKE_SERVER_DLL overrides which build is run (a private copy, say).
SRV="${M59_FAKE_SERVER_DLL:-$HERE/bin/Debug/net8.0/Meridian59.Net8FakeServer.dll}"
[ -f "$SRV" ] || SRV="$HERE/bin/Release/net8.0/Meridian59.Net8FakeServer.dll"
fail=0

run() { # case port
  local tmp; tmp="$(mktemp -d)"
  for f in "$SRC"/*; do b="$(basename "$f")"; [ "$b" = rsc0000.rsb ] || ln -s "$f" "$tmp/$b"; done
  M59_SNDNAME="$1" M59_SND_AFTER=20 setsid dotnet "$SRV" "$2" "$tmp" > "$tmp/server.log" 2>&1 < /dev/null &
  local sp=$!
  for _ in $(seq 1 60); do grep -q "fake server on" "$tmp/server.log" 2>/dev/null && break; sleep 0.5; done
  M59USER=tester M59PASS=tester M59SOUNDLOG=1 timeout 240 xvfb-run -a "$GODOT" --path "$PROJ" SceneShot.tscn -- \
    --out "$tmp/s.png" --res "$tmp" --host 127.0.0.1 --port "$2" --char Tester \
    --press "a1,a2,a3,a4,a5,a6,a7,a8" --wait 30 > "$tmp/client.log" 2>&1
  kill "$sp" 2>/dev/null
  LOG="$tmp/client.log"
}
need() { grep -q "$1" "$LOG" && echo "  ok   $2" || { echo "  FAIL $2"; fail=1; }; }
never() { grep -q "$1" "$LOG" && { echo "  FAIL $2"; fail=1; } || echo "  ok   $2"; }

echo "miscase:"
run miscase "$PORT"
need  '\[M59Sound\] rat_awr.ogg gain .* loop False' "one-shot 'rat_awr.wav' played"
need  '\[M59Sound\] RAT_AWR.ogg gain .* loop True'  "loop 'RAT_AWR.WAV' played"
need  '\[M59Sound\] music ambcave.ogg'              "music 'ambcave.wav' started"
never 'no such file\|no file for\|no music file\|unreadable' "nothing reported missing or unreadable"

echo "missing:"
run missing "$((PORT+1))"
need  '\[M59Sound\] music AMBCave.ogg'              "the room's music started"
need  'no music file for FXGoneMusic.ogg'           "the absent music track was reported"
never 'music FXGoneMusic' "the absent track was not started"
never '\[M59Sound\] FXGone' "the absent one-shot/loop did not play"
need  "stop asked for 'rat_awr.ogg'"                "the real loop's stop still worked afterwards"

[ "$fail" = 0 ] && echo "PASS" || echo "FAIL"
exit "$fail"
