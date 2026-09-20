#ifndef BK_PURE_VOLUMETRIC_FOG_COMMON_INCLUDED
#define BK_PURE_VOLUMETRIC_FOG_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
// Lighting.hlsl brings in Shadows.hlsl together with the Core helpers it depends on.
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

// Composite terrain height in [0,1] over the union of all source terrains.
// Negative values mark texels not covered by any terrain.
TEXTURE2D(_TerrainHeightmap);
SAMPLER(sampler_TerrainHeightmap);
TEXTURE2D(_TerrainShadowMap);
SAMPLER(sampler_TerrainShadowMap);
TEXTURE2D(_NoiseTex);
SAMPLER(sampler_NoiseTex);

float4 _TerrainOrigin;
float4 _TerrainSize;
float _MaximumFogHeightWS;
float _TerrainEdgeFadeWorld;
float _TerrainFollow;

float _Density;
float _GroundFogHeight;
float _TopSoftness;

float _LargeNoiseScale;
float _DetailNoiseScale;
float _NoiseAmount;
float _NoiseFloor;
float _NoiseContrast;
float _NoiseStrength;
float _NoiseCoverage;
float _NoiseHeightDistortion;
float _VerticalNoiseShear;
float4 _Wind;
float _FogTime;
float _NoiseTextureSize;
float _NoiseMipBias;
float _NoiseFilterScale;
float _RayJitterStrength;
float _RayStepSmoothing;

float _DebugView;
float _UseSceneDepth;
float _DepthBias;
float _MaximumRaySteps;
float _MinimumRaySteps;
float _TargetStepLength;
float _MaximumFogDistance;
float _NearFadeDistance;
float _FarFadeStart;

float4 _FogColor;
float4 _AmbientColor;
float4 _SunColor;
float4 _LightDirectionWS;
float _AmbientStrength;
float _AmbientShadowStrength;
float _SunStrength;
float _SunScattering;
float _Anisotropy;

float _EnableVolumetricShadows;
float _TerrainCastsFogShadows;
float _ShadowStrength;
float _TerrainShadowBias;
float _FogSelfShadowStrength;
float _HasTerrainShadowMap;
float _UseUnityRealtimeShadows;
float _TerrainShadowSoftness;
float _ShadowAntiBanding;

// Ratio of the camera target size to the size of the target the fog is rendered
// into. 1 when drawn directly; the renderer feature sets 2 or 4 for reduced
// resolution so screen-space UVs still map to the full-resolution depth texture.
float _GroundFogPixelScale;

struct GroundFogAttributes
{
    float4 positionOS : POSITION;
};

struct GroundFogVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
};

GroundFogVaryings GroundFogVert(GroundFogAttributes input)
{
    GroundFogVaryings output;
    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
    output.positionWS = TransformObjectToWorld(input.positionOS.xyz);

    // The proxy box only provides pixel coverage (ZTest Always, ZWrite Off), so its
    // depth is meaningless. Pin it to the middle of the clip range so the camera far
    // plane can never clip the box away; near-plane clipping still works in w.
#if UNITY_REVERSED_Z
    output.positionCS.z = 0.5 * output.positionCS.w;
#else
    output.positionCS.z = 0.0;
#endif
    return output;
}

float GroundFogInterleavedGradientNoise(float2 pixelPosition)
{
    // Stable interleaved-gradient dither. Avoids dynamic constant-array indexing,
    // which can crash some Unity shader-compiler versions.
    float2 pixel = floor(pixelPosition);
    return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
}

