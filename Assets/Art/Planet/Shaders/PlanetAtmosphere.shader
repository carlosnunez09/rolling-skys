// Place on a sphere scaled ~1.05–1.1x larger than the planet. No collider needed.

Shader "Custom/PlanetAtmosphere"
{
    Properties
    {
        // Atmosphere tones
        [HDR] _AtmosphereColor  ("Surface Atmosphere",    Color)        = (0.20, 0.55, 1.00, 1.0)
        [HDR] _HighAltColor     ("High Altitude",         Color)        = (0.65, 0.85, 1.00, 1.0)
        [HDR] _SunsetColor      ("Sunset / Backlit",      Color)        = (1.00, 0.50, 0.15, 1.0)

        // Sun scatter (Mie-style)
        [HDR] _ScatterColor     ("Sun Scatter Color",     Color)        = (1.00, 0.92, 0.60, 1.0)
        _ScatterPower           ("Scatter Tightness",     Range(2, 48)) = 12.0
        _ScatterStrength        ("Scatter Strength",      Range(0, 3))  = 1.0

        // Halo shape
        _HaloPower              ("Halo Tightness",        Range(2, 20)) = 6.0
        _HaloIntensity          ("Halo Intensity",        Range(0, 2))  = 0.70
        _Steps                  ("Toon Bands",            Range(1, 4))  = 2.0
        _EdgeFade               ("Edge Fade Start",       Range(0.1, 0.98)) = 0.45

        // Day/night
        _DaySideBias            ("Day-side Bias",         Range(0, 1))  = 0.70
        _SunsetStrength         ("Sunset Strength",       Range(0, 1))  = 0.55

        // Inner rim
        _RimPower               ("Inner Rim Tightness",   Range(2, 12)) = 7.0
        _RimStrength            ("Inner Rim Strength",    Range(0, 1))  = 0.18
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Transparent"
            "Queue"          = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector"= "True"
        }

        // ── Pass 1: Outer halo ─────────────────────────────────────────────────────
        // Back faces (Cull Front), additive blend.
        // Three layered effects:
        //   a) Altitude gradient — outer edge = thin/pale upper atmo, inward = dense blue
        //   b) Day-side bias     — sun side glows, shadow side is dim
        //   c) Sun scatter       — Mie-style forward glow toward the sun direction
        Pass
        {
            Name "Atmosphere_Halo"
            Tags { "LightMode" = "UniversalForward" }
            Blend  One One
            ZWrite Off
            ZTest  LEqual
            Cull   Front

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag_halo
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _AtmosphereColor;
                half4 _HighAltColor;
                half4 _SunsetColor;
                half4 _ScatterColor;
                float _ScatterPower;
                float _ScatterStrength;
                float _HaloPower;
                float _HaloIntensity;
                float _Steps;
                float _DaySideBias;
                float _SunsetStrength;
                float _RimPower;
                float _RimStrength;
                float _EdgeFade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = p.positionCS;
                OUT.positionWS  = p.positionWS;
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 frag_halo(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float3 V = normalize(_WorldSpaceCameraPos - IN.positionWS);
                Light  L = GetMainLight();

                // ── Halo shape ────────────────────────────────────────────────
                // Back face: NdotV = 0 at limb, -1 at back-centre.
                // saturate(1 + NdotV) → 1 at limb, 0 at back-centre.
                float raw      = saturate(1.0 + dot(N, V));
                float halo     = pow(raw, _HaloPower);

                // Toon bands on OPACITY
                float steps    = max(1.0, _Steps);
                float toonHalo = floor(halo * steps + 0.45) / steps;

                // Cubic fade — starts much earlier and falls off very softly so the sphere
                // silhouette never becomes visible; atmosphere dissolves gradually into space.
                float outerFade = smoothstep(1.0, _EdgeFade, raw);
                outerFade = outerFade * outerFade * outerFade;

                // ── Altitude gradient (colour only, not opacity) ───────────────
                // raw ≈ 1 at limb  → upper/outer atmosphere  → _HighAltColor (thin, pale)
                // raw ≈ 0.5        → main atmosphere band    → _AtmosphereColor (dense)
                half3 altColor = lerp(_AtmosphereColor.rgb, _HighAltColor.rgb, raw * raw);

                // ── Day-side bias ──────────────────────────────────────────────
                float sunDotV   = dot(L.direction, V);
                float sunFactor = sunDotV * 0.5 + 0.5;
                float dayMod    = lerp(1.0, saturate(sunFactor + 0.2), _DaySideBias);

                // ── Sunset / backlit colour ────────────────────────────────────
                float backlit = saturate(-sunDotV);
                altColor      = lerp(altColor, _SunsetColor.rgb, backlit * _SunsetStrength);

                // ── Sun scatter (Mie forward scatter) ─────────────────────────
                float scatter    = pow(saturate(sunDotV), _ScatterPower);
                half3 scatterCol = _ScatterColor.rgb * scatter * _ScatterStrength * toonHalo * outerFade;

                // ── Combine ───────────────────────────────────────────────────
                half3 finalCol = (altColor * toonHalo * dayMod * outerFade + scatterCol) * _HaloIntensity;
                return half4(finalCol, 1.0);   // alpha unused — additive blend
            }
            ENDHLSL
        }

        // ── Pass 2: Inner rim ──────────────────────────────────────────────────────
        // Front faces (Cull Back), alpha blend.
        // Toon-stepped rim tint on the visible planet face.
        // Fades on the shadow side following the day/night terminator.
        // Also applies altitude gradient so the outer rim reads as thin/pale.
        Pass
        {
            Name "Atmosphere_Rim"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend  SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull   Back

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag_rim
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _AtmosphereColor;
                half4 _HighAltColor;
                half4 _SunsetColor;
                half4 _ScatterColor;
                float _ScatterPower;
                float _ScatterStrength;
                float _HaloPower;
                float _HaloIntensity;
                float _Steps;
                float _DaySideBias;
                float _SunsetStrength;
                float _RimPower;
                float _RimStrength;
                float _EdgeFade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = p.positionCS;
                OUT.positionWS  = p.positionWS;
                OUT.normalWS    = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 frag_rim(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float3 V = normalize(_WorldSpaceCameraPos - IN.positionWS);
                Light  L = GetMainLight();

                // Rim shape: edge = 1, centre = 0
                float NdotV = saturate(dot(N, V));
                float raw   = 1.0 - NdotV;
                float rim   = pow(raw, _RimPower);

                // Toon bands
                float steps   = max(1.0, _Steps);
                float toonRim = floor(rim * steps + 0.3) / steps;

                // Squared fade — hides the rim's hard sphere edge more softly.
                float outerFade = smoothstep(0.0, 1.0 - _EdgeFade, NdotV);
                outerFade = outerFade * outerFade;

                // Altitude gradient: outer rim edge = high altitude (pale/thin)
                half3 rimColor = lerp(_AtmosphereColor.rgb, _HighAltColor.rgb, raw * raw);

                // Day terminator: shadow side fades out
                float NdotL  = dot(N, L.direction) * 0.5 + 0.5;
                float dayMod = lerp(1.0, saturate(NdotL + 0.15), _DaySideBias);

                float alpha  = toonRim * outerFade * _RimStrength * dayMod;
                return half4(rimColor, alpha);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
