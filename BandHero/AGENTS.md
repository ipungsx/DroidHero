
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