bool GroundFogIntersectUnitBox(float3 originOS, float3 directionOS, out float enterDistance, out float exitDistance)
{
    float3 safeDirection = directionOS;
    safeDirection.x = abs(safeDirection.x) < 1e-6 ? (safeDirection.x < 0.0 ? -1e-6 : 1e-6) : safeDirection.x;
    safeDirection.y = abs(safeDirection.y) < 1e-6 ? (safeDirection.y < 0.0 ? -1e-6 : 1e-6) : safeDirection.y;
    safeDirection.z = abs(safeDirection.z) < 1e-6 ? (safeDirection.z < 0.0 ? -1e-6 : 1e-6) : safeDirection.z;

    float3 inverseDirection = 1.0 / safeDirection;
    float3 t0 = (-0.5 - originOS) * inverseDirection;
    float3 t1 = ( 0.5 - originOS) * inverseDirection;
    float3 tMin = min(t0, t1);
    float3 tMax = max(t0, t1);
    enterDistance = max(max(tMin.x, tMin.y), tMin.z);
    exitDistance = min(min(tMax.x, tMax.y), tMax.z);
    enterDistance = max(enterDistance, 0.0);
    return exitDistance > enterDistance;
}

float GroundFogPhase(float cosineTheta, float g)
{
    float g2 = g * g;
    float denominator = max(1.0 + g2 - 2.0 * g * cosineTheta, 1e-3);
    return (1.0 - g2) / pow(denominator, 1.5);
}

float GroundFogTerrainEdgeFade(float2 terrainUV)
{
    if (any(terrainUV < 0.0) || any(terrainUV > 1.0))
        return 0.0;

    float2 uvFadeWidth = _TerrainEdgeFadeWorld / max(_TerrainSize.xz, float2(0.001, 0.001));
    float2 distanceToEdge = min(terrainUV, 1.0 - terrainUV);
    float2 fade = smoothstep(0.0, max(uvFadeWidth, 0.00001), distanceToEdge);
    return fade.x * fade.y;
}

float2 GroundFogRotate(float2 value)
{
    return float2(value.x * 0.7986355 - value.y * 0.6018150,
                  value.x * 0.6018150 + value.y * 0.7986355);
}

float GroundFogNoiseLod(float filterWorldSize, float worldScale)
{
    float textureSize = max(_NoiseTextureSize, 1.0);
    float footprintTexels = max(filterWorldSize / max(worldScale, 0.001) * textureSize, 1.0);
    return max(log2(footprintTexels) + _NoiseMipBias, 0.0);
}

struct GroundFogNoiseContext
{
    float2 windOffset;
    float largeLod;
    float detailLod;
    float warpFade;
    float detailWeight;
};

GroundFogNoiseContext GroundFogPrepareNoise(float filterWorldSize)
{
    GroundFogNoiseContext context;
    context.windOffset = _Wind.xy * (_Wind.z * _FogTime);
    context.largeLod = GroundFogNoiseLod(filterWorldSize, _LargeNoiseScale);
    context.detailLod = GroundFogNoiseLod(filterWorldSize, _DetailNoiseScale);
    context.warpFade = exp2(-context.largeLod * 0.7);
    context.detailWeight = 0.34 * exp2(-context.detailLod * 0.55);
    return context;
}

float GroundFogNoiseRaw(float3 positionWS, GroundFogNoiseContext context)
{
    float2 windOffset = context.windOffset;
    float2 verticalOffset = positionWS.y * _VerticalNoiseShear * float2(0.37, -0.21);
    float2 largeUV = (positionWS.xz + windOffset) / max(_LargeNoiseScale, 0.001) + verticalOffset;
    float3 firstSample = SAMPLE_TEXTURE2D_LOD(_NoiseTex, sampler_NoiseTex, largeUV, context.largeLod).rgb;

    // Cheap domain warp prevents the two layers from looking like sliding textures.
    // The warp fades with mip level so distant fog remains stable instead of sparkling.
    float2 warp = (firstSample.gb - 0.5) * (0.34 * context.warpFade);
    float2 detailUV = (GroundFogRotate(positionWS.xz) - windOffset * 0.61) /
        max(_DetailNoiseScale, 0.001) - verticalOffset * 1.7 + warp;
    float3 detailSample = SAMPLE_TEXTURE2D_LOD(_NoiseTex, sampler_NoiseTex, detailUV, context.detailLod).rgb;

    float broad = saturate(firstSample.r * 0.78 + firstSample.b * 0.22);
    float detail = saturate(detailSample.g * 0.68 + detailSample.b * 0.32);
    float combined = saturate(broad + (detail - 0.5) * context.detailWeight);

    combined = saturate((combined - 0.5) * max(_NoiseContrast, 0.25) + 0.5);
    float coverageEnd = min(_NoiseCoverage + 0.34, 1.0);
    return smoothstep(saturate(_NoiseCoverage), max(coverageEnd, _NoiseCoverage + 0.01), combined);
}

