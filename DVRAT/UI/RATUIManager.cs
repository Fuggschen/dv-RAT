using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// SerializeField fields are assigned by Unity Inspector.
#pragma warning disable 0649

namespace DVRAT
{
    /// <summary>Builds the screen at runtime in the prefab's canvas and shows pages from the <see cref="PageRegistry"/>.</summary>
    public class RATUIManager : MonoBehaviour
    {
        [SerializeField] private Canvas _rootCanvas;
        [Tooltip("Optional. Falls back to Unity's built-in Arial.")]
        [SerializeField] private Font _font;
        [Tooltip("Delay between elements appearing when a page builds up, to feel like old hardware.")]
        [SerializeField] private float _buildStepDelay = 0.03f;
        [SerializeField] private float _pageSwitchDelay = 0.15f;

        /// <summary>How long a tapped button shows pressed before it acts.</summary>
        private const float PressSeconds = 0.1f;

        /// <summary>Farther away the tablet ignores clicks.</summary>
        private const float MaxTouchDistance = 3f;
        private const string ImagesFolder = "images";
        private const string LogoFile = "rat_icon.png";

        private readonly PageRegistry _registry = new PageRegistry();
        private readonly Navigator _navigator = new Navigator(PageIds.Home);

        private RATTabletController _tablet;
        private RATAudioManager _audio;
        private PageHost _host;
        private StartMenu _startMenu;
        private RectTransform _overlay;
        private Text _overlayTitle;
        private Text _overlayStatus;
        private RectTransform _overlayBar;
        private GameObject _offScreen;
        private GameObject _crashScreen;
        private Text _crashText;
        private Text _crashPrompt;
        private Coroutine _overlayAnimation;
        private PageContext _context;
        private Page _currentPage;

        private const string RotationSetting = "rotation";
        private const string Inverted = "inverted";
        private bool _inverted;
        private Quaternion _uprightRotation;
        private Vector3 _uprightPosition;

        private const string BrightnessSetting = "brightness";
        /// <summary>Percent. Lower and the screen is unreadable.</summary>
        private const float MinBrightness = 10f;
        private Image _dimmer;
        private float _brightness = -1f;

        private const string ThemeSetting = "theme";
        private const string ButtonsSetting = "buttons";
        // Null until the settings were read once.
        private string _themeName;
        private string _buttonStyle;
        private Theme.Palette _palette = Theme.PaletteFor(Theme.DefaultPalette);
        private Coroutine _transition;
        private bool _isBuildingUp;
        // Set while LoadRemote waits. Booting must not wait for it, since it never ends without signal.
        private bool _waitingForSignal;
        // A tapped button shows pressed and acts when that ends.
        private bool _pressing;

        public bool IsUILoadingComplete => !_isBuildingUp || _waitingForSignal;
        public PageRegistry Registry => _registry;

        private void Awake()
        {
            if (_rootCanvas == null)
                _rootCanvas = GetComponentInChildren<Canvas>(true);
            if (_rootCanvas == null)
            {
                RATMain.LogError("RATUIManager: no Canvas found in the tablet prefab");
                enabled = false;
                return;
            }

            if (_rootCanvas.renderMode == RenderMode.WorldSpace)
                _rootCanvas.worldCamera = Camera.main;
            var raycaster = _rootCanvas.GetComponent<GraphicRaycaster>();
            if (raycaster == null)
                raycaster = _rootCanvas.gameObject.AddComponent<GraphicRaycaster>();
            // TryTouch clicks itself. An enabled raycaster would let the EventSystem click again on mouse release.
            raycaster.enabled = false;
            if (_font != null)
                UIFactory.Font = _font;

            _tablet = GetComponentInParent<RATTabletController>();
            _audio = _tablet != null ? _tablet.GetComponentInChildren<RATAudioManager>() : null;

            ButtonStyles.PixelsPerUnit = _rootCanvas.referencePixelsPerUnit;
            UpdateTheme();
            var canvasRoot = (RectTransform)_rootCanvas.transform;
            _uprightRotation = canvasRoot.localRotation;
            _uprightPosition = canvasRoot.localPosition;
            foreach (Transform child in canvasRoot)
                child.gameObject.SetActive(false);

            _host = new PageHost(canvasRoot, OpenStopwatch, OpenWeather, OpenMediaPlayer, GoBack, Logo, ToggleStartMenu);
            _startMenu = new StartMenu(_registry, _navigator);
            BuildOverlay(canvasRoot);
            _offScreen = UIFactory.Fill(canvasRoot, Theme.ScreenOff, "ScreenOff").gameObject;
            _dimmer = UIFactory.Fill(canvasRoot, Color.clear, "Dimmer");

            // Navigating to a page starts its lists on the first screen.
            _navigator.Navigated += id =>
            {
                _tablet?.Session.ResetLists(id);
                ResetLeftPages(id);
            };
            _navigator.Changed += id =>
            {
                _startMenu.Close();
                if (_tablet != null)
                    _tablet.Session.History = _navigator.History.ToList();
                ShowPage(id, animate: true);
            };

            ShowBlank();
        }

