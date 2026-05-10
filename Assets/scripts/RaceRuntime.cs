using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

public class RaceRuntime : MonoBehaviour {

    // ── Race Setup ─────────────────────────────────────────────────────────────

    [Header("Race Setup")]
    [SerializeField] WaypointPath _path;
    [SerializeField] int _totalLaps = 3;
    [SerializeField] float _countdownSeconds = 3f;
    [SerializeField] bool _autoStart = true;

    // ── Player ─────────────────────────────────────────────────────────────────

    [Header("Player")]
    [SerializeField] MovingCar _playerCar;
    [SerializeField] OrbitCamera _camera;   // referenced for future multi-cam switching

    // ── HUD — TextMeshProUGUI ──────────────────────────────────────────────────

    [Header("HUD")]
    [SerializeField] TextMeshProUGUI _lapText;          // "Lap  2 / 3"
    [SerializeField] TextMeshProUGUI _waypointText;     // "Checkpoint  5 / 12"
    [SerializeField] TextMeshProUGUI _remainingText;    // "Remaining  7"
    [SerializeField] TextMeshProUGUI _timeText;         // "1:23.456"
    [SerializeField] TextMeshProUGUI _positionText;     // "1st"
    [SerializeField] TextMeshProUGUI _countdownText;    // "3" "2" "1" "GO!"
    [SerializeField] TextMeshProUGUI _statusText;       // "FINISHED!" etc.

    // ── Win ────────────────────────────────────────────────────────────────────

    [Header("Win")]
    [SerializeField] UnityEvent _onWin;     // hook up sound/scene/UI in the Inspector

    // ── Public state ───────────────────────────────────────────────────────────

    public enum RacePhase { WaitingToStart, Countdown, Racing, Finished }
    public RacePhase Phase { get; private set; } = RacePhase.WaitingToStart;

    public RacerState Winner { get; private set; }
    public IReadOnlyList<RacerState> Racers => _racers;

    // ── Per-racer data ─────────────────────────────────────────────────────────

    public class RacerState {
        public string Name;
        public MovingCar Car;

        // Progress
        public int LapCount;
        public int LastPassedIndex = -1;   // -1 = no checkpoint crossed yet this lap
        public bool Finished;
        public int Position = 1;           // 1st, 2nd, … updated every frame

        // Time
        public float RaceTime;
        public float FinishTime;

        // Convenience: checkpoints crossed in the current lap (0 at lap start)
        public int CheckpointsCrossed => LastPassedIndex < 0 ? 0 : LastPassedIndex + 1;
    }

    // ── Private ────────────────────────────────────────────────────────────────

    RacerState[] _racers;
    float _phaseTimer;

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    void Start() {
        // Build racer list — add more entries here when you introduce AI / extra players
        _racers = new[] {
            new RacerState { Name = "Player", Car = _playerCar }
        };

        if (_autoStart)
            BeginCountdown();
    }

    void Update() {
        switch (Phase) {
            case RacePhase.Countdown: TickCountdown(); break;
            case RacePhase.Racing:    TickRacing();    break;
        }
        RefreshHUD();
    }

    // ── Phase transitions ──────────────────────────────────────────────────────

    void BeginCountdown() {
        Phase        = RacePhase.Countdown;
        _phaseTimer  = _countdownSeconds;
        SetCarInput(false);
        SetText(_statusText, "");
        SetText(_countdownText, Mathf.CeilToInt(_countdownSeconds).ToString());
    }