float GroundFogNoiseFromRaw(float rawNoise)
{
    float carving = saturate(rawNoise * max(_NoiseStrength, 0.0));
    float shaped = lerp(1.0, saturate(_NoiseFloor), carving);
    return lerp(1.0, shaped, saturate(_NoiseAmount));
}

// Returns the composite terrain height in [0,1], or a negative value where no
// terrain covers the position.
float GroundFogTerrainHeight01(float2 terrainUV)
{
    return SAMPLE_TEXTURE2D_LOD(_TerrainHeightmap, sampler_TerrainHeightmap, terrainUV, 0.0).r;
}

float GroundFogDensityAtPosition(
    float3 positionWS,
    GroundFogNoiseContext noiseContext)
{
    float2 terrainUV = (positionWS.xz - _TerrainOrigin.xz) /
        max(_TerrainSize.xz, float2(0.001, 0.001));
    float edgeFade = GroundFogTerrainEdgeFade(terrainUV);
    if (edgeFade <= 0.0)
        return 0.0;

    float terrainHeight01 = GroundFogTerrainHeight01(terrainUV);
    if (terrainHeight01 < 0.0)
        return 0.0;

    float sampledTerrainHeightWS = _TerrainOrigin.y + terrainHeight01 * _TerrainSize.y;
    float terrainHeightWS = lerp(
        _TerrainOrigin.y,
        sampledTerrainHeightWS,
        saturate(_TerrainFollow));
    float aboveGround = positionWS.y - terrainHeightWS;

    if (aboveGround < -1.0)
        return 0.0;

    // Noise only erodes the layer, so the base height remains an upper bound.
    float maximumGroundFogHeight = max(_GroundFogHeight, 0.1);
    if (positionWS.y > terrainHeightWS + max(maximumGroundFogHeight, 0.2) + _TopSoftness + 0.001)
        return 0.0;

    float rawNoise = 0.5;
    UNITY_BRANCH
    if (_NoiseAmount > 0.0)
        rawNoise = GroundFogNoiseRaw(positionWS, noiseContext);
    float noiseMultiplier = GroundFogNoiseFromRaw(rawNoise);
    float heightErosion = (1.0 - noiseMultiplier) * max(_NoiseHeightDistortion, 0.0);
    float effectiveGroundFogHeight = max(_GroundFogHeight - heightErosion, 0.1);

    float localGroundFog = 1.0 - smoothstep(
        max(effectiveGroundFogHeight * 0.12, 0.1),
        max(effectiveGroundFogHeight, 0.2),
        aboveGround);

    if (localGroundFog <= 0.00001)
        return 0.0;

    return max(_Density, 0.0) * localGroundFog * noiseMultiplier * edgeFade;
}

// Samples URP's main light shadow map at a world position, including cascade
// selection, hardware shadow comparison, shadow strength and distance fade.
float GroundFogMainLightShadowVisibility(float3 positionWS)
{
#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    #if defined(_MAIN_LIGHT_SHADOWS_CASCADE)
        half cascadeIndex = ComputeCascadeIndex(positionWS);
    #else
        half cascadeIndex = 0;
    #endif
    float4 shadowCoord = mul(_MainLightWorldToShadow[cascadeIndex], float4(positionWS, 1.0));
    ShadowSamplingData samplingData = GetMainLightShadowSamplingData();
    half4 shadowParams = GetMainLightShadowParams();
    float shadow = SampleShadowmap(
        TEXTURE2D_SHADOW_ARGS(_MainLightShadowmapTexture, sampler_LinearClampCompare),
        shadowCoord,
        samplingData,
        shadowParams,
        false);
    return lerp(shadow, 1.0, GetMainLightShadowFade(positionWS));
#else
    return 1.0;
#endif
}