        /// <summary>Resets <see cref="Page.LeaveVars"/> of each page that going to <paramref name="id"/> leaves, see <see cref="Navigator.Leaves"/>.</summary>
        private void ResetLeftPages(string id)
        {
            if (_tablet == null) return;
            string targetId = _registry.Resolve(id);
            bool changed = false;
            foreach (string pageId in _registry.Ids.ToList())
            {
                var page = _registry.Get(pageId);
                if (page == null || page.LeaveVars.Count == 0) continue;
                if (Navigator.Leaves(targetId, pageId, _navigator.History, p => _registry.Get(p)?.ParentId))
                    changed |= _tablet.Session.ResetVars(pageId, page.LeaveVars);
            }
            // A variable may pick what the tablet reads, e.g. the shown loco.
            if (changed)
                _tablet.RefreshDataNow();
        }

        // Coroutines stop with the object, so a press would never end.
        private void OnDisable() => _pressing = false;

        private void Update()
        {
            UpdateRotation();
            UpdateBrightness();
            UpdateTheme();
            UseTheme();
            if (_host != null && _host.Visible)
            {
                _host.SetClock(ClockText());
                UpdateWeather();
                UpdateMedia();
                _host.SetSignal(_tablet != null && _tablet.RadioSignal != null ? _tablet.RadioSignal.Bars : 0);
            }
            if (_crashScreen != null && _crashScreen.activeSelf)
                _crashPrompt.text = "Press the power button to continue " + (Time.time % 1f < 0.5f ? "_" : " ");
        }

        /// <summary>Turns the screen 180 degrees around the canvas middle, whatever its pivot.</summary>
        private void UpdateRotation()
        {
            bool inverted = _tablet != null && _tablet.Session.Setting(RotationSetting) == Inverted;
            if (inverted == _inverted || _rootCanvas == null) return;
            _inverted = inverted;

            var canvas = (RectTransform)_rootCanvas.transform;
            canvas.localRotation = _uprightRotation;
            canvas.localPosition = _uprightPosition;
            if (!inverted) return;
            var middle = canvas.TransformPoint(canvas.rect.center);
            canvas.localRotation = _uprightRotation * Quaternion.Euler(0f, 0f, 180f);
            canvas.position += middle - canvas.TransformPoint(canvas.rect.center);
        }

        private void UpdateBrightness()
        {
            if (_dimmer == null) return;
            var canvas = _dimmer.transform.parent;
            // Keep it on top, screens built later would draw over it.
            if (_dimmer.transform.GetSiblingIndex() != canvas.childCount - 1)
                _dimmer.transform.SetAsLastSibling();

            string setting = _tablet != null ? _tablet.Session.Setting(BrightnessSetting) : null;
            float brightness = float.TryParse(setting, NumberStyles.Float, CultureInfo.InvariantCulture, out float percent)
                ? Mathf.Clamp(percent, MinBrightness, 100f) / 100f
                : 1f;
            if (brightness == _brightness) return;
            _brightness = brightness;
            _dimmer.color = new Color(0f, 0f, 0f, 1f - brightness);
        }

