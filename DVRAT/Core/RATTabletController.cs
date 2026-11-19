using System;
using System.Collections;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Video;

// Inspector-assigned fields and reserved fields.
#pragma warning disable 0169, 0414, 0649

namespace DVRAT
{
    /// <summary>Tablet lifecycle, power states and coordination of UI, audio and data.</summary>
    public class RATTabletController : MonoBehaviour
    {
        #region Enums

        public enum TabletState
        {
            Off,
            Booting,
            Loading,
            Ready,
            ShuttingDown,
            Crashed
        }

        #endregion

        #region Serialized Fields (set in Unity Inspector)

        [Header("References")]
        [SerializeField] private Canvas _screenCanvas;

        [Header("Boot Settings")]
        [SerializeField] private float _bootDuration = 5.0f;
        [SerializeField] private VideoPlayer _bootVideoPlayer;

        [Header("Performance Simulation")]
        [Tooltip("Simulates the slow response of old hardware")]
        [SerializeField] private float _uiBuildDelay = 0.5f;
        [SerializeField] private float _menuTransitionTime = 1.2f;
        [SerializeField] private float _dataRefreshInterval = 2.0f;

        [Header("Touch Settings")]
        [SerializeField] private float _touchCooldown = 0.3f;

        #endregion

        #region Private Fields

        private const float ShutdownDuration = 2.0f;

        /// <summary>Jolt in m/s² above which the tablet may crash, about 2 g (a hard coupling). Certain at <see cref="ImpactSureMs2"/>, about 4 g.</summary>
        private const float ImpactMinMs2 = 20f;
        private const float ImpactSureMs2 = 40f;

        private TrainCar _impactCar;
        private Vector3? _lastCarVelocity;
        // Highest jolt of the current impact, 0 between impacts.
        private float _impactPeak;
        private const string PowerButtonName = "power_button";
        private const float PowerButtonReach = 3f;

        private Collider _powerButton;
        private bool _powerButtonIsControl;
        // Rest position while off. The button slides along local X while on.
        private Vector3 _powerButtonOut;
        // The game's button control resets the transform on every press, so the slide continues from this.
        private Vector3 _powerButtonPosition;
        private const float PowerButtonTravel = 0.008f;
        private const float PowerButtonMoveSeconds = 0.12f;
        private bool _powerButtonPressed;

        private TabletState _currentState = TabletState.Off;
        private float _stateTimer = 0f;

        private TrainDataProvider _trainData;
        private RouteDataProvider _routeData;

        private const double SignWarmUpMsPerFrame = 4;
        private bool _signsWarm;
        private JobDataProvider _jobData;
        private WorldDataProvider _worldData;
        private RoutePlannerProvider _routePlanner;

        private RATUIManager _uiManager;
        private RATAudioManager _audioManager;
        private float _lastTouchTime = 0f;

        #endregion

        #region Properties

        public TabletState CurrentState => _currentState;
        public bool IsReady => _currentState == TabletState.Ready;

        public TrainData CurrentTrainData { get; private set; }
        public RouteData CurrentRouteData { get; private set; }
        public JobData CurrentJobData { get; private set; }
        public WorldData CurrentWorldData { get; private set; }

        public TabletRadioSignal RadioSignal { get; private set; }

        public MediaPlayer Media { get; private set; }

        /// <summary>Saved with the gadget.</summary>
        public TabletSession Session { get; private set; } = new TabletSession();

        // Update boots the tablet once the gadget is active.
        private bool _bootAfterLoad;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            using (Timing.Measure("Setting up the tablet"))
                InitializeSubsystems();
        }

        private void Start()
        {
            SetState(TabletState.Off);
        }

        private void OnDisable()
        {
            // Unmounting deactivates the gadget, which should come back off.
            if (_currentState != TabletState.Off)
                SetState(TabletState.Off);
        }

        // A hard jolt of the tablet's car can crash it.
        private void FixedUpdate()
        {
            var body = _currentState == TabletState.Ready && _impactCar != null ? _impactCar.rb : null;
            if (body == null || body.isKinematic)
            {
                _lastCarVelocity = null;
                _impactPeak = 0f;
                return;
            }

            var velocity = body.velocity;
            float jolt = _lastCarVelocity.HasValue ? (velocity - _lastCarVelocity.Value).magnitude / Time.fixedDeltaTime : 0f;
            _lastCarVelocity = velocity;
            if (jolt >= ImpactMinMs2)
            {
                _impactPeak = Mathf.Max(_impactPeak, jolt);
                return;
            }
            if (_impactPeak <= 0f) return;

            float peak = _impactPeak;
            _impactPeak = 0f;
            // The host rolls for all copies. Its crash syncs with its state.
            if (MultiplayerSync.InGame && !MultiplayerSync.IsHost) return;
            if (UnityEngine.Random.value < Mathf.InverseLerp(ImpactMinMs2, ImpactSureMs2, peak))
            {
                RATMain.Log($"Tablet crashed from a jolt of {peak:F0} m/s² ({peak / 9.81f:F1} g)");
                SetState(TabletState.Crashed);
            }
        }

