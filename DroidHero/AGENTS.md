
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
  org.gradle.launcher.GradleMain --offline :launcher:assembleRelease
```

The APK lands in `launcher/build/outputs/apk/release/launcher-release.apk`. Check its
identity with `aapt2 dump badging` and its orientation with
`aapt2 dump xmltree --file AndroidManifest.xml`, both under
`/workspace/unity/sdk/build-tools/36.0.0/`.
