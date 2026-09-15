// Draws the player ONLY where something solid is in front of them.
//
// ==== WHY THIS IS A DEPTH TEST AND NOT A SCRIPT DECISION ====
//
// The obvious implementation is "notice the player is behind a bush, switch a
// glow on". That gives a silhouette covering the whole body while only a leaf
// is actually in the way, and it cannot follow the shape of the occluder at
// all. The depth buffer already knows, per pixel, whether something is nearer
// than the player — so the silhouette is cut out exactly by whatever is in
// front, for free, and it is correct while the bush sways.
//
// ==== AND WHY THE QUEUE MATTERS MORE THAN ANYTHING ELSE HERE ====
//
// ZTest Greater passes where the depth already in the buffer is NEARER than
// this fragment. That is only meaningful once the scene has written its depth.
// Render this in the Geometry range — or through a second camera with its own
// cleared depth — and the buffer is still empty, every fragment counts as
// unoccluded, and the silhouette is visible ALL THE TIME.
//
// That is exactly how this failed the first time it was attempted in this
// project. Transparent+50 puts the pass after every opaque draw, including the
// foliage and the player's own body, so the test means what it says.
//
// The vegetation in this project is Opaque with alpha clip, which is what makes
// any of this work: alpha-BLENDED foliage writes no depth, and a silhouette
// behind it would never appear.
Shader "HollowSiege/OccludedSilhouette"
{
    Properties
    {
        _Color    ("Silhouette", Color) = (0.35, 0.72, 1.0, 0.75)
        _RimColor ("Rim", Color)        = (0.75, 0.93, 1.0, 1.0)
        _RimPower ("Rim Falloff", Range(0.2, 8)) = 2.5
    }

    SubShader
    {
        Tags
        {
            "RenderType"       = "Transparent"
            "Queue"            = "Transparent+50"
            "RenderPipeline"   = "UniversalPipeline"
            "IgnoreProjector"  = "True"
        }

        Pass
        {
            Name "OccludedSilhouette"
            Tags { "LightMode" = "UniversalForward" }

            ZTest Greater       // the whole feature, in two words
            ZWrite Off
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _RimColor;
                float  _RimPower;
            CBUFFER_END

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

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                // Skinned meshes arrive already skinned — nothing special to do.
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   nrm = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS = pos.positionCS;
                OUT.normalWS   = nrm.normalWS;
                OUT.viewWS     = GetWorldSpaceNormalizeViewDir(pos.positionWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // A flat fill reads as a sticker. The rim gives the shape enough
                // form to be recognisable as a person rather than a blob, which
                // is the entire point of showing it.
                float ndv = saturate(dot(normalize(IN.normalWS), normalize(IN.viewWS)));
                float rim = pow(1.0 - ndv, _RimPower);

                half4 c = _Color;
                c.rgb = lerp(c.rgb, _RimColor.rgb, rim);
                // Rim brightens the edge WITHIN the current fade, so turning the
                // silhouette off still takes it all the way to invisible.
                c.a   = saturate(_Color.a * (1.0 + rim * 0.45));
                return c;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