        private void Update()
        {
            if (_bootAfterLoad && _currentState == TabletState.Off)
            {
                _bootAfterLoad = false;
                RATMain.Log("Tablet was on when the game was saved, booting");
                SetState(TabletState.Booting);
            }
            UpdateState();
            UpdateTouchInput();
            UpdateDataRefresh();
        }

        // Runs after the game's button control has moved the button.
        private void LateUpdate()
        {
            MovePowerButton(Time.deltaTime);
        }

        private void OnDestroy()
        {
            Cleanup();
        }

        #endregion

        #region Initialization

        private void InitializeSubsystems()
        {
            _trainData = new TrainDataProvider();
            _routeData = new RouteDataProvider();
            _jobData = new JobDataProvider();
            _worldData = new WorldDataProvider();
            _routePlanner = new RoutePlannerProvider();

            _uiManager = GetComponentInChildren<RATUIManager>();
            if (_uiManager == null)
            {
                RATMain.LogWarning("RATUIManager not found as child of tablet prefab!");
            }

            RadioSignal = gameObject.GetComponent<TabletRadioSignal>() ?? gameObject.AddComponent<TabletRadioSignal>();
            if (GetComponent<TabletSync>() == null) gameObject.AddComponent<TabletSync>();
            Media = gameObject.GetComponent<MediaPlayer>() ?? gameObject.AddComponent<MediaPlayer>();

            _audioManager = GetComponentInChildren<RATAudioManager>();
            if (_audioManager == null)
                _audioManager = gameObject.AddComponent<RATAudioManager>();

            foreach (var collider in GetComponentsInChildren<Collider>(true))
                if (collider.gameObject.name == PowerButtonName)
                    _powerButton = collider;
            if (_powerButton == null)
                RATMain.LogWarning($"No '{PowerButtonName}' collider on the tablet; it cannot be switched on");
            else
            {
                _powerButtonOut = _powerButton.transform.localPosition;
                _powerButtonPosition = _powerButtonOut;
                _powerButtonIsControl = MakePowerButtonControl(_powerButton.gameObject);
            }
        }

        #endregion

        #region State Machine

        private void SetState(TabletState newState)
        {
            var oldState = _currentState;
            _currentState = newState;
            _stateTimer = 0f;

            using (Timing.Measure($"Switching the tablet to {newState}"))
                OnStateChanged(oldState, newState);
        }

        private void UpdateState()
        {
            _stateTimer += Time.deltaTime;

            switch (_currentState)
            {
                case TabletState.Off:
                    break;

                case TabletState.Booting:
                    UpdateBooting();
                    break;

                case TabletState.Loading:
                    UpdateLoading();
                    break;

                case TabletState.ShuttingDown:
                    UpdateShuttingDown();
                    break;
            }
        }

        private void OnStateChanged(TabletState oldState, TabletState newState)
        {
            RATMain.Log($"Tablet state: {oldState} -> {newState}");

            switch (newState)
            {
                case TabletState.Booting:
                    Session.PoweredOn = true;
                    _signsWarm = false;
                    SetPowerButtonPressed(true);
                    _audioManager?.PlayHDDStartup();
                    // TODO: Start boot video (Win95 style)
                    _uiManager?.ShowBootScreen(_bootDuration);
                    break;

                case TabletState.Loading:
                    _audioManager?.StartHDDHum();
                    // So the first page has data.
                    RefreshAllData();
                    _lastDataRefreshTime = Time.time;
                    _uiManager?.BeginUILoading();
                    break;

                case TabletState.Ready:
                    _uiManager?.ShowMainMenu();
                    _audioManager?.SetHDDActive(true);
                    RestorePlannedRoute();
                    break;

                case TabletState.ShuttingDown:
                    Session.PoweredOn = false;
                    SetPowerButtonPressed(false);
                    _uiManager?.ShowShutdownScreen(ShutdownDuration);
                    _audioManager?.PlayShutdownSound();
                    Media?.Stop();
                    break;

                case TabletState.Off:
                    SetPowerButtonPressed(false);
                    _audioManager?.StopAll();
                    _uiManager?.ShowBlank();
                    break;

                case TabletState.Crashed:
                    Session.PoweredOn = false;
                    // The disk keeps spinning, without seeking.
                    _audioManager?.StopHDDBusy();
                    _audioManager?.SetSeeking(false);
                    Media?.Stop();
                    _uiManager?.ShowCrashScreen();
                    break;
            }
        }