float GroundFogTerrainHorizonVisibility(float3 positionWS, float shadowSampleSpacing)
{
    if (_TerrainCastsFogShadows <= 0.5)
        return 1.0;

    if (_HasTerrainShadowMap <= 0.5)
        return 1.0;

    float2 terrainUV = (positionWS.xz - _TerrainOrigin.xz) /
        max(_TerrainSize.xz, float2(0.001, 0.001));

    if (any(terrainUV < 0.0) || any(terrainUV > 1.0))
        return 1.0;

    float sampleHeightWS = positionWS.y + _TerrainShadowBias;

    // RGBA stores four independently baked sun horizons. This gives a continuous
    // penumbra in one texture fetch and avoids the contour terraces created by
    // blurring one horizon height or averaging binary PCF tests.
    float4 horizonHeight01 = SAMPLE_TEXTURE2D_LOD(_TerrainShadowMap, sampler_TerrainShadowMap, terrainUV, 0.0);
    float4 horizonHeightWS = _TerrainOrigin.y + horizonHeight01 * _TerrainSize.y;

    // Keep the transition at least as wide as the world-space interval between
    // cached shadow samples. This removes undersampled shadow slices without
    // changing the actual blocker height or adding per-pixel ray samples.
    float effectiveSoftness = max(
        max(_TerrainShadowSoftness, 0.1),
        max(shadowSampleSpacing, 0.0) * max(_ShadowAntiBanding, 0.0));
    // Centre the filter on the geometric horizon so grazing-light penumbrae do
    // not acquire a large apparent horizontal offset.
    float halfSoftness = effectiveSoftness * 0.5;
    float4 visibility = smoothstep(
        float4(-halfSoftness, -halfSoftness, -halfSoftness, -halfSoftness),
        float4( halfSoftness,  halfSoftness,  halfSoftness,  halfSoftness),
        float4(sampleHeightWS, sampleHeightWS, sampleHeightWS, sampleHeightWS) - horizonHeightWS);
    return dot(visibility, float4(0.25, 0.25, 0.25, 0.25));
}

float GroundFogTerrainShadowVisibility(float3 positionWS, float shadowSampleSpacing)
{
    if (_TerrainCastsFogShadows <= 0.5)
        return 1.0;
    float terrainVisibility = GroundFogTerrainHorizonVisibility(positionWS, shadowSampleSpacing);
#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    UNITY_BRANCH
    if (_UseUnityRealtimeShadows > 0.5 && GetMainLightShadowParams().x > 0.0)
    {
        float3 receiver = positionWS + _LightDirectionWS.xyz * _TerrainShadowBias;
        #if defined(_MAIN_LIGHT_SHADOWS_CASCADE)
            if (ComputeCascadeIndex(receiver) >= 4)
                return terrainVisibility;
        #endif
        float realtimeVisibility = GroundFogMainLightShadowVisibility(receiver);
        float fade = GetMainLightShadowFade(receiver);
        // MainLightShadowVisibility already fades towards one. Replace that distant
        // contribution with terrain shadows without applying the fade a second time.
        return saturate(realtimeVisibility + fade * (terrainVisibility - 1.0));
    }
#endif
    return terrainVisibility;
}

float GroundFogSelfShadowVisibility(float localDensity)
{
    if (_FogSelfShadowStrength <= 0.0001)
        return 1.0;

    // Use a short local approximation so terrain-cast shadows retain contrast.
    float selfShadowDistance = max(_GroundFogHeight * 0.35, 8.0);
    return exp(-max(localDensity, 0.0) * selfShadowDistance * _FogSelfShadowStrength);
}

