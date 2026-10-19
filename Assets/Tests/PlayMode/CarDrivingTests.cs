using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// PlayMode driving tests on SampleScene (offline scene-test mode, no network).
/// Game code lives in Assembly-CSharp (no asmdef), so game types are resolved by name.
public class CarDrivingTests {
    const float PlanetRadiusMin = 470f, PlanetRadiusMax = 540f; // terrain ~498 m, gravity outer 527 m
    static readonly Type CarType = Type.GetType("MovingCar, Assembly-CSharp");

    Gamepad _pad;
    LogGuard _log;

    [UnitySetUp]
    public IEnumerator SetUp () {
        Assert.NotNull(CarType, "MovingCar type not found in Assembly-CSharp");
        _log = new LogGuard();
        _pad = InputSystem.AddDevice<Gamepad>("TestPad");
        yield return SceneManager.LoadSceneAsync("SampleScene", LoadSceneMode.Single);
        yield return new WaitForSeconds(1f);
    }

    [UnityTearDown]
    public IEnumerator TearDown () {
        LogAssert.ignoreFailingMessages = true;
        if (_pad != null) InputSystem.RemoveDevice(_pad);
        _pad = null;
        yield return null;
        _log.Dispose();
        _log.AssertClean();
    }

    void SetPad (float throttle, float steer) {
        InputSystem.QueueStateEvent(_pad, new GamepadState { rightTrigger = throttle, leftStick = new Vector2(steer, 0f) });
    }

    static List<Rigidbody> FindCars () =>
        UnityEngine.Object.FindObjectsByType(CarType, FindObjectsSortMode.None)
            .Cast<Component>().Select(c => c.GetComponent<Rigidbody>()).Where(r => r != null).ToList();

    static float CarSpeed (Rigidbody rb) => (float)CarType.GetProperty("Speed").GetValue(rb.GetComponent(CarType));

    static bool IsGrounded (Rigidbody rb, float maxGap = 3.5f) {
        Vector3 down = -rb.position.normalized;
        foreach (var hit in Physics.RaycastAll(rb.position - down * 0.5f, down, maxGap + 0.5f, ~0, QueryTriggerInteraction.Ignore))
            if (hit.rigidbody != rb && hit.collider.GetComponentInParent(CarType) == null) return true;
        return false;
    }