        /// <summary>Call before building or updating the screen, since another tablet may use another theme.</summary>
        private void UseTheme()
        {
            Theme.Current = _palette;
            Theme.ButtonStyle = _buttonStyle ?? ButtonStyles.Simple;
        }

        private void UpdateTheme()
        {
            string name = _tablet != null ? _tablet.Session.Setting(ThemeSetting) : Theme.DefaultPalette;
            string buttons = _tablet != null ? _tablet.Session.Setting(ButtonsSetting) : ButtonStyles.Simple;
            if (name == _themeName && buttons == _buttonStyle) return;
            _themeName = name;
            _buttonStyle = buttons;
            _palette = Theme.PaletteFor(name);
            UseTheme();
            if (_host != null)
                RebuildShell();
        }

        /// <summary>Replaces the page frame and boot screen, which keep the colors they were built with.</summary>
        private void RebuildShell()
        {
            var canvasRoot = (RectTransform)_rootCanvas.transform;
            if (_transition != null)
                StopCoroutine(_transition);
            _transition = null;
            _isBuildingUp = false;
            _waitingForSignal = false;

            bool hostVisible = _host.Visible;
            int hostIndex = _host.Root.GetSiblingIndex();
            _startMenu.Close();
            Discard(_host.Root.gameObject);
            _host = new PageHost(canvasRoot, OpenStopwatch, OpenWeather, OpenMediaPlayer, GoBack, Logo, ToggleStartMenu);
            _host.Root.SetSiblingIndex(hostIndex);
            _host.Visible = hostVisible;

            bool overlayActive = _overlay.gameObject.activeSelf;
            int overlayIndex = _overlay.GetSiblingIndex();
            string title = _overlayTitle.text;
            string status = _overlayStatus.text;
            float progress = _overlayBar.anchorMax.x;
            Discard(_overlay.gameObject);
            BuildOverlay(canvasRoot);
            _overlay.SetSiblingIndex(overlayIndex);
            _overlay.gameObject.SetActive(overlayActive);
            _overlayTitle.text = title;
            _overlayStatus.text = status;
            SetProgress(progress);

            if (_currentPage == null) return;
            _context = new PageContext(_host.Content, _navigator, _registry, _tablet, Rebuild, UpdateDisplayedData);
            SetHeader(_currentPage);
            BuildCurrentPage();
        }

        /// <summary>Destroy runs at frame end, so hide first to stop touches.</summary>
        private static void Discard(GameObject gameObject)
        {
            gameObject.SetActive(false);
            Destroy(gameObject);
        }

        private string ClockText()
        {
            string real = DateTime.Now.ToString("HH:mm");
            var game = _tablet != null ? _tablet.CurrentWorldData?.GameTime : null;
            return game.HasValue ? $"{game.Value:HH:mm} / {real}" : real;
        }

        /// <summary>Shows the icon with the temperature when DVSeasons gives one. Without a loaded poster to take the icon from, names the forecast in words.</summary>
        private void UpdateWeather()
        {
            var world = _tablet != null && _tablet.IsReady ? _tablet.CurrentWorldData : null;
            int now = world != null ? ForecastSlot.IndexAt(world.Forecast, world.GameTime) : -1;
            string icon = now >= 0 ? world.Forecast[now].Icon : null;
            var sprite = WeatherIcons.For(icon);
            string temperature = world?.Seasons != null ? Format.Temperature(world.Seasons.TemperatureC) : null;
            string words = sprite == null && icon != null ? ForecastSlot.Describe(icon) : null;
            _host.SetWeather(sprite, words == null ? temperature : temperature == null ? words : words + "  " + temperature);
        }

        private const string StopwatchPage = "stopwatch";
        private const string WeatherPage = "weather";

        private void OpenStopwatch() => OpenFromHeader(StopwatchPage);

        private void OpenWeather() => OpenFromHeader(WeatherPage);

        private const string MediaPlayerPage = "media_player";

        private const string RadioPage = "radio";
        private const string PcAudioPage = "pc_audio";

