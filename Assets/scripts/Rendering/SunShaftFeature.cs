using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// URP Renderer Feature that adds screen-space sun shafts (god rays).
/// Add this to your PC_Renderer and/or Mobile_Renderer asset in the Renderer Features list.
/// Tune all parameters directly on the feature in the inspector.
/// </summary>
[Serializable]
public sealed class SunShaftFeature : ScriptableRendererFeature
{
    // ── Inspector Settings ──────────────────────────────────────────────────

    [Header("Color")]
    [ColorUsage(false, true)]
    public Color shaftColor = new Color(1f, 0.90f, 0.55f, 1f);

    [Header("Shape")]
    [Range(0f, 1f)]  public float intensity    = 0.55f;
    [Range(0f, 1f)]  public float falloff      = 0.92f;
    [Range(0f, 0.5f)]public float blurRadius   = 0.18f;
    [Range(4, 32)]   public int   samples      = 16;

    [Header("Occlusion")]
    [Range(0f, 1f)]  public float threshold    = 0.72f;

    public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;

    // ── Internals ───────────────────────────────────────────────────────────

    private SunShaftPass _pass;

    public override void Create()
    {
        _pass = new SunShaftPass(this)
        {
            renderPassEvent = renderPassEvent
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (renderingData.cameraData.cameraType == CameraType.Preview) return;
        if (renderingData.cameraData.cameraType == CameraType.SceneView &&
            !CoreUtils.AreAnimatedMaterialsEnabled(renderingData.cameraData.camera)) return;

        _pass.renderPassEvent = renderPassEvent;
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        _pass?.Dispose();
    }
}

// ── Render Pass ─────────────────────────────────────────────────────────────

internal sealed class SunShaftPass : ScriptableRenderPass, IDisposable
{
    private readonly SunShaftFeature _settings;
    private Material                 _material;

    private static readonly int _ShaftColorId   = Shader.PropertyToID("_ShaftColor");
    private static readonly int _SunScreenPosId = Shader.PropertyToID("_SunScreenPos");
    private static readonly int _FalloffId       = Shader.PropertyToID("_Falloff");
    private static readonly int _BlurRadiusId    = Shader.PropertyToID("_BlurRadius");
    private static readonly int _ThresholdId     = Shader.PropertyToID("_Threshold");
    private static readonly int _SamplesId       = Shader.PropertyToID("_Samples");

    private class PassData
    {
        public TextureHandle src;
        public Material      material;
    }

    public SunShaftPass(SunShaftFeature settings)
    {
        _settings        = settings;
        profilingSampler = new ProfilingSampler("Sun Shafts");
        _material        = CoreUtils.CreateEngineMaterial("Hidden/Custom/SunShaft");
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (_material == null) return;

        var resourceData = frameData.Get<UniversalResourceData>();
        var cameraData   = frameData.Get<UniversalCameraData>();

        // Project the sun (main directional light) to screen space
        Camera cam     = cameraData.camera;
        Light  sun     = RenderSettings.sun;
        Vector3 sunDir = sun != null ? -sun.transform.forward : Vector3.up;

        Vector3 sunVP  = cam.WorldToViewportPoint(cam.transform.position + sunDir * 1000f);
        bool    behind = sunVP.z < 0f;
        if (behind) sunVP = -sunVP; // mirror so the math still radiates outward

        _material.SetColor(_ShaftColorId,   _settings.shaftColor * _settings.intensity);
        _material.SetVector(_SunScreenPosId, new Vector4(sunVP.x, sunVP.y, behind ? 1f : 0f, 0f));
        _material.SetFloat(_FalloffId,       _settings.falloff);
        _material.SetFloat(_BlurRadiusId,    _settings.blurRadius);
        _material.SetFloat(_ThresholdId,     _settings.threshold);
        _material.SetInt(_SamplesId,         _settings.samples);

        // Allocate temp RT matching camera color
        var desc = cameraData.cameraTargetDescriptor;
        desc.depthBufferBits = 0;
        desc.msaaSamples     = 1;

        TextureHandle src  = resourceData.activeColorTexture;
        TextureHandle temp = UniversalRenderer.CreateRenderGraphTexture(
            renderGraph, desc, "_SunShaftTemp", false);

        using (var builder = renderGraph.AddRasterRenderPass<PassData>("Sun Shafts", out var passData))
        {
            passData.src      = src;
            passData.material = _material;

            builder.UseTexture(src);
            builder.SetRenderAttachment(temp, 0);
            builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
            {
                Blitter.BlitTexture(ctx.cmd, data.src, new Vector4(1, 1, 0, 0),
                                    data.material, 0);
            });
        }

        // Copy temp back to camera color
        using (var builder = renderGraph.AddRasterRenderPass<PassData>("Sun Shafts Blit Back", out var passData))
        {
            passData.src      = temp;
            passData.material = null;

            builder.UseTexture(temp);
            builder.SetRenderAttachment(src, 0);
            builder.SetRenderFunc(static (PassData data, RasterGraphContext ctx) =>
            {
                Blitter.BlitTexture(ctx.cmd, data.src, new Vector4(1, 1, 0, 0), 0, false);
            });
        }
    }

    public void Dispose()
    {
        CoreUtils.Destroy(_material);
        _material = null;
    }
}
