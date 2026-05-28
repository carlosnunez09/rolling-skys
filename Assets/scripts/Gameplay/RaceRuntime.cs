using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using Unity.Netcode;

/// <summary>
/// Central race controller. A single WaypointPath can span multiple planets —
/// checkpoints are waypoint gates anywhere in the world.
/// Trigger-based race starts supply the path and config directly.
/// </summary>
public class RaceRuntime : NetworkBehaviour {

    // ── Player ─────────────────────────────────────────────────────────────────

    [Header("Player")]
    [SerializeField] MovingCar _playerCar;
    [SerializeField] OrbitCamera _camera;

    // ── HUD — TextMeshProUGUI ──────────────────────────────────────────────────

    [Header("HUD — Race Info")]
    [SerializeField] TextMeshProUGUI _trackNameText;    // "Thunder Circuit"
    [SerializeField] TextMeshProUGUI _lapText;          // "Lap  2 / 3"
    [SerializeField] TextMeshProUGUI _waypointText;     // "Checkpoint  5 / 12"
    [SerializeField] TextMeshProUGUI _remainingText;    // "Remaining  7"
    [SerializeField] TextMeshProUGUI _timeText;         // "1:23.456"
    [SerializeField] TextMeshProUGUI _positionText;     // "1st"
    [SerializeField] TextMeshProUGUI _countdownText;    // "3" "2" "1" "GO!"
    [SerializeField] TextMeshProUGUI _statusText;       // "FINISHED!" / "WAITING…"

    [Header("HUD — Next Waypoint Arrow")]
    [Tooltip("A world-space Transform (e.g. a 3-D arrow child of the car) " +
             "whose forward axis will be pointed toward the next checkpoint.")]
    [SerializeField] Transform _nextWaypointArrow;

    // ── Checkpoint Markers ─────────────────────────────────────────────────────

    [Header("Checkpoint Markers")]
    [Tooltip("Prefab to spawn above each checkpoint while racing.")]
    [SerializeField] GameObject _checkpointMarkerPrefab;
    [Tooltip("Height above the waypoint's local up axis to place the marker.")]
    [SerializeField] float _checkpointMarkerHeight = 5f;

    // ── Events ─────────────────────────────────────────────────────────────────

    [Header("Events")]
    [SerializeField] UnityEvent _onRaceStart;
    [SerializeField] UnityEvent _onWin;
    [SerializeField] UnityEvent _onRaceEnd;

    // ── Public State ───────────────────────────────────────────────────────────

    public enum RacePhase { WaitingToStart, Countdown, Racing, Finished }
    public RacePhase   Phase      { get; private set; } = RacePhase.WaitingToStart;
    public WaypointPath ActivePath { get; private set; }

    public RacerState Winner { get; private set; }
    public IReadOnlyList<RacerState> Racers => _racers;

    /// World-space direction from the player toward the next checkpoint.
    public Vector3 NextWaypointDirection   { get; private set; } = Vector3.forward;
    public int     PlayerNextWaypointIndex { get; private set; } = 0;

    /// Track name supplied by the last <see cref="LoadTrackAndStart"/> call.
    public string ActiveTrackName { get; private set; }

    // ── Per-racer Data ─────────────────────────────────────────────────────────

    public class RacerState {
        public string    Name;
        public MovingCar Car;

        public int   LapCount;
        public int   LastPassedIndex = -1;
        public bool  Finished;
        public int   Position = 1;

        public float RaceTime;
        public float FinishTime;

        /// Total checkpoints crossed this lap (0 when none yet).
        public int CheckpointsCrossed => LastPassedIndex < 0 ? 0 : LastPassedIndex + 1;
    }

    // ── Private ────────────────────────────────────────────────────────────────

    RacerState[] _racers;
    float        _phaseTimer;
    WaypointPath _activePath;
    int          _totalLaps;
    float        _countdownSeconds;
    readonly List<GameObject> _checkpointMarkers = new List<GameObject>();

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    void Awake () {
        BuildRacerList();
    }

