#ifndef __COMMON__
#define __COMMON__

#define PI 3.14159265359

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

float IGN(float2 pos) {
    pos.x += TemporalFrame * 5.588238;
    return frac(52.9829189f * frac(dot(float2(0.06711056f, 0.00583715f), pos)));
}

#endif
