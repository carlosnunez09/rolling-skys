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

    const float LaneLength = 45f, LaneHalfWidth = 2.5f;

    /// Clones the scene car into parallel lanes alternating right/left of it. Lanes with
    /// an obstacle ahead are skipped -- notably the race-start gate a few metres in front
    /// of the scene car: its trigger starts a race whose countdown locks the player car's
    /// input, which used to stall one fleet car at the gate.
    List<Rigidbody> SpawnFleet (int count, float spacing) {
        var cars = FindCars();
        Assert.IsNotEmpty(cars, "SampleScene has no MovingCar");
        Rigidbody template = cars[0];
        Vector3 up = template.position.normalized;
        Vector3 fwd = Vector3.ProjectOnPlane(template.transform.forward, up).normalized;
        Vector3 right = Vector3.Cross(up, fwd);
        Assert.True(LaneIsClear(template.position, fwd, up, out string blocker), $"{template.name}'s lane is blocked by {blocker}");
        float rideHeight = HeightAboveGround(template);

        int spawned = 0;
        for (int lane = 1; spawned < count - 1 && lane <= 12; lane++) {
            float offset = spacing * ((lane + 1) / 2) * (lane % 2 == 1 ? 1f : -1f);
            Vector3 pos = GroundedOnShell(template.position + right * offset, rideHeight);
            Vector3 posUp = pos.normalized;
            Vector3 posFwd = Vector3.ProjectOnPlane(fwd, posUp).normalized;
            if (!LaneIsClear(pos, posFwd, posUp, out blocker)) {
                Debug.Log($"[CarDrivingTests] skipping lane {offset:+0;-0} m: {blocker}");
                continue;
            }
            spawned++;
            UnityEngine.Object.Instantiate(template.gameObject, pos, Quaternion.LookRotation(posFwd, posUp)).name = $"TestCar_{spawned}";
            Debug.Log($"[CarDrivingTests] TestCar_{spawned} in lane {offset:+0;-0} m");
        }
        return FindCars();
    }

    /// Sweeps a car-wide box along the lane at bumper-to-roof height. Solid colliders block
    /// unless they are gently rising ground; race-start and boost-pad triggers block too,
    /// since crossing them takes control away from the driver.
    static bool LaneIsClear (Vector3 pos, Vector3 fwd, Vector3 up, out string blocker) {
        Quaternion rot = Quaternion.LookRotation(fwd, up);
        Vector3 start = pos + up * 0.5f - fwd * 2f;
        var halfExtents = new Vector3(LaneHalfWidth, 0.6f, 1f);
        foreach (var hit in Physics.BoxCastAll(start, halfExtents, fwd, rot, LaneLength, ~0, QueryTriggerInteraction.Collide)) {
            Collider c = hit.collider;
            if (c.GetComponentInParent(CarType) != null) continue;
            if (c.isTrigger) {
                if (c.GetComponent("RaceStartTrigger") == null && c.GetComponent("BoostPad") == null) continue;
            } else if (hit.distance > 0f && Vector3.Dot(hit.normal, up) > 0.7f) {
                continue;
            }
            blocker = $"{c.name} at {hit.distance:F1} m";
            return false;
        }
        blocker = null;
        return true;
    }

    static float HeightAboveGround (Rigidbody rb) {
        Vector3 down = -rb.position.normalized;
        foreach (var hit in Physics.RaycastAll(rb.position - down * 0.5f, down, 10f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            if (hit.collider.GetComponentInParent(CarType) == null) return hit.distance - 0.5f;
        return 1f;
    }

    static Vector3 GroundedOnShell (Vector3 approx, float rideHeight) {
        Vector3 up = approx.normalized;
        foreach (var hit in Physics.RaycastAll(approx + up * 10f, -up, 30f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            if (hit.collider.GetComponentInParent(CarType) == null) return hit.point + up * (rideHeight + 0.05f);
        return approx;
    }

    static void Teleport (Rigidbody rb, Vector3 pos) {
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.position = pos;
        rb.transform.position = pos;
        Physics.SyncTransforms();
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

    static readonly Type RaceType = Type.GetType("RaceRuntime, Assembly-CSharp");
    static readonly Type TriggerType = Type.GetType("RaceStartTrigger, Assembly-CSharp");

    static Component TriggerRace (Component trigger) =>
        TriggerType.GetProperty("RaceRuntime").GetValue(trigger) as Component;

    static UnityEngine.Object TriggerPath (Component trigger) =>
        TriggerType.GetField("_path", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .GetValue(trigger) as UnityEngine.Object;

    static List<Component> FindRaceStartTriggers () =>
        UnityEngine.Object.FindObjectsByType(TriggerType, FindObjectsSortMode.None).Cast<Component>().ToList();

    [UnityTest]
    public IEnumerator RaceStartTriggers_AllLinkedToRaceRuntime () {
        Assert.NotNull(RaceType); Assert.NotNull(TriggerType);
        var triggers = FindRaceStartTriggers();
        if (triggers.Count == 0) Assert.Ignore("No race start in scene");
        foreach (var t in triggers) {
            Assert.NotNull(TriggerRace(t), $"race start trigger '{t.name}' at {t.transform.position} has no RaceRuntime");
            Assert.NotNull(TriggerPath(t), $"race start trigger '{t.name}' at {t.transform.position} has no path");
        }
        yield break;
    }

    [UnityTest, Timeout(60000)]
    public IEnumerator RaceStart_OnlyPlayerCarStartsCountdown () {
        Type raceType = RaceType;
        Assert.NotNull(raceType); Assert.NotNull(TriggerType);
        Rigidbody player = FindCars()[0];

        // SampleScene has several race starts; use the usable one nearest the scene car.
        var trigger = FindRaceStartTriggers()
            .Where(t => TriggerRace(t) != null && TriggerPath(t) != null)
            .OrderBy(t => (t.GetComponent<Collider>().bounds.center - player.position).sqrMagnitude)
            .FirstOrDefault();
        if (trigger == null) Assert.Ignore("No race start in scene");
        Component race = TriggerRace(trigger);
        string Phase () => raceType.GetProperty("Phase").GetValue(race).ToString();

        Vector3 gate = trigger.GetComponent<Collider>().bounds.center;
        Vector3 inGate = gate.normalized * player.position.magnitude;

        // A second Player-tagged car (stands in for a remote player's ghost). Its Start
        // registers it as RaceRuntime's player car, so hand that back to the scene car.
        Vector3 up = player.position.normalized;
        Vector3 aside = player.position - Vector3.Cross(up, player.transform.forward).normalized * 10f;
        var other = UnityEngine.Object.Instantiate(player.gameObject, aside, player.rotation).GetComponent<Rigidbody>();
        other.name = "TestCar_Other";
        yield return null;
        raceType.GetMethod("SetPlayerCar").Invoke(race, new object[] { player.GetComponent(CarType) });
        Assert.AreEqual("WaitingToStart", Phase());

        Teleport(other, inGate);
        for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();
        Assert.AreEqual("WaitingToStart", Phase(), "a non-player car entering the start trigger started the race");

        UnityEngine.Object.Destroy(other.gameObject);
        yield return null;
        Teleport(player, inGate);
        for (int i = 0; i < 10; i++) yield return new WaitForFixedUpdate();
        Assert.AreEqual("Countdown", Phase(), "the player car entering the start trigger should start the countdown");
    }
}