        /// <summary>Opens the radio for a station, the PC audio page for a capture, else the media player.</summary>
        private void OpenMediaPlayer() =>
            OpenFromHeader(_tablet == null || _tablet.Media == null ? MediaPlayerPage
                : _tablet.Media.Station != null ? RadioPage
                : _tablet.Media.CaptureApp != null ? PcAudioPage
                : MediaPlayerPage);

        private void UpdateMedia()
        {
            var state = _tablet != null && _tablet.Media != null ? _tablet.Media.State : PlayState.Stopped;
            if (_playIcon == null)
            {
                _playIcon = Icons.ForLabel(Icons.Label(Icons.Play));
                _pauseIcon = Icons.ForLabel(Icons.Label(Icons.Pause));
            }
            _host.SetMedia(state == PlayState.Playing ? _playIcon : state == PlayState.Paused ? _pauseIcon : null);
        }

        private Sprite _playIcon;
        private Sprite _pauseIcon;

        /// <summary>A view inside the page, like one track on Yard, is left before the page.</summary>
        private void GoBack()
        {
            if (_currentPage == null || _tablet == null || !_tablet.IsReady) return;
            if (_currentPage.CanGoBack)
                _currentPage.GoBack(_context);
            else if (_navigator.CanGoBack)
                _navigator.Back();
        }

        /// <summary>Prefixes the parent's title, except for the home menu.</summary>
        private string HeaderTitle(Page page)
        {
            var parent = _registry.Get(page.ParentId);
            return parent == null || parent.Id == PageIds.Home ? page.Title : $"{parent.Title} > {page.Title}";
        }

        private void SetHeader(Page page) => _host.SetTitle(HeaderTitle(page), page.CanGoBack || _navigator.CanGoBack);

        private void OpenFromHeader(string page)
        {
            if (_tablet == null || !_tablet.IsReady || !_registry.Contains(page) || _navigator.Current == page) return;
            _navigator.Open(page);
        }

        #region Screens

        public void ShowBlank()
        {
            if (_host == null) return;
            UseTheme();
            StopOverlayAnimation();
            if (_transition != null)
                StopCoroutine(_transition);
            _transition = null;
            _isBuildingUp = false;
            _waitingForSignal = false;
            _host.Visible = false;
            _overlay.gameObject.SetActive(false);
            _crashScreen?.SetActive(false);
            _offScreen.SetActive(true);
        }

        /// <summary>Win95-style blue screen, built on first use.</summary>
        public void ShowCrashScreen()
        {
            if (_host == null) return;
            ShowBlank();
            _offScreen.SetActive(false);
            if (_crashScreen == null)
                BuildCrashScreen((RectTransform)_rootCanvas.transform);

            string message = CrashMessages[UnityEngine.Random.Range(0, CrashMessages.Length)];
            _crashText.text =
                string.Format(message, Hex(8)) + "\n\n" +
                "*  Press the power button to switch the tablet off.\n" +
                "   You will lose any unsaved route plans.";
            _crashScreen.SetActive(true);
        }

        /// <summary>{0} becomes a random hex address. Max ~60 characters per line.</summary>
        private static readonly string[] CrashMessages =
        {
            "Your R.A.T. ran into a mousetrap at 0028:{0}.\nThe current route has been nibbled.",
            "A fatal cheese exception 0E has occurred at 0028:{0}\nin VXD GOUDA(01). The tablet smelled something.",
            "RAT.EXE has chewed through cable 0028:{0}.\nThe track network is now 3% shorter.",
            "Stack overflow: too many rats in one wagon.\nOverflow occurred at 0028:{0} in VXD BOXCAR(01).",
            "The R.A.T. tried to divide by 0 km/h at 0028:{0}.\nIt is still waiting for the train to arrive.",
            "General protection fault in TIMETABLE.DLL at {0}.\nThe 08:15 to Harbor left without the tablet.",
            "Fatal exception: the hamster wheel stopped spinning.\nHDD.SYS reports 0 RPM at 0028:{0}.",
            "Signal passed at danger in module SPAD.VXD at {0}.\nThe tablet is now writing a very long report.",
            "Out of memory: the rat forgot where it hid the route.\nLast seen near 0028:{0}.",
            "Coupler failure between RAM and CPU at 0028:{0}.\nPlease call the yard master. He will not answer.",
        };

