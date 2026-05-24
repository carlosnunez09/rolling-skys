Shader "Custom/VolumetricCloud"
{
    Properties
    {
        [HDR] _CloudColor    ("Cloud Color",    Color)         = (1, 1, 1, 1)
        [HDR] _ShadowColor   ("Shadow Color",   Color)         = (0.58, 0.70, 0.92, 1)
        _Steps               ("Shade Steps",    Range(1, 5))   = 3
        _NoiseScale          ("Noise Scale",    Float)         = 2.5
        _BumpAmount          ("Bump Amount",    Range(0, 0.5)) = 0.28
        _EdgeNoise           ("Edge Noise",     Range(0, 1))   = 0.65
        _EdgeSharpness       ("Edge Sharpness", Range(0, 1))   = 0.3
        _Density             ("Density",        Range(0, 1))   = 0.95
        _VolDepth            ("Volume Depth",   Range(0.01, 0.5)) = 0.18
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent+5"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector"= "True"
        }

        Pass
        {
            Name "ToonCloud"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off   // both faces so interior is visible when player enters

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _CloudColor;
                half4 _ShadowColor;
                float _Steps;
                float _NoiseScale;
                float _BumpAmount;
                float _EdgeNoise;
                float _EdgeSharpness;
                float _Density;
                float _VolDepth;
            CBUFFER_END

            // ── Noise ─────────────────────────────────────────────────────────

            float3 hash33(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 19.19);
                return frac((p.xxy + p.yxx) * p.zyx);
            }

            float valueNoise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                float3 u = f * f * (3.0 - 2.0 * f);
                float n000 = hash33(i                ).x;
                float n100 = hash33(i + float3(1,0,0)).x;
                float n010 = hash33(i + float3(0,1,0)).x;
                float n110 = hash33(i + float3(1,1,0)).x;
                float n001 = hash33(i + float3(0,0,1)).x;
                float n101 = hash33(i + float3(1,0,1)).x;
                float n011 = hash33(i + float3(0,1,1)).x;
                float n111 = hash33(i + float3(1,1,1)).x;
                return lerp(
                    lerp(lerp(n000, n100, u.x), lerp(n010, n110, u.x), u.y),
                    lerp(lerp(n001, n101, u.x), lerp(n011, n111, u.x), u.y),
                    u.z);
            }

            float fbm(float3 p)
            {
                float v = 0.0, a = 0.5;
                [unroll] for (int i = 0; i < 4; i++) { v += a * valueNoise(p); p *= 2.1; a *= 0.5; }
                return v;
            }

            // ── Structs ───────────────────────────────────────────────────────

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float3 positionOS  : TEXCOORD2;
            };

            // ── Vertex ────────────────────────────────────────────────────────

            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                float3 noisePos = IN.positionOS.xyz * _NoiseScale;
                float  n0  = fbm(noisePos);

                // Forward-difference normal perturbation
                float eps = 0.04;
                float3 dN;
                dN.x = fbm(noisePos + float3(eps, 0,   0  )) - n0;
                dN.y = fbm(noisePos + float3(0,   eps, 0  )) - n0;
                dN.z = fbm(noisePos + float3(0,   0,   eps)) - n0;
                float3 pertNormal = normalize(IN.normalOS - dN * (_BumpAmount * 8.0));

                float3 displaced = IN.positionOS.xyz + IN.normalOS * (n0 - 0.4) * _BumpAmount;

                VertexPositionInputs posInputs = GetVertexPositionInputs(displaced);
                OUT.positionHCS = posInputs.positionCS;
                OUT.positionWS  = posInputs.positionWS;
                OUT.normalWS    = TransformObjectToWorldNormal(pertNormal);
                OUT.positionOS  = IN.positionOS.xyz;
                return OUT;
            }

            // ── Fragment ──────────────────────────────────────────────────────

            half4 frag(Varyings IN, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                // Flip normal so interior back faces light correctly
                float3 normalWS = normalize(IN.normalWS) * (isFrontFace ? 1.0 : -1.0);
                float3 viewDir  = normalize(_WorldSpaceCameraPos - IN.positionWS);

                float rim = 1.0 - saturate(dot(normalWS, viewDir));

                float alpha;
                if (isFrontFace)
                {
                    // Exterior: noisy fluffy alpha edge breaks sphere silhouette
                    float edgeNoise = fbm(IN.positionOS * _NoiseScale * 1.7 + 0.35);
                    float noiseEdge = rim - edgeNoise * _EdgeNoise;
                    alpha = (1.0 - smoothstep(_EdgeSharpness, _EdgeSharpness + 0.28, noiseEdge)) * _Density;
                }
                else
                {
                    // Interior: dense cloud so player feels surrounded
                    float interiorNoise = fbm(-IN.positionOS * _NoiseScale * 1.3);
                    alpha = saturate(0.55 + interiorNoise * 0.5) * _Density;
                }

                clip(alpha - 0.02);

                // Fake volume: march 4 steps inward along sphere normal in OS.
                // normalize(positionOS) == outward sphere normal, negate for inward march.
                float3 marchDirOS = -normalize(IN.positionOS);
                float depthDensity = 0.0;
                [unroll] for (int s = 1; s <= 4; s++)
                {
                    float3 sPos = IN.positionOS + marchDirOS * _VolDepth * (float)s;
                    depthDensity += max(0.0, fbm(sPos * _NoiseScale) - 0.25) * 0.25;
                }

                // Toon stepped lighting blended with interior depth density
                Light  light    = GetMainLight();
                float  NdotL    = dot(normalWS, light.direction) * 0.5 + 0.5;
                float  combined = saturate(NdotL * 0.65 + depthDensity * 0.35);
                float  stepped  = floor(combined * max(1.0, _Steps)) / max(1.0, _Steps);
                half3  color    = lerp(_ShadowColor.rgb, _CloudColor.rgb, stepped);

                return half4(color, alpha);
            }

            ENDHLSL
        }
    }

    FallBack Off
}
