using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Keeps scene HUD components bound to the local player's runtime-spawned car.
/// Put this on the root Canvas; the car prefab should not own screen UI.
/// </summary>
public class LocalPlayerCanvasBinder : MonoBehaviour {
    [SerializeField]
    bool includeInactiveUi = true;

    MovingCar _boundCar;
    RaceRuntime _raceRuntime;
    CarHUD[] _huds;
    minimap[] _minimaps;

    void Awake () {
        if (Application.isBatchMode) {
            enabled = false;
            return;
        }

        CacheUiComponents();
    }

    void OnEnable () {
        CacheUiComponents();
        TryBindToLocalCar();
    }

    void LateUpdate () {
        if (!IsBindableCar(_boundCar))
            TryBindToLocalCar();
    }

    public void RefreshBindings () {
        _boundCar = null;
        CacheUiComponents();
        TryBindToLocalCar();
    }

    void CacheUiComponents () {
        if (_huds == null || _huds.Length == 0) {
            CarHUD hud = GetComponentInChildren<CarHUD>(includeInactiveUi);
            if (hud == null)
                hud = gameObject.AddComponent<CarHUD>();
        }

        _huds = GetComponentsInChildren<CarHUD>(includeInactiveUi);
        _minimaps = GetComponentsInChildren<minimap>(includeInactiveUi);
    }

    bool TryBindToLocalCar () {
        MovingCar localCar = FindLocalPlayerCar();
        if (localCar == null) return false;

        if (_raceRuntime == null)
            _raceRuntime = FindAnyObjectByType<RaceRuntime>();

        _raceRuntime?.SetPlayerCar(localCar);

        foreach (CarHUD hud in _huds)
            if (hud != null)
                hud.BindToCar(localCar, _raceRuntime);

        foreach (minimap map in _minimaps)
            if (map != null)
                map.BindToCar(localCar, _raceRuntime);

        _boundCar = localCar;
        return true;
    }

    static MovingCar FindLocalPlayerCar () {
        MovingCar[] cars = FindObjectsByType<MovingCar>(FindObjectsInactive.Exclude);
        foreach (MovingCar candidate in cars)
            if (IsBindableCar(candidate))
                return candidate;

        return null;
    }

    static bool IsBindableCar (MovingCar candidate) {
        if (candidate == null || !candidate.isActiveAndEnabled) return false;
        if (candidate.IsSpawned) return candidate.IsOwner;

        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager == null || !networkManager.IsListening;
    }
}