        private void BuildCrashScreen(RectTransform canvasRoot)
        {
            var blue = new Color(0f, 0f, 0.667f);
            var grey = new Color(0.667f, 0.667f, 0.667f);
            var font = CrashFont();

            var root = UIFactory.Stretch(UIFactory.Rect("CrashScreen", canvasRoot));
            UIFactory.Fill(root, blue);

            var header = Centered(UIFactory.Rect("Header", root), 110, 26, 150);
            header.gameObject.AddComponent<Image>().color = grey;
            var title = UIFactory.Label(header, "R.A.T.", Theme.FontTitle, blue, TextAnchor.MiddleCenter);
            title.font = font;
            UIFactory.Stretch(title.rectTransform);

            _crashText = UIFactory.Label(root, "", Theme.FontBody + 1, Color.white, TextAnchor.UpperLeft);
            _crashText.font = font;
            Centered(_crashText.rectTransform, 700, 180, 30);

            _crashPrompt = UIFactory.Label(root, "", Theme.FontBody + 1, Color.white, TextAnchor.MiddleCenter);
            _crashPrompt.font = font;
            Centered(_crashPrompt.rectTransform, 700, 26, -120);

            _crashScreen = root.gameObject;
        }

        /// <summary>System monospace font, Arial when none is installed.</summary>
        private static Font CrashFont()
        {
            foreach (string name in new[] { "Lucida Console", "Courier New" })
            {
                var font = Font.CreateDynamicFontFromOSFont(name, Theme.FontBody + 1);
                if (font != null) return font;
            }
            return UIFactory.Font;
        }

        private static string Hex(int digits)
        {
            var text = new System.Text.StringBuilder(digits);
            for (int i = 0; i < digits; i++)
                text.Append("0123456789ABCDEF"[UnityEngine.Random.Range(0, 16)]);
            return text.ToString();
        }

        public void ShowBootScreen(float duration)
        {
            ShowOverlay("R.A.T.", "Booting", 0f, 1f, duration);
        }

        public void ShowShutdownScreen(float duration)
        {
            ShowOverlay("R.A.T.", "Shutdown", 1f, 0f, duration);
        }

        public void BeginUILoading()
        {
            if (_host == null) return;
            UseTheme();
            StopOverlayAnimation();
            LoadPages();
            _overlay.gameObject.SetActive(false);
            _offScreen.SetActive(false);
            _crashScreen?.SetActive(false);
            _host.Visible = true;
            // Restore the saved history, skipping pages since removed.
            var saved = _tablet != null ? _tablet.Session.History : new List<string>();
            _navigator.Restore(saved.Where(id => _registry.Contains(id)).ToList());
        }

        /// <summary>Runs on every boot, not in Awake: edited JSON shows after a power cycle, and a restored tablet can exist before the stations are registered.</summary>
        private void LoadPages()
        {
            _currentPage = null;
            try
            {
                using (Timing.Measure("Loading the pages"))
                    ContentLibrary.RegisterAll(_registry);
            }
            catch (Exception ex)
            {
                RATMain.LogError($"Pages could not be built: {ex}");
                ContentLibrary.RegisterFallback(_registry, ex.Message);
            }
        }

        public bool ShowsFees => _currentPage != null && _currentPage.UsesFees;

        public void ShowMainMenu()
        {
            if (_host == null) return;
            if (_currentPage == null || !_host.Visible)
                BeginUILoading();
        }

        /// <summary>Applies another player's copy of the session: restore history, rebuild the page, or only refresh live values.</summary>
        public void ShowSyncedSession(bool historyChanged, bool rebuild)
        {
            if (_host == null || !_host.Visible || _tablet == null) return;
            if (historyChanged)
                _navigator.Restore(_tablet.Session.History.Where(id => _registry.Contains(id)).ToList());
            else if (rebuild)
                Rebuild();
            else
                UpdateDisplayedData();
        }

        public void NavigateToPage(string pageId)
        {
            if (!_registry.Contains(pageId))
            {
                RATMain.LogWarning($"No page with ID '{pageId}'");
                return;
            }
            _navigator.Open(_registry.Resolve(pageId));
        }