        private void UpdateBooting()
        {
            // Builds the signs of the route ahead behind the boot screen.
            if (!_signsWarm && _routeData != null)
                _signsWarm = _routeData.WarmUp(TrainCar, SignWarmUpMsPerFrame);
            if (_stateTimer >= _bootDuration)
            {
                SetState(TabletState.Loading);
            }
        }

        private void UpdateLoading()
        {
            if (_uiManager != null && _uiManager.IsUILoadingComplete)
            {
                SetState(TabletState.Ready);
            }
        }

        private void UpdateShuttingDown()
        {
            if (_stateTimer >= ShutdownDuration)
            {
                SetState(TabletState.Off);
            }
        }

        #endregion

        #region Save state

        /// <summary>See <see cref="Patches.SavePatches"/>.</summary>
        public JObject SaveState()
        {
            // PoweredOn follows the power button, not the state: the game saves again on unload, after the tablet went dark.
            Session.Stopwatch.Checkpoint(Time.time);
            return Session.ToJson();
        }

        /// <summary>A tablet saved while on boots again.</summary>
        public void LoadState(JToken state)
        {
            if (state == null) return;
            try
            {
                Session = TabletSession.FromJson(state);
            }
            catch (JsonException ex)
            {
                RATMain.LogWarning($"Saved tablet state could not be read, starting fresh: {ex.Message}");
                Session = new TabletSession();
            }
            _bootAfterLoad = Session.PoweredOn;
            RATMain.Log($"Tablet state loaded: {(Session.PoweredOn ? "on" : "off")}, page {Session.History.LastOrDefault() ?? "home"}");
        }

        /// <summary>Routes point to live track objects and can't be saved, so it is planned again.</summary>
        private void RestorePlannedRoute()
        {
            if (!Session.PlannerHasRoute || LastRoute != null || string.IsNullOrEmpty(Session.PlannerTo)) return;
            CalculateRoute(Session.PlannerFrom, Session.PlannerStops, Session.PlannerTo, result =>
            {
                Session.PlannerHasRoute = result != null && result.Success;
                _uiManager?.UpdateDisplayedData();
            });
        }

        #endregion

        #region Touch Input

        private readonly VrTouch _vrTouch = new VrTouch();

        private void UpdateTouchInput()
        {
            if (VRManager.IsVREnabled())
            {
                UpdateVrTouch();
                return;
            }

            // Desktop only. VR goes through VrTouch.
            if (!Input.GetMouseButtonDown(0) || Time.time - _lastTouchTime < _touchCooldown) return;

            if (!_powerButtonIsControl && IsPowerButtonUnderPointer())
            {
                _lastTouchTime = Time.time;
                _audioManager?.PlayHDDClick();
                TogglePower();
                return;
            }

            if (_currentState != TabletState.Ready) return;
            if (_uiManager != null && _uiManager.TryTouch())
            {
                _lastTouchTime = Time.time;
                _audioManager?.PlayHDDClick();
            }
        }

        private void SetPowerButtonPressed(bool pressed)
        {
            _powerButtonPressed = pressed;
            // LateUpdate doesn't run on a disabled tablet, so jump instead.
            if (!isActiveAndEnabled)
                MovePowerButton(float.PositiveInfinity);
        }

        /// <summary>The whole way takes <see cref="PowerButtonMoveSeconds"/>.</summary>
        private void MovePowerButton(float seconds)
        {
            if (_powerButton == null) return;
            var target = _powerButtonOut + (_powerButtonPressed ? new Vector3(PowerButtonTravel, 0f, 0f) : Vector3.zero);
            _powerButtonPosition = Vector3.MoveTowards(_powerButtonPosition, target, PowerButtonTravel * seconds / PowerButtonMoveSeconds);
            _powerButton.transform.localPosition = _powerButtonPosition;
        }

        /// <summary>"on", "off" or "crashed", as synced between players.</summary>
        public string PowerSummary =>
            _currentState == TabletState.Crashed ? "crashed"
            : _currentState == TabletState.Off || _currentState == TabletState.ShuttingDown ? "off"
            : "on";

