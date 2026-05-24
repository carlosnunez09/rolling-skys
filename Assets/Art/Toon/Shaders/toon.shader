Shader "Custom/toon"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        _ShadowColor("Shadow Color", Color) = (0.2, 0.2, 0.2, 1)
        _Steps("Light Steps", Float) = 3
        _SpecColor("Spec Color", Color) = (1, 1, 1, 1)
        _SpecThreshold("Spec Threshold", Range(0, 1)) = 0.8
        _OutlineColor("Outline Color", Color) = (0, 0, 0, 1)
        _OutlineWidth("Outline Width", Float) = 3
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front

            HLSLPROGRAM

            #pragma vertex vertOutline
            #pragma fragment fragOutline

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 tangentOS : TANGENT;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _BaseMap_ST;
                half4 _ShadowColor;
                float _Steps;
                half4 _SpecColor;
                float _SpecThreshold;
                half4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END

            Varyings vertOutline(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                float3 smoothNormalCS = mul((float3x3)UNITY_MATRIX_VP, mul((float3x3)UNITY_MATRIX_M, IN.tangentOS.xyz));
                float2 offset = normalize(smoothNormalCS.xy) * (_OutlineWidth * OUT.positionHCS.w * 0.01);
                OUT.positionHCS.xy += offset;
                return OUT;
            }

            half4 fragOutline(Varyings IN) : SV_Target
            {
                return _OutlineColor;
            }

            ENDHLSL
        }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float4 shadowCoord : TEXCOORD3;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _BaseMap_ST;
                half4 _ShadowColor;
                float _Steps;
                half4 _SpecColor;
                float _SpecThreshold;
                half4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = positionInputs.positionCS;
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionWS = positionInputs.positionWS;
                OUT.shadowCoord = TransformWorldToShadowCoord(positionInputs.positionWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 texColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv);
                float3 normalWS = normalize(IN.normalWS);
                Light light = GetMainLight(IN.shadowCoord);
                float NdotL = dot(normalWS, light.direction);
                float lightIntensity = saturate(NdotL * 0.5 + 0.5) * light.shadowAttenuation;
                float steps = max(_Steps, 1.0);
                float stepped = floor(lightIntensity * steps) / steps;
                half4 litColor = lerp(_ShadowColor, half4(1, 1, 1, 1), stepped);

                float3 viewDirWS = normalize(_WorldSpaceCameraPos - IN.positionWS);
                float3 halfDir = normalize(viewDirWS + light.direction);
                float NdotH = saturate(dot(normalWS, halfDir));
                float spec = step(_SpecThreshold, NdotH);

                half3 finalColor = texColor.rgb * _BaseColor.rgb * litColor.rgb;
                finalColor += spec * _SpecColor.rgb * light.color.rgb;

                return half4(finalColor, texColor.a * _BaseColor.a);
            }
            ENDHLSL
        }

    }
}
