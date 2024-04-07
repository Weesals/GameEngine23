#ifndef __COMMON__
#define __COMMON__

#define PI 3.14159265359

static const float LuminanceFactor = 0.25;

// Gather Orders (BL, BR, TL, TR)
#define GatherOrderIntel uint4(1, 0, 2, 3)
#define GatherOrderNvidia uint4(3, 2, 0, 1)

cbuffer WorldCB : register(b0)
{
    float3 _LightColor0;
    float3 _ViewSpaceLightDir0;
    float3 _ViewSpaceUpVector;
    float Time;
    float2 TemporalJitter;
    float TemporalFrame;
    matrix ShadowIVViewProjection;
    matrix View;
    matrix ViewProjection;
    matrix PreviousViewProjection;
    float2 Resolution;
}

float IGN(float2 pos, float seed = 0.0, bool newWeight = false) {
    if (newWeight) pos += float2(seed, TemporalFrame * 1.32);
    else pos.x += TemporalFrame * 2.588238 + seed;
    return frac(52.9829189f * frac(dot(float2(0.06711056f, 0.00583715f), pos)));
}

#endif
