# Droid Hero

A 2D rhythm game for Android, built with Unity. Five lanes, notes that rush toward you from a
horizon, and a score that punishes a late strum. It plays like Guitar Hero, drawn flat.

Two things make it different from most games in this genre:

- **Charts come from [enchor.us](https://www.enchor.us/)**, a community archive of Guitar Hero,
  Rock Band and Clone Hero charts, searched and downloaded from inside the game.
- **The music comes from your own device.** You pick an audio file, the game copies it into app
  storage and plays the chart against it. No recordings are bundled, so nothing copyrighted is
  distributed with the app.

That split is the whole point: the chart is the note data, the audio is yours. Drop in any track
you own and the notes line up with it.

## Contents

- [Download](#download)
- [How it plays](#how-it-plays)
- [Features](#features)
- [Requirements](#requirements)
- [Building](#building)
- [Running the tests](#running-the-tests)
- [Project layout](#project-layout)
- [How the audio works](#how-the-audio-works)
- [Local music](#local-music)
- [Notes on the code](#notes-on-the-code)
- [Licensing](#licensing)

## Download

Grab the latest APK from [Releases](https://github.com/ipungsx/DroidHero/releases/latest).

| | |
| --- | --- |
| Package | `com.droidhero.game` |
| Version | 1.5.5 (versionCode 14) |
| Android | 7.0 (API 24) or newer |
| CPU | ARM64 (`arm64-v8a`) and ARMv7 (`armeabi-v7a`) |
| Size | about 23 MB |
| Signed with | a debug key |

Because it is signed with a debug key, it is meant for sideloading. Two consequences:

- If you installed an earlier build that was signed with a different key, Android refuses the
  upgrade. Uninstall the old copy first.
- It cannot go on Google Play as-is; that needs a release keystore.

No music or charts are included in the APK. You supply both on first run.

## How it plays

```
Menu ──► Song select ──► Chart search (enchor.us) ──► Download .sng
  │           │
  │           └──► Pick a difficulty the chart actually has
  │                          │
  │                          ▼
  │                    Link local audio ──► Play
  │
  ├──► Leaderboard
  ├──► Themes
  └──► Calibration
```

Five lanes sit at the bottom of the screen. Notes travel down the highway from a horizon at the
top, and you tap the lane as the note crosses the strike line. Hold a sustained note to keep it
alive. A chord wants every one of its lanes held.

The song screen is deliberately two steps: pick a chart, then pick one of the difficulties that
chart really carries. Charts are not guaranteed to have all four — a charter may have authored
Expert only — so the game reads the note tracks and offers only what exists. Picking a
difficulty up front, as an earlier version did, meant a chart could load zero notes with no
warning.

### Scoring

| Judgement | Window | Points |
| --- | --- | --- |
| Perfect | ±45 ms | 100 |
| Great | ±75 ms | 80 |
| Good | ±115 ms | 50 |
| Miss | beyond ±140 ms | 0 |

Star rating at the end of a song is based on accuracy — the share of judged notes you hit:

| Stars | Accuracy |
| --- | --- |
| 5 | 95% or better |
| 4 | 90% |
| 3 | 80% |
| 2 | 65% |
| 1 | below 65% |

Three multipliers sit on top of that:

- **Combo.** Every ten notes in a row raises the multiplier, up to 4x.
- **Star power.** Ten notes fill one bar, up to three bars. Spending a bar opens an 8 second
  window that doubles the multiplier.
- **Overdrive.** Every 15,000 points crossed fires a free 8 second window that doubles it again.
  The stage lights flare and the highway turns gold while it lasts.

They stack, so a 4x combo held through a star power bar and an Overdrive window pays 16x. A
sustained note also pays up to double its base value depending on how much of the hold you keep.

## Features

| Feature | Detail |
| --- | --- |
| Chart search | Queries the enchor.us API by song or artist, with an in-game keyboard |
| Chart download | Fetches the `.sng` package and keeps it locally for offline play |
| Chart formats | `.sng` packages, `.chart` files and Rock Band style `notes.mid` |
| Local music | Android file picker copies a track from your device into app storage |
| Difficulties | Only those a chart really contains, read from its note tracks |
| Note types | Strum notes, hammer-ons and pull-offs, tap notes, sustains, chords |
| Input | Five lanes, multi-touch, with slide-in from beside the highway |
| Stage | Guitar prop, crowd, twelve-light back row, ten beams, confetti |
| Scoring | Per-note judgement, combo, star power, Overdrive, star rating |
| HUD | Live score, combo, meters, judgement popups, pause and quit |
| Leaderboard | Best score per song, kept on the device |
| Calibration | Device-wide sync offset, ±300 ms in 5 ms steps, saved between runs |
| Themes | Selectable lane themes and a light show toggle |

## Requirements

To build it:

- **Unity 6000.0.85f2** with the **Android Build Support** module, plus its SDK/NDK/JDK
- A licensed Unity Editor — Personal is enough
- Android NDK **r27** (`27.2.12479018`) and `cmake;3.22.1` in the SDK
- `libicu` on the host if you activate a licence from the command line

To play it: Android 7.0 or newer, ARM64 or ARMv7.

## Building

### From the command line

```bash
Unity -batchmode -nographics -quit \
      -projectPath DroidHero \
      -executeMethod DroidHero.EditorTools.BuildAndroid.BuildFromCommandLine \
      -logFile build.log
```

The APK lands at `DroidHero/Builds/DroidHero.apk`. `BuildAndroid` applies every player setting
through the Editor API instead of committed YAML, so a clean checkout builds the same way.

You can also use **Droid Hero → Build Android APK** from the Editor menu.

`BuildAndroid.ApplyExternalTools` reads its toolchain paths from the environment, so no editor
preferences need to be set by hand:

```bash
export JAVA_HOME=/path/to/jdk
export ANDROID_SDK_ROOT=/path/to/sdk
export ANDROID_NDK_ROOT=/path/to/sdk/ndk/27.2.12479018
```

### On GitHub Actions

`.github/workflows/build-apk.yml` builds the APK on a runner, so no local Unity install is
needed. It fetches a chart from enchor.us, runs the headless tests, then builds in game-ci's
Android editor image and uploads the APK as an artifact.

It triggers on a push to `main` (ignoring changes that only touch Markdown) and on manual
dispatch. Add these repository secrets under **Settings → Secrets and variables → Actions**:

| Secret | Value |
| --- | --- |
| `UNITY_LICENSE` | Full text of your licence file |
| `UNITY_EMAIL` | Email of the Unity account |
| `UNITY_PASSWORD` | That account's password |

Without them the build step is skipped and the run still goes green, so a fork with no licence
gets test results instead of a red X. The run carries a notice explaining why it skipped.

For a Plus or Pro licence, use `UNITY_SERIAL` instead of `UNITY_LICENSE`.

A password is mandatory even on Personal, because game-ci activates Unity from the command line
rather than installing a licence file. If you sign in to Unity with Google or another SSO
provider you may not have one — set a password on the Unity ID first, at
<https://cloud.unity.com/account/security>. Google sign-in can stay linked; the password works
alongside it.

Unity 6 no longer writes `Unity_lic.ulf` either. It keeps the licence as
`~/.config/unity3d/Unity/licenses/UnityEntitlementLicense.xml`, so activate with the licensing
client instead:

```bash
Unity.Licensing.Client --activate-all --include-personal \
  --username "$UNITY_EMAIL" --password "$UNITY_PASSWORD"
```

`--include-personal` falls back to a free Personal seat when no subscription is assigned.

## Running the tests

The chart parsers, scoring, note tracking, highway geometry and audio decoding have no
dependency on `UnityEngine` beyond logging, so they compile and run under plain Mono with no
licence and no GPU:

```bash
DroidHero/tools/run-tests.sh /path/to/chart.sng [more.sng ...]
```

It compiles the engine-independent sources with `mcs` and runs the suite against each chart you
pass in. The suite currently makes 253 checks, covering:

- `.sng`, `.chart` and MIDI parsing, and every difficulty present in the file
- a perfect run and an idle run
- star power, Overdrive, sustain lengths and the calibration offset
- lead-in timing and audio format detection
- highway geometry and lane hit-testing, including the narrowing highway
- the local-audio link store
- a full Opus decode, compared sample by sample against the streaming path

You need a real chart file to run it; the suite is not much use against a stub. CI downloads one
from enchor.us for exactly this reason.

Set `UNITY_DATA` to a Unity install to use its bundled Mono instead of the distro one:

```bash
UNITY_DATA=/opt/unity/Editor/Data DroidHero/tools/run-tests.sh chart.sng
```

### Checking an APK

```bash
python3 DroidHero/tools/verify_apk.py path/to/DroidHero.apk
```

It parses the archive and reports whether the APK can actually boot: 16-byte alignment of every
asset, the scene and its script bindings, both ABIs present with the right ELF class, the
metadata naming the classes that were compiled, and stale `MonoScript` entries. It prints
`OK: this APK can boot` or the reason it cannot.

This catches the failure mode that otherwise costs an hour: an APK that packages a stale
`libil2cpp.so`, installs cleanly, and shows a black screen.

## Project layout

```
.github/workflows/build-apk.yml   CI: headless tests, then the APK build
DroidHero/                        the Unity project
  Assets/
    Art/                          app icon
    Editor/BuildAndroid.cs        player settings and the build entry point
    Plugins/Concentus/            managed Opus decoder
    Plugins/Android/              file-picker activity, manifest, launcher activity
    Scripts/
      Audio/                      format detection, Opus decode, streaming clips
      Chart/                      .sng, .chart and MIDI parsers
      Core/                       screens, song library, settings, leaderboard
      Enchor/                     enchor.us search and download client
      Gameplay/                   note tracking, scoring, highway, touch input
      UI/                         procedural sprites, themes, IMGUI skin
  tools/                          test harness, APK verifier, icon and relink helpers
  ProjectSettings/                player settings and version
```

The Unity-facing code is deliberately thin. Anything worth testing lives behind a plain C#
boundary so the headless suite can reach it.

## How the audio works

`.sng` packages carry `song.opus`, and Unity has no decoder for it. The project ships its own:
`OpusAudioDecoder` parses the Ogg container and hands packets to Concentus, then
`OpusClipFactory` feeds the result into an `AudioClip` through a streaming reader.

Streaming is not optional. A five minute song is roughly 100 MB as float samples, which will not
fit comfortably on a phone. The streaming path holds under 1 MB.

```csharp
using (var stream = new OggOpusStream(data))     // about 15 ms to index a 26 MB chart
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
- **Restart.** Rewinding the packet cursor is not enough, because the codec carries filter state
  between frames. `Reset()` rebuilds the decoder; without it, replaying a song starts with the
  previous run's tail state.

## Local music

On Android the picker uses the Storage Access Framework, copies the chosen file into app-private
storage, and returns an absolute path Unity can read. Unity cannot open `content://` URIs, which
is why the copy happens before anything else.

| Container | Status |
| --- | --- |
| WAV | supported |
| Ogg Vorbis | supported |
| Ogg Opus | supported |
| MP3 | supported |
| FLAC | detected, rejected with a clear message |
| M4A | detected, rejected with a clear message |

FLAC and M4A are identified so the player gets a real explanation instead of silence.

An attachment is remembered per chart, so it survives a restart. Pick a file before downloading
a chart and it applies to the chart you download next. `X` on a song row detaches it and the
chart's own audio plays again. A remembered path whose file has since been deleted is ignored
rather than left to fail at play time.

## Notes on the code

A few constraints are worth knowing before changing anything.

**Charts are not the same as audio.** The chart supplies note timing; the audio supplies the
sound. If they disagree, notes will drift, which is what calibration is for. Calibration shifts
the whole chart against the audio in 5 ms steps, so a player can fix a device-specific delay
without touching the chart.

**Never use `GUI.TextField`, `GUI.PasswordField` or `TouchScreenKeyboard` in a device build.**
All three reach the Android input-method service, and that path throws out of `OnGUI` and takes
the screen down with it — the player sees a frozen screen instead of a text field. The search
screen draws its own letters as ordinary buttons for this reason. Stick to `Button`, `Label`,
`DrawTexture` and `BeginScrollView` in new UI.

**`FinalizeChart` runs more than once.** Each parser calls it, and `SongLibrary` calls it again to
fold in the calibration offset. Any field it rewrites in place has to survive a second call, or
the second pass will convert it twice. Sustains were zeroed for several releases by exactly this
bug. `TestFinalizeIsRepeatable` guards it.

**Touch bookkeeping must release as well as record.** A lane is held as long as any finger points
at it, so every path that records a finger needs a matching release: a finger that vanishes
between frames, and a finger dragged off the side of the highway. Both used to leak a held lane,
which reads to the player as a fret stuck down.

More build and packaging detail lives in [AGENTS.md](AGENTS.md) and
[DroidHero/AGENTS.md](DroidHero/AGENTS.md).

## Licensing

No music and no charts are bundled with this repository or the APK.

- Charts are fetched on demand from enchor.us. They are community transcriptions of note data,
  not audio.
- Audio is read from the player's own device and copied into app-private storage. It never leaves
  the device and is never redistributed.
- Game code, art and the app icon are part of this project.
- The Opus decoder is [Concentus](https://github.com/lostromb/concentus), a managed port of
  libopus, under its own licence.

If you fork this to publish a build, keep that separation: ship the engine, let players supply
their own audio.
