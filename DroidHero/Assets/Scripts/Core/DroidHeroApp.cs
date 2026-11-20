using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DroidHero.Chart;
using DroidHero.Enchor;
using DroidHero.Gameplay;
using DroidHero.UI;

namespace DroidHero.Core
{
    /// <summary>
    /// Builds the whole game in code: camera, backdrop, screens and gameplay host.
    ///
    /// There are no scenes or prefabs to wire up, so the project stays reviewable in a diff and
    /// the Android build has nothing to misconfigure. Menus are IMGUI; gameplay is sprites.
    /// </summary>
    public class DroidHeroApp : MonoBehaviour
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
        Vector2 _themeScroll;
        int _searchPage = 1;

        /// <summary>Difficulty the running (or last) song was actually loaded at.</summary>
        string _activeDifficulty = "";

        float _fade;

        string SaveRoot => Application.persistentDataPath;

        /// <summary>
        /// Height of the status toast, which every screen reserves before laying out its own
        /// footer. Without this a toast would sit on top of the bottom buttons and swallow taps.
        /// </summary>
        float StatusInset => string.IsNullOrEmpty(_status) ? 0f : Scale(84) + Scale(16);

        /// <summary>Bottom edge a screen may draw to without colliding with the status toast.</summary>
        float ContentBottom => UnityEngine.Screen.height - Scale(24) - StatusInset;

