using System.Collections;
using UnityEngine;
using BandHero.Enchor;
using BandHero.Gameplay;
using BandHero.UI;

namespace BandHero.Core
{
    /// <summary>
    /// Builds the whole game in code: camera, screens and gameplay host.
    ///
    /// There are no scenes or prefabs to wire up, so the project stays reviewable in a diff and
    /// the Android build has nothing to misconfigure.
    /// </summary>
    public class BandHeroApp : MonoBehaviour
    {
        enum Screen
        {
            Menu,
            SongSelect,
            EnchorSearch,
            Gameplay,
            Results,
            Calibration
        }

        readonly SongLibrary _library = new SongLibrary();
        readonly EnchorClient _enchor = new EnchorClient();
        readonly LocalAudioPicker _picker = new LocalAudioPicker();

        GameplayController _gameplay;
        Screen _screen = Screen.Menu;
        float _fittedAspect;

        string _searchQuery = "";
        EnchorSearchResult _searchResult;
        bool _searching;
        string _status = "";
        Vector2 _songScroll;
        Vector2 _searchScroll;
        int _searchPage = 1;

        void Awake()
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            UnityEngine.Screen.sleepTimeout = SleepTimeout.NeverSleep;

            SetupCamera();
            RefreshLibrary();
        }

        void SetupCamera()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                var cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<Camera>();
            }

            camera.orthographic = true;
            camera.orthographicSize = FitOrthographicSize(camera.aspect);
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.backgroundColor = LaneTheme.BackgroundColor;
            camera.clearFlags = CameraClearFlags.SolidColor;

            if (Object.FindFirstObjectByType<AudioListener>() == null) camera.gameObject.AddComponent<AudioListener>();

            _fittedAspect = camera.aspect;
        }

        /// <summary>
        /// Widens the view only as far as the highway needs, so tall phones keep every lane
        /// visible without shrinking the notes on wider screens.
        /// </summary>
        void RefitCameraIfNeeded()
        {
            Camera camera = Camera.main;
            if (camera == null || Mathf.Approximately(camera.aspect, _fittedAspect)) return;

            camera.orthographicSize = FitOrthographicSize(camera.aspect);
            _fittedAspect = camera.aspect;
        }

        static float FitOrthographicSize(float aspect)
        {
            var layout = new HighwayLayout();
            float required = layout.RequiredOrthographicSize(aspect);
            return Mathf.Max(6.4f, required);
        }

        void RefreshLibrary()
        {
            AppState.Songs = _library.Scan();
        }

        void Update()
        {
            RefitCameraIfNeeded();

            // In gameplay the HUD owns the back button, because pausing is what a player expects
            // there. Everywhere else it steps back a screen.
            if (_screen == Screen.Gameplay) return;
            if (!Input.GetKeyDown(KeyCode.Escape)) return;

            if (_screen != Screen.Menu) GoToMenu();
        }

        void OnGUI()
        {
            if (_screen == Screen.Gameplay) return;

            UiSkin.PanelRect(new Rect(0, 0, UnityEngine.Screen.width, UnityEngine.Screen.height),
                             new Color(0.04f, 0.04f, 0.07f, 1f));

            switch (_screen)
            {
                case Screen.Menu: DrawMenu(); break;
                case Screen.SongSelect: DrawSongSelect(); break;
                case Screen.EnchorSearch: DrawEnchorSearch(); break;
                case Screen.Results: DrawResults(); break;
                case Screen.Calibration: DrawCalibration(); break;
            }

            if (!string.IsNullOrEmpty(_status))
            {
                GUI.Label(new Rect(Scale(40), UnityEngine.Screen.height - Scale(110),
                                   UnityEngine.Screen.width - Scale(80), Scale(80)), _status, UiSkin.Small);
            }
        }

        static float Scale(float value) =>
            value * Mathf.Max(1f, UnityEngine.Screen.height / 1080f);

        void GoToMenu()
        {
            _screen = Screen.Menu;
            _status = "";
        }

        void DrawMenu()
        {
            float w = UnityEngine.Screen.width;
            float h = UnityEngine.Screen.height;

            GUI.Label(new Rect(0, Scale(120), w, Scale(80)), "BAND HERO", UiSkin.Title);
            GUI.Label(new Rect(0, Scale(196), w, Scale(40)), "2D rhythm game for Android", UiSkin.Small);

            float buttonWidth = Mathf.Min(Scale(420), w * 0.7f);
            float x = (w - buttonWidth) * 0.5f;
            float y = h * 0.38f;
            float step = Scale(86);

            if (GUI.Button(new Rect(x, y, buttonWidth, Scale(66)), "PLAY", UiSkin.ButtonAccent))
            {
                RefreshLibrary();
                if (AppState.Songs.Count == 0)
                {
                    _status = "No charts yet. Download one from enchor.us.";
                    _screen = Screen.EnchorSearch;
                }
                else
                {
                    _screen = Screen.SongSelect;
                }
            }

            if (GUI.Button(new Rect(x, y + step, buttonWidth, Scale(66)), "BROWSE ENCHOR.US", UiSkin.Button))
            {
                _screen = Screen.EnchorSearch;
            }

            if (GUI.Button(new Rect(x, y + step * 2f, buttonWidth, Scale(66)), "PICK LOCAL MUSIC", UiSkin.Button))
            {
                StartCoroutine(PickLocalAudio());
            }

            if (GUI.Button(new Rect(x, y + step * 3f, buttonWidth, Scale(66)), "CALIBRATION", UiSkin.Button))
            {
                _screen = Screen.Calibration;
            }

            if (GUI.Button(new Rect(x, y + step * 4f, buttonWidth, Scale(66)), "QUIT", UiSkin.Button))
            {
                Application.Quit();
            }

            GUI.Label(new Rect(x, h - Scale(150), buttonWidth, Scale(70)),
                      AppState.Songs.Count + " song(s) in library\nCharts: " + _library.ChartsDirectory,
                      UiSkin.Small);
        }

        void DrawSongSelect()
        {
            float w = UnityEngine.Screen.width;
            float h = UnityEngine.Screen.height;

            GUI.Label(new Rect(Scale(40), Scale(30), w - Scale(80), Scale(60)), "SELECT A SONG", UiSkin.Heading);

            if (GUI.Button(new Rect(w - Scale(200), Scale(34), Scale(160), Scale(52)), "BACK", UiSkin.Button))
            {
                GoToMenu();
                return;
            }

            float listTop = Scale(110);
            var view = new Rect(Scale(40), listTop, w - Scale(80), h - listTop - Scale(40));

            if (AppState.Songs.Count == 0)
            {
                GUI.Label(view, "Your library is empty.\nDownload charts from enchor.us, or pick local audio.",
                          UiSkin.Body);
                return;
            }

            float rowHeight = Scale(112);
            var content = new Rect(0, 0, view.width - Scale(24), AppState.Songs.Count * rowHeight);
            _songScroll = GUI.BeginScrollView(view, _songScroll, content);

            for (int i = 0; i < AppState.Songs.Count; i++)
            {
                SongEntry song = AppState.Songs[i];
                var row = new Rect(0, i * rowHeight, content.width, rowHeight - Scale(10));
                UiSkin.PanelRect(row, UiSkin.Panel);

                // A song with local audio needs a third button, so the labels give up the space.
                float labelWidth = row.width - Scale(song.HasLocalAudio ? 300 : 200);

                GUI.Label(new Rect(row.x + Scale(20), row.y + Scale(14), labelWidth, Scale(34)),
                          song.DisplayName, UiSkin.Body);
                GUI.Label(new Rect(row.x + Scale(20), row.y + Scale(50), labelWidth, Scale(30)),
                          song.Subtitle, UiSkin.Small);

                string length = song.LengthSeconds > 0f
                    ? Mathf.FloorToInt(song.LengthSeconds / 60f) + ":" +
                      Mathf.FloorToInt(song.LengthSeconds % 60f).ToString("00")
                    : "--:--";

                GUI.Label(new Rect(row.x + row.width - Scale(300), row.y + Scale(20), Scale(90), Scale(30)),
                          length, UiSkin.Small);

                float playX = row.x + row.width - Scale(song.HasLocalAudio ? 265 : 190);
                float audioX = row.x + row.width - Scale(song.HasLocalAudio ? 175 : 95);

                if (GUI.Button(new Rect(playX, row.y + Scale(26), Scale(song.HasLocalAudio ? 85 : 90), Scale(54)),
                               "PLAY", UiSkin.ButtonAccent))
                {
                    AppState.SelectedSong = song;
                    StartSong(song, AppState.Difficulty);
                }

                if (GUI.Button(new Rect(audioX, row.y + Scale(26), Scale(85), Scale(54)),
                               song.HasLocalAudio ? "CHANGE" : "AUDIO", UiSkin.Button))
                {
                    AppState.SelectedSong = song;
                    StartCoroutine(AttachLocalAudio(song));
                }

                // Without this the only way back to the chart's own audio is to pick another
                // file, which leaves a wrong attachment stuck.
                if (song.HasLocalAudio &&
                    GUI.Button(new Rect(row.x + row.width - Scale(85), row.y + Scale(26), Scale(75), Scale(54)),
                               "X", UiSkin.Button))
                {
                    _library.DetachLocalAudio(song);
                    _status = song.DisplayName + " uses the chart audio again.";
                }
            }

            GUI.EndScrollView();
        }

        void DrawEnchorSearch()
        {
            float w = UnityEngine.Screen.width;
            float h = UnityEngine.Screen.height;

            GUI.Label(new Rect(Scale(40), Scale(30), w - Scale(80), Scale(60)), "ENCHOR.US CHARTS", UiSkin.Heading);

            if (GUI.Button(new Rect(w - Scale(200), Scale(34), Scale(160), Scale(52)), "BACK", UiSkin.Button))
            {
                GoToMenu();
                return;
            }

            float searchY = Scale(110);
            _searchQuery = GUI.TextField(new Rect(Scale(40), searchY, w - Scale(340), Scale(56)),
                                         _searchQuery ?? "", 64, UiSkin.TextField);

            if (GUI.Button(new Rect(w - Scale(280), searchY, Scale(130), Scale(56)), "SEARCH", UiSkin.ButtonAccent))
            {
                _searchPage = 1;
                StartCoroutine(RunSearch());
            }

            if (GUI.Button(new Rect(w - Scale(140), searchY, Scale(100), Scale(56)), "NEXT", UiSkin.Button))
            {
                _searchPage++;
                StartCoroutine(RunSearch());
            }

            var listView = new Rect(Scale(40), searchY + Scale(76), w - Scale(80), h - searchY - Scale(200));

            if (_searching)
            {
                GUI.Label(listView, "Searching enchor.us...", UiSkin.Body);
                return;
            }

            if (_searchResult == null || _searchResult.data == null || _searchResult.data.Length == 0)
            {
                GUI.Label(listView, "Search for a song or artist.\nCharts download as .sng and are stored locally.",
                          UiSkin.Body);
                return;
            }

            float rowHeight = Scale(124);
            var content = new Rect(0, 0, listView.width - Scale(24), _searchResult.data.Length * rowHeight);
            _searchScroll = GUI.BeginScrollView(listView, _searchScroll, content);

            for (int i = 0; i < _searchResult.data.Length; i++)
            {
                EnchorChart chart = _searchResult.data[i];
                var row = new Rect(0, i * rowHeight, content.width, rowHeight - Scale(10));
                UiSkin.PanelRect(row, UiSkin.Panel);

                GUI.Label(new Rect(row.x + Scale(20), row.y + Scale(12), row.width - Scale(230), Scale(32)),
                          chart.name ?? "Untitled", UiSkin.Body);
                GUI.Label(new Rect(row.x + Scale(20), row.y + Scale(46), row.width - Scale(230), Scale(28)),
                          (chart.artist ?? "Unknown") + "  ·  " + (chart.charter ?? ""), UiSkin.Small);
                GUI.Label(new Rect(row.x + Scale(20), row.y + Scale(76), row.width - Scale(230), Scale(28)),
                          "Guitar " + DifficultyLabel(chart.diff_guitar) + "  ·  " + chart.DisplayLength,
                          UiSkin.Small);

                if (GUI.Button(new Rect(row.x + row.width - Scale(160), row.y + Scale(32), Scale(140), Scale(58)),
                               "DOWNLOAD", UiSkin.ButtonAccent))
                {
                    StartCoroutine(DownloadChart(chart));
                }
            }

            GUI.EndScrollView();
        }

        void DrawCalibration()
        {
            float w = UnityEngine.Screen.width;
            float h = UnityEngine.Screen.height;

            GUI.Label(new Rect(Scale(40), Scale(30), w - Scale(80), Scale(60)), "AUDIO CALIBRATION", UiSkin.Heading);

            if (GUI.Button(new Rect(w - Scale(200), Scale(34), Scale(160), Scale(52)), "BACK", UiSkin.Button))
            {
                GoToMenu();
                return;
            }

            GUI.Label(new Rect(Scale(60), h * 0.26f, w - Scale(120), Scale(120)),
                      "Notes land early or late? Nudge the offset until notes sit on the strike line "
                      + "when you strum on the beat.\n\nLocal music files often carry a short silence "
                      + "at the start that chart-embedded audio does not, so this is worth setting once "
                      + "per device.", UiSkin.Body);

            float buttonWidth = Scale(140);
            float centerX = w * 0.5f;
            float rowY = h * 0.50f;

            if (GUI.Button(new Rect(centerX - buttonWidth * 2f - Scale(20), rowY, buttonWidth, Scale(76)),
                           "-20", UiSkin.Button))
            {
                _library.CalibrationMilliseconds -= 20;
            }

            if (GUI.Button(new Rect(centerX - buttonWidth - Scale(6), rowY, buttonWidth, Scale(76)),
                           "-5", UiSkin.Button))
            {
                _library.CalibrationMilliseconds -= AudioCalibration.StepMilliseconds;
            }

            if (GUI.Button(new Rect(centerX + Scale(6), rowY, buttonWidth, Scale(76)),
                           "+5", UiSkin.Button))
            {
                _library.CalibrationMilliseconds += AudioCalibration.StepMilliseconds;
            }

            if (GUI.Button(new Rect(centerX + buttonWidth + Scale(20), rowY, buttonWidth, Scale(76)),
                           "+20", UiSkin.Button))
            {
                _library.CalibrationMilliseconds += 20;
            }

            GUI.Label(new Rect(0f, rowY + Scale(100), w, Scale(80)),
                      AudioCalibration.Describe(_library.CalibrationMilliseconds), UiSkin.Title);

            if (GUI.Button(new Rect(centerX - Scale(160), rowY + Scale(200), Scale(320), Scale(66)),
                           "RESET", UiSkin.Button))
            {
                _library.CalibrationMilliseconds = 0;
            }

            GUI.Label(new Rect(Scale(60), h - Scale(140), w - Scale(120), Scale(100)),
                      "Range " + AudioCalibration.MinMilliseconds + " to " + AudioCalibration.MaxMilliseconds
                      + " ms. The offset is applied to every chart, on top of the offset the chart declares.",
                      UiSkin.Small);
        }

        void DrawResults()
        {
            float w = UnityEngine.Screen.width;
            float h = UnityEngine.Screen.height;

            GUI.Label(new Rect(0, Scale(110), w, Scale(70)), "SONG COMPLETE", UiSkin.Title);
            GUI.Label(new Rect(0, Scale(196), w, Scale(44)), AppState.LastSongName, UiSkin.Heading);

            float y = h * 0.34f;
            float lineHeight = Scale(58);
            float x = w * 0.5f - Scale(240);

            GUI.Label(new Rect(x, y, Scale(480), lineHeight), "Score:  " + AppState.LastScore.ToString("N0"),
                      UiSkin.Body);
            GUI.Label(new Rect(x, y + lineHeight, Scale(480), lineHeight),
                      "Max combo:  " + AppState.LastMaxCombo, UiSkin.Body);
            GUI.Label(new Rect(x, y + lineHeight * 2f, Scale(480), lineHeight),
                      "Accuracy:  " + AppState.LastAccuracy.ToString("F1") + "%", UiSkin.Body);
            GUI.Label(new Rect(x, y + lineHeight * 3f, Scale(480), lineHeight),
                      "Stars:  " + new string('*', Mathf.Max(1, AppState.LastStars)), UiSkin.Body);

            float buttonWidth = Mathf.Min(Scale(420), w * 0.7f);
            float buttonX = (w - buttonWidth) * 0.5f;

            if (GUI.Button(new Rect(buttonX, h - Scale(200), buttonWidth, Scale(66)), "PLAY AGAIN", UiSkin.ButtonAccent))
            {
                if (AppState.SelectedSong != null) StartSong(AppState.SelectedSong, AppState.Difficulty);
            }

            if (GUI.Button(new Rect(buttonX, h - Scale(120), buttonWidth, Scale(66)), "MAIN MENU", UiSkin.Button))
            {
                GoToMenu();
            }
        }

        static string DifficultyLabel(int value)
        {
            string[] labels = { "-", "Easy", "Medium", "Hard", "Expert", "Expert+" };
            return value > 0 && value < labels.Length ? labels[value] : "-";
        }

        IEnumerator PickLocalAudio()
        {
            _status = "Opening the audio picker...";

            string picked = null;
            string error = null;

            yield return _picker.PickRoutine(path => picked = path, message => error = message);

            if (string.IsNullOrEmpty(picked))
            {
                _status = error ?? "No file selected.";
                yield break;
            }

            // Keep the pick so it can be attached to whichever chart is downloaded next; a
            // player who opens the picker before choosing a song should not lose their file.
            AppState.PendingLocalAudioPath = picked;
            RefreshLibrary();

            if (AppState.Songs.Count == 0)
            {
                _status = "Audio selected. Download a chart and it will use this file.";
                _screen = Screen.EnchorSearch;
                yield break;
            }

            SongEntry target = AppState.SelectedSong ?? AppState.Songs[0];
            if (!_library.AttachLocalAudio(target, picked))
            {
                _status = "That audio file is no longer readable.";
                yield break;
            }

            AppState.SelectedSong = target;
            _status = "Audio attached to " + target.DisplayName;
            _screen = Screen.SongSelect;
        }

        IEnumerator AttachLocalAudio(SongEntry song)
        {
            _status = "Pick an audio file for " + song.DisplayName + "...";

            string picked = null;
            string error = null;

            yield return _picker.PickRoutine(path => picked = path, message => error = message);

            if (string.IsNullOrEmpty(picked))
            {
                _status = error ?? "No file selected.";
                yield break;
            }

            if (!_library.AttachLocalAudio(song, picked))
            {
                _status = "That audio file is no longer readable.";
                yield break;
            }

            _status = "Audio attached to " + song.DisplayName;
        }

        IEnumerator RunSearch()
        {
            _searching = true;
            _status = "";

            yield return _enchor.Search(_searchQuery, _searchPage, EnchorClient.InstrumentGuitar, "expert",
                                        result => _searchResult = result,
                                        error => _status = error);

            _searching = false;
        }

        IEnumerator DownloadChart(EnchorChart chart)
        {
            _status = "Downloading " + chart.name + "...";

            byte[] data = null;
            string error = null;

            yield return _enchor.DownloadChart(chart.md5,
                                               bytes => data = bytes,
                                               message => error = message);

            if (data == null)
            {
                _status = error ?? "Download failed.";
                yield break;
            }

            SongEntry entry = _library.SaveChart(data, chart.md5);
            if (entry == null)
            {
                _status = "Downloaded, but the chart could not be read.";
                yield break;
            }

            RefreshLibrary();
            _status = "Saved: " + entry.DisplayName;
            AppState.SelectedSong = entry;

            // A file picked before the chart existed is applied here, which is the only moment
            // the two can be matched up.
            if (!string.IsNullOrEmpty(AppState.PendingLocalAudioPath))
            {
                if (_library.AttachLocalAudio(entry, AppState.PendingLocalAudioPath))
                {
                    _status = "Saved with your local audio: " + entry.DisplayName;
                }
                AppState.PendingLocalAudioPath = null;
            }

            _screen = Screen.SongSelect;
        }

        void StartSong(SongEntry entry, string difficulty)
        {
            LoadedSong song = _library.Load(entry, difficulty, out string error);

            if (song == null)
            {
                _status = error;
                return;
            }

            _screen = Screen.Gameplay;

            var host = new GameObject("Gameplay");
            _gameplay = host.AddComponent<GameplayController>();
            _gameplay.Finished += EndSong;
            _gameplay.Begin(song);
        }

        void EndSong(GameSession session)
        {
            if (_gameplay != null)
            {
                _gameplay.Finished -= EndSong;
                _gameplay.Stop();
                Destroy(_gameplay.gameObject);
                _gameplay = null;
            }

            if (session != null && AppState.SelectedSong != null)
            {
                AppState.LastScore = session.Score.Score;
                AppState.LastMaxCombo = session.Score.MaxCombo;
                AppState.LastAccuracy = session.Score.Accuracy;
                AppState.LastStars = session.Score.Stars;
                AppState.LastSongName = AppState.SelectedSong.DisplayName;
                AppState.LastSongArtist = AppState.SelectedSong.Artist;
                _screen = Screen.Results;
            }
            else
            {
                _screen = Screen.SongSelect;
            }
        }
    }
}
