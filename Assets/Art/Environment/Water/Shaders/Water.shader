Shader "Custom/Water"
{
    Properties
    {
        [Header(Color)]
        _WaterColor      ("Water Color",     Color)         = (0.08, 0.40, 0.62, 0.72)
        _EdgeColor       ("Edge Rim Color",  Color)         = (0.55, 0.82, 1.00, 0.90)

        [Header(Specular)]
        _SpecularColor   ("Specular Color",  Color)         = (1.0, 1.0, 1.0, 1.0)
        _Smoothness      ("Smoothness",      Range(8, 512)) = 120
        _SpecularStrength("Specular Strength", Range(0, 1)) = 0.75

        [Header(Surface)]
        _FresnelPower    ("Fresnel Power",   Range(1, 8))   = 3.0
        _WaveSpeed       ("Wave Speed",      Float)         = 1.2
        _WaveScale       ("Wave Scale",      Float)         = 2.0
        _WaveStrength    ("Wave Strength",   Range(0, 1))   = 0.35

        [Header(Foam)]
        _FoamColor       ("Foam Color",      Color)         = (1, 1, 1, 1)
        _FoamDepth       ("Shore Width",     Float)         = 2.0
        _FoamScale       ("Foam Scale",      Float)         = 4.0
        _FoamSpeed       ("Foam Speed",      Float)         = 0.3
        _FoamCutoff      ("Foam Cutoff",     Range(0, 1))   = 0.48
        _FoamSoftness    ("Foam Softness",   Range(0.01, 0.5)) = 0.22

        [Header(Wake)]
        _WakeFadeSpeed   ("Wake Fade Speed", Range(1, 20))    = 6.0

        [Header(Sky Reflection)]
        [HDR] _SkyColor           ("Sky Color",           Color)        = (0.40, 0.65, 1.00, 1)
        [HDR] _CloudReflectColor  ("Cloud Reflect Color", Color)        = (1.00, 1.00, 1.00, 1)
        _CloudReflectScale        ("Cloud Scale",         Float)        = 0.12
        _CloudReflectCoverage     ("Cloud Coverage",      Range(0, 1))  = 0.48
        _CloudReflectStrength     ("Reflect Strength",    Range(0, 1))  = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "WaterForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4  _WaterColor;
                half4  _EdgeColor;
                half4  _SpecularColor;
                float  _Smoothness;
                float  _SpecularStrength;
                float  _FresnelPower;
                float  _WaveSpeed;
                float  _WaveScale;
                float  _WaveStrength;
                half4  _FoamColor;
                float  _FoamDepth;
                float  _FoamScale;
                float  _FoamSpeed;
                float  _FoamCutoff;
                float  _FoamSoftness;
                float  _WakeFadeSpeed;
                half4  _SkyColor;
                half4  _CloudReflectColor;
                float  _CloudReflectScale;
                float  _CloudReflectCoverage;
                float  _CloudReflectStrength;
            CBUFFER_END

            // Global trample data — written by GrassTrampler.cs via Shader.SetGlobal*
            // No Properties entry needed; works automatically when trampler is in scene.
            float4 _TramplePos;
            float  _TrampleRadius;
            float  _TrampleStrength;
            float4 _TrampleHistory[64];
            float  _TrampleGameTime;
            float  _RecoveryDuration;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float4 screenPos   : TEXCOORD2;
                float3 viewDir     : TEXCOORD3;
            };

            // Builds a consistent tangent frame from any surface normal.
            // Works on spheres, ramps, or any curved surface.
            void GetTangentFrame(float3 N, out float3 T, out float3 B)
            {
                float3 up = abs(N.y) < 0.9 ? float3(0, 1, 0) : float3(1, 0, 0);
                T = normalize(cross(up, N));
                B = cross(N, T);
            }

            // Wave normal in surface-local space — no xz assumption.
            float3 WaveNormal(float3 posWS, float3 meshN, float time)
            {
                float3 T, B;
                GetTangentFrame(meshN, T, B);

                // Project world position onto the surface tangent plane for wave UVs.
                float2 uv = float2(dot(posWS, T), dot(posWS, B)) * _WaveScale;
                float  t  = time * _WaveSpeed;

                float dhx = cos(uv.x * 1.70 + t * 1.10)
                           + cos(uv.x * 0.85 - t * 0.80 + uv.y * 1.20) * 0.5;
                float dhy = sin(uv.y * 1.50 - t * 0.90)
                           + sin(uv.y * 0.65 + t * 1.30 + uv.x * 0.80) * 0.5;

                // Perturb along the tangent/bitangent so waves flow across the surface.
                return normalize(meshN + (T * dhx + B * dhy) * _WaveStrength * 0.25);
            }

            // Surface-projected distance from posWS to a point — ignores radial
            // (height) difference so it works correctly on a curved sphere surface.
            float SurfaceDist(float3 posWS, float3 meshN, float3 other)
            {
                float3 delta = other - posWS;
                float3 tangentDelta = delta - dot(delta, meshN) * meshN;
                return length(tangentDelta);
            }

            // Returns 0-1 influence of the trampler at a water fragment.
            // Mirrors the grass shader's ApplyTrample logic exactly.
            float TrampleInfluence(float3 posWS, float3 meshN)
            {
                float r    = max(_TrampleRadius, 0.001);
                float best = 0.0;

                // Live position — always full strength
                float liveD = SurfaceDist(posWS, meshN, _TramplePos.xyz);
                float liveF = saturate(1.0 - liveD / r);
                best = max(best, liveF * liveF);

                // History stamps — fade matches grass shader exactly
                for (int i = 0; i < 64; i++)
                {
                    float age  = _TrampleGameTime - _TrampleHistory[i].w;
                    float fade = 1.0 - saturate(age * _WakeFadeSpeed / max(_RecoveryDuration, 1.0));
                    if (fade < 0.001) continue;

                    float d = SurfaceDist(posWS, meshN, _TrampleHistory[i].xyz);
                    float f = saturate(1.0 - d / r);
                    best = max(best, f * f * fade);
                }

                return best;
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = frac(sin(dot(i,               float2(127.1, 311.7))) * 43758.5);
                float b = frac(sin(dot(i + float2(1,0), float2(127.1, 311.7))) * 43758.5);
                float c = frac(sin(dot(i + float2(0,1), float2(127.1, 311.7))) * 43758.5);
                float d = frac(sin(dot(i + float2(1,1), float2(127.1, 311.7))) * 43758.5);
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            // 3-octave FBM for cloud reflection — sampled in reflected XZ world space.
            float CloudFbm(float2 p)
            {
                float v = 0.0, a = 0.5;
                [unroll] for (int i = 0; i < 3; i++) { v += a * ValueNoise(p); p *= 2.1; a *= 0.5; }
                return v;
            }

            Varyings vert(Attributes input)
            {
                Varyings output  = (Varyings)0;
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionHCS = pos.positionCS;
                output.positionWS  = pos.positionWS;
                output.normalWS    = TransformObjectToWorldNormal(input.normalOS);
                output.screenPos   = ComputeScreenPos(pos.positionCS);
                output.viewDir     = GetWorldSpaceNormalizeViewDir(pos.positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 meshN = normalize(input.normalWS);
                float3 N     = WaveNormal(input.positionWS, meshN, _Time.y);
                float3 V = normalize(input.viewDir);

                Light  mainLight = GetMainLight();
                float3 L = normalize(mainLight.direction);
                float3 H = normalize(L + V);

                float NdotL = saturate(dot(N, L));
                float NdotH = saturate(dot(N, H));
                float NdotV = saturate(dot(N, V));

                // ── Trample / wake ─────────────────────────────────────────
                float trample = TrampleInfluence(input.positionWS, meshN);

                // Calm the surface where disturbed — pull back toward the bare sphere normal
                N = normalize(lerp(N, meshN, trample * 0.85));

                // Recalculate dot products after normal change
                NdotL = saturate(dot(N, L));
                NdotH = saturate(dot(N, H));
                NdotV = saturate(dot(N, V));

                // ── Blinn-Phong specular ────────────────────────────────────
                float spec = pow(NdotH, _Smoothness) * _SpecularStrength * NdotL;

                // Fresnel rim
                float fresnel   = pow(1.0 - NdotV, _FresnelPower);
                half3 baseColor = lerp(_WaterColor.rgb, _EdgeColor.rgb, fresnel);
                half3 litColor  = baseColor * (NdotL * 0.5 + 0.5)
                                + _SpecularColor.rgb * spec;

                // Shore foam
                float2 screenUV  = input.screenPos.xy / input.screenPos.w;
                float  sceneDep  = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float  surfDep   = input.screenPos.w;
                float  depthDiff = max(0.0, sceneDep - surfDep);

                float  shoreMask = 1.0 - saturate(depthDiff / max(_FoamDepth, 0.001));

                float3 fT, fB;
                GetTangentFrame(meshN, fT, fB);
                float2 surfUV = float2(dot(input.positionWS, fT), dot(input.positionWS, fB));

                float2 fuv1  = surfUV * _FoamScale
                             + _Time.y * _FoamSpeed * float2( 0.30,  0.10);
                float2 fuv2  = surfUV * _FoamScale * 0.65
                             + _Time.y * _FoamSpeed * float2(-0.12,  0.22);
                float  foamN = ValueNoise(fuv1) * 0.6 + ValueNoise(fuv2) * 0.4;
                float  foam  = smoothstep(_FoamCutoff, _FoamCutoff + _FoamSoftness, foamN) * shoreMask;

                // Wake edge brightening — visible ring at the disturbance boundary
                float wakeEdge = smoothstep(0.15, 0.5, trample)
                               * (1.0 - smoothstep(0.55, 1.0, trample))
                               * 1.4;
                litColor += _FoamColor.rgb * wakeEdge;

                // ── Sky / cloud reflection ──────────────────────────────────
                // Compute the reflected view ray and sample cloud FBM in its XZ plane.
                // This gives each water fragment a plausible cloud overhead colour
                // without needing a live reflection camera.
                float3 reflDir  = reflect(-V, N);
                // Flatten to horizontal so clouds feel above the water, not behind it.
                float2 cloudUV  = (reflDir.xz / max(abs(reflDir.y) + 0.01, 0.1))
                                  * _CloudReflectScale
                                  + _Time.y * 0.008;
                float  cloudN   = CloudFbm(cloudUV);
                float  cloudMask = smoothstep(_CloudReflectCoverage - 0.1, _CloudReflectCoverage + 0.1, cloudN);

                // Sky base tint follows the fresnel so grazing angles get more sky colour.
                half3 skyReflect   = lerp(_SkyColor.rgb, _CloudReflectColor.rgb, cloudMask);
                float reflFresnel  = pow(1.0 - NdotV, 2.5);
                litColor = lerp(litColor, skyReflect, reflFresnel * _CloudReflectStrength);

                half3  finalColor = lerp(litColor, _FoamColor.rgb, foam);
                half   finalAlpha = max(_WaterColor.a, foam * _FoamColor.a * shoreMask);

                return half4(finalColor, finalAlpha);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