        void Awake()
        {
            Application.targetFrameRate = 60;
            QualitySettings.vSyncCount = 0;
            UnityEngine.Screen.sleepTimeout = SleepTimeout.NeverSleep;

            // Portrait is the only layout the menus and the five-lane highway are sized for, so
            // it is enforced at runtime as well as in the manifest: a device that ignores the
            // manifest's screenOrientation would otherwise rotate mid-song.
            UnityEngine.Screen.orientation = ScreenOrientation.Portrait;

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

            if (UnityEngine.Object.FindFirstObjectByType<AudioListener>() == null) camera.gameObject.AddComponent<AudioListener>();

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

            // Anchored to the very bottom, and every screen keeps its footer above ContentBottom,
            // so the toast never covers a button.
            var box = new Rect(Scale(24), h - boxHeight - Scale(20), w - Scale(48), boxHeight);
            UiSkin.Card(box, new Color(0.06f, 0.06f, 0.10f, 0.96f));

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
                    // A selected song opens a detail panel; back closes the panel before
                    // leaving the screen, so the two steps behave like a stack.
                    if (AppState.SelectedSong != null) AppState.SelectedSong = null;
                    else GoToMenu();
                    break;
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
            GUI.Label(new Rect(0, h * 0.08f, w, Scale(96)), "DROID HERO", UiSkin.Title);
            GUI.Label(new Rect(0, h * 0.08f + Scale(88), w, Scale(40)),
                      "TWO-DIMENSIONAL RHYTHM ACTION", UiSkin.Center);

            float buttonWidth = Mathf.Min(Scale(460), w * 0.78f);
            float x = (w - buttonWidth) * 0.5f;
            float y = h * 0.30f;
            float step = Scale(92);
            float halfWidth = (buttonWidth - Scale(16)) * 0.5f;

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
                    AppState.SelectedSong = null;
                    GoTo(Screen.SongSelect);
                }
            }

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

            // Difficulty lives on the song screen, so the menu only points the way there.
            GUI.Label(new Rect(x, y + step * 3f, buttonWidth, Scale(28)), "DIFFICULTY", UiSkin.Tiny);
            GUI.Label(new Rect(x, y + step * 3f + Scale(28), buttonWidth, Scale(40)),
                      "chosen per song", UiSkin.Center);

            string footer = AppState.Songs.Count + " song(s)  ·  " + LaneTheme.Current.Name +
                            "  ·  lights " + (_settings.LightShow ? "on" : "off");
            GUI.Label(new Rect(Scale(24), ContentBottom - Scale(46), w - Scale(48), Scale(40)), footer, UiSkin.Small);

            // QUIT sits in the header row, below the title block, so it never overlaps it.
            if (GUI.Button(new Rect(w - Scale(116), Scale(20), Scale(96), Scale(52)), "QUIT", UiSkin.ButtonGhost))
            {
                Application.Quit();
            }
        }

        /// <summary>
        /// Difficulty chips for one song, limited to the sections that song actually charts.
        ///
        /// enchor.us does not list a chart's difficulties, so they are read out of the package
        /// when it is saved. A song with only Expert therefore offers Expert alone, instead of
        /// letting a player pick a difficulty that would load zero notes.
        /// </summary>
        void DrawDifficultyRow(Rect rect, float totalWidth, SongEntry song)
        {
            List<string> available = AvailableDifficulties(song);

            GUI.Label(new Rect(rect.x, rect.y - Scale(32), totalWidth, Scale(28)),
                      available.Count > 1 ? "DIFFICULTY" : "DIFFICULTY  ·  only one charted", UiSkin.Tiny);

            float gap = Scale(10);
            float chipWidth = (totalWidth - gap * (available.Count - 1)) / available.Count;

            for (int i = 0; i < available.Count; i++)
            {
                string difficulty = available[i];
                var chip = new Rect(rect.x + i * (chipWidth + gap), rect.y, chipWidth, rect.height);

                bool selected = string.Equals(difficulty, DifficultyFor(song), StringComparison.OrdinalIgnoreCase);
                Color accent = DifficultyColor(ChartDifficulty.IndexOf(difficulty));

                if (UiSkin.Chip(chip, difficulty.ToUpperInvariant(), selected, accent))
                {
                    _library.RememberDifficulty(song, difficulty);
                }
            }
        }

        /// <summary>
        /// Difficulties offered for a song: those it charts, or every difficulty when the
        /// package could not be introspected so an unknown chart is never locked out.
        /// </summary>
        static List<string> AvailableDifficulties(SongEntry song)
        {
            if (song != null && song.Difficulties != null && song.Difficulties.Count > 0)
            {
                return song.Difficulties;
            }

            return new List<string>(ChartDifficulty.All);
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

            DrawHeader("SELECT A SONG", AppState.Songs.Count + " chart(s)");

            if (AppState.Songs.Count == 0)
            {
                GUI.Label(new Rect(Scale(24), Scale(130), w - Scale(48), Scale(140)),
                          "Your library is empty.\nDownload charts from enchor.us, or pick local audio.",
                          UiSkin.Body);

                if (GUI.Button(new Rect(Scale(24), ContentBottom - Scale(72), Scale(240), Scale(64)),
                               "BROWSE ENCHOR", UiSkin.ButtonAccent))
                {
                    GoTo(Screen.EnchorSearch);
                }

                return;
            }

            if (AppState.SelectedSong != null && AppState.Songs.Contains(AppState.SelectedSong))
            {
                DrawSongDetail(w);
                return;
            }

            DrawSongList(w);
        }

        /// <summary>Step one: the list. Tapping a row opens that song's detail panel.</summary>
        void DrawSongList(float w)
        {
            float listTop = Scale(122);
            float footerHeight = Scale(84);

            var view = new Rect(Scale(20), listTop, w - Scale(40),
                                Mathf.Max(Scale(120), ContentBottom - listTop - footerHeight));

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
                float textWidth = Mathf.Max(Scale(120), row.width - (textX - row.x) - Scale(140));

                GUI.Label(new Rect(textX, row.y + Scale(18), textWidth, Scale(36)),
                          song.DisplayName, UiSkin.CenterBold);
                GUI.Label(new Rect(textX, row.y + Scale(56), textWidth, Scale(30)),
                          song.Subtitle, UiSkin.Small);
                GUI.Label(new Rect(textX, row.y + Scale(88), textWidth, Scale(28)),
                          LengthText(song) + "  ·  " + DifficultySummary(song),
                          UiSkin.Tiny);

                if (GUI.Button(new Rect(row.x + row.width - Scale(124), row.y + Scale(38),
                                        Scale(104), Scale(58)),
                               "SELECT", UiSkin.ButtonAccent))
                {
                    AppState.SelectedSong = song;
                    _status = "";
                }
            }

            GUI.EndScrollView();

            // Footer row: the two ways to add music, side by side and clear of the list.
            float footerY = ContentBottom - Scale(64);
            float halfWidth = (w - Scale(64)) * 0.5f;

            if (GUI.Button(new Rect(Scale(20), footerY, halfWidth, Scale(60)), "LOCAL AUDIO", UiSkin.Button))
            {
                StartCoroutine(PickLocalAudio());
            }

            if (GUI.Button(new Rect(Scale(44) + halfWidth, footerY, halfWidth, Scale(60)),
                           "GET CHARTS", UiSkin.Button))
            {
                GoTo(Screen.EnchorSearch);
            }
        }

        /// <summary>
        /// Step two: one song with its own difficulty chips. Difficulty is only offered once a
        /// song is chosen, because the chips depend on the sections that song contains.
        /// </summary>
        void DrawSongDetail(float w)
        {
            SongEntry song = AppState.SelectedSong;

            float panelWidth = Mathf.Min(Scale(680), w - Scale(40));
            float panelX = (w - panelWidth) * 0.5f;

            // The panel is a fixed stack of rows. On a short phone with the status toast up the
            // stack would run under the toast, so the art shrinks and the gaps compress; the
            // buttons keep their tap size.
            float top = Scale(122);
            float artSide = Scale(150);
            float ghostRow = song.HasLocalAudio ? Scale(56) : 0f;
            float fixedHeight = artSide + Scale(62) + Scale(34) + Scale(78) + Scale(64) + ghostRow;
            float gapTotal = Scale(46 + 22 + 18 + 16) + (song.HasLocalAudio ? Scale(16) : 0f);

            float room = ContentBottom - top;
            if (fixedHeight > room)
            {
                artSide = Mathf.Max(Scale(96), artSide - (fixedHeight - room));
                fixedHeight = artSide + Scale(62) + Scale(34) + Scale(78) + Scale(64) + ghostRow;
            }

            float gapScale = Mathf.Clamp01((room - fixedHeight) / Mathf.Max(1f, gapTotal));
            float Gap(float value) => Scale(value) * gapScale;

            var art = new Rect(panelX, top, artSide, artSide);
            DrawAlbumArt(song, art);

            float textX = art.xMax + Scale(20);
            float textWidth = panelX + panelWidth - textX;

            float textStep = artSide / Scale(150);
            GUI.Label(new Rect(textX, art.y + Scale(6) * textStep, textWidth, Scale(44) * textStep),
                      song.DisplayName, UiSkin.CenterBold);
            GUI.Label(new Rect(textX, art.y + Scale(54) * textStep, textWidth, Scale(32) * textStep),
                      song.Artist, UiSkin.Small);
            GUI.Label(new Rect(textX, art.y + Scale(90) * textStep, textWidth, Scale(30) * textStep),
                      LengthText(song) + (song.HasLocalAudio ? "  ·  local audio" : "  ·  chart audio"),
                      UiSkin.Tiny);
            GUI.Label(new Rect(textX, art.y + Scale(120) * textStep, textWidth, Scale(30) * textStep),
                      song.Charter.Length > 0 ? "charted by " + song.Charter : "charter unknown", UiSkin.Tiny);

            float chipTop = art.yMax + Gap(46);
            var chipRect = new Rect(panelX, chipTop, panelWidth, Scale(62));
            DrawDifficultyRow(chipRect, panelWidth, song);

            // Best score for the difficulty currently selected, so the chip choice has context.
            float bestY = chipRect.yMax + Gap(22);
            GUI.Label(new Rect(panelX, bestY, panelWidth, Scale(34)),
                      "Best on " + DifficultyFor(song) + ": " +
                      _leaderboard.BestScore(song.DisplayName, DifficultyFor(song)).ToString("N0"),
                      UiSkin.Center);

            float buttonWidth = Mathf.Min(Scale(560), w - Scale(40));
            float buttonX = (w - buttonWidth) * 0.5f;
            float halfWidth = (buttonWidth - Scale(16)) * 0.5f;
            float startY = bestY + Scale(34) + Gap(18);

            if (GUI.Button(new Rect(buttonX, startY, buttonWidth, Scale(78)), "START", UiSkin.ButtonAccent))
            {
                StartSong(song, DifficultyFor(song));
                return;
            }

            float secondaryY = startY + Scale(78) + Gap(16);

            if (GUI.Button(new Rect(buttonX, secondaryY, halfWidth, Scale(64)),
                           song.HasLocalAudio ? "CHANGE AUDIO" : "USE MY AUDIO", UiSkin.Button))
            {
                StartCoroutine(AttachLocalAudio(song));
            }

            if (GUI.Button(new Rect(buttonX + halfWidth + Scale(16), secondaryY, halfWidth, Scale(64)),
                           "BACK TO LIST", UiSkin.Button))
            {
                AppState.SelectedSong = null;
            }

            if (song.HasLocalAudio &&
                GUI.Button(new Rect(buttonX, secondaryY + Scale(64) + Gap(16), buttonWidth, Scale(56)),
                           "USE CHART AUDIO INSTEAD", UiSkin.ButtonGhost))
            {
                _library.DetachLocalAudio(song);
                _status = song.DisplayName + " uses the chart audio again.";
            }
        }

        /// <summary>
        /// The difficulty a song is set to: what the player last chose for it, resolved against
        /// the sections the chart really carries. The app-wide setting is only a first-run
        /// default, which is why two songs can sit on different difficulties at once.
        /// </summary>
        string DifficultyFor(SongEntry song) => _library.DifficultyFor(song, _settings.Difficulty);

        /// <summary>Compact list of the difficulties a chart carries, for the song row.</summary>
        static string DifficultySummary(SongEntry song)
        {
            List<string> available = AvailableDifficulties(song);
            return string.Join("/", available.ToArray());
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

            DrawHeader("ENCHOR.US CHARTS", "search and download");

            float searchY = Scale(122);
            float fieldHeight = Scale(60);

            // Two button columns are reserved on the right, so the field can never run under them.
            float buttonWidth = Scale(116);
            float gap = Scale(10);
            float buttonsWidth = buttonWidth * 2f + gap;
            float fieldWidth = Mathf.Max(Scale(120), w - Scale(40) - buttonsWidth - Scale(12));

            _searchQuery = GUI.TextField(new Rect(Scale(20), searchY, fieldWidth, fieldHeight),
                                         _searchQuery ?? "", 64, UiSkin.TextField);

            float buttonsX = Scale(20) + fieldWidth + Scale(12);

            if (GUI.Button(new Rect(buttonsX, searchY, buttonWidth, fieldHeight), "SEARCH", UiSkin.ButtonAccent))
            {
                _searchPage = 1;
                StartCoroutine(RunSearch());
            }

            bool hasResults = _searchResult != null && _searchResult.data != null && _searchResult.data.Length > 0;
            bool canPage = hasResults && !_searching;

            // "found" is the total the API matched, so the last page is the one that reaches it.
            // Without this NEXT would happily walk past the end into an empty page.
            bool hasNextPage = canPage && _searchPage * EnchorClient.PageSize < _searchResult.found;

            if (GUI.Button(new Rect(buttonsX + buttonWidth + gap, searchY, buttonWidth, fieldHeight),
                           "NEXT", hasNextPage ? UiSkin.Button : UiSkin.ButtonGhost))
            {
                if (hasNextPage)
                {
                    _searchPage++;
                    StartCoroutine(RunSearch());
                }
            }

            float pagerY = searchY + fieldHeight + Scale(12);
            bool canPrev = canPage && _searchPage > 1;

            if (GUI.Button(new Rect(Scale(20), pagerY, Scale(116), Scale(52)),
                           "PREV", canPrev ? UiSkin.Button : UiSkin.ButtonGhost))
            {
                if (canPrev)
                {
                    _searchPage--;
                    StartCoroutine(RunSearch());
                }
            }

            GUI.Label(new Rect(Scale(148), pagerY, w - Scale(168), Scale(52)),
                      "Page " + _searchPage + (hasResults ? "  ·  " + _searchResult.found + " charts found" : ""),
                      UiSkin.Tiny);

            var listView = new Rect(Scale(20), pagerY + Scale(64), w - Scale(40),
                                    Mathf.Max(Scale(120), ContentBottom - pagerY - Scale(64)));

            if (_searching)
            {
                GUI.Label(listView, "Searching enchor.us...", UiSkin.Body);
                return;
            }

            if (!hasResults)
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

                float textWidth = Mathf.Max(Scale(120), row.width - Scale(170) - Scale(16));

                GUI.Label(new Rect(row.x + Scale(16), row.y + Scale(14), textWidth, Scale(34)),
                          chart.name ?? "Untitled", UiSkin.CenterBold);
                GUI.Label(new Rect(row.x + Scale(16), row.y + Scale(50), textWidth, Scale(28)),
                          (chart.artist ?? "Unknown") + "  ·  " + (chart.charter ?? ""), UiSkin.Small);
                GUI.Label(new Rect(row.x + Scale(16), row.y + Scale(80), textWidth, Scale(28)),
                          "Guitar " + DifficultyLabel(chart.diff_guitar) + "  ·  " +
                          "Drums " + DifficultyLabel(chart.diff_drums) + "  ·  " + chart.DisplayLength,
                          UiSkin.Tiny);

                if (GUI.Button(new Rect(row.x + row.width - Scale(160), row.y + Scale(42), Scale(144), Scale(60)),
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

            float buttonWidth = Mathf.Min(Scale(440), w * 0.8f);
            float buttonX = (w - buttonWidth) * 0.5f;
            float menuY = ContentBottom - Scale(70);
            float playAgainY = menuY - Scale(86);

            float cardWidth = Mathf.Min(Scale(560), w * 0.9f);
            float cardHeight = Scale(300);

            // The card is centred by preference but slides up on short screens, so it and the
            // rank line always stay clear of the buttons underneath.
            float cardTop = Mathf.Min(h * 0.27f, playAgainY - cardHeight - Scale(60));
            cardTop = Mathf.Max(Scale(228), cardTop);

            var card = new Rect((w - cardWidth) * 0.5f, cardTop, cardWidth, cardHeight);
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
                      _activeDifficulty.Length > 0 ? _activeDifficulty : _settings.Difficulty, UiSkin.CenterBold);

            if (AppState.LastRank > 0)
            {
                GUI.Label(new Rect(0, card.yMax + Scale(8), w, Scale(40)),
                          "Leaderboard rank #" + AppState.LastRank + " on " + _activeDifficulty,
                          UiSkin.CenterBold);
            }

            if (GUI.Button(new Rect(buttonX, playAgainY, buttonWidth, Scale(70)),
                           "PLAY AGAIN", UiSkin.ButtonAccent))
            {
                if (AppState.SelectedSong != null) StartSong(AppState.SelectedSong, DifficultyFor(AppState.SelectedSong));
            }

            if (GUI.Button(new Rect(buttonX, menuY, buttonWidth, Scale(70)), "MAIN MENU", UiSkin.Button))
            {
                GoToMenu();
            }
        }

        // --------------------------------------------------------- leaderboard

        void DrawLeaderboard()
        {
            float w = UnityEngine.Screen.width;

            DrawHeader("LEADERBOARD", "best runs on this device");

            float footerHeight = Scale(84);
            var view = new Rect(Scale(20), Scale(150), w - Scale(40),
                                Mathf.Max(Scale(120), ContentBottom - Scale(150) - footerHeight));

            if (AppState.SelectedSong != null)
            {
                GUI.Label(new Rect(Scale(20), Scale(122), w - Scale(40), Scale(28)),
                          AppState.SelectedSong.DisplayName + "  ·  " +
                          DifficultyFor(AppState.SelectedSong), UiSkin.Tiny);

                string songDifficulty = DifficultyFor(AppState.SelectedSong);
                List<ScoreRecord> top = _leaderboard.Top(AppState.SelectedSong.DisplayName, songDifficulty);

                if (top.Count == 0)
                {
                    GUI.Label(new Rect(view.x, view.y + Scale(20), view.width, Scale(80)),
                              "No runs yet for this song at " + songDifficulty + ".\nPlay it to set a score.",
                              UiSkin.Body);
                    DrawLeaderboardFooter(w, true);
                    return;
                }

                DrawScoreTable(view, top);
                DrawLeaderboardFooter(w, true);
                return;
            }

            List<ScoreRecord> best = _leaderboard.BestPerSong();
            if (best.Count == 0)
            {
                GUI.Label(new Rect(view.x, view.y + Scale(20), view.width, Scale(80)),
                          "No scores yet.\nFinish a song and your best run lands here.", UiSkin.Body);
                DrawLeaderboardFooter(w, false);
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
            DrawLeaderboardFooter(w, false);
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

        void DrawLeaderboardFooter(float w, bool songFocused)
        {
            float y = ContentBottom - Scale(60);

            if (songFocused)
            {
                float halfWidth = (w - Scale(60)) * 0.5f;

                if (GUI.Button(new Rect(Scale(20), y, halfWidth, Scale(60)), "ALL SONGS", UiSkin.Button))
                {
                    AppState.SelectedSong = null;
                }

                if (GUI.Button(new Rect(Scale(40) + halfWidth, y, halfWidth, Scale(60)), "CLEAR THIS", UiSkin.ButtonGhost))
                {
                    string cleared = DifficultyFor(AppState.SelectedSong);
                    _leaderboard.Clear(AppState.SelectedSong.DisplayName, cleared);
                    _status = "Cleared " + cleared + " scores for " + AppState.SelectedSong.DisplayName + ".";
                }

                return;
            }

            if (GUI.Button(new Rect(Scale(20), y, w - Scale(40), Scale(60)), "CLEAR ALL", UiSkin.ButtonGhost))
            {
                _leaderboard.Clear();
                _status = "Leaderboard cleared.";
            }
        }

        // -------------------------------------------------------------- themes

        void DrawThemes()
        {
            float w = UnityEngine.Screen.width;

            DrawHeader("THEMES", "gameplay look and light show");

            // The light-show toggle owns a fixed strip at the bottom, so the theme list is
            // scrolled inside whatever height is left instead of running under it.
            float toggleHeight = Scale(96);
            float toggleY = ContentBottom - toggleHeight;
            float rowHeight = Scale(112);

            var listView = new Rect(Scale(20), Scale(122), w - Scale(40),
                                    Mathf.Max(Scale(120), toggleY - Scale(122) - Scale(12)));
            var content = new Rect(0, 0, listView.width - Scale(20), GameplayTheme.Count * rowHeight);

            _themeScroll = GUI.BeginScrollView(listView, _themeScroll, content);

            for (int i = 0; i < GameplayTheme.Count; i++)
            {
                GameplayTheme theme = GameplayTheme.At(i);
                var row = new Rect(0, i * rowHeight, content.width, rowHeight - Scale(12));
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

                float textX = row.x + Scale(190);
                float textWidth = Mathf.Max(Scale(120), row.width - Scale(190) - Scale(120));

                GUI.Label(new Rect(textX, row.y + Scale(14), textWidth, Scale(34)),
                          theme.Name, UiSkin.CenterBold);
                GUI.Label(new Rect(textX, row.y + Scale(50), textWidth, Scale(30)),
                          theme.Blurb, UiSkin.Tiny);

                if (GUI.Button(new Rect(row.x + row.width - Scale(100), row.y + Scale(22),
                                        Scale(88), Scale(56)),
                               selected ? "ON" : "SET", selected ? UiSkin.ButtonAccent : UiSkin.Button))
                {
                    _settings.ThemeIndex = i;
                    _settings.Save(SaveRoot);
                    LaneTheme.Select(i);
                    if (_backdrop != null) _backdrop.ApplyTheme();
                    Camera.main.backgroundColor = LaneTheme.BackgroundColor;
                }
            }

            GUI.EndScrollView();

            var toggleRow = new Rect(Scale(20), toggleY, w - Scale(40), toggleHeight);
            UiSkin.Card(toggleRow, new Color(0.10f, 0.10f, 0.15f, 0.94f));

            float toggleTextWidth = Mathf.Max(Scale(120), toggleRow.width - Scale(180));

            GUI.Label(new Rect(toggleRow.x + Scale(16), toggleRow.y + Scale(14), toggleTextWidth, Scale(34)),
                      "LIGHT SHOW", UiSkin.CenterBold);
            GUI.Label(new Rect(toggleRow.x + Scale(16), toggleRow.y + Scale(50), toggleTextWidth, Scale(30)),
                      "Stage lights blink on every drum hit", UiSkin.Tiny);

            if (GUI.Button(new Rect(toggleRow.x + toggleRow.width - Scale(140), toggleRow.y + Scale(20),
                                    Scale(120), Scale(56)),
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

            DrawHeader("AUDIO CALIBRATION", AudioCalibration.Describe(_library.CalibrationMilliseconds));

            // The screen is a fixed stack of rows, so on a short phone the stack is compressed
            // rather than allowed to run past the status toast at the bottom.
            float top = Scale(120);
            float available = Mathf.Max(Scale(300), ContentBottom - top);
            float vs = Mathf.Min(1f, available / Scale(560f));

            GUI.Label(new Rect(Scale(24), top, w - Scale(48), Scale(140) * vs),
                      "Notes land early or late? Nudge the offset until notes sit on the strike line "
                      + "when you strum on the beat.\n\nLocal music files often carry a short silence "
                      + "at the start that chart-embedded audio does not, so this is worth setting once "
                      + "per device.", UiSkin.Body);

            // Four buttons across one row, sized from the width so they never overrun a narrow phone.
            float margin = Scale(24);
            float gap = Scale(10);
            float buttonWidth = Mathf.Min(Scale(140), (w - margin * 2f - gap * 3f) / 4f);
            float totalWidth = buttonWidth * 4f + gap * 3f;
            float startX = (w - totalWidth) * 0.5f;
            float rowY = top + Scale(204) * vs;

            string[] labels = { "-20", "-5", "+5", "+20" };
            int[] steps = { -20, -AudioCalibration.StepMilliseconds, AudioCalibration.StepMilliseconds, 20 };

            for (int i = 0; i < labels.Length; i++)
            {
                if (GUI.Button(new Rect(startX + i * (buttonWidth + gap), rowY, buttonWidth, Scale(76)),
                               labels[i], UiSkin.Button))
                {
                    _library.CalibrationMilliseconds += steps[i];
                }
            }

            GUI.Label(new Rect(0f, rowY + Scale(92), w, Scale(56)),
                      AudioCalibration.Describe(_library.CalibrationMilliseconds), UiSkin.Title);

            float resetWidth = Mathf.Min(Scale(320), w - Scale(48));
            float resetY = rowY + Scale(166);

            if (GUI.Button(new Rect((w - resetWidth) * 0.5f, resetY, resetWidth, Scale(64)),
                           "RESET", UiSkin.ButtonGhost))
            {
                _library.CalibrationMilliseconds = 0;
            }

            GUI.Label(new Rect(Scale(24), resetY + Scale(88), w - Scale(48), Scale(80)),
                      "Range " + AudioCalibration.MinMilliseconds + " to " + AudioCalibration.MaxMilliseconds
                      + " ms. The offset is applied to every chart, on top of the offset the chart declares.",
                      UiSkin.Small);
        }

        void DrawHeader(string title, string subtitle)
        {
            float w = UnityEngine.Screen.width;

            // The BACK button owns the right-hand column, so the title and subtitle stop short
            // of it instead of running underneath.
            float backWidth = Scale(100);
            float textWidth = Mathf.Max(Scale(120), w - Scale(20) - backWidth - Scale(24));

            GUI.Label(new Rect(Scale(20), Scale(24), textWidth, Scale(52)), title, UiSkin.Heading);
            GUI.Label(new Rect(Scale(20), Scale(74), textWidth, Scale(28)), subtitle, UiSkin.Tiny);

            if (GUI.Button(new Rect(w - Scale(120), Scale(28), backWidth, Scale(56)), "BACK", UiSkin.ButtonGhost))
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

            // The list was rescanned above, so work from the entry that is actually in it.
            SongEntry fresh = AppState.Songs.Find(s => s.ChartPath == target.ChartPath) ?? target;

            if (!_library.AttachLocalAudio(fresh, picked))
            {
                _status = "That audio file is no longer readable.";
                yield break;
            }

            AppState.SelectedSong = fresh;
            _status = "Audio attached to " + fresh.DisplayName;
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

            // No difficulty filter: the API would then only return charts rated for that one
            // level. The song screen offers the difficulties each chart actually contains.
            yield return StartCoroutine(_enchor.Search(_searchQuery, _searchPage, EnchorClient.InstrumentGuitar,
                                                       null,
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

            // RefreshLibrary rebuilds the list from disk, so the saved entry has to be replaced
            // by its fresh twin before the song screen can match it.
            SongEntry saved = AppState.Songs.Find(s => s.ChartPath == entry.ChartPath) ?? entry;

            // Land on a difficulty this chart actually charts, so the detail panel opens ready
            // to play instead of showing a chip the chart cannot honour.
            string landed = ChartDifficulty.Resolve(_settings.Difficulty, saved.Difficulties);
            _library.RememberDifficulty(saved, landed);

            _status = "Saved: " + saved.DisplayName + "  ·  " + DifficultySummary(saved);
            AppState.SelectedSong = saved;

            // A file picked before the chart existed is applied here, which is the only moment
            // the two can be matched up.
            if (!string.IsNullOrEmpty(AppState.PendingLocalAudioPath))
            {
                if (_library.AttachLocalAudio(saved, AppState.PendingLocalAudioPath))
                {
                    _status = "Saved with your local audio: " + saved.DisplayName;
                }
                AppState.PendingLocalAudioPath = null;
            }

            GoTo(Screen.SongSelect);
        }

        // ------------------------------------------------------------ gameplay

        void StartSong(SongEntry entry, string difficulty)
        {
            // The chart may not carry the saved difficulty; Load resolves what it can actually
            // play, and the HUD/results report that same value rather than the preference.
            string resolved = ChartDifficulty.Resolve(difficulty, entry.Difficulties);

            LoadedSong song = _library.Load(entry, resolved, out string error);

            if (song == null)
            {
                _status = error;
                return;
            }

            _activeDifficulty = resolved;
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
            StartSong(entry, DifficultyFor(entry));
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
                Difficulty = _activeDifficulty.Length > 0 ? _activeDifficulty : _settings.Difficulty,
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
