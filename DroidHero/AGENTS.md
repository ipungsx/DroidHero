
## Unity 6 Android build notes

Four things break an Android build on Unity 6 that older projects take for granted:

- `com.unity.modules.unitywebrequestaudio` must be listed in `Packages/manifest.json`.
  Without it `UnityWebRequestMultimedia` and `DownloadHandlerAudioClip` do not resolve,
  which breaks `AudioDecoder`.
- Android resources in `Assets/Plugins/Android/res` are rejected outright. Use an AAR, or
  set the label and icon through `PlayerSettings` as `BuildAndroid.ApplyIcon` does.
- `UnityPlayerActivity` is gone. A custom activity extends `UnityPlayerGameActivity`, and
  its manifest entry needs `android:theme="@style/BaseUnityGameActivityTheme"` or the app
  crashes on launch.
- There is no `.ulf` file. Activate with `Unity.Licensing.Client --activate-all
  --include-personal`, which writes `UnityEntitlementLicense.xml`.

Build in the `unityci/editor:ubuntu-6000.0.85f2-android-4.0.0` image; the Android module
is not installable on a plain Linux editor.

## Rebuilding IL2CPP without the Editor

When the native side is relinked by hand from the Bee DAG, the pipeline has five stages and
each one silently keeps the previous file if you skip it:

1. strip the assemblies with `UnityLinker` into
   `Library/Bee/artifacts/Android/ManagedStripped/` — its input is
   `Library/Bee/PlayerScriptAssemblies/Assembly-CSharp.dll`, so the freshly compiled DLL has
   to be copied there first
2. run `il2cpp --convert-to-cpp`, which regenerates both the C++ under
   `Library/Bee/artifacts/Android/il2cppOutput/cpp/` and the metadata under
   `il2cppOutput/data/`
3. compile that C++ into `Library/Bee/artifacts/Android/<arch>/*.o`, archive it into
   `il2cpp.a` and link `<arch>/libil2cpp.so`
4. run `llvm-objcopy` over the linked `.so` to strip it — **the input is
   `Library/Bee/artifacts/objcopy_*/libil2cpp.dbg.so`, which must be overwritten with the
   fresh link first**
5. copy the stripped `.so` into `unityLibrary/src/main/jniLibs/<abi>/` **and** copy
   `il2cppOutput/data/Metadata/global-metadata.dat` plus
   `il2cppOutput/data/Resources/mscorlib.dll-resources.dat` into
   `unityLibrary/src/main/assets/bin/Data/Managed/{Metadata,Resources}/`, before Gradle runs

Stage 5's metadata half is the easiest to miss: type and string-literal names live in
`global-metadata.dat`, not in the `.so`, so a stale copy gives an APK whose native code is new
but whose type table still describes the old assemblies.

Skipping any of steps 4 and 5 leaves an APK that builds and installs fine but still runs
the old code. Check with `md5sum` on the APK's `lib/<abi>/libil2cpp.so` against the objcopy
output, not with `nm` — the stripped library keeps no local symbols.

### The DAG stores absolute paths from the build that wrote it

`Library/Bee/Player*.dag.json` records each node's command line verbatim, including absolute
input paths. After the project directory is renamed, the arm64 nodes may point at the new path
while the arm32 nodes still name the old one, so a relink of `armeabi-v7a` fails or silently
reuses the previous objects and the APK ships two ABIs built from different C#. v1.4.0 did
this: `arm64-v8a` had `DroidHeroApp`, `armeabi-v7a` still had `BandHeroApp`, and the app drew
nothing. Always relink **both** ABIs and check the result per ABI before packaging:

```bash
grep -c DroidHeroApp Library/Bee/artifacts/objcopy_*/libil2cpp.sym.so
grep -c BandHeroApp Library/Bee/artifacts/objcopy_*/libil2cpp.sym.so   # must be 0
```

