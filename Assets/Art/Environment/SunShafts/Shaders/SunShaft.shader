// Screen-space sun shaft (god ray) shader for URP 17 / Unity 6.
// Used by SunShaftFeature.cs — do not assign manually.
Shader "Hidden/Custom/SunShaft"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "SunShaft_Radial"

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D_X(_BlitTexture);
            SAMPLER(sampler_BlitTexture);

            // Set by SunShaftFeature each frame
            float4 _ShaftColor;     // rgb = tinted intensity, a = unused
            float4 _SunScreenPos;   // xy = viewport [0..1], z = 1 if sun is behind camera
            float  _Falloff;        // per-sample decay (0.85–0.97)
            float  _BlurRadius;     // total march length in UV space
            float  _Threshold;      // minimum luminance to count as sky/sun
            int    _Samples;        // number of radial steps (8–32)

            half4 frag(Varyings input) : SV_Target
            {
                float2 uv      = input.texcoord;
                float2 sunUV   = _SunScreenPos.xy;
                bool   behind  = _SunScreenPos.z > 0.5;

                float2 delta   = (sunUV - uv) * _BlurRadius / max(_Samples, 1);
                float2 sampleUV = uv;
                half3  shaft    = 0;
                float  weight   = 1.0;
                float  totalW   = 0.0;

                for (int i = 0; i < _Samples; i++)
                {
                    sampleUV += delta;
                    half3  col  = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture,
                                                     saturate(sampleUV)).rgb;
                    float  lum  = dot(col, float3(0.299, 0.587, 0.114));
                    shaft      += col * max(0.0, lum - _Threshold) / max(_Threshold, 0.001)
                                  * weight;
                    totalW     += weight;
                    weight     *= _Falloff;
                }

                if (totalW > 0.0) shaft /= totalW;

                // Kill shafts when sun is behind the camera
                shaft *= 1.0 - saturate(behind ? 10.0 : 0.0);

                half4 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_BlitTexture, uv);
                scene.rgb  += shaft * _ShaftColor.rgb;
                return scene;
            }
            ENDHLSL
        }
    }
}