    static void AssertSane (Rigidbody rb, string tag) {
        Vector3 p = rb.position;
        Assert.False(float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z) || float.IsInfinity(p.sqrMagnitude), $"{tag}: NaN/Inf position");
        float r = p.magnitude;
        Assert.That(r, Is.InRange(PlanetRadiusMin, PlanetRadiusMax), $"{tag}: left planet shell (r={r:F1})");
        Assert.Less(rb.linearVelocity.magnitude, 150f, $"{tag}: exploded velocity {rb.linearVelocity.magnitude:F1}");
    }

    List<Rigidbody> SpawnFleet (int count, float spacing) {
        var cars = FindCars();
        Assert.IsNotEmpty(cars, "SampleScene has no MovingCar");
        Rigidbody template = cars[0];
        Vector3 up = template.position.normalized;
        Vector3 fwd = Vector3.ProjectOnPlane(template.transform.forward, up).normalized;
        Vector3 right = Vector3.Cross(up, fwd);
        for (int i = 1; i < count; i++) {
            Vector3 pos = template.position + right * spacing * i;
            pos = pos.normalized * template.position.magnitude;          // stay on the shell
            Quaternion rot = Quaternion.LookRotation(Vector3.ProjectOnPlane(fwd, pos.normalized), pos.normalized);
            UnityEngine.Object.Instantiate(template.gameObject, pos, rot).name = $"TestCar_{i}";
        }
        return FindCars();
    }

    [UnityTest, Timeout(60000)]
    public IEnumerator SceneCar_SettlesGroundedAtSpawn () {
        var car = FindCars()[0];
        yield return new WaitForSeconds(2f);
        AssertSane(car, car.name);
        Assert.True(IsGrounded(car), $"{car.name} not grounded after settle (r={car.position.magnitude:F1})");
    }

    [UnityTest, Timeout(90000)]
    public IEnumerator Fleet_DrivesAround_StaysGroundedSeparatedAndSane () {
        var cars = SpawnFleet(4, 8f);
        Assert.AreEqual(4, cars.Count);
        yield return new WaitForSeconds(1.5f);
        var start = cars.ToDictionary(c => c, c => c.position);
        var travelled = cars.ToDictionary(c => c, c => 0f);
        var last = new Dictionary<Rigidbody, Vector3>(start);
        var groundedSamples = cars.ToDictionary(c => c, c => 0);
        float peak = 0f;
        int samples = 0;

        // 8 s: full throttle with a slow left/right weave.
        for (float t = 0; t < 8f; t += Time.deltaTime) {
            SetPad(1f, Mathf.Sin(t * 1.2f) * 0.6f);
            yield return null;
            if (Time.frameCount % 5 != 0) continue;
            samples++;
            foreach (var c in cars) {
                AssertSane(c, c.name);
                travelled[c] += Vector3.Distance(last[c], c.position);
                last[c] = c.position;
                if (IsGrounded(c)) groundedSamples[c]++;
                peak = Mathf.Max(peak, CarSpeed(c));
            }
        }
        SetPad(0f, 0f);

        foreach (var c in cars) {
            float ratio = groundedSamples[c] / (float)samples;
            Debug.Log($"[CarDrivingTests] {c.name}: travelled {travelled[c]:F1} m, displacement {Vector3.Distance(start[c], c.position):F1} m, grounded {ratio:P0}");
            Assert.Greater(travelled[c], 30f, $"{c.name} barely moved");
            Assert.Greater(ratio, 0.7f, $"{c.name} airborne too much ({ratio:P0})");
        }
        for (int i = 0; i < cars.Count; i++)
            for (int j = i + 1; j < cars.Count; j++)
                Assert.Greater(Vector3.Distance(cars[i].position, cars[j].position), 1.5f, $"{cars[i].name} and {cars[j].name} overlap");
        Debug.Log($"[CarDrivingTests] peak speed {peak:F1}");
    }

    [UnityTest, Timeout(90000)]
    public IEnumerator Fleet_HeadOnCollision_DoesNotExplode () {
        var cars = FindCars();
        Rigidbody a = cars[0];
        Vector3 up = a.position.normalized;
        Vector3 fwd = Vector3.ProjectOnPlane(a.transform.forward, up).normalized;
        Vector3 bPos = (a.position + fwd * 25f).normalized * a.position.magnitude;
        var b = UnityEngine.Object.Instantiate(a.gameObject, bPos,
            Quaternion.LookRotation(-Vector3.ProjectOnPlane(fwd, bPos.normalized), bPos.normalized)).GetComponent<Rigidbody>();
        b.name = "TestCar_HeadOn";
        yield return new WaitForSeconds(1f);
        float minDist = float.MaxValue;
        for (float t = 0; t < 5f; t += Time.deltaTime) {
            SetPad(1f, 0f);
            yield return null;
            AssertSane(a, a.name); AssertSane(b, b.name);
            minDist = Mathf.Min(minDist, Vector3.Distance(a.position, b.position));
        }
        SetPad(0f, 0f);
        yield return new WaitForSeconds(2f);
        AssertSane(a, a.name); AssertSane(b, b.name);
        Debug.Log($"[CarDrivingTests] head-on min distance {minDist:F2} m");
        Assert.Greater(minDist, 0.5f, "cars interpenetrated");
    }

    [UnityTest, Timeout(120000)]
    public IEnumerator SingleCar_SteersThroughFirstWaypoints () {
        var car = FindCars()[0];
        var wps = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None)
            .Where(t => t.name.StartsWith("Waypoint_"))
            .OrderBy(t => int.TryParse(t.name.Substring(9), out int n) ? n : 999).ToList();
        if (wps.Count < 2) Assert.Ignore("No waypoints in scene");
        yield return new WaitForSeconds(1f);

        int start = 0; float best = float.MaxValue;
        for (int i = 0; i < wps.Count; i++) { float d = Vector3.Distance(car.position, wps[i].position); if (d < best) { best = d; start = i; } }

        int reached = 0, target = start;
        const int goal = 3;
        float t0 = Time.time;
        for (; Time.time - t0 < 60f && reached < goal;) {
            Vector3 up = car.position.normalized;
            Vector3 to = Vector3.ProjectOnPlane(wps[target].position - car.position, up);
            Vector3 fwd = Vector3.ProjectOnPlane(car.transform.forward, up);
            float angle = Vector3.SignedAngle(fwd, to, up);
            SetPad(Mathf.Abs(angle) > 60f ? 0.5f : 1f, Mathf.Clamp(angle / 30f, -1f, 1f));
            yield return null;
            AssertSane(car, car.name);
            if (to.magnitude < 15f) { reached++; target = (target + 1) % wps.Count; }
        }
        SetPad(0f, 0f);
        Debug.Log($"[CarDrivingTests] waypoints reached {reached}/{goal} starting at {wps[start].name} in {Time.time - t0:F1}s");
        Assert.GreaterOrEqual(reached, goal, $"only reached {reached} waypoints");
    }
}