A stale ABI also carries the old `global-metadata.dat` expectations, so the mismatch shows up
as a black screen rather than a load error.

### Renaming a class does not rename the scene's copy of it

Steps 1–5 above rebuild the managed and native code, but nothing re-serialises the player
data. A `MonoScript` stores the class name and namespace as plain strings in
`globalgamemanagers.assets`, and the scene's `MonoBehaviour` reaches its class through a
`path_id` into that table. Rename a class and relink by hand, and the metadata says
`DroidHero.Core.DroidHeroApp` while the table still says `BandHero.Core.BandHeroApp`, so
the component resolves to nothing and the app boots to a black screen.

A real Editor build re-serialises those files. Without a licence, patch them directly: both
`m_ClassName` and `m_Namespace` are length-prefixed, 4-byte-aligned strings, so a same-size
overwrite keeps every later offset valid. That works whenever the new string lands in the same
aligned field: `BandHero.Gameplay` (17) and `DroidHero.Gameplay` (18) both round to 20, and
`BandHeroApp` (11) and `DroidHeroApp` (12) both round to 12. A bare `BandHero` (8) does not
fit `DroidHero` (9), so that one has to be re-serialised.

```
globalgamemanagers.assets.split0: 39 namespaces and class names
level0: 1, the scene's root GameObject m_Name
```

### Re-serialising level0 must keep 16-byte alignment

Unity 6 aligns every object in a `SerializedFile` to **16 bytes**, and lays out the object
table from that alignment. UnityPy pads to 8 (`data_writer.align_stream(8)` in
`SerializedFile.save`), so a plain `env.file.save()` shrinks the file and leaves object
offsets that Unity never produces. The shipped v1.4.0 did exactly that: `level0` went from
1632 to 1616 bytes and three objects landed on offsets 744, 808 and 952, none 16-aligned,
while every asset in the working v1.3.1 build is 16-aligned. That is the black screen.

Force the writer to Unity's alignment before saving, and prove it reproduces Unity by
round-tripping the untouched scene — the output must equal the input byte for byte:

```python
from UnityPy.streams import EndianBinaryWriter
_orig = EndianBinaryWriter.align_stream
EndianBinaryWriter.align_stream = lambda self, a=4: _orig(self, 16 if a == 8 else a)

env = UnityPy.load(pristine)          # assets/bin/Data/level0 from a real build
assert env.file.save() == pristine    # if this fails, do not ship the patched file
```

Then rename and save. The result must still report `byte_start % 16 == 0` for every object.

`tools/rename_scene_object.py` does all of the above and is the only supported way to
rename the root object:

```
python3 tools/rename_scene_object.py \
  Library/Bee/artifacts/Android/Split/level0/level0 /tmp/level0 BandHero DroidHero
```

Verify by concatenating `globalgamemanagers.assets.split*` and parsing it with UnityPy: every
`MonoScript` should report the new namespace, and the `path_id` the scene points at should
resolve to the new class. A `grep -c BandHero` over every entry in the finished APK has to
reach zero — checking only the `.so` and `global-metadata.dat` misses this entirely.

`tools/verify_apk.py` performs that audit plus the alignment, ELF and metadata checks in one
pass. It exits non-zero on the shipped v1.4.0 and zero on a good build, so run it on every
APK before publishing:

```
python3 tools/verify_apk.py Builds/DroidHero-v1.5.0.apk
```

## A throwing `Awake` looks exactly like a black screen

`DroidHeroApp` draws its entire UI from `OnGUI` under a fade that starts at `0`. If `Awake`
throws, `Update` never runs, `_fade` never advances, and the app sits on the camera's
`(0.05, 0.05, 0.08)` clear colour with no menu and no log unless you are watching logcat.
v1.4.1 shipped that way: `Awake` gained `Screen.orientation = ScreenOrientation.Portrait`,
which is redundant because both `AndroidManifest.xml` (`screenOrientation="portrait"`) and
`ProjectSettings.asset` (`defaultScreenOrientation: 0`, `useOSAutorotation: 0`) already
force portrait. It was removed in v1.4.2, and `Awake` is now wrapped in `try`/`catch` that
reports through `_status` so a future startup fault is visible instead of silent.

