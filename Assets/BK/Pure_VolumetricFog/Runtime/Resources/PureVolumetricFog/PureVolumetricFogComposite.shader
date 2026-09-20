Shader "Hidden/BK/Pure Volumetric Fog Composite"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "PureVolumetricFogComposite"
            ZTest Always
            ZWrite Off
            Cull Off
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            // xy = 1 / fog texture size, zw = fog texture size in texels.
            float4 _GroundFogTexelSize;
            // Relative eye-depth difference at which a low-resolution sample stops
            // contributing to a full-resolution pixel.
            float _GroundFogDepthTolerance;

            float4 SampleFog(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0);
            }

            float SampleEyeDepth(float2 uv)
            {
                return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
            }

            // Depth-aware bilinear upsample. Each low-resolution fog texel was marched
            // against the scene depth at its own texel centre; a full-resolution pixel
            // blends only the neighbouring texels whose depth resembles its own, so fog
            // does not bleed across silhouettes. When nothing matches, the closest-depth
            // texel is used.
            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float sceneDepth = SampleEyeDepth(uv);

                float2 texel = _GroundFogTexelSize.xy;
                float2 position = uv * _GroundFogTexelSize.zw - 0.5;
                float2 fraction = frac(position);
                float2 base = (floor(position) + 0.5) * texel;

                float2 uv00 = base;
                float2 uv10 = base + float2(texel.x, 0.0);
                float2 uv01 = base + float2(0.0, texel.y);
                float2 uv11 = base + texel;

                float4 bilinear = float4(
                    (1.0 - fraction.x) * (1.0 - fraction.y),
                    fraction.x * (1.0 - fraction.y),
                    (1.0 - fraction.x) * fraction.y,
                    fraction.x * fraction.y);

                float4 depths = float4(
                    SampleEyeDepth(uv00),
                    SampleEyeDepth(uv10),
                    SampleEyeDepth(uv01),
                    SampleEyeDepth(uv11));
                float4 difference = abs(depths - sceneDepth) / max(sceneDepth, 1e-3);
                float4 depthWeights = saturate(1.0 - difference / max(_GroundFogDepthTolerance, 1e-4));
                float4 weights = bilinear * depthWeights;
                float total = dot(weights, float4(1.0, 1.0, 1.0, 1.0));

                float4 fog00 = SampleFog(uv00);
                float4 fog10 = SampleFog(uv10);
                float4 fog01 = SampleFog(uv01);
                float4 fog11 = SampleFog(uv11);

                if (total > 1e-4)
                {
                    return (fog00 * weights.x + fog10 * weights.y +
                            fog01 * weights.z + fog11 * weights.w) / total;
                }

                float minimum = min(min(difference.x, difference.y), min(difference.z, difference.w));
                if (difference.x == minimum) return fog00;
                if (difference.y == minimum) return fog10;
                if (difference.z == minimum) return fog01;
                return fog11;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
