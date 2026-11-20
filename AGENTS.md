# Band Hero — repository notes

Unity 6 (6000.0.85f2) Android rhythm game in `BandHero/`. Charts are downloaded from
enchor.us; audio comes from the player's device to avoid shipping copyrighted recordings.

## Verifying changes

The quickest check needs no Unity licence:

```bash
BandHero/tools/run-tests.sh /tmp/sample.sng        # headless suite, needs a chart file
./compile_check.sh                                  # runtime scripts vs real UnityEngine
./compile_check_editor.sh                           # Editor scripts vs UnityEditor
```

## Building an APK locally

`/workspace/unity/Editor/Unity` can build the APK in batch mode once the Android module and
toolchain are in place. `BuildAndroid.ApplyExternalTools` reads the paths from the
environment, so no editor preferences are needed:

```bash
export JAVA_HOME=/workspace/unity/jdk
export ANDROID_SDK_ROOT=/workspace/unity/sdk
export ANDROID_NDK_ROOT=/workspace/unity/sdk/ndk/27.2.12479018
/workspace/unity/Editor/Unity -batchmode -nographics -quit \
  -projectPath "$PWD/BandHero" -buildTarget Android \
  -executeMethod BandHero.EditorTools.BuildAndroid.BuildFromCommandLine \
  -logFile "$PWD/BandHero/build.log"
```

The toolchain is not installed by default. Getting it in place took these steps, and each one
failed loudly before the next was satisfied:

- The Android module unpacks from the player package's `Payload` (gzip'd `odc` cpio, 76-byte
  header) into `Editor/Data/PlaybackEngines/AndroidPlayer`. Without it the build reports
  `Android build failed` before any Gradle work.
- `cmdline-tools` must sit in the `cmdline-tools/latest/` layout, or Unity reports
  `sdkmanager does not exist`. `platform-tools` and `build-tools` must also be installed.
- The NDK must be **r27** (`ndk;27.2.12479018`). r23 is rejected with a bare
  `Android NDK not found`, which is misleading because the directory is valid.
- `cmake;3.22.1` must be installed in the SDK, or the build stops at `Missing CMake 3.22.1`.
- Delete `.utmp` between builds when switching NDK or CMake. A stale
  `hash_key.txt` reference fails the CMake configure step.
- Gradle writes into the project as root; `sudo rm -rf .utmp Library/Bee Library/Android` clears
  a cache the current user cannot remove.

### When the licence is gone

The editor licence lives in the container's home directory, so a container restart can lose it.
Then every build stops at `No valid Unity Editor license found` and the log shows
`Processed 0 license files`. You do not need to re-activate Unity to ship an APK: the previous
successful build leaves a complete Gradle project in
`BandHero/Library/Bee/Android/Prj/IL2CPP/Gradle`, with `libil2cpp.so`, `libunity.so` and the
Unity data already staged. Bump `versionName`/`versionCode` in `launcher/build.gradle` and
`gradle.properties`, then run Gradle from that directory:

```bash
cd BandHero/Library/Bee/Android/Prj/IL2CPP/Gradle
export JAVA_HOME=/workspace/unity/jdk ANDROID_HOME=/workspace/unity/sdk
java -Xmx4096M -cp "/workspace/unity/Editor/Data/PlaybackEngines/AndroidPlayer/Tools/gradle/lib/*" \
  org.gradle.launcher.GradleMain assembleRelease --no-daemon
```

Do not pass `--offline`: the Android Gradle plugin is resolved through the plugin portal, so
offline fails at configuration. The result lands in `launcher/build/outputs/apk/release/`.
That path re-signs and re-packages native code that is already there. To ship a **C# change**
without a licence, rebuild the IL2CPP stages by hand from the recorded Tundra DAG
(`BandHero/Library/Bee/Player*.dag.json`), which stores the exact command line of every node:

1. Compile `Assets/Scripts` with the saved Roslyn response file
   (`Library/Bee/artifacts/*/Assembly-CSharp.rsp`) into
   `Library/Bee/PlayerScriptAssemblies/Assembly-CSharp.dll`.
2. Re-run the `Code-stripping Assemblies with UnityLinker` node into
   `Library/Bee/artifacts/Android/ManagedStripped/`. Skipping this leaves the stripped assembly
   referencing `netstandard`, and `il2cpp --convert-to-cpp` then dies with exit 255 inside
   `DataModelBuilder.BuildCecilSourcedData`.
3. Re-run the `Converting Assemblies with IL2CPP` node, which rewrites
   `il2cppOutput/cpp/*` and `il2cppOutput/data/Metadata/global-metadata.dat`.
4. Re-run each `Compiling Cpp`/`Compiling C` node whose source changed, then the two
   `Linking il2cpp.a` and two `Linking libil2cpp.so` nodes, then the `NdkObjCopy` chain.
5. Copy the stripped `libil2cpp.so` into
   `Library/Bee/Android/Prj/IL2CPP/Gradle/unityLibrary/src/main/jniLibs/<abi>/` and the new
   `global-metadata.dat` into `.../assets/bin/Data/Managed/Metadata/`, then run Gradle.

Each node's `Action` field is the literal shell command; `CopyFiles` nodes have no action and
need a plain `cp`. Delete nothing in `Library/Bee` first — the DAG names inputs by hash.

`libicu` must be installed (`sudo apt-get install -y libicu76`). Without it the licensing
client aborts with `Couldn't find a valid ICU package` and the build hangs until it times out.

The debug keystore also lives in the home directory, so a restarted container signs with a new
key. Android refuses to upgrade an app across signing keys, so anyone who installed an earlier
build must uninstall it before installing the new APK.

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

- `.github/workflows/build-apk.yml` builds the APK; it is the reproducible path, since the
  local toolchain above has to be assembled by hand.
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