        private void BuildOverlay(RectTransform canvasRoot)
        {
            _overlay = UIFactory.Stretch(UIFactory.Rect("Overlay", canvasRoot));
            UIFactory.Fill(_overlay, Theme.Background);

            if (Logo != null)
            {
                var logo = Centered(UIFactory.Rect("Logo", _overlay), 170, 170, 95).gameObject.AddComponent<RawImage>();
                logo.texture = Logo;
                logo.raycastTarget = false;
            }

            _overlayTitle = UIFactory.Label(_overlay, "", Theme.FontLarge, Theme.Highlight, TextAnchor.MiddleCenter, FontStyle.Bold);
            Centered(_overlayTitle.rectTransform, 400, 44, -25);
            _overlayStatus = UIFactory.Label(_overlay, "", Theme.FontBody, Theme.TextDim, TextAnchor.MiddleCenter);
            Centered(_overlayStatus.rectTransform, 400, 26, -60);

            var frame = Centered(UIFactory.Rect("Progress", _overlay), 360, 14, -100);
            frame.gameObject.AddComponent<Image>().color = Theme.Panel;
            _overlayBar = UIFactory.Stretch(UIFactory.Rect("Fill", frame), 2, 2, 2, 2);
            _overlayBar.gameObject.AddComponent<Image>().color = Theme.Text;
        }

        /// <summary><paramref name="y"/> is pixels above the screen centre.</summary>
        private static RectTransform Centered(RectTransform rect, float width, float height, float y)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(width, height);
            rect.anchoredPosition = new Vector2(0, y);
            return rect;
        }

        private Texture2D _logo;

        private Texture2D Logo => _logo != null ? _logo : _logo = LoadLogo();

        private void ToggleStartMenu() => _startMenu.Toggle(_host.Root);