## Metadata usage indices are baked into every object file

Adding or removing any method call changes `Il2CppMetadataUsage.c`, and the runtime type
tokens in it are referenced by index from the other generated `.cpp` files. Removing the
single `set_orientation` call shifted every later token by 4. Rebuilding only the files whose
source text changed therefore produces a library that links but reads the wrong types.

After any C# change, recompile **all** `Compiling Cpp/C for libil2cpp` nodes for both ABIs,
not just the ones whose generated source differs. The full set is 252 nodes and takes a few
minutes with `ThreadPoolExecutor(max_workers=8)`. The build is deterministic: a full rebuild
and an incremental one produce byte-identical `libil2cpp.so`.

`tools/relink_il2cpp.py --dll <Assembly-CSharp.dll>` replays all five stages from the DAG and
is the supported way to do this. Three things it guards against, each of which produced an
APK that built and installed but still ran the previous release:

**The DLL's file name becomes the assembly name.** Compiling to `DroidHero.dll` makes
UnityLinker write `DroidHero.dll` into `ManagedStripped/` and leave the stale
`Assembly-CSharp.dll` beside it, and il2cpp then converts the stale one. Compile to
`Assembly-CSharp.dll`. The script refuses any other name and checks that the stripped DLL is
newer than its input.

**il2cpp can add parts that the DAG never names.** A C# change grew `Assembly-CSharp` from
three parts to four, so `Assembly-CSharp__3.cpp` had no compile node. The link then failed on
undefined symbols, because the recorded link lists every object explicitly. Compile the
generated `.cpp` files, not the recorded list, and append the extra objects to the link.

**The output directory holds more than the generated C++.** `bdwgc`, `libil2cpp` helpers and
the two-arm assembly stubs compile into the same directory and the recorded `llvm-ar` names
them. Rebuilding the archive from that directory alone drops them; the link then fails on
symbols from `Mono.Security`. Replay the recorded `ar` command and do not clear `.o` files
that have no matching `.cpp`.

Putting a new object in both the archive and the link command line fails with duplicate
symbols, so the extras belong on the command line only.

Verify by symbol, not by exit code. The stripped `.so` keeps no local symbols, so compare
`md5sum` of `lib/<abi>/libil2cpp.so` in the APK against the objcopy output, and check that
`global-metadata.dat` contains the new method names:

```
strings -a .../Managed/Metadata/global-metadata.dat | grep -c AutoStrumChords
```

## Packaging by hand

The Gradle template under `Library/Bee/Android/Prj/IL2CPP/Gradle/` is generated once and
then never refreshed, so `gradle.properties` and `launcher/build.gradle` keep the
applicationId, namespace and version of whatever release generated it. After a rename or a
version bump, edit both files before packaging or the APK ships the old identity.

There is no `gradlew` wrapper in that directory and no `gradle` on PATH. Run the bundled
distribution directly:

```
JAVA_HOME=/workspace/unity/jdk /workspace/unity/jdk/bin/java -Xmx4096m \
  -cp "/workspace/unity/Editor/Data/PlaybackEngines/AndroidPlayer/Tools/gradle/lib/*" \
  org.gradle.launcher.GradleMain :launcher:assembleRelease
```

Drop `--offline` on a fresh container: `~/.gradle/caches/modules-2` starts empty and the
Android Gradle Plugin has to be fetched once. Later builds work offline.

The APK lands in `launcher/build/outputs/apk/release/launcher-release.apk`. Check its
identity with `aapt2 dump badging` and its orientation with
`aapt2 dump xmltree --file AndroidManifest.xml`, both under
`/workspace/unity/sdk/build-tools/36.0.0/`.
