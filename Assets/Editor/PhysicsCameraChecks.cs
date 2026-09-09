using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

/// <summary>Deterministic regression checks using temporary objects, without editing the active scene.</summary>
public static class PhysicsCameraChecks {
    static int checks;
    static Gamepad testPad;
    static MovingCar drivingCar;
    static double drivingStart;
    static Vector3 drivingOrigin;
    static float peakSpeed;
    static int drivePhase = -1;
    static string drivingResult = "Not run";

    public static string BeginDrivingSmokeCheck() {
        if (!EditorApplication.isPlaying) return "Enter Play mode first.";
        if (testPad != null) return "Already running";
        foreach (var car in UnityEngine.Object.FindObjectsByType<MovingCar>(FindObjectsInactive.Exclude))
            if (car.HasLocalControl) { drivingCar = car; break; }
        if (drivingCar == null) return "No local car found.";
        testPad = InputSystem.AddDevice<Gamepad>();
        drivingStart = EditorApplication.timeSinceStartup;
        drivingOrigin = drivingCar.transform.position;
        peakSpeed = 0;
        drivePhase = -1;
        drivingResult = "Running: accelerate, turn, brake, coast.";
        EditorApplication.update += TickDrivingCheck;
        return drivingResult;
    }

    public static string DrivingSmokeResult() => drivingResult;

    static void TickDrivingCheck() {
        double elapsed = EditorApplication.timeSinceStartup - drivingStart;
        if (!EditorApplication.isPlaying || drivingCar == null) {
            FinishDrivingCheck("Stopped: Play mode ended or target disappeared.");
            return;
        }
        Vector3 velocity = drivingCar.GetComponent<Rigidbody>().linearVelocity;
        if (float.IsNaN(velocity.sqrMagnitude) || float.IsInfinity(velocity.sqrMagnitude)) {
            FinishDrivingCheck("FAIL: non-finite vehicle velocity.");
            return;
        }
        peakSpeed = Mathf.Max(peakSpeed, velocity.magnitude);
        int phase = elapsed < 2 ? 0 : elapsed < 3 ? 1 : elapsed < 4 ? 2 : 3;
        if (phase != drivePhase) {
            drivePhase = phase;
            Vector2 stick = phase == 0 ? Vector2.up : phase == 1 ? new Vector2(.4f, 1f) : phase == 2 ? Vector2.down : Vector2.zero;
            InputSystem.QueueStateEvent(testPad, new GamepadState { leftStick = stick });
        }
        if (elapsed < 6) return;
        float travelled = Vector3.Distance(drivingOrigin, drivingCar.transform.position);
        FinishDrivingCheck((peakSpeed > .5f && travelled > .2f ? "PASS" : "FAIL") +
            $": live driving smoke check; peak {peakSpeed:F2} m/s, displacement {travelled:F2} m; finite velocity throughout.");
    }

    static void FinishDrivingCheck(string result) {
        EditorApplication.update -= TickDrivingCheck;
        if (testPad != null) InputSystem.RemoveDevice(testPad);
        testPad = null;
        drivingCar = null;
        drivingResult = result;
        Debug.Log(result);
    }

    [MenuItem("Tools/Rolling Skys/Check Physics and Camera")]
    public static void RunFromMenu() => Debug.Log(Run());

