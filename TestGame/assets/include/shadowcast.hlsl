#ifndef __SHADOWCAST__
#define __SHADOWCAST__

#include "include/retained.hlsl"

struct ShadowCast_VSInput
{
    uint primitiveId : INSTANCE;
    float4 position : POSITION;
};
struct ShadowCast_PSInput
{
    float4 position : SV_POSITION;
};

ShadowCast_PSInput ShadowCast_VSMain(ShadowCast_VSInput input)
{
    ShadowCast_PSInput result;
    InstanceData instance = instanceData[input.primitiveId];
    float3 worldPos = mul(instance.Model, float4(input.position.xyz, 1.0)).xyz;
    result.position = mul(ViewProjection, float4(worldPos, 1.0));
#if defined(VULKAN)
    result.position.y = -result.position.y;
#endif
    return result;
}

float4 ShadowCast_PSMain(ShadowCast_PSInput input) : SV_TARGET
{
    return 1.0;
}

#endif
