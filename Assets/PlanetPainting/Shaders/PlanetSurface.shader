Shader "Planet Painting/PlanetSurface"
{
    Properties
    {
        // ── Layer 0 (vertex R) ───────────────────────────────────────────────
        [Header(Layer 0  Vertex R)]
        [NoScaleOffset] _Layer0Albedo    ("Albedo",       2D)           = "white" {}
        [NoScaleOffset] _Layer0Normal    ("Normal Map",   2D)           = "bump"  {}
        [NoScaleOffset] _Layer0Mask      ("Mask (R=Metal G=AO B=_ A=Smooth)", 2D) = "white" {}
        _Layer0Tiling    ("Tiling",       Float)          = 4
        _Layer0TriBlend  ("Tri Blend",    Range(1,8))     = 4
        _Layer0Brightness("Brightness",   Range(0,3))     = 1
        _Layer0Metallic  ("Metallic",     Range(0,1))     = 0
        _Layer0Smoothness("Smoothness",   Range(0,1))     = 0.3
        _Layer0AO        ("Ambient OCC",  Range(0,1))     = 1

        // ── Layer 1 (vertex G) ───────────────────────────────────────────────
        [Header(Layer 1  Vertex G)]
        [NoScaleOffset] _Layer1Albedo    ("Albedo",       2D)           = "white" {}
        [NoScaleOffset] _Layer1Normal    ("Normal Map",   2D)           = "bump"  {}
        [NoScaleOffset] _Layer1Mask      ("Mask (R=Metal G=AO B=_ A=Smooth)", 2D) = "white" {}
        _Layer1Tiling    ("Tiling",       Float)          = 4
        _Layer1TriBlend  ("Tri Blend",    Range(1,8))     = 4
        _Layer1Brightness("Brightness",   Range(0,3))     = 1
        _Layer1Metallic  ("Metallic",     Range(0,1))     = 0
        _Layer1Smoothness("Smoothness",   Range(0,1))     = 0.3
        _Layer1AO        ("Ambient OCC",  Range(0,1))     = 1

        // ── Layer 2 (vertex B) ───────────────────────────────────────────────
        [Header(Layer 2  Vertex B)]
        [NoScaleOffset] _Layer2Albedo    ("Albedo",       2D)           = "white" {}
        [NoScaleOffset] _Layer2Normal    ("Normal Map",   2D)           = "bump"  {}
        [NoScaleOffset] _Layer2Mask      ("Mask (R=Metal G=AO B=_ A=Smooth)", 2D) = "white" {}
        _Layer2Tiling    ("Tiling",       Float)          = 4
        _Layer2TriBlend  ("Tri Blend",    Range(1,8))     = 4
        _Layer2Brightness("Brightness",   Range(0,3))     = 1
        _Layer2Metallic  ("Metallic",     Range(0,1))     = 0
        _Layer2Smoothness("Smoothness",   Range(0,1))     = 0.3
        _Layer2AO        ("Ambient OCC",  Range(0,1))     = 1

        // ── Layer 3 (vertex A) ───────────────────────────────────────────────
        [Header(Layer 3  Vertex A)]
        [NoScaleOffset] _Layer3Albedo    ("Albedo",       2D)           = "white" {}
        [NoScaleOffset] _Layer3Normal    ("Normal Map",   2D)           = "bump"  {}
        [NoScaleOffset] _Layer3Mask      ("Mask (R=Metal G=AO B=_ A=Smooth)", 2D) = "white" {}
        _Layer3Tiling    ("Tiling",       Float)          = 4
        _Layer3TriBlend  ("Tri Blend",    Range(1,8))     = 4
        _Layer3Brightness("Brightness",   Range(0,3))     = 1
        _Layer3Metallic  ("Metallic",     Range(0,1))     = 0
        _Layer3Smoothness("Smoothness",   Range(0,1))     = 0.3
        _Layer3AO        ("Ambient OCC",  Range(0,1))     = 1

        // ── Blending ─────────────────────────────────────────────────────────
        [Header(Blending)]
        _BlendSharpness ("Blend Height Influence (0=pure vertex color)", Range(0,2))  = 0
        _BlendContrast  ("Blend Edge Sharpness (0=soft 1=hard)",        Range(0,1))  = 0.2

        // ── Variation ────────────────────────────────────────────────────────
        [Header(Variation)]
        _TileVariation  ("Tile Variation (breaks repetition)", Range(0,1)) = 0.25

        // ── Toon Lighting ────────────────────────────────────────────────────
        [Header(Toon Lighting)]
        _ToonStrength    ("Toon Strength (0=smooth 1=stepped)", Range(0,1))    = 0.8
        _ToonSteps       ("Toon Steps",                         Range(1,6))    = 3
        _LightWrap       ("Light Wrap (fills dark side)",        Range(0,0.99)) = 0.4
        _ToonShadow      ("Shadow Tint",                         Color)         = (0.15, 0.18, 0.28, 1)
        _ShadowStrength  ("Shadow Strength",                     Range(0,1))    = 0.85
        _AmbientStrength ("Ambient Fill",                        Range(0,1))    = 0.35
        _RimColor        ("Rim Color",                           Color)         = (0.75, 0.88, 1.0, 1)
        _RimPower        ("Rim Power",                           Range(0.5,8))  = 3.0
        _RimStrength     ("Rim Strength",                        Range(0,1))    = 0.25

        // ── Global ───────────────────────────────────────────────────────────
        [Header(Global)]
        _NormalStrength ("Normal Strength", Range(0,2))       = 1
        [Toggle] _DebugVertexColors ("Debug Vertex Colors",   Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Geometry"
        }
        LOD 300

        // ── ForwardLit ────────────────────────────────────────────────────────
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // ── Textures ─────────────────────────────────────────────────────
            TEXTURE2D(_Layer0Albedo); SAMPLER(sampler_Layer0Albedo);
            TEXTURE2D(_Layer0Normal); SAMPLER(sampler_Layer0Normal);
            TEXTURE2D(_Layer0Mask);   SAMPLER(sampler_Layer0Mask);

            TEXTURE2D(_Layer1Albedo); SAMPLER(sampler_Layer1Albedo);
            TEXTURE2D(_Layer1Normal); SAMPLER(sampler_Layer1Normal);
            TEXTURE2D(_Layer1Mask);   SAMPLER(sampler_Layer1Mask);

            TEXTURE2D(_Layer2Albedo); SAMPLER(sampler_Layer2Albedo);
            TEXTURE2D(_Layer2Normal); SAMPLER(sampler_Layer2Normal);
            TEXTURE2D(_Layer2Mask);   SAMPLER(sampler_Layer2Mask);

            TEXTURE2D(_Layer3Albedo); SAMPLER(sampler_Layer3Albedo);
            TEXTURE2D(_Layer3Normal); SAMPLER(sampler_Layer3Normal);
            TEXTURE2D(_Layer3Mask);   SAMPLER(sampler_Layer3Mask);

            // ── CBUFFER ──────────────────────────────────────────────────────
            // All three passes must declare this block with identical layout
            // for the SRP Batcher to accept the shader.
            CBUFFER_START(UnityPerMaterial)
                float _Layer0Tiling;    float _Layer0TriBlend;  float _Layer0Brightness;
                float _Layer0Metallic;  float _Layer0Smoothness; float _Layer0AO;
                float _Layer1Tiling;    float _Layer1TriBlend;  float _Layer1Brightness;
                float _Layer1Metallic;  float _Layer1Smoothness; float _Layer1AO;
                float _Layer2Tiling;    float _Layer2TriBlend;  float _Layer2Brightness;
                float _Layer2Metallic;  float _Layer2Smoothness; float _Layer2AO;
                float _Layer3Tiling;    float _Layer3TriBlend;  float _Layer3Brightness;
                float _Layer3Metallic;  float _Layer3Smoothness; float _Layer3AO;
                float _BlendSharpness;
                float _BlendContrast;
                float _TileVariation;
                float _ToonStrength;
                float _ToonSteps;
                float _LightWrap;
                float4 _ToonShadow;
                float _ShadowStrength;
                float _AmbientStrength;
                float4 _RimColor;
                float _RimPower;
                float _RimStrength;
                float _NormalStrength;
                float _DebugVertexColors;
            CBUFFER_END

            // ── Structs ──────────────────────────────────────────────────────
            struct Attributes
            {
                float4 positionOS  : POSITION;
                float3 normalOS    : NORMAL;
                float4 tangentOS   : TANGENT;
                float4 vertexColor : COLOR;
                float2 lightmapUV  : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float4 vertexColor : TEXCOORD2;
                float  fogFactor   : TEXCOORD3;
                DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 4);
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // ── Helpers ──────────────────────────────────────────────────────

            float3 TriplanarWeights(float3 n, float sharpness)
            {
                float3 w = pow(abs(n), sharpness);
                return w / (w.x + w.y + w.z + 1e-5);
            }

            // Hash-based UV offset per macro cell — breaks tiling repetition.
            float2 CellOffset(float2 cellID)
            {
                float2 h = frac(sin(float2(dot(cellID, float2(127.1, 311.7)),
                                           dot(cellID, float2(269.5, 183.3)))) * 43758.5453);
                return (h - 0.5);
            }

            float4 TriplanarSample(TEXTURE2D_PARAM(tex, smp),
                                   float3 pos, float3 bw, float tiling, float variation)
            {
                float2 uvX = pos.zy * tiling;
                float2 uvY = pos.xz * tiling;
                float2 uvZ = pos.xy * tiling;

                if (variation > 0.001)
                {
                    float cellScale = tiling * 0.18;
                    uvX += CellOffset(floor(pos.zy * cellScale)) * variation;
                    uvY += CellOffset(floor(pos.xz * cellScale)) * variation;
                    uvZ += CellOffset(floor(pos.xy * cellScale)) * variation;
                }

                float4 cx = SAMPLE_TEXTURE2D(tex, smp, uvX);
                float4 cy = SAMPLE_TEXTURE2D(tex, smp, uvY);
                float4 cz = SAMPLE_TEXTURE2D(tex, smp, uvZ);
                return cx * bw.x + cy * bw.y + cz * bw.z;
            }

            // Triplanar normal — Ben Golus technique, rotates each face into WS.
            float3 TriplanarNormal(TEXTURE2D_PARAM(tex, smp),
                                   float3 pos, float3 worldNormal,
                                   float3 bw, float tiling, float strength, float variation)
            {
                float2 uvX = pos.zy * tiling;
                float2 uvY = pos.xz * tiling;
                float2 uvZ = pos.xy * tiling;

                if (variation > 0.001)
                {
                    float cellScale = tiling * 0.18;
                    uvX += CellOffset(floor(pos.zy * cellScale)) * variation;
                    uvY += CellOffset(floor(pos.xz * cellScale)) * variation;
                    uvZ += CellOffset(floor(pos.xy * cellScale)) * variation;
                }

                float3 tnX = UnpackNormal(SAMPLE_TEXTURE2D(tex, smp, uvX));
                float3 tnY = UnpackNormal(SAMPLE_TEXTURE2D(tex, smp, uvY));
                float3 tnZ = UnpackNormal(SAMPLE_TEXTURE2D(tex, smp, uvZ));

                // Whiteout blending — avoids flat seams on each projected face.
                float3 nX = float3(tnX.y + worldNormal.z, tnX.x + worldNormal.y, worldNormal.x);
                float3 nY = float3(tnY.x + worldNormal.x, tnY.y + worldNormal.z, worldNormal.y);
                float3 nZ = float3(tnZ.x + worldNormal.x, tnZ.y + worldNormal.y, worldNormal.z);

                nX = lerp(float3(0, 0, 1), nX, strength);
                nY = lerp(float3(0, 0, 1), nY, strength);
                nZ = lerp(float3(0, 0, 1), nZ, strength);

                return normalize(nX * bw.x + nY * bw.y + nZ * bw.z);
            }

            // Blend vertex-colour weights with optional texture-height influence.
            // _BlendSharpness = 0  -> pure vertex colour (default, always safe)
            // _BlendContrast  = 0  -> linear soft blend;  1 = winner-takes-most
            float4 ContrastBlend(float4 vc, float4 heights)
            {
                float4 w   = saturate(vc + heights * _BlendSharpness);
                float  sum = w.x + w.y + w.z + w.w;
                w /= max(sum, 1e-5);

                float p = 1.0 + _BlendContrast * 6.0;
                w = pow(max(w, 1e-5), p);
                return w / max(w.x + w.y + w.z + w.w, 1e-5);
            }

            // ── Vertex ───────────────────────────────────────────────────────
            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs posInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   nrmInputs = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);

                OUT.positionHCS = posInputs.positionCS;
                OUT.positionWS  = posInputs.positionWS;
                OUT.normalWS    = nrmInputs.normalWS;
                OUT.vertexColor = IN.vertexColor;
                OUT.fogFactor   = ComputeFogFactor(posInputs.positionCS.z);

                OUTPUT_LIGHTMAP_UV(IN.lightmapUV, unity_LightmapST, OUT.lightmapUV);
                #if !defined(LIGHTMAP_ON)
                    OUT.vertexSH = SampleSHVertex(nrmInputs.normalWS);
                #endif

                return OUT;
            }

            // ── Fragment ─────────────────────────────────────────────────────
            half4 frag(Varyings IN) : SV_Target
            {
                float3 pos      = IN.positionWS;
                float3 normalWS = normalize(IN.normalWS);

                // ── Vertex colour weights ─────────────────────────────────────
                float4 vc    = IN.vertexColor;
                float  vcSum = vc.r + vc.g + vc.b + vc.a;
                if (vcSum < 1e-4) vc = float4(1, 0, 0, 0);
                else              vc /= vcSum;

                if (_DebugVertexColors > 0.5)
                    return half4(vc.rgb, 1);

                // ── Triplanar weights per layer ───────────────────────────────
                float3 tw0 = TriplanarWeights(normalWS, _Layer0TriBlend);
                float3 tw1 = TriplanarWeights(normalWS, _Layer1TriBlend);
                float3 tw2 = TriplanarWeights(normalWS, _Layer2TriBlend);
                float3 tw3 = TriplanarWeights(normalWS, _Layer3TriBlend);
                float  var = _TileVariation;

                // ── Sample albedos ────────────────────────────────────────────
                float4 alb0 = TriplanarSample(TEXTURE2D_ARGS(_Layer0Albedo, sampler_Layer0Albedo), pos, tw0, _Layer0Tiling, var);
                float4 alb1 = TriplanarSample(TEXTURE2D_ARGS(_Layer1Albedo, sampler_Layer1Albedo), pos, tw1, _Layer1Tiling, var);
                float4 alb2 = TriplanarSample(TEXTURE2D_ARGS(_Layer2Albedo, sampler_Layer2Albedo), pos, tw2, _Layer2Tiling, var);
                float4 alb3 = TriplanarSample(TEXTURE2D_ARGS(_Layer3Albedo, sampler_Layer3Albedo), pos, tw3, _Layer3Tiling, var);
                alb0.rgb *= _Layer0Brightness;
                alb1.rgb *= _Layer1Brightness;
                alb2.rgb *= _Layer2Brightness;
                alb3.rgb *= _Layer3Brightness;

                // ── Sample mask maps (R=Metallic, G=AO, A=Smoothness) ─────────
                // Default "white" fallback means mask.r=1, mask.g=1, mask.a=1,
                // so the scalar sliders act as a direct multiplier when no map is set.
                float4 mask0 = TriplanarSample(TEXTURE2D_ARGS(_Layer0Mask, sampler_Layer0Mask), pos, tw0, _Layer0Tiling, var);
                float4 mask1 = TriplanarSample(TEXTURE2D_ARGS(_Layer1Mask, sampler_Layer1Mask), pos, tw1, _Layer1Tiling, var);
                float4 mask2 = TriplanarSample(TEXTURE2D_ARGS(_Layer2Mask, sampler_Layer2Mask), pos, tw2, _Layer2Tiling, var);
                float4 mask3 = TriplanarSample(TEXTURE2D_ARGS(_Layer3Mask, sampler_Layer3Mask), pos, tw3, _Layer3Tiling, var);

                // ── Height-based contrast blend ───────────────────────────────
                float4 heights = float4(
                    dot(alb0.rgb, float3(0.299, 0.587, 0.114)),
                    dot(alb1.rgb, float3(0.299, 0.587, 0.114)),
                    dot(alb2.rgb, float3(0.299, 0.587, 0.114)),
                    dot(alb3.rgb, float3(0.299, 0.587, 0.114)));

                float4 bw = ContrastBlend(vc, heights);

                // ── Blend albedo ──────────────────────────────────────────────
                float3 albedo = alb0.rgb * bw.r
                              + alb1.rgb * bw.g
                              + alb2.rgb * bw.b
                              + alb3.rgb * bw.a;

                // ── Blend normals ─────────────────────────────────────────────
                float3 n0 = TriplanarNormal(TEXTURE2D_ARGS(_Layer0Normal, sampler_Layer0Normal), pos, normalWS, tw0, _Layer0Tiling, _NormalStrength, var);
                float3 n1 = TriplanarNormal(TEXTURE2D_ARGS(_Layer1Normal, sampler_Layer1Normal), pos, normalWS, tw1, _Layer1Tiling, _NormalStrength, var);
                float3 n2 = TriplanarNormal(TEXTURE2D_ARGS(_Layer2Normal, sampler_Layer2Normal), pos, normalWS, tw2, _Layer2Tiling, _NormalStrength, var);
                float3 n3 = TriplanarNormal(TEXTURE2D_ARGS(_Layer3Normal, sampler_Layer3Normal), pos, normalWS, tw3, _Layer3Tiling, _NormalStrength, var);

                float3 blendedNormal = normalize(n0 * bw.r + n1 * bw.g + n2 * bw.b + n3 * bw.a);

                // ── Blend PBR scalars ─────────────────────────────────────────
                // mask.r = Metallic,  mask.g = AO,  mask.a = Smoothness
                float metallic   = mask0.r * _Layer0Metallic   * bw.r
                                 + mask1.r * _Layer1Metallic   * bw.g
                                 + mask2.r * _Layer2Metallic   * bw.b
                                 + mask3.r * _Layer3Metallic   * bw.a;

                float smoothness = mask0.a * _Layer0Smoothness * bw.r
                                 + mask1.a * _Layer1Smoothness * bw.g
                                 + mask2.a * _Layer2Smoothness * bw.b
                                 + mask3.a * _Layer3Smoothness * bw.a;

                float ao         = mask0.g * _Layer0AO         * bw.r
                                 + mask1.g * _Layer1AO         * bw.g
                                 + mask2.g * _Layer2AO         * bw.b
                                 + mask3.g * _Layer3AO         * bw.a;

                albedo *= ao;

                // ── Lighting ──────────────────────────────────────────────────
                float3 viewDir     = GetWorldSpaceNormalizeViewDir(pos);
                float4 shadowCoord = TransformWorldToShadowCoord(pos);
                Light  light       = GetMainLight(shadowCoord);

                // Light wrap shifts NdotL so light bleeds further around spheres.
                float NdotL   = dot(blendedNormal, light.direction);
                float wrapped = saturate((NdotL + _LightWrap) / (1.0 + _LightWrap));

                // Toon quantise, blended with smooth by _ToonStrength.
                float stepped = floor(wrapped * max(_ToonSteps, 1.0) + 0.5)
                              / max(_ToonSteps, 1.0);
                float finalL  = lerp(wrapped, stepped, _ToonStrength);

                float shadow  = lerp(1.0, light.shadowAttenuation, _ShadowStrength);
                finalL       *= shadow;

                float3 litColor = lerp(_ToonShadow.rgb, float3(light.color), finalL) * albedo;

                // Toon-quantised specular highlight.
                float3 halfDir  = normalize(viewDir + light.direction);
                float  NdotH    = saturate(dot(blendedNormal, halfDir));
                float  specPow  = exp2(smoothness * 10.0 + 1.0);
                float  specRaw  = pow(NdotH, specPow) * metallic;
                float  specStep = floor(specRaw * 3.0 + 0.5) / 3.0;
                litColor += float3(light.color) * lerp(specRaw, specStep, _ToonStrength) * shadow;

                // Additional point / spot lights.
                #ifdef _ADDITIONAL_LIGHTS
                uint addCount = GetAdditionalLightsCount();
                for (uint i = 0; i < addCount; ++i)
                {
                    Light  add     = GetAdditionalLight(i, pos);
                    float  addWrap = saturate((dot(blendedNormal, add.direction) + _LightWrap)
                                             / (1.0 + _LightWrap));
                    float  addSt   = floor(addWrap * max(_ToonSteps, 1.0) + 0.5) / max(_ToonSteps, 1.0);
                    float  addL    = lerp(addWrap, addSt, _ToonStrength)
                                   * add.shadowAttenuation * add.distanceAttenuation;
                    litColor      += float3(add.color) * albedo * addL * 0.5;
                }
                #endif

                // Rim — fades out on dark side so it reads as intentional.
                float NdotV   = saturate(dot(viewDir, blendedNormal));
                float rim     = pow(1.0 - NdotV, _RimPower);
                float rimMask = saturate(finalL * 3.0);
                litColor     += _RimColor.rgb * rim * _RimStrength * rimMask;

                // Ambient fill — keeps unlit side from going pure black.
                float3 sh         = SAMPLE_GI(IN.lightmapUV, IN.vertexSH, blendedNormal);
                float3 ambient    = max(sh, _ToonShadow.rgb) * albedo;
                float3 finalColor = litColor + ambient * _AmbientStrength;

                finalColor = MixFog(finalColor, IN.fogFactor);
                return half4(finalColor, 1);
            }
            ENDHLSL
        }

        // ── Shadow caster ─────────────────────────────────────────────────────
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex   ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            // Must match ForwardLit CBUFFER layout exactly for SRP Batcher.
            CBUFFER_START(UnityPerMaterial)
                float _Layer0Tiling;    float _Layer0TriBlend;  float _Layer0Brightness;
                float _Layer0Metallic;  float _Layer0Smoothness; float _Layer0AO;
                float _Layer1Tiling;    float _Layer1TriBlend;  float _Layer1Brightness;
                float _Layer1Metallic;  float _Layer1Smoothness; float _Layer1AO;
                float _Layer2Tiling;    float _Layer2TriBlend;  float _Layer2Brightness;
                float _Layer2Metallic;  float _Layer2Smoothness; float _Layer2AO;
                float _Layer3Tiling;    float _Layer3TriBlend;  float _Layer3Brightness;
                float _Layer3Metallic;  float _Layer3Smoothness; float _Layer3AO;
                float _BlendSharpness;
                float _BlendContrast;
                float _TileVariation;
                float _ToonStrength;
                float _ToonSteps;
                float _LightWrap;
                float4 _ToonShadow;
                float _ShadowStrength;
                float _AmbientStrength;
                float4 _RimColor;
                float _RimPower;
                float _RimStrength;
                float _NormalStrength;
                float _DebugVertexColors;
            CBUFFER_END

            struct ShadowAttribs
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            ShadowVaryings ShadowVert(ShadowAttribs IN)
            {
                ShadowVaryings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                float3 posWS  = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nrmWS  = TransformObjectToWorldNormal(IN.normalOS);
                float4 posCS  = TransformWorldToHClip(ApplyShadowBias(posWS, nrmWS, _MainLightPosition.xyz));
                #if UNITY_REVERSED_Z
                    posCS.z = min(posCS.z, posCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    posCS.z = max(posCS.z, posCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif
                OUT.positionCS = posCS;
                return OUT;
            }

            half4 ShadowFrag(ShadowVaryings IN) : SV_Target { return 0; }
            ENDHLSL
        }

        // ── Depth only ────────────────────────────────────────────────────────
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex   DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Must match ForwardLit CBUFFER layout exactly for SRP Batcher.
            CBUFFER_START(UnityPerMaterial)
                float _Layer0Tiling;    float _Layer0TriBlend;  float _Layer0Brightness;
                float _Layer0Metallic;  float _Layer0Smoothness; float _Layer0AO;
                float _Layer1Tiling;    float _Layer1TriBlend;  float _Layer1Brightness;
                float _Layer1Metallic;  float _Layer1Smoothness; float _Layer1AO;
                float _Layer2Tiling;    float _Layer2TriBlend;  float _Layer2Brightness;
                float _Layer2Metallic;  float _Layer2Smoothness; float _Layer2AO;
                float _Layer3Tiling;    float _Layer3TriBlend;  float _Layer3Brightness;
                float _Layer3Metallic;  float _Layer3Smoothness; float _Layer3AO;
                float _BlendSharpness;
                float _BlendContrast;
                float _TileVariation;
                float _ToonStrength;
                float _ToonSteps;
                float _LightWrap;
                float4 _ToonShadow;
                float _ShadowStrength;
                float _AmbientStrength;
                float4 _RimColor;
                float _RimPower;
                float _RimStrength;
                float _NormalStrength;
                float _DebugVertexColors;
            CBUFFER_END

            struct DepthAttribs
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            DepthVaryings DepthVert(DepthAttribs IN)
            {
                DepthVaryings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 DepthFrag(DepthVaryings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }

    FallBack "Universal Render Pipeline/Lit"
}