    void TickCountdown() {
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

    void BeginRacing() {
        Phase = RacePhase.Racing;
        SetCarInput(true);
    }

    // ── Race tick ──────────────────────────────────────────────────────────────

    void TickRacing() {
        bool anyoneActive = false;

        foreach (var racer in _racers) {
            if (racer.Finished) continue;
            anyoneActive    = true;
            racer.RaceTime += Time.deltaTime;
            TickProgress(racer);
        }

        RecalculatePositions();

        if (!anyoneActive)
            Phase = RacePhase.Finished;
    }

    // ── Waypoint / lap logic ───────────────────────────────────────────────────

    void TickProgress(RacerState racer) {
        if (!_path || !racer.Car) return;

        // The next checkpoint this racer needs to cross
        int nextIdx = racer.LastPassedIndex < 0
            ? 0
            : _path.GetNextIndex(racer.LastPassedIndex);

        // Non-loop paths end once the last checkpoint is crossed
        if (!_path.IsLoop && racer.LastPassedIndex >= _path.Count - 1) return;

        Waypoint nextWP = _path.GetWaypoint(nextIdx);
        if (!nextWP) return;

        float dist = Vector3.Distance(racer.Car.transform.position, nextWP.transform.position);
        if (dist > nextWP.Width * 0.5f) return;

        // ── Checkpoint crossed ─────────────────────────────────────────────────
        racer.LastPassedIndex = nextIdx;

        if (nextIdx == _path.Count - 1) {
            // Completed a lap
            racer.LapCount++;
            racer.LastPassedIndex = -1;   // reset for the next lap

            if (racer.LapCount >= _totalLaps)
                FinishRacer(racer);
        }
    }

    void FinishRacer(RacerState racer) {
        racer.Finished    = true;
        racer.FinishTime  = racer.RaceTime;
        racer.Car.enabled = false;

        if (Winner == null) {
            // First racer to finish wins
            Winner = racer;

            if (racer.Car == _playerCar) {
                SetText(_statusText, "FINISHED!");
                _onWin?.Invoke();

                // ── WIN LOGIC ─────────────────────────────────────────────────
                // Add anything here that fires when the player completes the race.
                // Examples:
                //   ShowResultsScreen();
                //   SceneManager.LoadScene("Results");
                //   AudioSource.PlayClipAtPoint(winClip, transform.position);
                //   PlayerPrefs.SetFloat("BestTime_" + _path.name, racer.FinishTime);
                // ─────────────────────────────────────────────────────────────
            }
        }
    }

    // ── Position ranking ───────────────────────────────────────────────────────

    void RecalculatePositions() {
        // Compare every racer against every other — O(n²), fine for ≤ ~16 racers
        for (int i = 0; i < _racers.Length; i++) {
            int pos = 1;
            for (int j = 0; j < _racers.Length; j++) {
                if (j != i && IsAheadOf(_racers[j], _racers[i]))
                    pos++;
            }
            _racers[i].Position = pos;
        }
    }

    // a is considered "ahead" of b if it has more laps, or equal laps but more checkpoints
    bool IsAheadOf(RacerState a, RacerState b) {
        if (a.LapCount != b.LapCount) return a.LapCount > b.LapCount;
        return a.CheckpointsCrossed > b.CheckpointsCrossed;
    }

    // ── HUD refresh ────────────────────────────────────────────────────────────

    void RefreshHUD() {
        RacerState player = PlayerState();
        if (player == null) return;

        int total     = _path ? _path.Count : 0;
        int passed    = player.CheckpointsCrossed;
        int remaining = Mathf.Max(0, total - passed);
        int displayLap = Mathf.Min(player.LapCount + 1, _totalLaps);

        SetText(_lapText,       $"Lap  {displayLap} / {_totalLaps}");
        SetText(_waypointText,  $"Checkpoint  {passed} / {total}");
        SetText(_remainingText, $"Remaining  {remaining}");
        SetText(_timeText,      FormatTime(player.RaceTime));
        SetText(_positionText,  Ordinal(player.Position));
    }

    RacerState PlayerState() {
        foreach (var r in _racers)
            if (r.Car == _playerCar) return r;
        return _racers is { Length: > 0 } ? _racers[0] : null;
    }

    // ── Utilities ──────────────────────────────────────────────────────────────

    void SetCarInput(bool on) {
        if (_playerCar) _playerCar.enabled = on;
    }

    static void SetText(TextMeshProUGUI tmp, string msg) {
        if (tmp) tmp.text = msg;
    }

    static string FormatTime(float t) {
        int   mins = (int)(t / 60f);
        float secs = t % 60f;
        return $"{mins}:{secs:00.000}";
    }

    static string Ordinal(int n) => n switch {
        1 => "1st",
        2 => "2nd",
        3 => "3rd",
        _ => $"{n}th"
    };

    // ── Public API ─────────────────────────────────────────────────────────────

    // Call from a start button / trigger if _autoStart is off
    public void StartRace() {
        if (Phase == RacePhase.WaitingToStart)
            BeginCountdown();
    }
}