    void Start () {
        SetText(_statusText, "Drive to a start line…");
        SetArrowVisible(false);
        // Input is NOT disabled at startup — the car is freely drivable until a
        // race begins.  Input is only gated during the countdown.
    }

    void Update () {
        switch (Phase) {
            case RacePhase.Countdown: TickCountdown(); break;
            case RacePhase.Racing:    TickRacing();    break;
        }
        RefreshHUD();
        UpdateNextWaypointArrow();
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by <see cref="RaceStartTrigger"/> when the player enters a start zone.
    /// The path can span any number of planets — waypoints just need to be placed
    /// wherever the track goes.
    /// </summary>
    public void LoadTrackAndStart (WaypointPath path, string trackName, int totalLaps, float countdownSeconds) {
        if (path == null) return;

        // Ignore re-entry to the same active track mid-race
        if (Phase == RacePhase.Racing && _activePath == path) return;

        ActiveTrackName   = trackName;
        _activePath       = path;
        ActivePath        = path;
        _totalLaps        = Mathf.Max(1, totalLaps);
        _countdownSeconds = Mathf.Max(1f, countdownSeconds);

        ResetRacers();
        Winner = null;

        SetText(_trackNameText, trackName);

        BeginCountdown();
        _onRaceStart?.Invoke();
    }

    /// Manual start from code — mirrors the trigger-based API.
    public void StartRaceManual (WaypointPath path, string trackName, int totalLaps = 3, float countdownSeconds = 3f)
        => LoadTrackAndStart(path, trackName, totalLaps, countdownSeconds);

    public void RebuildRacerList () {
        BuildRacerList();
    }

    /// <summary>
    /// Called by the local owner's MovingCar after it spawns on the network so the
    /// HUD and input-gating always target the correct car without requiring a manual
    /// Inspector reference in multiplayer sessions.
    /// </summary>
    public void SetPlayerCar (MovingCar car) {
        if (car == null) return;

        _playerCar = car;
        if (_camera == null)
            _camera = FindFirstObjectByType<OrbitCamera>();
        if (_camera != null)
            _camera.SetFocus(car.transform);
        BuildRacerList();
    }

    // ── Phase Transitions ──────────────────────────────────────────────────────

    void BeginCountdown () {
        Phase       = RacePhase.Countdown;
        _phaseTimer = _countdownSeconds;
        SetCarInput(false);
        SetArrowVisible(false);
        SetText(_statusText,    "");
        SetText(_countdownText, Mathf.CeilToInt(_countdownSeconds).ToString());
    }

    void TickCountdown () {
        _phaseTimer -= Time.deltaTime;

        if (_phaseTimer > 0f) {
            SetText(_countdownText, Mathf.CeilToInt(_phaseTimer).ToString());
        } else if (_phaseTimer > -0.7f) {
            SetText(_countdownText, "GO!");
        } else {
            SetText(_countdownText, "");
            BeginRacing();
        }
    }

    void BeginRacing () {
        Phase = RacePhase.Racing;
        SetCarInput(true);
        SetArrowVisible(true);
        SpawnCheckpointMarkers();
    }

    // ── Race Tick ──────────────────────────────────────────────────────────────

    void TickRacing () {
        bool anyoneActive = false;

        foreach (var racer in _racers) {
            if (racer.Finished) continue;
            anyoneActive    = true;
            racer.RaceTime += Time.deltaTime;
            TickProgress(racer);
        }

        RecalculatePositions();

        if (!anyoneActive) {
            RemoveCheckpointMarkers();
            Phase = RacePhase.Finished;
            _onRaceEnd?.Invoke();
        }
    }

    // ── Waypoint / Lap Logic ───────────────────────────────────────────────────

    void TickProgress (RacerState racer) {
        if (_activePath == null || !racer.Car) return;

        int nextIdx = racer.LastPassedIndex < 0
            ? 0
            : _activePath.GetNextIndex(racer.LastPassedIndex);

        // Non-loop path: stop after the last waypoint
        if (!_activePath.IsLoop && racer.LastPassedIndex >= _activePath.Count - 1) return;

        Waypoint nextWP = _activePath.GetWaypoint(nextIdx);
        if (!nextWP) return;

        if (!_activePath.IsInsideGate(racer.Car.transform.position, nextIdx)) return;

        // ── Checkpoint crossed ─────────────────────────────────────────────────
        racer.LastPassedIndex = nextIdx;

        // Lap completion: whenever we cross the last waypoint
        if (nextIdx == _activePath.Count - 1) {
            racer.LapCount++;
            racer.LastPassedIndex = -1;   // reset to "before first checkpoint"

            if (racer.LapCount >= _totalLaps)
                FinishRacer(racer);
        }
    }

    void FinishRacer (RacerState racer) {
        racer.Finished   = true;
        racer.FinishTime = racer.RaceTime;

        // Only gate input for the local player car; never block the host when
        // a remote racer finishes.
        if (racer.Car == _playerCar)
            SetCarInput(false);

        if (Winner == null) {
            Winner = racer;

            if (racer.Car == _playerCar) {
                SetText(_statusText, "FINISHED!");
                _onWin?.Invoke();
            }
        }
    }

    // ── Position Ranking ───────────────────────────────────────────────────────

    void RecalculatePositions () {
        for (int i = 0; i < _racers.Length; i++) {
            int pos = 1;
            for (int j = 0; j < _racers.Length; j++) {
                if (j != i && IsAheadOf(_racers[j], _racers[i]))
                    pos++;
            }
            _racers[i].Position = pos;
        }
    }

    bool IsAheadOf (RacerState a, RacerState b) {
        if (a.LapCount != b.LapCount) return a.LapCount > b.LapCount;
        return a.CheckpointsCrossed > b.CheckpointsCrossed;
    }

    // ── Next Waypoint Arrow ────────────────────────────────────────────────────

    void UpdateNextWaypointArrow () {
        if (_activePath == null || _playerCar == null) return;

        RacerState player = PlayerState();
        if (player == null) return;

        int nextIdx = player.LastPassedIndex < 0
            ? 0
            : _activePath.GetNextIndex(player.LastPassedIndex);

        PlayerNextWaypointIndex = nextIdx;

        Waypoint nextWP = _activePath.GetWaypoint(nextIdx);
        if (!nextWP) return;

        Vector3 toWaypoint = nextWP.transform.position - _playerCar.transform.position;
        if (toWaypoint.sqrMagnitude < 0.01f) return;

        NextWaypointDirection = toWaypoint.normalized;

        // Rotate arrow so its forward points toward the next checkpoint.
        // We use the car's local up so the arrow tilts correctly on curved planets.
        if (_nextWaypointArrow != null) {
            Vector3 up = _playerCar.transform.up;
            // Project the direction onto the plane perpendicular to the car's up so the
            // arrow doesn't awkwardly tip for high-altitude waypoints.
            Vector3 flat = Vector3.ProjectOnPlane(NextWaypointDirection, up);
            if (flat.sqrMagnitude > 0.001f)
                _nextWaypointArrow.rotation = Quaternion.LookRotation(flat.normalized, up);
        }
    }

    // ── HUD Refresh ────────────────────────────────────────────────────────────

    void RefreshHUD () {
        RacerState player = PlayerState();
        if (player == null) return;

        int total      = _activePath ? _activePath.Count : 0;
        int passed     = player.CheckpointsCrossed;
        int remaining  = Mathf.Max(0, total - passed);
        int displayLap = Mathf.Min(player.LapCount + 1, _totalLaps);

        SetText(_lapText,       $"Lap  {displayLap} / {_totalLaps}");
        SetText(_waypointText,  $"Checkpoint  {passed} / {total}");
        SetText(_remainingText, $"Remaining  {remaining}");
        SetText(_timeText,      FormatTime(player.RaceTime));
        SetText(_positionText,  Ordinal(player.Position));
    }

    // ── Checkpoint Marker Lifecycle ────────────────────────────────────────────

    void SpawnCheckpointMarkers () {
        RemoveCheckpointMarkers();
        if (_checkpointMarkerPrefab == null || _activePath == null) return;

        foreach (Waypoint wp in _activePath.Waypoints) {
            if (!wp) continue;
            Vector3 spawnPos = wp.transform.position + wp.transform.up * _checkpointMarkerHeight;
            GameObject marker = Instantiate(_checkpointMarkerPrefab, spawnPos, wp.transform.rotation);
            marker.transform.SetParent(wp.transform, true);
            _checkpointMarkers.Add(marker);
        }
    }

    void RemoveCheckpointMarkers () {
        foreach (GameObject marker in _checkpointMarkers)
            if (marker) Destroy(marker);
        _checkpointMarkers.Clear();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    void BuildRacerList () {
#if UNITY_SERVER && !UNITY_EDITOR
        // Server builds the list once all players have spawned.
        // Called again from OnPlayerConnected when late joiners arrive.
        var allCars = FindObjectsByType<MovingCar>(FindObjectsSortMode.None);
        _racers = new RacerState[allCars.Length];
        for (int i = 0; i < allCars.Length; i++) {
            var net = allCars[i].GetComponent<NetworkObject>();
            _racers[i] = new RacerState {
                Name = $"Player {net.OwnerClientId}",
                Car  = allCars[i]
            };
        }
#else
        // Client keeps the original single-player list for local HUD.
        // The server is authoritative on positions/laps — client just displays.
        if (_playerCar == null) {
            _racers = System.Array.Empty<RacerState>();
            return;
        }
        _racers = new[] { new RacerState { Name = "Player", Car = _playerCar } };
#endif
    }

    /// <summary>
    /// Called by the owning client when it detects a checkpoint crossing.
    /// Server validates the report against expected waypoint order before
    /// updating authoritative lap state.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void ReportCheckpointServerRpc (int waypointIndex, ServerRpcParams rpcParams = default) {
        ulong clientId = rpcParams.Receive.SenderClientId;

        foreach (var racer in _racers) {
            var net = racer.Car.GetComponent<NetworkObject>();
            if (net.OwnerClientId != clientId) continue;

            int expectedNext = racer.LastPassedIndex < 0
                ? 0
                : _activePath.GetNextIndex(racer.LastPassedIndex);

            if (waypointIndex != expectedNext) return; // reject out-of-order reports

            racer.LastPassedIndex = waypointIndex;

            if (waypointIndex == _activePath.Count - 1) {
                racer.LapCount++;
                racer.LastPassedIndex = -1;
                if (racer.LapCount >= _totalLaps)
                    FinishRacer(racer);
            }
            break;
        }
    }

    void ResetRacers () {
        foreach (var r in _racers) {
            r.LapCount        = 0;
            r.LastPassedIndex = -1;
            r.Finished        = false;
            r.Position        = 1;
            r.RaceTime        = 0f;
            r.FinishTime      = 0f;
        }
    }

    RacerState PlayerState () {
        if (_racers == null || _playerCar == null) return null;
        foreach (var r in _racers)
            if (r != null && r.Car == _playerCar) return r;
        return _racers.Length > 0 ? _racers[0] : null;
    }

    /// Gate input without disabling the component so physics keeps running.
    void SetCarInput (bool on) {
        if (_playerCar) _playerCar.SetInputEnabled(on);
    }

    /// Show or hide the 3-D waypoint arrow (hides during countdown / before race).
    void SetArrowVisible (bool on) {
        if (_nextWaypointArrow) _nextWaypointArrow.gameObject.SetActive(on);
    }

    static void SetText (TextMeshProUGUI tmp, string msg) {
        if (tmp) tmp.text = msg;
    }

    static string FormatTime (float t) {
        int   mins = (int)(t / 60f);
        float secs = t % 60f;
        return $"{mins}:{secs:00.000}";
    }

    static string Ordinal (int n) => n switch {
        1 => "1st",
        2 => "2nd",
        3 => "3rd",
        _ => $"{n}th"
    };
}
