#ifndef __SHADOW_RECEIVE__
#define __SHADOW_RECEIVE__

#include <common.hlsl>

Texture2D<float> ShadowMap : register(t4);
SamplerComparisonState ShadowSampler : register(s3);

float3 ViewToShadow(float3 viewPos) {
    float4 shadowVPos = mul(ShadowIVViewProjection, float4(viewPos, 1.0));
    shadowVPos.xyz /= shadowVPos.w;
    shadowVPos.y *= -1.0;
    //float4 shadowSample = ShadowMap.Sample(BilinearSampler, 0.5 + shadowVPos.xy * 0.5);
    float2 shadowUV = 0.5 + shadowVPos.xy * 0.5;
    //float noise = IGN(input.position.xy);
    //shadowUV += (float2(noise, frac(noise * 100)) - 0.5) / 512;
    return float3(shadowUV, shadowVPos.z - 0.002);
}

#endif