    public static string Run() {
        checks = 0;
        Check(Mathf.Approximately(VehiclePhysicsMath.GripFraction(0f, .02f), 0f), "Ice has no artificial grip");
        Check(Mathf.Approximately(VehiclePhysicsMath.GripFraction(3f, .02f), 1f), "High grip cannot reverse lateral velocity");
        float at50 = 10f, at100 = 10f;
        for (int i = 0; i < 50; i++) at50 *= 1f - VehiclePhysicsMath.GripFraction(.02f, .02f);
        for (int i = 0; i < 100; i++) at100 *= 1f - VehiclePhysicsMath.GripFraction(.02f, .01f);
        Check(Mathf.Abs(at50 - at100) < .001f, "Drift grip is tick-rate independent");
        Check(Mathf.Abs(.1f + VehiclePhysicsMath.BrakingAcceleration(.1f, 120f, .02f) * .02f) < .00001f, "Brakes do not overshoot zero");
        Check(VehiclePhysicsMath.BrakingAcceleration(-10f, 20f, .02f) > 0, "Reverse coasting opposes motion");

        var objects = new List<UnityEngine.Object>();
        Scene scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
            UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Additive);
        try {
            Vector3 origin = new Vector3(12000, 12000, 12000);
            GameObject target = Make("Target", origin, scene, objects);
            target.AddComponent<BoxCollider>();
            Camera camera = Make("Check Camera", origin, scene, objects).AddComponent<Camera>();
            camera.enabled = false;
            camera.nearClipPlane = .1f;
            camera.aspect = 1.6f;
            var solver = new CameraObstructionSolver();
            Physics.SyncTransforms();
            Vector3 result = solver.Resolve(camera, target.transform, origin, Vector3.back, 5, -1, 4, 3, false);
            Check(Mathf.Abs(Vector3.Distance(origin, result) - 5) < .01f, "Camera ignores its own car");

            GameObject wall = Make("Wall", origin + Vector3.back * 2, scene, objects);
            BoxCollider box = wall.AddComponent<BoxCollider>();
            box.size = new Vector3(4, 4, .2f);
            box.isTrigger = true;
            Physics.SyncTransforms();
            solver.Reset();
            result = solver.Resolve(camera, target.transform, origin, Vector3.back, 5, -1, 4, 3, false);
            Check(Mathf.Abs(Vector3.Distance(origin, result) - 5) < .01f, "Race triggers do not collapse camera distance");
            box.isTrigger = false;
            Physics.SyncTransforms();
            solver.Reset();
            result = solver.Resolve(camera, target.transform, origin, Vector3.back, 5, -1, 4, 3, true);
            Check(Vector3.Distance(origin, result) < 1.9f, "Unsupported walls retain hard collision");
            Check(solver.IsPathBlocked(origin, origin + Vector3.back * 5, target.transform, -1), "Lag pivot cannot cross walls");

            var tracker = new CameraFocusTracker();
            tracker.Snap(origin);
            tracker.Update(origin + Vector3.right * 10f, 5f, 0.5f, null, null, 0);
            Check(Vector3.Distance(origin + Vector3.right * 10f, tracker.Point) <= CameraFocusTracker.MaxLagMetres + 0.001f,
                "Focus lag is capped at 1.5 metres");
            GameObject lagWall = Make("Lag wall", origin + Vector3.right * 0.4f, scene, objects);
            BoxCollider lagBox = lagWall.AddComponent<BoxCollider>();
            lagBox.size = new Vector3(.2f, 4f, 4f);
            Physics.SyncTransforms();
            tracker.Snap(origin + Vector3.right * 5f);
            tracker.Update(origin, 5f, 0f, solver, target.transform, -1);
            Check(Vector3.Distance(tracker.Point, origin) < 0.01f, "Focus tracker resets when a wall splits the lag");

            Shader toon = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Art/Toon/Shaders/toon.shader");
            var material = new Material(toon);
            objects.Add(material);
            wall.AddComponent<MeshRenderer>().sharedMaterial = material;
            solver.Reset();
            result = solver.Resolve(camera, target.transform, origin, Vector3.back, 5, -1, 4, 3, true);
            Check(Mathf.Abs(Vector3.Distance(origin, result) - 3f) < .01f, "Toon cutaway preserves comfort distance");
            material.SetFloat("_CameraCutawayExempt", 1);
            solver.Reset();
            result = solver.Resolve(camera, target.transform, origin, Vector3.back, 5, -1, 4, 3, true);
            Check(Vector3.Distance(origin, result) < 1.9f, "Exempt materials remain solid to the camera");

            // 70 self colliders exercise the nonalloc query's overflow path.
            for (int i = 0; i < 70; i++) {
                GameObject child = Make("Self collider", origin + Vector3.back * (.3f + i * .05f), scene, objects);
                child.transform.SetParent(target.transform, true);
                child.AddComponent<SphereCollider>().radius = .04f;
            }
            Physics.SyncTransforms();
            solver.Reset();
            result = solver.Resolve(camera, target.transform, origin, Vector3.back, 5, -1, 4, 3, false);
            Check(Vector3.Distance(origin, result) < 1.9f, "Saturated casts still find the wall");

            var gravity = Make("Box gravity", origin, scene, objects).AddComponent<AdaptiveGravitySource>();
            SetEnum(gravity, "shape", "Box");
            SetEnum(gravity, "boxMode", "AllFaces");
            Set(gravity, "internalGravity", false);
            Set(gravity, "boundaryDistance", Vector3.one * 5);
            Set(gravity, "boxInnerDistance", 5f);
            Set(gravity, "boxInnerFalloff", 5f);
            Invoke(gravity, "OnValidate");
            Vector3 g = gravity.GetGravity(origin + new Vector3(2, 0, 4));
            Check(g.z > 0 && Mathf.Abs(g.x) < .001f, "Room gravity pulls toward nearest Z wall, not X");
            g = gravity.GetGravity(origin + new Vector3(-4, 0, 2));
            Check(g.x < 0 && Mathf.Abs(g.z) < .001f, "Room gravity pulls toward negative X wall");
            SetEnum(gravity, "boxMode", "SingleFace");
            SetEnum(gravity, "singleFaceAxis", "UpY");
            Set(gravity, "internalGravity", true);
            Set(gravity, "boxOuterDistance", 3f);
            Set(gravity, "boxOuterFalloff", 4f);
            Invoke(gravity, "OnValidate");
            Check(gravity.GetGravity(origin + new Vector3(6, 1, 0)) == Vector3.zero, "Finite road gravity respects footprint");
            Check(gravity.GetGravity(origin + Vector3.up).y < 0, "Road gravity still acts inside footprint");

            foreach (string path in new[] {
                "Assets/Art/Toon/Shaders/toon.shader", "Assets/Art/Planet/Shaders/PlanetSurface.shader",
                "Assets/Art/Environment/Grass/Shaders/GrassToon.shader", "Assets/Art/Environment/Trees/Shaders/leavesShader.shader" }) {
                Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                Check(shader != null && !ShaderUtil.ShaderHasError(shader), "Shader compiles: " + path);
            }
            return $"PASS: {checks} physics, camera and shader checks.";
        } finally {
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null) {
                    if (objects[i] is GameObject go && go.TryGetComponent<CameraOcclusionFade>(out var effect)) Invoke(effect, "OnDisable");
                    UnityEngine.Object.DestroyImmediate(objects[i]);
                }
            UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
        }
    }

    static GameObject Make(string name, Vector3 position, Scene scene, List<UnityEngine.Object> objects) {
        var go = new GameObject(name);
        SceneManager.MoveGameObjectToScene(go, scene);
        go.transform.position = position;
        objects.Add(go);
        return go;
    }
    static void Check(bool condition, string description) {
        if (!condition) throw new Exception("FAILED: " + description);
        checks++;
    }
    static void Invoke(object obj, string name) => obj.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(obj, null);
    static FieldInfo Field(object obj, string name) => obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
    static void Set(object obj, string name, object value) => Field(obj, name).SetValue(obj, value);
    static void SetEnum(object obj, string name, string value) => Set(obj, name, Enum.Parse(Field(obj, name).FieldType, value));

    public static string RenderCutawayCheck() {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
            UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Additive);
        var objects = new List<UnityEngine.Object>();
        string folder = System.IO.Path.GetFullPath("Temp/CameraChecks");
        System.IO.Directory.CreateDirectory(folder);
        try {
            Vector3 origin = Vector3.one * 12000;
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Art/Toon/Shaders/toon.shader");
            var red = new Material(shader); objects.Add(red);
            red.SetColor("_BaseColor", new Color(.95f, .08f, .03f));
            red.SetColor("_SpecColor", Color.black);
            red.SetColor("_ShadowColor", new Color(.8f, .8f, .8f));
            red.SetFloat("_OutlineWidth", 0);
            var blue = new Material(shader); objects.Add(blue);
            blue.SetColor("_BaseColor", new Color(.05f, .3f, .7f));
            blue.SetColor("_SpecColor", Color.black);
            blue.SetColor("_ShadowColor", new Color(.8f, .8f, .8f));
            blue.SetFloat("_OutlineWidth", 0);
            GameObject target = GameObject.CreatePrimitive(PrimitiveType.Cube); objects.Add(target);
            SceneManager.MoveGameObjectToScene(target, scene);
            target.transform.position = origin;
            target.GetComponent<Renderer>().sharedMaterial = red;
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube); objects.Add(wall);
            SceneManager.MoveGameObjectToScene(wall, scene);
            wall.transform.position = origin + Vector3.back * 2;
            wall.transform.localScale = new Vector3(6, 4, .3f);
            wall.GetComponent<Renderer>().sharedMaterial = blue;
            Camera camera = Make("Cutaway preview", origin + Vector3.back * 6, scene, objects).AddComponent<Camera>();
            camera.enabled = false;
            camera.fieldOfView = 50;
            camera.nearClipPlane = .1f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.03f, .04f, .06f);
            var fade = camera.gameObject.AddComponent<CameraOcclusionFade>();
            // These callbacks are not automatic for edit-mode-only components.
            Invoke(fade, "OnEnable");
            fade.SetTarget(target.transform, origin);
            Color blocked = Capture(camera, folder + "/blocked.png", false);
            Color revealed = Capture(camera, folder + "/revealed.png", true);
            Check(blocked.b > blocked.r, "Baseline wall hides red target");
            Check(revealed.r > revealed.b, "Cutaway reveals target without changing toon material");
            Check(Shader.GetGlobalVector("_CameraCutawaySettings").z == 0, "Cutaway globals reset after camera rendering");
            Invoke(fade, "OnDisable");
            return "PASS: rendered occlusion and target exemption. Images: " + folder;
        } finally {
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null) {
                    if (objects[i] is GameObject go && go.TryGetComponent<CameraOcclusionFade>(out var effect)) Invoke(effect, "OnDisable");
                    UnityEngine.Object.DestroyImmediate(objects[i]);
                }
            UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
        }
    }

    static Color Capture(Camera camera, string path, bool reveal) {
        CameraOcclusionFade fade = camera.GetComponent<CameraOcclusionFade>();
        // Explicitly unsubscribe / subscribe in edit mode, where normal MonoBehaviour callbacks are inactive.
        Invoke(fade, "OnDisable");
        if (reveal) { Invoke(fade, "OnEnable"); }
        RenderTexture previous = RenderTexture.active;
        var texture = new RenderTexture(800, 500, 24);
        var pixels = new Texture2D(800, 500, TextureFormat.RGB24, false);
        try {
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture.active = texture;
            pixels.ReadPixels(new Rect(0, 0, 800, 500), 0, 0);
            pixels.Apply();
            System.IO.File.WriteAllBytes(path, pixels.EncodeToPNG());
            return pixels.GetPixel(400, 250);
        } finally {
            camera.targetTexture = null;
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(pixels);
            texture.Release();
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