float3 GroundFogTerrainHeightDebugColor(float height01)
{
    float3 low = float3(0.03, 0.08, 0.28);
    float3 middle = float3(0.04, 0.78, 0.48);
    float3 high = float3(1.00, 0.72, 0.08);
    return height01 < 0.5
        ? lerp(low, middle, height01 * 2.0)
        : lerp(middle, high, (height01 - 0.5) * 2.0);
}

float4 GroundFogProjectedDebug(float3 positionWS)
{
    float2 terrainUV = (positionWS.xz - _TerrainOrigin.xz) / max(_TerrainSize.xz, float2(0.001, 0.001));
    if (any(terrainUV < 0.0) || any(terrainUV > 1.0))
        return 0.0;

    float terrainHeight01 = GroundFogTerrainHeight01(terrainUV);
    if (terrainHeight01 < 0.0)
        return 0.0;

    float3 color;

    if (_DebugView > 3.5)
    {
        float terrainHeightWS = _TerrainOrigin.y + terrainHeight01 * _TerrainSize.y;
        float3 shadowSampleWS = float3(
            positionWS.x,
            terrainHeightWS + max(_GroundFogHeight * 0.35, 2.0),
            positionWS.z);
        GroundFogNoiseContext noiseContext = GroundFogPrepareNoise(1.0);
        float shadowDensity = GroundFogDensityAtPosition(shadowSampleWS, noiseContext);
        float visibility =
            GroundFogTerrainShadowVisibility(shadowSampleWS, max(_TargetStepLength, 1.0)) *
            GroundFogSelfShadowVisibility(shadowDensity);
        color = lerp(float3(0.015, 0.025, 0.07), float3(1.0, 0.92, 0.48), visibility);
    }
    else if (_DebugView > 2.5)
    {
        GroundFogNoiseContext noiseContext = GroundFogPrepareNoise(1.0);
        float noiseValue = GroundFogNoiseRaw(positionWS, noiseContext);
        color = lerp(float3(0.02, 0.01, 0.05), float3(1.0, 0.92, 0.30), noiseValue);
    }
    else
    {
        color = GroundFogTerrainHeightDebugColor(terrainHeight01);
    }

    const float alpha = 0.92;
    return float4(color * alpha, alpha);
}

