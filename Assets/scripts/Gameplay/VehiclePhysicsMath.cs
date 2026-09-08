using UnityEngine;

public static class VehiclePhysicsMath {
    // Preserve the existing tuning at 50 Hz without overshoot at higher friction or other tick rates.
    public static float GripFraction(float gripAt50Hz, float deltaTime) =>
        1f - Mathf.Pow(1f - Mathf.Clamp01(gripAt50Hz), Mathf.Max(0f, deltaTime) / 0.02f);

    public static float BrakingAcceleration(float speed, float requestedDeceleration, float deltaTime) =>
        -Mathf.Sign(speed) * Mathf.Min(Mathf.Max(0f, requestedDeceleration),
            Mathf.Abs(speed) / Mathf.Max(deltaTime, 0.0001f));
}
