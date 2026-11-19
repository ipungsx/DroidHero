#!/bin/bash
# Runs the headless test suite against real enchor.us charts, without a Unity licence.
#
# The parsing, gameplay and audio code has no UnityEngine dependency beyond logging, so it
# compiles with Mono and links the same Concentus build the game ships.
set -u
cd "$(dirname "$0")/.."

U=/workspace/unity/Editor/Data
MCS="$U/MonoBleedingEdge/bin/mcs"
MONO="$U/MonoBleedingEdge/bin/mono"
OUT=/tmp/bhtest-bin
mkdir -p "$OUT"
cp -f Assets/Plugins/Concentus/Concentus.dll "$OUT/"

CONCENTUS=Assets/Plugins/Concentus/Concentus.dll
# Facades forward the netstandard contracts Concentus asks for to Mono's mscorlib.
FAC=$U/MonoBleedingEdge/lib/mono/4.5/Facades

# Only the engine-independent code: the visual, input and app layers need UnityEngine.
SOURCES=$(find Assets/Scripts -name '*.cs' \
  ! -path '*/UI/*' \
  ! -path '*/Core/BandHeroApp.cs' \
  ! -path '*/Core/LocalAudioPicker.cs' \
  ! -path '*/Gameplay/GameplayController.cs' \
  ! -path '*/Gameplay/HighwayRenderer.cs' \
  ! -path '*/Gameplay/NoteViewPool.cs' \
  ! -path '*/Gameplay/TouchInputController.cs' \
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
