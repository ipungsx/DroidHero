using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using BandHero.Chart;
using BandHero.Enchor;
using BandHero.Gameplay;
using BandHero.UI;

namespace BandHero.Core
{
    /// <summary>
    /// Builds the whole game in code: camera, backdrop, screens and gameplay host.
    ///
    /// There are no scenes or prefabs to wire up, so the project stays reviewable in a diff and
    /// the Android build has nothing to misconfigure. Menus are IMGUI; gameplay is sprites.
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
            Leaderboard,
            Themes,
            Calibration
        }

        // Built in Awake: SongLibrary's constructor reads Application.persistentDataPath, which
        // Unity forbids during MonoBehaviour construction (field initializers).
        SongLibrary _library;
        EnchorClient _enchor;
        LocalAudioPicker _picker;

        GameSettings _settings;
        Leaderboard _leaderboard;
        MenuBackdrop _backdrop;

        GameplayController _gameplay;
        Screen _screen = Screen.Menu;
        Screen _previousScreen = Screen.Menu;
        float _fittedAspect;

        string _searchQuery = "";
        EnchorSearchResult _searchResult;
        bool _searching;
        string _status = "";
        Vector2 _songScroll;
        Vector2 _searchScroll;
        Vector2 _boardScroll;
        int _searchPage = 1;

        float _fade;

        string SaveRoot => Application.persistentDataPath;

        void Awake()
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            UnityEngine.Screen.sleepTimeout = SleepTimeout.NeverSleep;

            _library = new SongLibrary();
            _enchor = new EnchorClient();
            _picker = new LocalAudioPicker();

            _settings = GameSettings.Load(SaveRoot);
            _leaderboard = new Leaderboard(SaveRoot);
            LaneTheme.Select(_settings.ThemeIndex);

            SetupCamera();
            BuildBackdrop();
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

        void BuildBackdrop()
        {
            var host = new GameObject("MenuBackdrop");
            host.transform.SetParent(transform, false);
            _backdrop = host.AddComponent<MenuBackdrop>();
            _backdrop.Build();
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
            camera.backgroundColor = LaneTheme.BackgroundColor;
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

            // Menus fade in when they open, which hides the one-frame pop of a new screen.
            if (_fade < 1f) _fade = Mathf.Min(1f, _fade + Time.unscaledDeltaTime * 5f);

            // In gameplay the HUD owns the back button, because pausing is what a player expects
            // there. Everywhere else it steps back a screen.
            if (_screen == Screen.Gameplay) return;
            if (!Input.GetKeyDown(KeyCode.Escape)) return;

            if (_screen == Screen.Menu) return;
            GoBack();
        }

        void OnGUI()
        {
            if (_screen == Screen.Gameplay) return;

            Color previous = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, _fade);
            GUI.depth = 0;

            // A scrim keeps menu text readable over the animated stage without hiding it.
            UiSkin.Fill(new Rect(0, 0, UnityEngine.Screen.width, UnityEngine.Screen.height),
                        new Color(0.03f, 0.03f, 0.06f, 0.58f));

            switch (_screen)
            {
                case Screen.Menu: DrawMenu(); break;
                case Screen.SongSelect: DrawSongSelect(); break;
                case Screen.EnchorSearch: DrawEnchorSearch(); break;
                case Screen.Results: DrawResults(); break;
                case Screen.Leaderboard: DrawLeaderboard(); break;
                case Screen.Themes: DrawThemes(); break;
                case Screen.Calibration: DrawCalibration(); break;
            }

            GUI.color = previous;

            if (!string.IsNullOrEmpty(_status))
            {
                DrawStatus();
            }
        }

        void DrawStatus()
        {
            float w = UnityEngine.Screen.width;
            float h = UnityEngine.Screen.height;
            float boxHeight = Scale(84);

            var box = new Rect(Scale(24), h - boxHeight - Scale(24), w - Scale(48), boxHeight);
            UiSkin.Card(box, new Color(0.06f, 0.06f, 0.10f, 0.92f));

            GUI.Label(new Rect(box.x + Scale(18), box.y + Scale(10), box.width - Scale(80), box.height - Scale(20)),
                      _status, UiSkin.Small);

            if (GUI.Button(new Rect(box.x + box.width - Scale(56), box.y + Scale(20), Scale(40), Scale(44)),
                           "x", UiSkin.ButtonGhost))
            {
                _status = "";
            }
        }

        static float Scale(float value) =>
            value * Mathf.Max(1f, UnityEngine.Screen.height / 1080f);

        void GoToMenu()
        {
            _screen = Screen.Menu;
            _fade = 0f;
            if (_backdrop != null) _backdrop.Pulse();
        }

        void GoTo(Screen screen)
        {
            _previousScreen = _screen;
            _screen = screen;
            _fade = 0f;
            if (_backdrop != null) _backdrop.Pulse();
        }

        void GoBack()
        {
            switch (_screen)
            {
                case Screen.SongSelect:
                case Screen.EnchorSearch:
                case Screen.Leaderboard:
                case Screen.Themes:
                case Screen.Calibration:
                    GoToMenu();
                    break;
                case Screen.Results:
                    GoTo(Screen.SongSelect);
                    break;
                default:
                    GoToMenu();
                    break;
            }
        }

        // ---------------------------------------------------------------- menu

        void DrawMenu()
        {
            float w = UnityEngine.Screen.width;
            float h = UnityEngine.Screen.height;

            // Title block, drawn large and spaced so it reads as a logo rather than a label.
            GUI.Label(new Rect(0, h * 0.10f, w, Scale(96)), "BAND HERO", UiSkin.Title);
            GUI.Label(new Rect(0, h * 0.10f + Scale(84), w, Scale(40)),
                      "TWO-DIMENSIONAL RHYTHM ACTION", UiSkin.Center);

            float buttonWidth = Mathf.Min(Scale(460), w * 0.78f);
            float x = (w - buttonWidth) * 0.5f;
            float y = h * 0.34f;
            float step = Scale(92);

            if (GUI.Button(new Rect(x, y, buttonWidth, Scale(76)), "PLAY", UiSkin.ButtonAccent))
            {
                RefreshLibrary();
                if (AppState.Songs.Count == 0)
                {
                    _status = "No charts yet. Download one from enchor.us.";
                    GoTo(Screen.EnchorSearch);
                }
                else
                {
                    GoTo(Screen.SongSelect);
                }
            }

            float halfWidth = (buttonWidth - Scale(16)) * 0.5f;

            if (GUI.Button(new Rect(x, y + step, halfWidth, Scale(70)), "CHARTS", UiSkin.Button))
            {
                GoTo(Screen.EnchorSearch);
            }

            if (GUI.Button(new Rect(x + halfWidth + Scale(16), y + step, halfWidth, Scale(70)),
                           "LEADERBOARD", UiSkin.Button))
            {
                GoTo(Screen.Leaderboard);
            }

            if (GUI.Button(new Rect(x, y + step * 2f, halfWidth, Scale(70)), "THEMES", UiSkin.Button))
            {
                GoTo(Screen.Themes);
            }

            if (GUI.Button(new Rect(x + halfWidth + Scale(16), y + step * 2f, halfWidth, Scale(70)),
                           "CALIBRATION", UiSkin.Button))
            {
                GoTo(Screen.Calibration);
            }

            DrawDifficultyRow(new Rect(x, y + step * 3f, buttonWidth, Scale(56)), buttonWidth);

            // Footer: what is installed and which theme is live.
            string footer = AppState.Songs.Count + " song(s)  ·  " + _settings.Difficulty + "  ·  " +
                            LaneTheme.Current.Name + "  ·  lights " + (_settings.LightShow ? "on" : "off");
            GUI.Label(new Rect(Scale(24), h - Scale(96), w - Scale(48), Scale(40)), footer, UiSkin.Small);

            if (GUI.Button(new Rect(w - Scale(120), Scale(20), Scale(96), Scale(52)), "QUIT", UiSkin.ButtonGhost))
            {
                Application.Quit();
            }
        }

        /// <summary>
        /// Difficulty is chosen before the song, because charts only contain the difficulties
        /// their author charted and the same song can be very different between them.
        /// </summary>
        void DrawDifficultyRow(Rect rect, float totalWidth)
        {
            GUI.Label(new Rect(rect.x, rect.y - Scale(30), totalWidth, Scale(28)), "DIFFICULTY", UiSkin.Tiny);

            float gap = Scale(10);
            float chipWidth = (totalWidth - gap * (ChartDifficulty.All.Length - 1)) / ChartDifficulty.All.Length;

            for (int i = 0; i < ChartDifficulty.All.Length; i++)
            {
                var chip = new Rect(rect.x + i * (chipWidth + gap), rect.y, chipWidth, rect.height);

                bool selected = i == _settings.DifficultyIndex;
                Color accent = DifficultyColor(i);

                if (UiSkin.Chip(chip, ChartDifficulty.All[i].ToUpperInvariant(), selected, accent))
                {
                    _settings.DifficultyIndex = i;
                    _settings.Save(SaveRoot);
                }
            }
        }

        static Color DifficultyColor(int index)
        {
            switch (index)
            {
                case 0: return new Color(0.45f, 0.92f, 0.55f);
                case 1: return new Color(0.45f, 0.78f, 1f);
                case 2: return new Color(1f, 0.78f, 0.32f);
                default: return new Color(1f, 0.42f, 0.46f);
            }
        }

        // ---------------------------------------------------------- song select

        void DrawSongSelect()
        {
            float w = UnityEngine.Screen.width;
            float h = UnityEngine.Screen.height;

            DrawHeader("SELECT A SONG", AppState.Songs.Count + " chart(s)");

            float listTop = Scale(122);
            var view = new Rect(Scale(20), listTop, w - Scale(40), h - listTop - Scale(96));

            if (AppState.Songs.Count == 0)
            {
                GUI.Label(view, "Your library is empty.\nDownload charts from enchor.us, or pick local audio.",
                          UiSkin.Body);
                return;
            }

            float rowHeight = Scale(136);
            var content = new Rect(0, 0, view.width - Scale(20), AppState.Songs.Count * rowHeight);
            _songScroll = GUI.BeginScrollView(view, _songScroll, content);

            for (int i = 0; i < AppState.Songs.Count; i++)
            {
                SongEntry song = AppState.Songs[i];
                var row = new Rect(0, i * rowHeight, content.width, rowHeight - Scale(12));

                UiSkin.Card(row, new Color(0.10f, 0.10f, 0.15f, 0.94f));

                // Album art column: charts carry their own art, and a tinted block stands in
                // when they do not so every row keeps the same shape.
                var art = new Rect(row.x + Scale(12), row.y + Scale(12), Scale(104), Scale(104));
                DrawAlbumArt(song, art);

                float textX = art.xMax + Scale(16);
                float textWidth = row.width - (textX - row.x) - Scale(300);

                GUI.Label(new Rect(textX, row.y + Scale(16), textWidth, Scale(36)),
                          song.DisplayName, UiSkin.CenterBold);
                GUI.Label(new Rect(textX, row.y + Scale(54), textWidth, Scale(30)),
                          song.Subtitle, UiSkin.Small);
                GUI.Label(new Rect(textX, row.y + Scale(84), textWidth, Scale(28)),
                          LengthText(song) + "  ·  best " + _leaderboard.BestScore(song.DisplayName, _settings.Difficulty).ToString("N0"),
                          UiSkin.Tiny);

                float playX = row.x + row.width - Scale(196);
                if (GUI.Button(new Rect(playX, row.y + Scale(40), Scale(88), Scale(56)), "PLAY", UiSkin.ButtonAccent))
                {
                    AppState.SelectedSong = song;
                    StartSong(song, _settings.Difficulty);
                }

                if (GUI.Button(new Rect(playX + Scale(96), row.y + Scale(40), Scale(88), Scale(56)),
                               song.HasLocalAudio ? "CHANGE" : "AUDIO", UiSkin.Button))
                {
                    AppState.SelectedSong = song;
                    StartCoroutine(AttachLocalAudio(song));
                }

                if (song.HasLocalAudio &&
                    GUI.Button(new Rect(row.x + row.width - Scale(48), row.y + Scale(14), Scale(36), Scale(36)),
                               "x", UiSkin.ButtonGhost))
                {
                    _library.DetachLocalAudio(song);
                    _status = song.DisplayName + " uses the chart audio again.";
                }
            }

            GUI.EndScrollView();

            if (GUI.Button(new Rect(Scale(20), h - Scale(84), Scale(200), Scale(60)), "LOCAL AUDIO", UiSkin.Button))
            {
                StartCoroutine(PickLocalAudio());
            }
        }

        void DrawAlbumArt(SongEntry song, Rect rect)
        {
            if (song.AlbumArt != null && song.AlbumArt.Length > 0)
            {
                if (song.AlbumTexture == null)
                {
                    var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (texture.LoadImage(song.AlbumArt)) song.AlbumTexture = texture;
                }

                if (song.AlbumTexture != null)
                {
                    GUI.DrawTexture(rect, song.AlbumTexture, ScaleMode.ScaleAndCrop, true);
                    return;
                }
            }

            // No art in the chart: a gradient block tinted by the theme still looks deliberate.
            UiSkin.VerticalGradient(rect, LaneTheme.AccentColor * 0.65f, LaneTheme.BackgroundGlowColor);
        }

        static string LengthText(SongEntry song)
        {
            if (song.LengthSeconds <= 0f) return "--:--";

            int total = Mathf.FloorToInt(song.LengthSeconds);
            return (total / 60) + ":" + (total % 60).ToString("00");
        }

        // -------------------------------------------------------------- enchor

        void DrawEnchorSearch()
        {
            float w = UnityEngine.Screen.width;
            float h = UnityEngine.Screen.height;

            DrawHeader("ENCHOR.US CHARTS", "search and download");

            float searchY = Scale(122);
            _searchQuery = GUI.TextField(new Rect(Scale(20), searchY, w - Scale(320), Scale(60)),
                                         _searchQuery ?? "", 64, UiSkin.TextField);

            if (GUI.Button(new Rect(w - Scale(290), searchY, Scale(130), Scale(60)), "SEARCH", UiSkin.ButtonAccent))
            {
                _searchPage = 1;
                StartCoroutine(RunSearch());
            }

            if (GUI.Button(new Rect(w - Scale(150), searchY, Scale(130), Scale(60)), "NEXT", UiSkin.Button))
            {
                _searchPage++;
                StartCoroutine(RunSearch());
            }

            var listView = new Rect(Scale(20), searchY + Scale(80), w - Scale(40), h - searchY - Scale(200));

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

            float rowHeight = Scale(150);
            var content = new Rect(0, 0, listView.width - Scale(20), _searchResult.data.Length * rowHeight);
            _searchScroll = GUI.BeginScrollView(listView, _searchScroll, content);

            for (int i = 0; i < _searchResult.data.Length; i++)
            {
                EnchorChart chart = _searchResult.data[i];
                var row = new Rect(0, i * rowHeight, content.width, rowHeight - Scale(12));
                UiSkin.Card(row, new Color(0.10f, 0.10f, 0.15f, 0.94f));

                GUI.Label(new Rect(row.x + Scale(16), row.y + Scale(14), row.width - Scale(220), Scale(34)),
                          chart.name ?? "Untitled", UiSkin.CenterBold);
                GUI.Label(new Rect(row.x + Scale(16), row.y + Scale(50), row.width - Scale(220), Scale(28)),
                          (chart.artist ?? "Unknown") + "  ·  " + (chart.charter ?? ""), UiSkin.Small);
                GUI.Label(new Rect(row.x + Scale(16), row.y + Scale(80), row.width - Scale(220), Scale(28)),
                          "Guitar " + DifficultyLabel(chart.diff_guitar) + "  ·  " +
                          "Drums " + DifficultyLabel(chart.diff_drums) + "  ·  " + chart.DisplayLength,
                          UiSkin.Tiny);

                if (GUI.Button(new Rect(row.x + row.width - Scale(170), row.y + Scale(42), Scale(150), Scale(60)),
                               "DOWNLOAD", UiSkin.ButtonAccent))
                {
                    StartCoroutine(DownloadChart(chart));
                }
            }

            GUI.EndScrollView();
        }

        static string DifficultyLabel(int value)
        {
            string[] labels = { "-", "Easy", "Normal", "Hard", "Expert", "Expert+" };
            return value > 0 && value < labels.Length ? labels[value] : "-";
        }

        // ------------------------------------------------------------- results

        void DrawResults()
        {
            float w = UnityEngine.Screen.width;
            float h = UnityEngine.Screen.height;

            GUI.Label(new Rect(0, Scale(64), w, Scale(80)), "SONG COMPLETE", UiSkin.Title);
            GUI.Label(new Rect(0, Scale(142), w, Scale(44)), AppState.LastSongName, UiSkin.Center);
            GUI.Label(new Rect(0, Scale(184), w, Scale(36)), AppState.LastSongArtist, UiSkin.Small);

            float cardWidth = Mathf.Min(Scale(560), w * 0.9f);
            var card = new Rect((w - cardWidth) * 0.5f, h * 0.27f, cardWidth, Scale(300));
            UiSkin.Card(card, new Color(0.09f, 0.09f, 0.14f, 0.95f));

            GUI.Label(new Rect(card.x, card.y + Scale(16), card.width, Scale(30)), "SCORE", UiSkin.Center);
            GUI.Label(new Rect(card.x, card.y + Scale(44), card.width, Scale(70)),
                      AppState.LastScore.ToString("N0"), UiSkin.Title);

            UiSkin.Stars(new Rect(card.x, card.y + Scale(112), card.width, Scale(44)), AppState.LastStars);

            float rowY = card.y + Scale(168);
            float rowHeight = Scale(38);
            float halfWidth = card.width * 0.5f;

            GUI.Label(new Rect(card.x + Scale(28), rowY, halfWidth, rowHeight),
                      "Max combo", UiSkin.Small);
            GUI.Label(new Rect(card.x + halfWidth, rowY, halfWidth - Scale(28), rowHeight),
                      AppState.LastMaxCombo.ToString(), UiSkin.CenterBold);

            GUI.Label(new Rect(card.x + Scale(28), rowY + rowHeight, halfWidth, rowHeight),
                      "Accuracy", UiSkin.Small);
            GUI.Label(new Rect(card.x + halfWidth, rowY + rowHeight, halfWidth - Scale(28), rowHeight),
                      AppState.LastAccuracy.ToString("F1") + "%", UiSkin.CenterBold);

            GUI.Label(new Rect(card.x + Scale(28), rowY + rowHeight * 2f, halfWidth, rowHeight),
                      "Difficulty", UiSkin.Small);
            GUI.Label(new Rect(card.x + halfWidth, rowY + rowHeight * 2f, halfWidth - Scale(28), rowHeight),
                      _settings.Difficulty, UiSkin.CenterBold);

            if (AppState.LastRank > 0)
            {
                GUI.Label(new Rect(0, card.yMax + Scale(8), w, Scale(40)),
                          "Leaderboard rank #" + AppState.LastRank + " on " + _settings.Difficulty,
                          UiSkin.CenterBold);
            }

            float buttonWidth = Mathf.Min(Scale(440), w * 0.8f);
            float buttonX = (w - buttonWidth) * 0.5f;

            if (GUI.Button(new Rect(buttonX, h - Scale(200), buttonWidth, Scale(70)), "PLAY AGAIN", UiSkin.ButtonAccent))
            {
                if (AppState.SelectedSong != null) StartSong(AppState.SelectedSong, _settings.Difficulty);
            }

            if (GUI.Button(new Rect(buttonX, h - Scale(118), buttonWidth, Scale(70)), "MAIN MENU", UiSkin.Button))
            {
                GoToMenu();
            }
        }

        // --------------------------------------------------------- leaderboard

        void DrawLeaderboard()
        {
            float w = UnityEngine.Screen.width;
            float h = UnityEngine.Screen.height;

            DrawHeader("LEADERBOARD", "best runs on this device");

            var view = new Rect(Scale(20), Scale(122), w - Scale(40), h - Scale(220));

            if (AppState.SelectedSong != null)
            {
                List<ScoreRecord> top = _leaderboard.Top(AppState.SelectedSong.DisplayName, _settings.Difficulty);
                GUI.Label(new Rect(view.x, view.y - Scale(30), view.width, Scale(30)),
                          AppState.SelectedSong.DisplayName + "  ·  " + _settings.Difficulty, UiSkin.Tiny);

                if (top.Count == 0)
                {
                    GUI.Label(new Rect(view.x, view.y + Scale(20), view.width, Scale(80)),
                              "No runs yet for this song at " + _settings.Difficulty + ".\nPlay it to set a score.",
                              UiSkin.Body);
                    DrawLeaderboardFooter(w, h);
                    return;
                }

                DrawScoreTable(view, top);
                DrawLeaderboardFooter(w, h);
                return;
            }

            List<ScoreRecord> best = _leaderboard.BestPerSong();
            if (best.Count == 0)
            {
                GUI.Label(new Rect(view.x, view.y + Scale(20), view.width, Scale(80)),
                          "No scores yet.\nFinish a song and your best run lands here.", UiSkin.Body);
                DrawLeaderboardFooter(w, h);
                return;
            }

            float rowHeight = Scale(84);
            var content = new Rect(0, 0, view.width - Scale(20), best.Count * rowHeight);
            _boardScroll = GUI.BeginScrollView(view, _boardScroll, content);

            for (int i = 0; i < best.Count; i++)
            {
                ScoreRecord record = best[i];
                var row = new Rect(0, i * rowHeight, content.width, rowHeight - Scale(10));
                UiSkin.Card(row, new Color(0.10f, 0.10f, 0.15f, 0.94f));

                GUI.Label(new Rect(row.x + Scale(16), row.y + Scale(10), Scale(48), Scale(48)),
                          (i + 1).ToString(), UiSkin.CenterBold);
                GUI.Label(new Rect(row.x + Scale(70), row.y + Scale(8), row.width - Scale(260), Scale(34)),
                          record.Song, UiSkin.CenterBold);
                GUI.Label(new Rect(row.x + Scale(70), row.y + Scale(42), row.width - Scale(260), Scale(28)),
                          record.Difficulty + "  ·  " + record.Accuracy.ToString("F1") + "%  ·  x" + record.MaxCombo,
                          UiSkin.Tiny);
                GUI.Label(new Rect(row.x + row.width - Scale(190), row.y + Scale(14), Scale(174), Scale(44)),
                          record.Score.ToString("N0"), UiSkin.CenterBold);
            }

            GUI.EndScrollView();
            DrawLeaderboardFooter(w, h);
        }

        void DrawScoreTable(Rect view, List<ScoreRecord> records)
        {
            float rowHeight = Scale(76);

            for (int i = 0; i < records.Count; i++)
            {
                ScoreRecord record = records[i];
                var row = new Rect(view.x, view.y + i * rowHeight, view.width, rowHeight - Scale(10));
                UiSkin.Card(row, new Color(0.10f, 0.10f, 0.15f, 0.94f));

                Color rankColor = i == 0 ? UiSkin.Gold : UiSkin.TextPrimary;
                var rankStyle = new GUIStyle(UiSkin.CenterBold) { normal = { textColor = rankColor } };

                GUI.Label(new Rect(row.x + Scale(16), row.y + Scale(8), Scale(54), Scale(48)),
                          "#" + (i + 1), rankStyle);
                GUI.Label(new Rect(row.x + Scale(76), row.y + Scale(8), row.width - Scale(280), Scale(32)),
                          record.Score.ToString("N0") + "  ·  " + record.Accuracy.ToString("F1") + "%",
                          UiSkin.CenterBold);
                GUI.Label(new Rect(row.x + Scale(76), row.y + Scale(40), row.width - Scale(280), Scale(26)),
                          "x" + record.MaxCombo + "  ·  " + record.PlayedAtText, UiSkin.Tiny);
                UiSkin.Stars(new Rect(row.x + row.width - Scale(150), row.y + Scale(16), Scale(130), Scale(36)),
                             record.Stars);
            }
        }

        void DrawLeaderboardFooter(float w, float h)
        {
            float buttonWidth = (w - Scale(60)) * 0.5f;

            if (GUI.Button(new Rect(Scale(20), h - Scale(84), buttonWidth, Scale(60)), "ALL SONGS", UiSkin.Button))
            {
                AppState.SelectedSong = null;
            }

            if (GUI.Button(new Rect(Scale(40) + buttonWidth, h - Scale(84), buttonWidth, Scale(60)),
                           "CLEAR", UiSkin.ButtonGhost))
            {
                _status = "Leaderboard kept on this device; nothing is uploaded.";
            }
        }

        // -------------------------------------------------------------- themes

        void DrawThemes()
        {
            float w = UnityEngine.Screen.width;
            float h = UnityEngine.Screen.height;

            DrawHeader("THEMES", "gameplay look and light show");

            float rowHeight = Scale(112);
            float top = Scale(122);

            for (int i = 0; i < GameplayTheme.Count; i++)
            {
                GameplayTheme theme = GameplayTheme.At(i);
                var row = new Rect(Scale(20), top + i * rowHeight, w - Scale(40), rowHeight - Scale(12));
                bool selected = i == _settings.ThemeIndex;

                UiSkin.Card(row, selected
                    ? new Color(theme.Accent.r * 0.45f, theme.Accent.g * 0.45f, theme.Accent.b * 0.45f, 0.96f)
                    : new Color(0.10f, 0.10f, 0.15f, 0.94f));

                // A miniature of the five lanes, so the choice is visible before selecting it.
                float swatch = Scale(28);
                for (int lane = 0; lane < LaneInfo.Count; lane++)
                {
                    var chip = new Rect(row.x + Scale(16) + lane * (swatch + Scale(4)),
                                        row.y + Scale(20), swatch, Scale(58));
                    UiSkin.Fill(chip, theme.Lane(lane));
                }

                GUI.Label(new Rect(row.x + Scale(190), row.y + Scale(14), row.width - Scale(260), Scale(34)),
                          theme.Name, UiSkin.CenterBold);
                GUI.Label(new Rect(row.x + Scale(190), row.y + Scale(50), row.width - Scale(260), Scale(30)),
                          theme.Blurb, UiSkin.Tiny);

                if (GUI.Button(new Rect(row.x + row.width - Scale(56), row.y + Scale(20), Scale(44), Scale(60)),
                               selected ? "ON" : "SET", selected ? UiSkin.ButtonAccent : UiSkin.Button))
                {
                    _settings.ThemeIndex = i;
                    _settings.Save(SaveRoot);
                    LaneTheme.Select(i);
                    if (_backdrop != null) _backdrop.ApplyTheme();
                    Camera.main.backgroundColor = LaneTheme.BackgroundColor;
                }
            }

            float toggleY = top + GameplayTheme.Count * rowHeight + Scale(8);
            var toggleRow = new Rect(Scale(20), toggleY, w - Scale(40), Scale(88));
            UiSkin.Card(toggleRow, new Color(0.10f, 0.10f, 0.15f, 0.94f));

            GUI.Label(new Rect(toggleRow.x + Scale(16), toggleRow.y + Scale(12), toggleRow.width - Scale(200), Scale(34)),
                      "LIGHT SHOW", UiSkin.CenterBold);
            GUI.Label(new Rect(toggleRow.x + Scale(16), toggleRow.y + Scale(46), toggleRow.width - Scale(200), Scale(30)),
                      "Stage lights blink on every drum hit", UiSkin.Tiny);

            if (GUI.Button(new Rect(toggleRow.x + toggleRow.width - Scale(150), toggleRow.y + Scale(16),
                                    Scale(130), Scale(56)),
                           _settings.LightShow ? "ON" : "OFF",
                           _settings.LightShow ? UiSkin.ButtonAccent : UiSkin.Button))
            {
                _settings.LightShow = !_settings.LightShow;
                _settings.Save(SaveRoot);
            }
        }

        // --------------------------------------------------------- calibration

        void DrawCalibration()
        {
            float w = UnityEngine.Screen.width;
            float h = UnityEngine.Screen.height;

            DrawHeader("AUDIO CALIBRATION", AudioCalibration.Describe(_library.CalibrationMilliseconds));

            GUI.Label(new Rect(Scale(24), h * 0.22f, w - Scale(48), Scale(140)),
                      "Notes land early or late? Nudge the offset until notes sit on the strike line "
                      + "when you strum on the beat.\n\nLocal music files often carry a short silence "
                      + "at the start that chart-embedded audio does not, so this is worth setting once "
                      + "per device.", UiSkin.Body);

            float buttonWidth = Scale(140);
            float centerX = w * 0.5f;
            float rowY = h * 0.48f;

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
                           "RESET", UiSkin.ButtonGhost))
            {
                _library.CalibrationMilliseconds = 0;
            }

            GUI.Label(new Rect(Scale(24), h - Scale(120), w - Scale(48), Scale(90)),
                      "Range " + AudioCalibration.MinMilliseconds + " to " + AudioCalibration.MaxMilliseconds
                      + " ms. The offset is applied to every chart, on top of the offset the chart declares.",
                      UiSkin.Small);
        }

        void DrawHeader(string title, string subtitle)
        {
            float w = UnityEngine.Screen.width;

            GUI.Label(new Rect(Scale(20), Scale(24), w - Scale(160), Scale(52)), title, UiSkin.Heading);
            GUI.Label(new Rect(Scale(20), Scale(72), w - Scale(160), Scale(28)), subtitle, UiSkin.Tiny);

            if (GUI.Button(new Rect(w - Scale(120), Scale(28), Scale(100), Scale(56)), "BACK", UiSkin.ButtonGhost))
            {
                GoBack();
            }
        }

        // ----------------------------------------------------------- coroutines

        IEnumerator PickLocalAudio()
        {
            _status = "Opening the audio picker...";

            string picked = null;
            string error = null;

            yield return StartCoroutine(_picker.PickRoutine(path => picked = path, message => error = message));

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
                GoTo(Screen.EnchorSearch);
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
            GoTo(Screen.SongSelect);
        }

        IEnumerator AttachLocalAudio(SongEntry song)
        {
            _status = "Pick an audio file for " + song.DisplayName + "...";

            string picked = null;
            string error = null;

            yield return StartCoroutine(_picker.PickRoutine(path => picked = path, message => error = message));

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

            // The API takes lowercase section names (easy/medium/hard/expert), where our
            // difficulty is called Normal.
            string apiDifficulty = ChartDifficulty.ToSectionName(_settings.Difficulty).ToLowerInvariant();

            yield return StartCoroutine(_enchor.Search(_searchQuery, _searchPage, EnchorClient.InstrumentGuitar,
                                                       apiDifficulty,
                                                       result => _searchResult = result,
                                                       error => _status = error));

            _searching = false;
        }

        IEnumerator DownloadChart(EnchorChart chart)
        {
            _status = "Downloading " + chart.name + "...";

            byte[] data = null;
            string error = null;

            yield return StartCoroutine(_enchor.DownloadChart(chart.md5,
                                                             bytes => data = bytes,
                                                             message => error = message,
                                                             fraction => _status = "Downloading " + chart.name +
                                                                                   "  " + Mathf.RoundToInt(fraction * 100f) + "%"));

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

            GoTo(Screen.SongSelect);
        }

        // ------------------------------------------------------------ gameplay

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
            _gameplay.RestartRequested += RestartSong;
            _gameplay.Begin(song, LaneTheme.Current, _settings.LightShow);
        }

        void RestartSong()
        {
            SongEntry entry = AppState.SelectedSong;
            if (entry == null) return;

            TearDownGameplay();
            StartSong(entry, _settings.Difficulty);
        }

        void TearDownGameplay()
        {
            if (_gameplay == null) return;

            _gameplay.Finished -= EndSong;
            _gameplay.RestartRequested -= RestartSong;
            _gameplay.Stop();
            Destroy(_gameplay.gameObject);
            _gameplay = null;
        }

        void EndSong(GameSession session)
        {
            TearDownGameplay();

            if (session == null || AppState.SelectedSong == null)
            {
                GoTo(Screen.SongSelect);
                return;
            }

            AppState.LastScore = session.Score.Score;
            AppState.LastMaxCombo = session.Score.MaxCombo;
            AppState.LastAccuracy = session.Score.Accuracy;
            AppState.LastStars = session.Score.Stars;
            AppState.LastSongName = AppState.SelectedSong.DisplayName;
            AppState.LastSongArtist = AppState.SelectedSong.Artist;

            var record = new ScoreRecord
            {
                Song = AppState.SelectedSong.DisplayName,
                Artist = AppState.SelectedSong.Artist,
                Difficulty = _settings.Difficulty,
                Score = session.Score.Score,
                MaxCombo = session.Score.MaxCombo,
                Accuracy = session.Score.Accuracy,
                Stars = session.Score.Stars,
            };

            _leaderboard.Submit(record);
            AppState.LastRank = RankOf(record);

            GoTo(Screen.Results);
        }

        /// <summary>Position of a submitted run in its own song/difficulty group, or 0 if unplaced.</summary>
        int RankOf(ScoreRecord record)
        {
            List<ScoreRecord> top = _leaderboard.Top(record.Song, record.Difficulty);

            for (int i = 0; i < top.Count; i++)
            {
                if (top[i].Score == record.Score &&
                    Mathf.Approximately((float)top[i].Accuracy, (float)record.Accuracy) &&
                    top[i].MaxCombo == record.MaxCombo)
                {
                    return i + 1;
                }
            }

            return 0;
        }
    }
}