        public void ApplyRemotePower(string power)
        {
            // The remote power overrides the saved one.
            _bootAfterLoad = false;
            if (power == null || power == PowerSummary) return;
            switch (power)
            {
                case "on":
                    SetState(TabletState.Booting);
                    break;
                case "crashed":
                    SetState(TabletState.Crashed);
                    break;
                case "off":
                    // Same as pressing the button here.
                    TogglePower();
                    break;
            }
        }

        /// <summary>Only the planner's stations sync, so the route is calculated again here.</summary>
        public void FollowSyncedRoute()
        {
            if (_currentState != TabletState.Ready || !Session.PlannerHasRoute || string.IsNullOrEmpty(Session.PlannerTo)) return;
            if (LastRoute != null && PlannerWidget.IsRouteFor(LastRoute, Session, Session.PlannerUnderWire)) return;
            CalculateRoute(Session.PlannerFrom, Session.PlannerStops, Session.PlannerTo, result => _uiManager?.UpdateDisplayedData());
        }

        /// <summary>See <see cref="TabletSync"/>.</summary>
        public void ShowSyncedSession(bool historyChanged, bool rebuild)
        {
            // Loading counts too: the boot page is already up and loading ignores the history.
            if (_currentState == TabletState.Ready || _currentState == TabletState.Loading)
                _uiManager?.ShowSyncedSession(historyChanged, rebuild);
        }

        private void TogglePower()
        {
            if (_currentState == TabletState.Off)
                SetState(TabletState.Booting);
            else if (_currentState == TabletState.Crashed)
            {
                // Goes dark at once, only the disk winds down.
                SetState(TabletState.Off);
                _audioManager?.PlayShutdownSound();
            }
            else if (_currentState != TabletState.ShuttingDown)
                SetState(TabletState.ShuttingDown);
        }

        /// <summary>Makes the button a game cab button, as ZCouplers does, so VR hands can press it. False falls back to the mouse ray.</summary>
        private bool MakePowerButtonControl(GameObject button)
        {
            try
            {
                // Components added to an inactive object set up on activation.
                bool wasActive = button.activeSelf;
                button.SetActive(false);
                button.layer = LayerMask.NameToLayer("Interactable");
                var spec = button.AddComponent<DV.CabControls.Spec.Button>();
                spec.createRigidbody = false;
                spec.useJoints = false;
                // The travel is animated here, so the game must not move it.
                spec.pushLocalOffset = Vector3.zero;
                spec.colliderGameObjects = new[] { button };
                // VRTK throws for a runtime button without poses.
                spec.handPosesOverride = new DV.Interaction.InteractionHandPoses();
                spec.disableTouchUse = true;
                button.SetActive(wasActive);

                var control = button.GetComponent<DV.CabControls.ButtonBase>();
                if (control == null)
                {
                    RATMain.LogWarning("The power button could not be made a game control; it only works with the mouse");
                    return false;
                }
                control.Used += OnPowerButtonUsed;
                return true;
            }
            catch (Exception ex)
            {
                RATMain.LogWarning($"The power button could not be made a game control, it only works with the mouse: {ex.Message}");
                return false;
            }
        }

        private void OnPowerButtonUsed()
        {
            if (Time.time - _lastTouchTime < _touchCooldown) return;
            _lastTouchTime = Time.time;
            _audioManager?.PlayHDDClick();
            TogglePower();
        }

        private bool _vrTouchTried;

        /// <summary>Attaches the screen control once, see <see cref="VrTouch"/>.</summary>
        private void UpdateVrTouch()
        {
            if (_vrTouchTried || _uiManager == null || _uiManager.ScreenRect == null) return;
            _vrTouchTried = true;
            _vrTouch.Attach(_uiManager.ScreenRect, OnVrScreenPressed);
        }

        private void OnVrScreenPressed(Vector3 point)
        {
            if (_currentState != TabletState.Ready || Time.time - _lastTouchTime < _touchCooldown) return;
            if (_uiManager.TryTouchAt(point))
            {
                _lastTouchTime = Time.time;
                _audioManager?.PlayHDDClick();
            }
        }

        private bool IsPowerButtonUnderPointer()
        {
            if (_powerButton == null) return false;
            var camera = Camera.main;
            if (camera == null) return false;

            Vector2 position = Cursor.lockState == CursorLockMode.Locked
                ? new Vector2(Screen.width / 2f, Screen.height / 2f)
                : (Vector2)Input.mousePosition;
            // Collider.Raycast tests only this collider, so it works on the button's trigger.
            return _powerButton.Raycast(camera.ScreenPointToRay(position), out _, PowerButtonReach);
        }

        #endregion

        #region Data Refresh

