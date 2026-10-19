using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// Host-mode netcode test (single process): MainMenu -> GameNetworkManager.StartHost ->
/// SampleScene, host car spawns grounded at slot 0 and can drive.
/// (A second in-process client is not possible with NGO's NetworkManager singleton;
/// host+client is covered by the two-window build test.)
public class NetworkHostSpawnTests {
    static readonly Type GnmType = Type.GetType("GameNetworkManager, Assembly-CSharp");
    static readonly Type CarType = Type.GetType("MovingCar, Assembly-CSharp");
    Gamepad _pad;
    LogGuard _log;

    [UnityTearDown]
    public IEnumerator TearDown () {
        LogAssert.ignoreFailingMessages = true;
        if (_pad != null) InputSystem.RemoveDevice(_pad);
        _log?.Dispose();
        var nm = Unity.Netcode.NetworkManager.Singleton;
        if (nm != null) { nm.Shutdown(); yield return null; UnityEngine.Object.Destroy(nm.gameObject); }
        var gnm = GnmType != null ? UnityEngine.Object.FindAnyObjectByType(GnmType) as Component : null;
        if (gnm != null) UnityEngine.Object.Destroy(gnm.gameObject);
        yield return null;
    }

    [UnityTest, Timeout(120000)]
    public IEnumerator Host_SpawnsGroundedCarAtSlot0_AndDrives () {
        _log = new LogGuard();
        Assert.NotNull(GnmType); Assert.NotNull(CarType);
        // Test isolation: earlier tests lazily create a standalone SteamManager (via SteamManager.Instance).
        // MainMenu's "networkmanager" GameObject also carries a SteamManager whose Awake does
        // Destroy(gameObject) when another instance exists -- which takes NetworkManager and
        // GameNetworkManager down with it (known bug, see report). Clear it so this test is order-independent.
        var steamType = Type.GetType("SteamManager, Assembly-CSharp");
        if (steamType != null)
            foreach (Component c in UnityEngine.Object.FindObjectsByType(steamType, FindObjectsSortMode.None))
                UnityEngine.Object.DestroyImmediate(c.gameObject);
        yield return null;
        yield return SceneManager.LoadSceneAsync("MainMenu", LoadSceneMode.Single);
        for (int f = 0; f < 6; f++) {
            var nms = UnityEngine.Object.FindObjectsByType<Unity.Netcode.NetworkManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Debug.Log($"[NetworkHostSpawnTests] frame {f}: managers={nms.Length} [{string.Join(",", nms.Select(n => n.gameObject.scene.name + "/" + n.name + (n.isActiveAndEnabled ? "" : "(inactive)")))}] singleton={(Unity.Netcode.NetworkManager.Singleton ? Unity.Netcode.NetworkManager.Singleton.name : "null")}");
            yield return null;
        }
        float w0 = Time.realtimeSinceStartup;
        while (Unity.Netcode.NetworkManager.Singleton == null && Time.realtimeSinceStartup - w0 < 5f) yield return null;
        Assert.NotNull(Unity.Netcode.NetworkManager.Singleton, "MainMenu has no NetworkManager");
        yield return new WaitForSeconds(0.5f); // let GameNetworkManager.Start subscribe callbacks (as when a user clicks Host)
        Debug.Log($"[NetworkHostSpawnTests] after delay: Singleton={(Unity.Netcode.NetworkManager.Singleton != null)}, managers={UnityEngine.Object.FindObjectsByType<Unity.Netcode.NetworkManager>(FindObjectsSortMode.None).Length}");
        var gnm = GnmType.GetProperty("Instance").GetValue(null);
        Assert.NotNull(gnm, "GameNetworkManager.Instance missing in MainMenu");
        // Use a free port so this doesn't collide with a running game host on 7777.
        var l = new System.Net.Sockets.UdpClient(0); ushort port = (ushort)((System.Net.IPEndPoint)l.Client.LocalEndPoint).Port; l.Close();
        GnmType.GetField("_port", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(gnm, port);
        GnmType.GetMethod("StartHost").Invoke(gnm, null);

        float t0 = Time.realtimeSinceStartup;
        Component car = null;
        while (Time.realtimeSinceStartup - t0 < 30f) {
            car = UnityEngine.Object.FindObjectsByType(CarType, FindObjectsSortMode.None).Cast<Component>()
                .FirstOrDefault(c => { var no = c.GetComponent<Unity.Netcode.NetworkObject>(); return no != null && no.IsSpawned && no.IsPlayerObject; });
            if (car != null) break;
            if (Time.frameCount % 120 == 0) {
                var nmd = Unity.Netcode.NetworkManager.Singleton;
                Debug.Log($"[NetworkHostSpawnTests] t={Time.realtimeSinceStartup - t0:F1} scene={SceneManager.GetActiveScene().name} listening={nmd?.IsListening} clients={nmd?.ConnectedClientsIds.Count} cars={UnityEngine.Object.FindObjectsByType(CarType, FindObjectsSortMode.None).Length}");
            }
            yield return null;
        }
        Assert.NotNull(car, "host player car never spawned");
        Assert.AreEqual("SampleScene", SceneManager.GetActiveScene().name);
        Assert.True(car.GetComponent<Unity.Netcode.NetworkObject>().IsOwner);

        yield return null;
        var activeCars = UnityEngine.Object.FindObjectsByType(CarType, FindObjectsSortMode.None).Cast<Component>().ToList();
        Debug.Log($"[NetworkHostSpawnTests] active cars: {string.Join(", ", activeCars.Select(c => $"{c.name} spawned={c.GetComponent<Unity.Netcode.NetworkObject>()?.IsSpawned} player={c.GetComponent<Unity.Netcode.NetworkObject>()?.IsPlayerObject}"))}");
        var sp1 = GameObject.Find("SpawnPoint_01");
        Assert.NotNull(sp1);
        var rb = car.GetComponent<Rigidbody>();
        Assert.Less(Vector3.Distance(rb.position, sp1.transform.position), 4f, "host car not at SpawnPoint_01");
        Assert.AreEqual(8, GameObject.FindObjectsByType<Transform>(FindObjectsSortMode.None).Count(t => t.name.StartsWith("SpawnPoint_")),
            "expected 8 spawn slots (3 authored + 5 generated)");

        yield return new WaitForSeconds(1.5f);
        Vector3 start = rb.position;
        _pad = InputSystem.AddDevice<Gamepad>("NetTestPad");
        for (float t = 0; t < 4f; t += Time.deltaTime) {
            InputSystem.QueueStateEvent(_pad, new GamepadState { rightTrigger = 1f });
            yield return null;
        }
        float moved = Vector3.Distance(start, rb.position);
        Debug.Log($"[NetworkHostSpawnTests] host car moved {moved:F1} m, r={rb.position.magnitude:F1}");
        Assert.Greater(moved, 10f);
        Assert.That(rb.position.magnitude, Is.InRange(470f, 540f));
        _log.AssertClean();
        Assert.AreEqual(1, activeCars.Count, "host session should have exactly one car (offline scene-test car should be disabled)");
    }
}
