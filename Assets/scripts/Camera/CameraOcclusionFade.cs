using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Per-camera shader cutaway. Does not change materials, queues, collision or shadows.
/// Bound by <see cref="FollowCameraPose"/>; add in Edit mode to serialize radius/softness.
/// </summary>
[RequireComponent(typeof(Camera))]
public sealed class CameraOcclusionFade : MonoBehaviour {
    [SerializeField, Min(0.2f)] float revealRadius = 1.4f;
    [SerializeField, Range(0.05f, 1f)] float edgeSoftness = 0.35f;
    static readonly int TargetId = Shader.PropertyToID("_CameraCutawayTarget");
    static readonly int CameraId = Shader.PropertyToID("_CameraCutawayCamera");
    static readonly int SettingsId = Shader.PropertyToID("_CameraCutawaySettings");
    static readonly int ExemptId = Shader.PropertyToID("_CameraCutawayExempt");
    Camera view;
    Transform target;
    Vector3 focusPosition;
    MaterialPropertyBlock block;
    readonly List<Exemption> exemptions = new List<Exemption>();
    struct Exemption { public Renderer renderer; public int slot; public float original; }

    public void SetTarget(Transform next, Vector3 position) {
        focusPosition = position;
        if (target == next) return;
        RestoreExemptions();
        target = next;
        ProtectTarget();
    }

    void OnEnable() {
        if (block == null) block = new MaterialPropertyBlock();
        view = GetComponent<Camera>();
        // Idempotent subscription also makes editor previews and assembly reloads safe.
        RenderPipelineManager.beginCameraRendering -= BeginCamera;
        RenderPipelineManager.endCameraRendering -= EndCamera;
        ProtectTarget();
        RenderPipelineManager.beginCameraRendering += BeginCamera;
        RenderPipelineManager.endCameraRendering += EndCamera;
    }

    void OnDisable() {
        RenderPipelineManager.beginCameraRendering -= BeginCamera;
        RenderPipelineManager.endCameraRendering -= EndCamera;
        Shader.SetGlobalVector(SettingsId, Vector4.zero);
        RestoreExemptions();
    }

    void BeginCamera(ScriptableRenderContext context, Camera camera) {
        if (camera != view) return;
        Shader.SetGlobalVector(CameraId, camera.transform.position);
        Shader.SetGlobalVector(TargetId, focusPosition);
        Shader.SetGlobalVector(SettingsId, target != null
            ? new Vector4(revealRadius, edgeSoftness, 1f, camera.nearClipPlane) : Vector4.zero);
    }

    void EndCamera(ScriptableRenderContext context, Camera camera) {
        if (camera == view) Shader.SetGlobalVector(SettingsId, Vector4.zero);
    }

    void ProtectTarget() {
        if (target == null || exemptions.Count > 0 || !isActiveAndEnabled) return;
        if (block == null) block = new MaterialPropertyBlock();
        foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true)) {
            Material[] materials = renderer.sharedMaterials;
            for (int slot = 0; slot < materials.Length; slot++) {
                if (materials[slot] == null || !materials[slot].HasProperty(ExemptId) || materials[slot].GetFloat(ExemptId) > .5f) continue;
                renderer.GetPropertyBlock(block, slot);
                // Preserve a renderer-wide block when introducing a per-material override.
                if (block.isEmpty) renderer.GetPropertyBlock(block);
                exemptions.Add(new Exemption { renderer = renderer, slot = slot, original = block.GetFloat(ExemptId) });
                block.SetFloat(ExemptId, 1f);
                renderer.SetPropertyBlock(block, slot);
            }
        }
    }

    void RestoreExemptions() {
        foreach (Exemption entry in exemptions) {
            if (entry.renderer == null) continue;
            entry.renderer.GetPropertyBlock(block, entry.slot);
            block.SetFloat(ExemptId, entry.original);
            entry.renderer.SetPropertyBlock(block, entry.slot);
        }
        exemptions.Clear();
    }

    public static bool SupportsCutaway(Collider collider) {
        Renderer renderer = collider.GetComponent<Renderer>();
        if (renderer == null) return false;
        Material[] materials = renderer.sharedMaterials;
        if (materials.Length == 0) return false;
        foreach (Material material in materials)
            if (material == null || !material.HasProperty(ExemptId) || material.GetFloat(ExemptId) > 0.5f) return false;
        return true;
    }
}
