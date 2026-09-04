// Draws the player as a flat silhouette ONLY where something is already in front
// of them (ZTest Greater). Unoccluded, it renders nothing at all.
//
// This is the fix for losing the player in bushes. The bushes are terrain-painted
// details — GPU instances with no GameObject and no collider — so CameraOcclusion,
// which fades occluding objects that carry a FadingObject component, can never
// see them and never fades them. A depth-tested silhouette needs no detection at
// all: whatever occludes the player, foliage included, the shape shows through.
Shader "Hollow/PlayerSilhouette"
{
    Properties
    {
        _SilhouetteColor ("Silhouette Color", Color) = (0.55, 0.85, 1.0, 0.85)
        _Fresnel ("Rim Boost", Range(0,4)) = 1.2
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent+100"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "Silhouette"
            Tags { "LightMode" = "UniversalForward" }

            ZTest Greater      // only where geometry is already in front
            ZWrite Off
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 viewWS     : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _SilhouetteColor;
                float  _Fresnel;
            CBUFFER_END

            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                VertexPositionInputs p = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = p.positionCS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewWS = GetWorldSpaceViewDir(p.positionWS);
                return OUT;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                // Slightly brighter at the edges so the shape reads as a figure
                // rather than a flat blob.
                float3 n = normalize(IN.normalWS);
                float3 v = normalize(IN.viewWS);
                float rim = pow(saturate(1.0 - saturate(dot(n, v))), 2.0);

                half4 c = _SilhouetteColor;
                c.rgb *= 1.0 + rim * _Fresnel;
                c.a = saturate(c.a * (0.75 + rim * 0.35));
                return c;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
