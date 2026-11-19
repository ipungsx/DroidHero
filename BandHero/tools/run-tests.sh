#!/bin/bash
# Runs the headless test suite against real enchor.us charts, without a Unity licence.
#
# The parsing, gameplay and audio code has no UnityEngine dependency beyond logging, so it
# compiles with Mono and links the same Concentus build the game ships.
set -u
cd "$(dirname "$0")/.."

# Unity ships its own Mono; CI installs the distro package instead. Prefer Unity's
# copy when it is present, and fall back to whatever is on PATH.
U="${UNITY_DATA:-}"
[ -n "$U" ] || { [ -d /workspace/unity/Editor/Data ] && U=/workspace/unity/Editor/Data; }

MCS="${MCS:-${U:+$U/MonoBleedingEdge/bin/mcs}}"
MONO="${MONO:-${U:+$U/MonoBleedingEdge/bin/mono}}"
FAC="${FAC:-${U:+$U/MonoBleedingEdge/lib/mono/4.5/Facades}}"

[ -x "${MCS:-}" ] || MCS=$(command -v mcs || command -v csc || true)
[ -x "${MONO:-}" ] || MONO=$(command -v mono || true)
[ -d "${FAC:-}" ] || FAC=$(find /usr/lib/mono -type d -name Facades 2>/dev/null | head -1)

for tool in MCS MONO; do
  if [ -z "${!tool:-}" ]; then
    echo "FAIL: $tool not found. Install Mono or set $tool." >&2
    exit 1
  fi
done
[ -d "${FAC:-}" ] || { echo "FAIL: netstandard facades not found. Set FAC." >&2; exit 1; }

OUT=/tmp/bhtest-bin
mkdir -p "$OUT"
cp -f Assets/Plugins/Concentus/Concentus.dll "$OUT/"

CONCENTUS=Assets/Plugins/Concentus/Concentus.dll

# Only the engine-independent code: the visual, input and app layers need UnityEngine.
SOURCES=$(find Assets/Scripts -name '*.cs' \
  ! -path '*/UI/*' \
  ! -path '*/Core/BandHeroApp.cs' \
  ! -path '*/Core/LocalAudioPicker.cs' \
  ! -path '*/Gameplay/GameplayController.cs' \
  ! -path '*/Gameplay/HighwayRenderer.cs' \
  ! -path '*/Gameplay/NoteViewPool.cs' \
  ! -path '*/Gameplay/TouchInputController.cs' \
  ! -path '*/Gameplay/GameplayHud.cs' \
  ! -path '*/Audio/AudioDecoder.cs' \
  ! -path '*/Audio/OpusClipFactory.cs' \
  ! -path '*/Core/SongLibrary.cs' \
  ! -path '*/Core/SongEntry.cs' \
  ! -path '*/Core/AppState.cs' \
  ! -path '*/Enchor/*')

"$MCS" -target:exe -out:"$OUT/tests.exe" \
  -langversion:7.2 \
  -r:"$CONCENTUS" \
  -r:System.Core.dll \
  -r:"$FAC/System.Runtime.dll" \
  -r:"$FAC/System.IO.dll" \
  -r:"$FAC/System.Collections.dll" \
  -r:"$FAC/System.Threading.dll" \
  $SOURCES tools/UnityShims.cs tools/HeadlessTests.cs 2>&1 | grep -E "error CS" | head -20

if [ ! -f "$OUT/tests.exe" ]; then
  echo "FAIL: tests did not compile"
  exit 1
fi

"$MONO" "$OUT/tests.exe" "$@"
exit $?
