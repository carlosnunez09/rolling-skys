Shader "Custom/GrassToon"
{
    Properties
    {
        [Header(Colors)]
        _RootColor("Root Color", Color) = (0.05, 0.25, 0.05, 1)
        _TipColor("Tip Color", Color) = (0.35, 0.75, 0.15, 1)
        _ShadowColor("Shadow Color", Color) = (0.02, 0.12, 0.02, 1)
        _ShadowStrength("Shadow Strength", Range(0, 1)) = 0.5

        [Header(Bloom)]
        [HDR] _EmissionColor("Tip Emission (HDR)", Color) = (0.4, 1.0, 0.2, 1)
        _TipEmission("Tip Emission Intensity", Range(0, 8)) = 1.5

        [Header(Alpha)]
        _MainTex("Blade Mask (alpha channel)", 2D) = "white" {}
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.4
        // How sharply the procedural taper edge clips (lower = harder edge)
        _TaperSoftness("Taper Edge Softness", Range(0.01, 0.5)) = 0.08

        [Header(Wind)]
        _WindStrength("Wind Strength", Float) = 0.25
        _WindSpeed("Wind Speed", Float) = 1.2
        _WindDirection("Wind Direction", Vector) = (1, 0, 0.3, 0)

        [Header(Debug)]
        [Toggle] _DebugHeight("Show UV Height (root=black tip=white)", Float) = 0
        _TestBend("Test Bend", Range(0, 4)) = 0

    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" }

        // ── Shared macro block ────────────────────────────────────────────────
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4  _RootColor;
            half4  _TipColor;
            half4  _ShadowColor;
            half   _ShadowStrength;
            half4  _EmissionColor;
            half   _TipEmission;
            float  _WindStrength;
            float  _WindSpeed;
            float4 _WindDirection;
            float4 _MainTex_ST;
            half   _Cutoff;
            half   _TaperSoftness;
            float  _TestBend;
            float  _DebugHeight;
        CBUFFER_END

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);

        // Trampling globals — written every frame by GrassTrampler.cs
        float4 _TramplePos;
        float  _TrampleRadius;
        float  _TrampleStrength;

        // Trail history — xyz = world position, w = game time when stamped
        #define TRAMPLE_HIST 64
        float4 _TrampleHistory[TRAMPLE_HIST];
        float  _TrampleGameTime;    // current Time.time passed from C#
        float  _RecoveryDuration;   // seconds until a stamped position fully recovers

        // Shared wind function
        float3 AnimateWind(float3 worldPos, float height)
        {
            float  phase   = worldPos.x * 1.2 + worldPos.z * 0.8;
            float  wave    = sin(_Time.y * _WindSpeed + phase)
                           + sin(_Time.y * _WindSpeed * 2.3 + phase * 1.7) * 0.3;
            float3 windDir = normalize(_WindDirection.xyz);
            return worldPos + windDir * (wave * _WindStrength * height * height);
        }

        float3 ApplyTrample(float3 worldPos, float height)
        {
            float3 rootWS  = mul(UNITY_MATRIX_M, float4(0, 0, 0, 1)).xyz;
            float  maxBend = 0;
            float3 bestDir = float3(1, 0, 0);

            // Live trampler — full strength, no age fade
            {
                float3 toRoot  = rootWS - _TramplePos.xyz;
                float  d       = length(toRoot);
                float  f       = saturate(1.0 - d / max(_TrampleRadius, 0.001));
                       f      *= f;
                if (f > maxBend) { maxBend = f; if (d > 0.001) bestDir = normalize(toRoot); }
            }

            // Historical stamps — fade out over RecoveryDuration
            for (int i = 0; i < TRAMPLE_HIST; i++)
            {
                float3 hPos   = _TrampleHistory[i].xyz;
                float  hTime  = _TrampleHistory[i].w;
                float  age    = _TrampleGameTime - hTime;
                float  fade   = 1.0 - saturate(age / max(_RecoveryDuration, 1.0));
                if (fade < 0.001) continue;

                float3 toRoot = rootWS - hPos;
                float  d      = length(toRoot);
                float  f      = saturate(1.0 - d / max(_TrampleRadius, 0.001));
                       f      = f * f * fade;
                if (f > maxBend) { maxBend = f; if (d > 0.001) bestDir = normalize(toRoot); }
            }

            return worldPos + bestDir * (maxBend * _TrampleStrength * height);
        }
        ENDHLSL

        // ── Lit pass ──────────────────────────────────────────────────────────
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                half3  normalWS    : TEXCOORD1;
                float3 positionWS  : TEXCOORD2;
                float4 shadowCoord : TEXCOORD3;
                half   height      : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                Varyings OUT;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                half   height  = saturate(IN.uv.y);
                float3 animWS  = AnimateWind(TransformObjectToWorld(IN.positionOS.xyz), height);
                       animWS  = ApplyTrample(animWS, height);
                // Test bend: lean tips along blade local-X (always tangent to surface).
                float3 localRight = normalize(mul((float3x3)UNITY_MATRIX_M, float3(1, 0, 0)));
                       animWS += localRight * (_TestBend * height);

                OUT.positionHCS = TransformWorldToHClip(animWS);
                OUT.positionWS  = animWS;
                OUT.normalWS    = half3(TransformObjectToWorldNormal(IN.normalOS));
                OUT.shadowCoord = TransformWorldToShadowCoord(animWS);
                OUT.uv          = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.height      = height;
                return OUT;
            }

            half4 frag(Varyings IN, half facing : VFACE) : SV_Target
            {
                // ── Mask ─────────────────────────────────────────────────────
                // texMask reads the alpha channel, which Unity generates from
                // the texture's grayscale luminance when imported with
                // alphaSource=FromGrayScale, sRGB=false, alphaIsTransparency=true.
                // White blade silhouettes become alpha=1, black background alpha=0.
                half texMask = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).a;

                // Early-out on fully masked pixels before doing any taper math.
                clip(texMask - _Cutoff);

                // Procedural taper on top of the texture silhouette.
                // Narrows blade width linearly to a point at the tip.
                half edgeDist = abs(IN.uv.x - 0.5h) * 2.0h;   // 0=centre, 1=edge
                half allowedW = 1.0h - IN.uv.y;                 // 1 at root, 0 at tip
                half taper    = smoothstep(allowedW + _TaperSoftness,
                                           allowedW - _TaperSoftness, edgeDist);
                clip(taper - _Cutoff);

                // ── Lighting ─────────────────────────────────────────────────
                half3 normalWS = normalize(IN.normalWS) * sign(facing);
                Light  light   = GetMainLight(IN.shadowCoord);
                half   NdotL   = dot(normalWS, half3(light.direction));
                half   lightI  = saturate(NdotL * 0.5h + 0.5h) * half(light.shadowAttenuation);
                half3  litMul  = lerp(1.0h - _ShadowStrength, 1.0h, lightI) * half3(light.color);

                // ── Color ─────────────────────────────────────────────────────
                half3 baseColor = lerp(_RootColor.rgb, _TipColor.rgb, IN.height) * litMul;

                half  emitMask = IN.height * IN.height * lightI;
                half3 emission = _EmissionColor.rgb * (_TipEmission * emitMask);

                // Debug: show UV height as greyscale to verify UV layout.
                if (_DebugHeight > 0.5)
                    return half4(IN.height, IN.height, IN.height, 1.0h);

                // Alpha=1 post-clip: AlphaTest queue writes fully opaque depth.
                return half4(baseColor + emission, 1.0h);
            }
            ENDHLSL
        }

        // ── Shadow caster (alpha-aware) ────────────────────────────────────────
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            Cull Off
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex   vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vertShadow(Attributes IN)
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                Varyings OUT;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                half   height   = saturate(IN.uv.y);
                float3 animWS   = AnimateWind(TransformObjectToWorld(IN.positionOS.xyz), height);
                       animWS   = ApplyTrample(animWS, height);
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);

                // Standard URP shadow bias
                float4 posHCS = TransformWorldToHClip(
                    ApplyShadowBias(animWS, normalWS, _MainLightPosition.xyz));
                // Clamp depth to near plane to avoid shadow pancaking
                #if UNITY_REVERSED_Z
                    posHCS.z = min(posHCS.z, posHCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    posHCS.z = max(posHCS.z, posHCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif

                OUT.positionHCS = posHCS;
                OUT.uv          = TRANSFORM_TEX(IN.uv, _MainTex);
                return OUT;
            }

            half4 fragShadow(Varyings IN) : SV_Target
            {
                // Texture silhouette first — discard black background pixels.
                half texMask = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).a;
                clip(texMask - _Cutoff);

                // Then procedural taper.
                half edgeDist = abs(IN.uv.x - 0.5h) * 2.0h;
                half allowedW = 1.0h - IN.uv.y;
                half taper    = smoothstep(allowedW + _TaperSoftness,
                                           allowedW - _TaperSoftness, edgeDist);
                clip(taper - _Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
