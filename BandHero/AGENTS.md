
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

When the native side is relinked by hand from the Bee DAG, the pipeline has four stages and
each one silently keeps the previous file if you skip it:

1. compile the generated C++ into `Library/Bee/artifacts/Android/<arch>/*.o`
2. archive those objects into `il2cpp.a` and link `<arch>/libil2cpp.so`
3. run `llvm-objcopy` over the linked `.so` to strip it — **the input is
   `Library/Bee/artifacts/objcopy_*/libil2cpp.dbg.so`, which must be overwritten with the
   fresh link first**
4. copy the stripped `.so` into `unityLibrary/src/main/jniLibs/<abi>/` before Gradle runs

Skipping step 3's copy or step 4 leaves an APK that builds and installs fine but still runs
the old code. Check with `md5sum` on the APK's `lib/<abi>/libil2cpp.so` against the objcopy
output, not with `nm` — the stripped library keeps no local symbols.