        private float _lastDataRefreshTime = 0f;

        private void UpdateDataRefresh()
        {
            if (_currentState != TabletState.Ready) return;
            if (Time.time - _lastDataRefreshTime < _dataRefreshInterval) return;

            _lastDataRefreshTime = Time.time;
            RefreshAllData();
        }

        /// <summary>Refreshes at once, e.g. after a setting changed what is read.</summary>
        public void RefreshDataNow()
        {
            if (_currentState != TabletState.Ready) return;
            _lastDataRefreshTime = Time.time;
            RefreshAllData();
        }

        /// <summary>Adds the fees if the last refresh skipped them.</summary>
        public void ReadFeesNow()
        {
            var world = CurrentWorldData;
            if (world == null || world.FeesRead) return;
            try
            {
                WorldDataProvider.ReadFees(world);
            }
            catch (Exception ex)
            {
                RATMain.LogError($"Error reading the fees: {ex}");
            }
        }

        private void RefreshAllData()
        {
            try
            {
                var car = TrainCar;
                if (car != _impactCar)
                {
                    _impactCar = car;
                    _lastCarVelocity = null;
                }
                using (Timing.Measure("Reading the train"))
                    CurrentTrainData = _trainData?.Refresh(car, Session.ChosenLoco);
                using (Timing.Measure("Reading the route"))
                    CurrentRouteData = _routeData?.Refresh(car, _routePlanner?.LastRoute,
                        Session.Setting("showSwitchDamage") != "false", Session.Setting("showTrackDamage") != "false");
                using (Timing.Measure("Reading the jobs"))
                    CurrentJobData = _jobData?.Refresh(car);
                using (Timing.Measure("Reading the world"))
                    CurrentWorldData = _worldData?.Refresh(car, Session.Setting("station"), _uiManager != null && _uiManager.ShowsFees);

                // No click here: a tick per refresh sounded like a glitch in the hum loop.
                _uiManager?.UpdateDisplayedData();

            }
            catch (Exception ex)
            {
                RATMain.LogError($"Error refreshing data: {ex}");
            }
        }

        #endregion

        #region Public API

        public void NavigateToPage(string pageId)
        {
            if (!IsReady) return;

            _audioManager?.PlayHDDClick();
            _uiManager?.NavigateToPage(pageId);
        }

        /// <summary>The mounted car, else the player's car. Null when neither.</summary>
        public TrainCar TrainCar
        {
            get
            {
                // Not "??": a destroyed car equals null only through Unity's == operator.
                var gadget = GetComponentInParent<DV.Customization.Gadgets.GadgetBase>();
                TrainCar car = gadget != null ? gadget.TrainCar : null;
                string source = "mounted";
                if (car == null)
                {
                    car = global::TrainCar.Resolve(gameObject);
                    source = "parent";
                }
                if (car == null)
                {
                    car = PlayerManager.Car;
                    source = "player";
                }

                string described = car != null ? $"{car.ID} ({car.carLivery?.id}, {source})" : "none";
                if (described != _loggedCar)
                {
                    _loggedCar = described;
                    RATMain.Log($"Tablet reads the train of {described}");
                }
                return car;
            }
        }

        private string _loggedCar;

        public RoutePlannerProvider.RouteResult LastRoute => _routePlanner?.LastRoute;

        /// <summary>Main thread only.</summary>
        public TrackNetwork TrackNetwork => _routePlanner?.Network;

        /// <summary>Main thread only.</summary>
        public bool PlanUnderWire => Session.PlannerUnderWire && CatenaryWires.Present;

        /// <summary>Main thread only.</summary>
        public bool StationUnderWire(string station) => _routePlanner != null && _routePlanner.UnderWire(station);

        /// <summary>A null <paramref name="startStation"/> starts at the tablet's train.</summary>
        public void CalculateRoute(string startStation, System.Collections.Generic.IList<string> stops, string destStation,
            Action<RoutePlannerProvider.RouteResult> onDone)
        {
            if (!IsReady) return;

            _audioManager?.PlayHDDBusy();
            _routePlanner?.CalculateRoute(TrainCar, startStation, stops, destStation, PlanUnderWire, (result) =>
            {
                _audioManager?.StopHDDBusy();
                _audioManager?.PlayHDDClick();
                onDone?.Invoke(result);
            });
        }

        #endregion

        #region Cleanup

        private void Cleanup()
        {
            _trainData?.Dispose();
            _routeData?.Dispose();
            _jobData?.Dispose();
            _worldData?.Dispose();
            _routePlanner?.Dispose();
        }

        #endregion
    }
}