float4 GroundFogFrag(GroundFogVaryings input) : SV_Target
{
    float3 rayOriginWS = GetCameraPositionWS();
    float3 rayDirectionWS = normalize(input.positionWS - rayOriginWS);
    float3 rayOriginOS = mul(UNITY_MATRIX_I_M, float4(rayOriginWS, 1.0)).xyz;
    float3 rayDirectionOS = mul((float3x3)UNITY_MATRIX_I_M, rayDirectionWS);

    float enterDistance;
    float exitDistance;
    if (!GroundFogIntersectUnitBox(rayOriginOS, rayDirectionOS, enterDistance, exitDistance))
        discard;

    exitDistance = min(exitDistance, max(_MaximumFogDistance, 1.0));
    float unclippedExitDistance = exitDistance;
    float sceneRayDistance = exitDistance;

    if (_UseSceneDepth > 0.5)
    {
        float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS.xy * max(_GroundFogPixelScale, 1.0));
        float rawSceneDepth = SampleSceneDepth(screenUV);
        float sceneEyeDepth = LinearEyeDepth(rawSceneDepth, _ZBufferParams);
        float3 rayDirectionVS = mul((float3x3)UNITY_MATRIX_V, rayDirectionWS);
        float eyeDepthPerWorldUnit = max(-rayDirectionVS.z, 1e-5);
        sceneRayDistance = sceneEyeDepth / eyeDepthPerWorldUnit;
        exitDistance = min(exitDistance, sceneRayDistance - _DepthBias);
    }

    if (exitDistance <= enterDistance)
        discard;

    if (_DebugView > 1.5 && _DebugView < 2.5)
    {
        float alpha = 0.24;
        return float4(float3(0.10, 0.58, 0.95) * alpha, alpha);
    }

    if ((_DebugView > 0.5 && _DebugView < 1.5) || _DebugView > 2.5)
    {
        float debugDistance = (_UseSceneDepth > 0.5)
            ? clamp(sceneRayDistance - max(_DepthBias, 0.05), enterDistance, unclippedExitDistance)
            : lerp(enterDistance, unclippedExitDistance, 0.55);
        float3 debugPositionWS = rayOriginWS + rayDirectionWS * debugDistance;
        float4 debugColor = GroundFogProjectedDebug(debugPositionWS);
        if (debugColor.a <= 0.0)
            discard;
        return debugColor;
    }

    // No sample (including the smoothed density) can contribute on this ray.
    float minimumRayHeight = min(rayOriginWS.y + rayDirectionWS.y * enterDistance,
        rayOriginWS.y + rayDirectionWS.y * exitDistance);
    if (minimumRayHeight > _MaximumFogHeightWS)
        discard;

    // A ray heading upward leaves the fog layer where it crosses the maximum fog
    // height. Ending the march there avoids stepping through empty air above it.
    if (rayDirectionWS.y > 1e-4)
    {
        float slabExitDistance = (_MaximumFogHeightWS - rayOriginWS.y) / rayDirectionWS.y;
        exitDistance = min(exitDistance, max(slabExitDistance, enterDistance));
    }
    else if (rayDirectionWS.y < -1e-4 && rayOriginWS.y > _MaximumFogHeightWS)
    {
        // A downward ray starting above the layer enters it at the same crossing.
        float slabEnterDistance = (_MaximumFogHeightWS - rayOriginWS.y) / rayDirectionWS.y;
        enterDistance = max(enterDistance, min(slabEnterDistance, exitDistance));
    }

    if (exitDistance <= enterDistance)
        discard;

    float totalDistance = exitDistance - enterDistance;
    int maximumSteps = clamp((int)_MaximumRaySteps, 8, 128);
    int minimumSteps = clamp((int)_MinimumRaySteps, 4, maximumSteps);

    // Limit vertical sample spacing on steep view rays to prevent concentric bands.
    float verticalFeatureSize = max(
        min(max(_GroundFogHeight, 1.0), max(_TopSoftness, 1.0)),
        8.0);
    float targetVerticalStep = max(verticalFeatureSize * 0.22, 2.0);
    float angleAwareStepLength = targetVerticalStep /
        max(abs(rayDirectionWS.y), 0.08);
    float requestedStepLength = min(
        max(_TargetStepLength, 2.0),
        angleAwareStepLength);
    int steps = clamp(
        (int)ceil(totalDistance / requestedStepLength),
        minimumSteps,
        maximumSteps);
    float stepLength = totalDistance / max(steps, 1);

    // Stable screen-space jitter breaks coherent ray shells into stationary dither.
    float stableDither = GroundFogInterleavedGradientNoise(input.positionCS.xy);
    float rayOffset = lerp(0.5, stableDither, saturate(_RayJitterStrength));
    float distanceAlongRay = enterDistance + rayOffset * stepLength;

    float3 towardLightWS = normalize(_LightDirectionWS.xyz);
    float phase = GroundFogPhase(dot(towardLightWS, -rayDirectionWS), _Anisotropy);
    float3 ambientLighting = _AmbientColor.rgb * _AmbientStrength;
    float3 directLighting = _SunColor.rgb * (_SunStrength * phase);
    ambientLighting = max(ambientLighting, float3(0.025, 0.025, 0.025));

    float3 accumulatedColor = 0.0;
    float transmittance = 1.0;
    float farFadeBegin = max(_MaximumFogDistance * saturate(_FarFadeStart), 0.0);

    float noiseFilterWorldSize = stepLength * max(_NoiseFilterScale, 0.25);
    GroundFogNoiseContext noiseContext = GroundFogPrepareNoise(noiseFilterWorldSize);
    float shadowSampleSpacing = stepLength;
    bool sampleTerrainShadows = _EnableVolumetricShadows > 0.5 &&
        _TerrainCastsFogShadows > 0.5 &&
        (_UseUnityRealtimeShadows > 0.5 || _HasTerrainShadowMap > 0.5);

    float previousSampleDensity = 0.0;
    float previousRawDensity = 0.0;
    bool hasPreviousRegularSample = false;

    [loop]
    for (int i = 0; i < 128; i++)
    {
        if (i >= steps || distanceAlongRay >= exitDistance || transmittance < 0.008)
            break;

        float3 samplePositionWS = rayOriginWS + rayDirectionWS * distanceAlongRay;

        // Preserve the sampling lattice and the first empty sample after fog,
        // which still contributes through trapezoidal density smoothing.
        UNITY_BRANCH
        if (samplePositionWS.y > _MaximumFogHeightWS && previousSampleDensity <= 0.0)
        {
            previousRawDensity = 0.0;
            hasPreviousRegularSample = true;
            distanceAlongRay += stepLength;
            continue;
        }

        float rawSampleDensity = GroundFogDensityAtPosition(
            samplePositionWS,
            noiseContext);

        float nearFade = _NearFadeDistance > 0.001
            ? smoothstep(0.0, _NearFadeDistance, distanceAlongRay)
            : 1.0;
        float farFade = 1.0 - smoothstep(
            farFadeBegin,
            max(_MaximumFogDistance, farFadeBegin + 0.001),
            distanceAlongRay);
        float sampleDensity = rawSampleDensity * nearFade * farFade;

        // Trapezoidal integration smooths ray slices without extra texture fetches.
        float smoothing = saturate(_RayStepSmoothing);
        float integratedDensity = sampleDensity;
        float integratedRawDensity = rawSampleDensity;
        if (hasPreviousRegularSample)
        {
            integratedDensity = lerp(
                sampleDensity,
                0.5 * (previousSampleDensity + sampleDensity),
                smoothing);
            integratedRawDensity = lerp(
                rawSampleDensity,
                0.5 * (previousRawDensity + rawSampleDensity),
                smoothing);
        }

        if (integratedDensity > 0.0000001)
        {
            float terrainVisibility = 1.0;
            float selfShadowVisibility = 1.0;
            if (_EnableVolumetricShadows > 0.5)
            {
                terrainVisibility = sampleTerrainShadows
                    ? GroundFogTerrainShadowVisibility(samplePositionWS, shadowSampleSpacing)
                    : 1.0;
                selfShadowVisibility = GroundFogSelfShadowVisibility(integratedRawDensity);
            }

            float sunVisibility = lerp(
                1.0,
                terrainVisibility,
                saturate(_ShadowStrength)) * selfShadowVisibility;

            // Terrain occludes ambient light more softly than direct sunlight.
            float ambientOcclusion = lerp(0.28, 1.0, terrainVisibility);
            float ambientVisibility = lerp(
                1.0,
                ambientOcclusion,
                saturate(_AmbientShadowStrength) * saturate(_ShadowStrength));

            float3 lighting = ambientLighting * ambientVisibility +
                directLighting * sunVisibility;
            lighting = max(lighting, float3(0.04, 0.04, 0.04));

            float sampleAlpha = 1.0 - exp(-integratedDensity * stepLength);
            float contribution = transmittance * sampleAlpha;

            // Keep direct in-scattering on the same visibility mask as sunlight.
            float3 sunScattering = _SunColor.rgb *
                (_SunScattering * phase * sunVisibility);
            float3 sampleColor = _FogColor.rgb * lighting + sunScattering;
            accumulatedColor += sampleColor * contribution;
            transmittance *= 1.0 - sampleAlpha;
        }

        previousSampleDensity = sampleDensity;
        previousRawDensity = rawSampleDensity;
        hasPreviousRegularSample = true;
        distanceAlongRay += stepLength;
    }

    float alpha = 1.0 - transmittance;
    return float4(accumulatedColor, alpha);
}

#endif
