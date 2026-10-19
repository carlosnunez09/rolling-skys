
/// Collects real errors/exceptions while ignoring known environment noise (no Steam / audio device).
public sealed class LogGuard : System.IDisposable {
    static readonly string[] Noise = { "Steam", "FMOD", "ALSA", "dlopen", "steamclient" };
    public readonly System.Collections.Generic.List<string> Errors = new System.Collections.Generic.List<string>();
    public LogGuard () { UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true; UnityEngine.Application.logMessageReceived += OnLog; }
    void OnLog (string msg, string stack, UnityEngine.LogType type) {
        if (type != UnityEngine.LogType.Error && type != UnityEngine.LogType.Exception && type != UnityEngine.LogType.Assert) return;
        foreach (var n in Noise) if (msg.Contains(n)) return;
        Errors.Add($"{type}: {msg}");
    }
    public void AssertClean () => NUnit.Framework.Assert.IsEmpty(Errors, "Unexpected errors:\n" + string.Join("\n", Errors));
    public void Dispose () { UnityEngine.Application.logMessageReceived -= OnLog; }
}