        /// <summary>Loaded from the images folder, so it can be replaced without rebuilding the bundle.</summary>
        private static Texture2D LoadLogo()
        {
            string modPath = RATMain.ModEntry?.Path;
            if (string.IsNullOrEmpty(modPath)) return null;

            string path = Path.Combine(modPath, ImagesFolder, LogoFile);
            if (!File.Exists(path))
            {
                RATMain.LogWarning($"Boot logo not found: {path}");
                return null;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (texture.LoadImage(File.ReadAllBytes(path)))
                return texture;
            RATMain.LogWarning($"Boot logo is not a readable PNG: {path}");
            return null;
        }

        private void ShowOverlay(string title, string status, float from, float to, float duration)
        {
            if (_host == null) return;
            UseTheme();
            StopOverlayAnimation();
            _host.Visible = false;
            _overlay.gameObject.SetActive(true);
            _offScreen.SetActive(false);
            _crashScreen?.SetActive(false);
            _overlayTitle.text = title;
            _overlayStatus.text = status;
            _overlayAnimation = StartCoroutine(AnimateProgress(from, to, duration));
        }

        private IEnumerator AnimateProgress(float from, float to, float duration)
        {
            for (float t = 0; t < duration; t += Time.deltaTime)
            {
                SetProgress(Mathf.Lerp(from, to, Mathf.Floor(t / duration * 20f) / 20f));
                yield return null;
            }
            SetProgress(to);
            _overlayAnimation = null;
        }

        private void SetProgress(float value)
        {
            _overlayBar.anchorMax = new Vector2(Mathf.Clamp01(value), 1);
        }

        private void StopOverlayAnimation()
        {
            if (_overlayAnimation != null)
                StopCoroutine(_overlayAnimation);
            _overlayAnimation = null;
        }

        #endregion

        #region Pages

        private void ShowPage(string pageId, bool animate)
        {
            var page = _registry.Get(pageId);
            if (page == null)
            {
                RATMain.LogWarning($"No page with ID '{pageId}'");
                return;
            }

            if (_transition != null)
                StopCoroutine(_transition);
            _waitingForSignal = false;
            _transition = StartCoroutine(SwitchTo(page, animate));
        }

        private IEnumerator SwitchTo(Page page, bool animate)
        {
            _isBuildingUp = true;
            _host.ClearContent();
            SetHeader(page);
            if (animate && _pageSwitchDelay > 0)
                yield return new WaitForSeconds(_pageSwitchDelay);

            // Another tablet may have set its own colors while this one waited.
            UseTheme();
            _currentPage = page;
            _context = new PageContext(_host.Content, _navigator, _registry, _tablet, Rebuild, UpdateDisplayedData);
            if (page.NeedsSignal)
                yield return LoadRemote();
            // The last refresh skipped the fees if the previous page didn't show them.
            if (page.UsesFees)
                _tablet?.ReadFeesNow();
            BuildCurrentPage();

            if (animate)
                yield return BuildUp(_host.Content);
            _isBuildingUp = false;
            _transition = null;
        }

        private float RadioStrength => _tablet != null && _tablet.RadioSignal != null
            ? _tablet.RadioSignal.Strength
            : 1f;

        /// <summary>Loading bar that follows the radio signal. Leaves the content area empty for the page.</summary>
        private IEnumerator LoadRemote()
        {
            _host.SetSoftKeys(new SoftKey[0]);
            UIFactory.Height(UIFactory.Label(_host.Content, "Receiving data...", Theme.FontBody, Theme.TextDim), Theme.RowHeight);
            float progress = 0f;
            ProgressBar.Create(_host.Content, () => Mathf.Floor(progress * LoadingBarSteps) / LoadingBarSteps);
            _waitingForSignal = true;
            while (progress < 1f)
            {
                progress = SignalLoad.Advance(progress, Time.deltaTime, RadioStrength);
                yield return null;
            }
            _waitingForSignal = false;
            _host.ClearContent();
        }

        private const float LoadingBarSteps = 20f;

        private void Rebuild()
        {
            if (_currentPage == null) return;
            UseTheme();
            _host.ClearContent();
            BuildCurrentPage();
            AnimateContent();
        }

        private void AnimateContent()
        {
            if (_transition != null)
                StopCoroutine(_transition);
            _waitingForSignal = false;
            _transition = StartCoroutine(BuildUpContent());
        }

        private IEnumerator BuildUpContent()
        {
            _isBuildingUp = true;
            yield return BuildUp(_host.Content);
            _isBuildingUp = false;
            _transition = null;
        }

        private void BuildCurrentPage()
        {
            try
            {
                using (Timing.Measure($"Building page {_currentPage.Id}"))
                {
                    _currentPage.Build(_context);
                    _currentPage.Refresh(_context);
                }
            }
            catch (Exception ex)
            {
                RATMain.LogError($"Page '{_currentPage.Id}' failed to build: {ex}");
                _host.ClearContent();
                UIFactory.Height(UIFactory.Label(_host.Content, "This page could not be displayed.", Theme.FontBody, Theme.Warning), Theme.RowHeight);
            }
            SetHeader(_currentPage);
            _host.SetSoftKeys(SoftKeysFor(_currentPage));
        }

        private IEnumerable<SoftKey> SoftKeysFor(Page page)
        {
            IEnumerable<SoftKey> pageKeys;
            try
            {
                pageKeys = new List<SoftKey>(page.SoftKeys(_context));
            }
            catch (Exception ex)
            {
                RATMain.LogError($"Page '{page.Id}' soft keys failed: {ex.Message}");
                yield break;
            }
            foreach (var key in pageKeys)
                yield return key.Action == null ? key : key.WithAction(() => RunAndAnimate(key.Action));
        }

        /// <summary>Animates when the key changed the page in place. Keys that open or rebuild a page already animate.</summary>
        private void RunAndAnimate(Action action)
        {
            var page = _currentPage;
            action();
            if (_currentPage == page && _transition == null)
                AnimateContent();
        }

        #endregion

        #region Data

        public void UpdateDisplayedData()
        {
            // A refresh rebuilds the soft keys and would drop a tap on a key showing pressed.
            if (_currentPage == null || _context == null || _isBuildingUp || _pressing) return;
            UseTheme();

            try
            {
                using (Timing.Measure($"Updating page {_currentPage.Id}"))
                    _currentPage.Refresh(_context);
            }
            catch (Exception ex)
            {
                RATMain.LogError($"Page '{_currentPage.Id}' failed to refresh: {ex}");
            }
            // A list growing past one screen adds paging keys.
            SetHeader(_currentPage);
            _host.SetSoftKeys(SoftKeysFor(_currentPage));
        }

        #endregion

        #region Touch

        /// <summary>The crosshair while the cursor is locked, else the mouse.</summary>
        private static Vector2 PointerPosition() =>
            Cursor.lockState == CursorLockMode.Locked
                ? new Vector2(Screen.width / 2f, Screen.height / 2f)
                : (Vector2)Input.mousePosition;

        /// <summary>Returns true when an enabled control was hit.</summary>
        public bool TryTouch() => TryTouchAt(PointerPosition());

        /// <summary>Clicks at a world point, e.g. where a VR hand tapped.</summary>
        public bool TryTouchAt(Vector3 point)
        {
            var camera = ViewCamera();
            return camera != null && TryTouchAt((Vector2)camera.WorldToScreenPoint(point));
        }

        /// <summary>In VR the main camera can be missing, as ZCouplers notes.</summary>
        private static Camera ViewCamera()
        {
            var camera = Camera.main;
            return camera != null ? camera : PlayerManager.PlayerCamera;
        }

        public RectTransform ScreenRect => _rootCanvas != null ? (RectTransform)_rootCanvas.transform : null;

        private bool TryTouchAt(Vector2 screenPosition)
        {
            if (_rootCanvas == null || _pressing) return false;
            UseTheme();
            var raycaster = _rootCanvas.GetComponent<GraphicRaycaster>();
            var camera = ViewCamera();
            if (raycaster == null || camera == null) return false;
            if (Vector3.Distance(camera.transform.position, _rootCanvas.transform.position) > MaxTouchDistance) return false;

            // The main camera can change at runtime, e.g. after teleporting.
            _rootCanvas.worldCamera = camera;

            var pointer = new PointerEventData(EventSystem.current) { position = screenPosition };
            var results = new List<RaycastResult>();
            raycaster.Raycast(pointer, results);

            foreach (var result in results)
            {
                var button = result.gameObject.GetComponentInParent<Button>();
                if (button != null)
                {
                    if (!button.interactable) return false;
                    StartCoroutine(PressAndClick(button));
                    return true;
                }

                var touchable = result.gameObject.GetComponent<IRATTouchable>();
                if (touchable != null)
                {
                    touchable.OnTouch();
                    return true;
                }
            }
            return false;
        }

        private IEnumerator PressAndClick(Button button)
        {
            var release = ButtonStyles.Press(button, Theme.ButtonStyle);
            // Set only after the press shows, so a throw above cannot block later taps.
            _pressing = true;
            yield return new WaitForSeconds(PressSeconds);
            _pressing = false;
            // Gone when the tablet switched off or the theme changed meanwhile.
            if (button == null || !button.isActiveAndEnabled) yield break;
            UseTheme();
            release();
            button.onClick.Invoke();
        }

        #endregion

        #region Build-up animation

        private IEnumerator BuildUp(RectTransform root)
        {
            if (_buildStepDelay <= 0) yield break;

            var groups = new List<CanvasGroup>();
            foreach (var graphic in root.GetComponentsInChildren<Graphic>())
            {
                var group = graphic.gameObject.GetComponent<CanvasGroup>();
                if (group == null)
                    group = graphic.gameObject.AddComponent<CanvasGroup>();
                group.alpha = 0f;
                groups.Add(group);
            }
            if (_audio != null)
                // Each step waits at least a frame, so short delays take longer than they say.
                _audio.ReadFor(groups.Count * Mathf.Max(_buildStepDelay, Time.smoothDeltaTime));

            foreach (var group in groups)
            {
                if (group == null) continue;
                group.alpha = 1f;
                yield return new WaitForSeconds(_buildStepDelay);
            }
        }

        #endregion
    }

    /// <summary>For non-button UI elements that react to a tap.</summary>
    public interface IRATTouchable
    {
        void OnTouch();
    }
}
