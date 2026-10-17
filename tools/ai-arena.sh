#!/usr/bin/env bash
# Headless AI v AI arena: plays the matches in a config with no visuals and writes CSV/JSON results.
#   tools/ai-arena.sh tools/arena/example.json [output-dir]
# The Unity editor must not have this project open (batch mode needs the project to itself).
set -euo pipefail
cd "$(dirname "$0")/.."
CONFIG="${1:?usage: tools/ai-arena.sh <config.json> [output-dir]}"
OUT="${2:-Logs/arena/$(basename "${CONFIG%.*}")-$(date +%Y%m%d-%H%M%S)}"
UNITY="${UNITY:-/Applications/Unity/Hub/Editor/6000.6.4f1/Unity.app/Contents/MacOS/Unity}"
mkdir -p "$OUT"
echo "Arena: $CONFIG -> $OUT (log: $OUT/unity.log)"
set +e
"$UNITY" -batchmode -nographics -projectPath . -executeMethod OrbitalFoundry.EditorTools.AiArenaCli.Run \
  -arenaConfig "$CONFIG" -arenaOut "$OUT" -logFile "$OUT/unity.log"
STATUS=$?
set -e
grep -h "^\[arena\]" "$OUT/unity.log" | sed 's/^\[arena\] //' || true
exit $STATUS
