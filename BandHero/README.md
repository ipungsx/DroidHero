# Band Hero

A 2D rhythm game for Android built with Unity — a Guitar Hero style highway where charts come
from [enchor.us](https://www.enchor.us/) and the music comes from your own device, so nothing
copyrighted is shipped with the game.

## What it does

| Feature | Detail |
| --- | --- |
| Chart search | Queries the enchor.us API by song or artist, filtered by instrument and difficulty |
| Chart download | Fetches the `.sng` package and saves it locally for offline play |
| Local music | Android file picker copies a song from your device into app storage |
| Difficulties | Easy, Medium, Hard, Expert from the chart's own note tracks |
| Scoring | Per-note judgement, combo multiplier, star power, sustain notes, star rating |
| Input | Multi-touch five-lane highway, sized for a phone in portrait |
| HUD | Live score, combo, star power meter, judgement popups, pause and quit |
| Calibration | Device-wide sync offset in 5 ms and 20 ms steps, saved between runs |

Charts ship an Ogg Opus audio track. Unity cannot decode Opus, so the project carries its own
decoder (see [Audio](#audio)).

## Requirements

- Unity **6000.0.85f2** with the **Android Build Support** module
- A licensed Unity Editor (Personal is fine) — see [Building](#building)
- Android 7.0 (API 24) or newer

## Building

The APK is produced from the command line:

```bash
Unity -batchmode -nographics -quit \
      -projectPath BandHero \
      -executeMethod BandHero.EditorTools.BuildAndroid.BuildFromCommandLine \
      -logFile build.log
```

The output lands at `BandHero/Builds/BandHero.apk`. `BuildAndroid` applies every player setting
through the Editor API rather than committed YAML, so a clean checkout builds identically.

You can also use **Band Hero → Build Android APK** from the Editor menu.

### Building on GitHub Actions

`.github/workflows/build-apk.yml` builds the APK on a runner, so no local Unity install is
needed. It runs the headless tests first, then builds in game-ci's Android editor image and
uploads `BandHero.apk` as a downloadable artifact.

Add three repository secrets under **Settings → Secrets and variables → Actions**:

| Secret | Value |
| --- | --- |
| `UNITY_LICENSE` | Full text of your `Unity_lic.ulf` file (Personal licence) |
| `UNITY_EMAIL` | The email address of the Unity account |
| `UNITY_PASSWORD` | That account's password |

Then run **Actions → Build APK → Run workflow**, or push to `main`. For a Plus/Pro licence,
replace `UNITY_LICENSE` with `UNITY_SERIAL` and keep the other two.

Without those secrets the build step is skipped and the run stays green, so a fork without a
licence still gets test results. Look for the notice on the run for the reason.

### Activating without a `.ulf` file (Unity 6)

Unity 6 no longer writes `Unity_lic.ulf`. It stores the licence as
`~/.config/unity3d/Unity/licenses/UnityEntitlementLicense.xml` instead, so the
`UNITY_LICENSE` route below only applies to Unity 2022 and older. On Unity 6, activate
from the command line and let the licensing client write that file:

```bash
Unity.Licensing.Client --activate-all --include-personal \
  --username "$UNITY_EMAIL" --password "$UNITY_PASSWORD"
```

The binary lives in `<Editor>/Data/Resources/Licensing/Client/`. `--include-personal`
falls back to a free Personal seat when no subscription is assigned, which is what makes
this work for a personal account. Both commands need the account to have a password.

### Getting the licence file when you sign in with Google or SSO

Signing in with "Continue with Google" (or any browser flow) does not create a password, and
game-ci needs one: it activates Unity from the command line with `-username` and `-password`.
Two separate steps fix that.

**1. Set a Unity ID password.** A Google-linked account still has a Unity ID underneath.
Go to <https://cloud.unity.com/account/security>, sign in, and set a password. Alternatively
use the *Reset your password* link at <https://login.unity.com> with the same email address.
Keep Google sign-in linked if you like; the password works alongside it.

**2. Generate the `.ulf` file.** The Unity Hub is the only way to activate a Personal licence,
and the file is easy to miss:

1. Open Unity Hub → **Preferences** (gear icon) → **Licenses**.
2. Click **Add** → **Get a free personal license** and finish the flow.
   Do not skip this even if a licence already appears in the list — an existing entry does
   not guarantee the file exists on disk.
3. Copy the file from one of these paths:

   | OS | Path |
   | --- | --- |
   | Windows | `C:\ProgramData\Unity\Unity_lic.ulf` |
   | macOS | `/Library/Application Support/Unity/Unity_lic.ulf` |
   | Linux | `~/.local/share/unity3d/Unity/Unity_lic.ulf` |

4. Paste the whole file contents into the `UNITY_LICENSE` secret.
5. Sign out of Unity Hub afterwards. Hub 3.7 and later dropped the *Return license* action for
   Personal, and signing out is how the seat is released so CI can take it.

A licence is not tied to an OS or Editor version, so generating it on Windows and building on
Linux is fine.

### Build prerequisites

Two things must be in place before the command above can succeed, and neither ships with this
repository:

1. **A Unity license.** Without one the Editor exits with
   `No valid Unity Editor license found. Please activate your license.` Activate with
   `Unity -batchmode -username <user> -password <pass> -serial <serial> -quit`, or sign in
   through Unity Hub.
2. **The Android Build Support module.** Check `Editor/Data/PlaybackEngines/` — it must contain
   `AndroidPlayer`. Add the module through Unity Hub if it is missing.

## Verifying without Unity

The gameplay, chart parsing and audio decoding are covered by a headless harness that needs no
Unity license and no GPU:

```bash
BandHero/tools/run-tests.sh <chart.sng> [more.sng ...]
```

It compiles the engine-independent sources with `mcs` and runs the suite against each chart you
pass in: SNG parsing, all difficulties, a perfect run, an idle run, star power, lead-in timing,
audio format detection, highway geometry, the local-audio link store, the calibration offset, and a
full Opus decode whose output is compared sample-by-sample against the streaming path.

Two compile checks keep the Unity-facing code honest:

```bash
./compile_check.sh          # runtime scripts against real UnityEngine assemblies
./compile_check_editor.sh   # Editor build script against UnityEditor assemblies
```

## Layout

```
Assets/
  Editor/BuildAndroid.cs        Player settings + APK build entry point
  Plugins/Concentus/            Managed Opus decoder (netstandard)
  Plugins/Android/              File-picker activity, manifest, launcher icons
  Scripts/
    Audio/                      Format detection, Opus decode + streaming clips
    Chart/                      .sng, .chart and MIDI parsers
    Core/                       App screens, song library, local file picker
    Enchor/                     enchor.us search and download client
    Gameplay/                   Note tracking, scoring, highway rendering, touch input
    UI/                         Procedural sprites and IMGUI skin
tools/
  run-tests.sh                  Headless test harness
  HeadlessTests.cs              Test suite
```

## Audio

`.sng` packages carry `song.opus`, which Unity has no decoder for. `OpusAudioDecoder` parses the
Ogg container itself and hands packets to Concentus, then `OpusClipFactory` feeds the result to
an `AudioClip` through a streaming reader.

Streaming matters: a five-minute song is roughly 100 MB of float samples, which will not fit on a
phone. The streaming path holds under 1 MB.

```csharp
using (var stream = new OggOpusStream(data))     // ~15 ms to index a 26 MB chart
{
    var buffer = new float[4096];
    int read;
    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
    {
        // feed buffer[0..read]
    }
}
```

Two details the decoder has to get right, both covered by tests:

- **Length.** The Ogg granule position is authoritative. The final packet decodes past it with
  encoder padding, so the stream is trimmed to the granule rather than trusting the packet sum.
- **Restart.** Rewinding the packet cursor is not enough — the codec carries filter state between
  frames, so `Reset()` rebuilds the decoder. Without this, replaying a song starts it with the
  previous run's tail state.

## Local music

Requirement: music comes from the player's own device rather than being bundled. On Android the
picker uses the Storage Access Framework, copies the chosen file into app-private storage, and
returns an absolute path Unity can read.

Supported containers: WAV, Ogg Vorbis, Ogg Opus, MP3. FLAC and M4A are detected only so the player
gets a clear "not supported" message instead of silence.

An attachment is remembered per chart, so it is still there after a restart. Pick a file before
downloading a chart and it is applied to the chart you download next. `X` on the song row detaches
it, and the chart's own audio plays again. A remembered path whose file has since been deleted is
ignored rather than left to fail at play time.

## Licence notes

No music or charts are included. Charts are fetched on demand from enchor.us and audio is read
from the player's device, which keeps the distributed game free of copyrighted recordings.
