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

## CI

- `.github/workflows/build-apk.yml` builds the APK; the local container has no Unity licence
  and no `AndroidPlayer` module, so CI is the only build path.
- The `test` job runs in `unityci/editor:ubuntu-6000.0.85f2-base-4.0.0` so it uses the same
  Mono as the player build; set `UNITY_DATA=/opt/unity/Editor/Data` for `run-tests.sh`.
- `containerRegistryImageVersion` must stay at `4`; line 3 fails with "No sdkmanager found"
  on Unity 6.
- The build needs `UNITY_LICENSE`, `UNITY_EMAIL` and `UNITY_PASSWORD` secrets. Without them
  game-ci stops at "Missing Unity License File". The workflow checks for them first and skips
  the build instead, so a fork without a licence still gets green test runs.
- game-ci does not install the `.ulf`; it extracts the serial from `DeveloperData` inside it and
  activates from the command line. That is why a password is mandatory even for Personal, and
  why a Google/SSO login has to set a Unity ID password first.
- `BuildForCi` reads `-customBuildPath`, which game-ci sets to
  `build/Android/BandHero.apk`; the artifact upload expects that exact path.
- Roslyn aborts with an ICU error in this image unless
  `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` is set.
