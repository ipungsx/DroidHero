# Band Hero — repository notes

Unity 6 (6000.0.85f2) Android rhythm game in `BandHero/`. Charts are downloaded from
enchor.us; audio comes from the player's device to avoid shipping copyrighted recordings.

## Verifying changes

The Unity editor in this environment has **no licence and no AndroidPlayer module**, so the
editor cannot open the project or build an APK. Use the licence-free harness instead:

```bash
BandHero/tools/run-tests.sh /tmp/sample.sng        # headless suite, needs a chart file
./compile_check.sh                                  # runtime scripts vs real UnityEngine
./compile_check_editor.sh                           # Editor scripts vs UnityEditor
```

`run-tests.sh` compiles with Mono `mcs` from `/workspace/unity/Editor/Data/MonoBleedingEdge/bin`.
Anything touching `UnityEngine` must be added to its exclusion list — a new UI/input file will
otherwise break the harness with `CS0246 MonoBehaviour`.

## Gotchas

- `ProjectSettings/ProjectVersion.txt` must carry the real revision (`b028303d8e7d`), not zeros.
- Charts embed Ogg Opus, which Unity cannot decode; `Assets/Plugins/Concentus/Concentus.dll`
  plus the streaming clip factory handle it. A full song is ~100 MB of float samples, so the
  streaming path is not optional.
- Unity cannot read `content://` URIs, so the Java picker copies the chosen file into app
  storage before returning a path.
- `GameplayHud` owns the back button during play; `BandHeroApp.Update` must not also handle it
  or pause toggles twice.
