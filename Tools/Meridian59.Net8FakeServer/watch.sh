#!/bin/bash
# watch.sh <clientlog> <triggerfile> <pattern> <command> [<pattern> <command> ...]
#
# The log-watching half of the fixture's one trigger mechanism. The server
# polls M59_TRIGGER for lines (see "THE TRIGGER" in Program.cs); this watches
# the client's own log and appends a line the moment a pattern appears, so
# "while the loot window is open" or "as soon as the designer asked" is
# written once, here, rather than guessed at as a sleep.
#
#   M59_TRIGGER=/tmp/t ... fake server ... &
#   ./watch.sh /tmp/client.log /tmp/t 'LootPanel] open' 'loot empty' &
#   ... run the client, writing /tmp/client.log ...
#
# Each pattern fires once. The script exits when all of them have.
set -u
LOG=$1; TRIG=$2; shift 2
declare -a PAT CMD
while [ $# -ge 2 ]; do PAT+=("$1"); CMD+=("$2"); shift 2; done
left=${#PAT[@]}
: > "$TRIG"

# tail -F, not -f: the client truncates and rewrites its log on start.
tail -n +1 -F "$LOG" 2>/dev/null | while IFS= read -r line; do
   for i in "${!PAT[@]}"; do
      [ -z "${PAT[$i]}" ] && continue
      case "$line" in
         *"${PAT[$i]}"*)
            echo "${CMD[$i]}" >> "$TRIG"
            echo "watch: \"${PAT[$i]}\" -> ${CMD[$i]}" >&2
            PAT[$i]=""
            left=$((left-1))
            ;;
      esac
   done
   [ "$left" -le 0 ] && break
done
