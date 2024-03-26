#ifndef __VOLUMETRICS_HLSL__
#define __VOLUMETRICS_HLSL__

#include <noise.hlsl>
#include <common.hlsl>
#include <shadowreceive.hlsl>

SamplerState PointSampler : register(s0);
SamplerState BilinearSampler : register(s1);
Texture3D<float4> FogDensity : register(t5);
Texture2D<float4> Noise2D : register(t6);
float CloudDensity;
matrix InvViewProjection;
float3 _WorldSpaceLightDir0;


static const float cloudMaxY = 45;
static const float cloudMinY = 36;
static const float cloudTextureDensity = 45.0;
static const float3 cloudWindDir = normalize(float3(1, 0, -1));
static const float cloudWindSpeed = 0.5;

static float GlobalDensityBias = 2.5;
static float GlobalMipBias = 0.0;

float3 ApplyWind(float3 p) {
    return p + (cloudWindSpeed * cloudWindDir) * Time;
}
float3 WorldToCloudUVs(float3 uv) {
    const bool EnableUVRotate = true;
    if (EnableUVRotate) {
        const half3 n = normalize(float3(0.3, 1, 0.0));
        const half4 bc = n.xzxz * float4(n.zz / (n.y + 1.0), 1.0, 1.0);
        const float3 tangent = float3(n.y + bc.y, -bc.zx);
        const float3 bitangent = bc.xwy - float3(0, 0, 1);
        uv = float3(
            dot(uv, tangent),
            dot(uv, n),
            dot(uv, bitangent)
        );
    }
    uv *= (1.0 / cloudTextureDensity);
    return uv;
}

float SampleCloudsRaw(float3 uv
    , bool EnableThreeBlend = true
    , bool EnableExtendedCatmull = true
) {
    // Blend between 3 frames
    float timeN = Time * 0.03 * cloudWindSpeed - dot(uv, cloudWindDir) * 0.8;
    float3 cloudSamples = FogDensity.SampleLevel(BilinearSampler, uv.xzy, GlobalMipBias).rgb;
    //cloudSamples = PermuteO3(frac(dot(uv, float3(1, 2, 3))));
    float clouds = cloudSamples.x;
    if (EnableThreeBlend) {
        float3 time3 = timeN - float3(1.0, 2.0, 0.0) / 3.0;
        bool3 mask = EnableExtendedCatmull ? frac(time3 * 0.5 + 0.5 / 3.0) < 0.5 : true;
        float3 cloudBias = select(mask, cloudSamples, 1.0 - cloudSamples);
        float3 cloudTangents = float3(cloudBias.yz - cloudBias.zx, (1 - cloudBias.x) - cloudBias.y);
        float3 cloudTimes = frac(time3) * 3.0 - 1.0;
        float3 cloudPhase = saturate(1.0 - abs(cloudTimes));
        clouds = dot(cloudPhase, cloudBias + (cloudTangents * cloudTimes));
    }
    
    return clouds;
}

void PrimeClouds(float3 worldPos, float3 rayDir) {
    float midLayerHeight = (cloudMinY + cloudMaxY) / 2.0;
    float3 p = worldPos / 1000.0;
    float3 time3 = Time * 0.002 + p.x - float3(1.0, 2.0, 0.0) / 3.0;
    p.x -= Time * 0.002;
    float3 cloud = dot(Noise2D.Sample(BilinearSampler, p.xz).rgb,
        saturate(1.0 - abs(frac(time3) * 3.0 - 1.0)));
    GlobalDensityBias = lerp(0.5, 6.5, cloud);
}

float MaskClouds(float3 p, float clouds) {
    // Mask above/below
    //clouds *= saturate((cloudMaxY + 0.5 - p.y) / 4.0);
    //clouds *= saturate((p.y - cloudMinY + 0.5) / 3.0);
    // Add a bias to move density out of the range, to encourage solid 
    // values within the range. This depends heavily on the texture values
    float Bias = 0.8 - 1.0;
    const float2 TransitionScale = (1.0 / 4.0) * float2(-1.0, 1.0);
    const float2 CloudMinMaxBias = (float2(cloudMinY, cloudMaxY) * TransitionScale + Bias);
    clouds -= dot(saturate(p.y * TransitionScale - CloudMinMaxBias), 1.0);
    
    clouds = pow(saturate(clouds * 5.0 - GlobalDensityBias), 2.0);
    return clouds;
}

float SampleClouds(float3 p, float3 uv) {
    float clouds = SampleCloudsRaw(uv);
    
    clouds = MaskClouds(p, clouds);
    // Add density multiplier
    return clouds * CloudDensity;
}
float SampleClouds(float3 p) {
    float3 uv = p;
    
    uv = ApplyWind(uv);
    uv = WorldToCloudUVs(uv);
    
    return SampleClouds(p, uv);
}
float HenyeyGreenstein(float LdotV, float g) {
	float gg = g * g;
	return (1.0 - gg) / pow(1.0 + gg - 2.0 * g * LdotV, 1.5);
}
float GetCloudRaycast(float3 worldPos, float3 rayDir, float bias = 0.5, const int stepCount = 2) {
    // Determine steps to get through the layer
    float distance = clamp((cloudMaxY - worldPos.y) / rayDir.y, 1.0, 10.0);
    float stepAmount = distance / stepCount;
    float3 stepIncrement = rayDir * stepAmount;
    worldPos += stepIncrement * bias;
    
    // Accumulate density
    float density = 0.0;
    for (int i = 0; i < stepCount; i++) {
		density += SampleClouds(worldPos);
        worldPos += stepIncrement;
    }
    return exp2(-density * stepAmount);
}
float GetGroundShadow(float3 worldPos, float bias = 0.5, int stepCount = 2) {
    // Step to the bottom of the clouds
    float3 lightDir = _WorldSpaceLightDir0;
    worldPos += lightDir * max((cloudMinY - worldPos.y) / lightDir.y, 0);
    return GetCloudRaycast(worldPos, _WorldSpaceLightDir0, bias, stepCount);
}
float GetVolumeShadow(float3 p, float bias = 0.5, int stepCount = 1, bool enableShadowReceive = true) {
    float shadow = GetCloudRaycast(p, _WorldSpaceLightDir0, bias, stepCount);
    if (enableShadowReceive) {
        float3 shadowPos = ViewToShadow(p);
        shadow *= ShadowMap.SampleCmpLevelZero(ShadowSampler, shadowPos.xy, shadowPos.z).r;
    }

    return shadow;
}

#endif
