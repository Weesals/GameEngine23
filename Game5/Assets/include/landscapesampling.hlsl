#ifndef __LANDSCAPE_SAMPLING__
#define __LANDSCAPE_SAMPLING__

#define PI 3.14159265359

#include <common.hlsl>
#include <landscapecommon.hlsl>

SamplerState AnisotropicSampler : register(s2);
#if EDGE
    Texture2D<float4> EdgeTex : register(t3);
#else
    Texture2DArray<half4> BaseMaps : register(t3);
    Texture2DArray<half4> BumpMaps : register(t4);
#endif

struct SampleContext {
    Texture2DArray<half4> BaseMaps;
    Texture2DArray<half4> BumpMaps;
    float3 WorldPos;
    float2 PositionCS;
    float3 TriPlanarWeights;
};
struct ControlPoint {
    uint4 Data;
    uint Layer;
    float Rotation;
    float2x2 RotationMatrix;
};
struct TerrainSample {
    half3 Albedo;
    half3 Normal;
    half Height;
    half Metallic;
    half Roughness;
};

ControlPoint DecodeControlMap(uint cp) {
    ControlPoint o;
    o.Data = ((cp >> uint4(0, 8, 16, 24)) & 0xff);
    o.Layer = o.Data.r;
    o.Rotation = o.Data.g * (3.1415 * 2.0 / 256.0);
    float2 sc; sincos(o.Rotation, sc.x, sc.y);
    o.RotationMatrix = float2x2(sc.y, -sc.x, sc.x, sc.y);
    return o;
}
TerrainSample SampleTerrain(SampleContext context, ControlPoint cp, bool enableTriPlanar = false) {
    TerrainSample o;
    LayerData data = _LandscapeLayerData[cp.Layer];
    
    float3 worldPos = context.WorldPos * data.Scale;
    float2 uv = mul(cp.RotationMatrix, worldPos.xz);
    float2 duvdx = ddx(uv);
    float2 duvdy = ddy(uv);
    
    if (enableTriPlanar) {
        float triWeight = saturate(3.0 - context.TriPlanarWeights.y * 5.0);
        if (QuadReadLaneAt(triWeight, 0) > 0.0) {
            float2 xuv = worldPos.zy;
            float2 xduvdx = ddx(xuv);
            float2 xduvdy = ddy(xuv);
            float2 zuv = worldPos.xy;
            float2 zduvdx = ddx(zuv);
            float2 zduvdy = ddy(zuv);
            float rnd1 = IGN(context.PositionCS);
            if (triWeight > rnd1) {
                float rnd2 = frac(rnd1 * 10.0);
                context.TriPlanarWeights.xz = abs(context.TriPlanarWeights.xz);
                float xzWeight = context.TriPlanarWeights.x / (context.TriPlanarWeights.x + context.TriPlanarWeights.z);
                xzWeight = saturate(xzWeight * 51.0 - 25);
                if (xzWeight > rnd2) {
                    uv = xuv;
                    duvdx = xduvdx;
                    duvdy = xduvdy;
                } else {
                    uv = zuv;
                    duvdx = zduvdx;
                    duvdy = zduvdy;
                }
            }
        }
    }
    
    uv.y += data.UVScrollY * Time;

    half4 bumpSample = context.BumpMaps.Sample(AnisotropicSampler, float3(uv, cp.Layer));
    o.Normal = bumpSample.rgb * 2.0 - 1.0;
    o.Normal.y *= -1;
    o.Normal.xy = mul((half2x2)cp.RotationMatrix, o.Normal.xy);
    o.Metallic = (half)data.Metallic;
    o.Roughness = (half)data.Roughness * bumpSample.a;
    
    half4 baseSample = context.BaseMaps.Sample(AnisotropicSampler, float3(uv, cp.Layer));
    o.Albedo = baseSample.rgb;
    o.Height = baseSample.a;
    
    return o;
}

uint2 Sort(uint2 v) {
    return uint2(min(v.x, v.y), max(v.x, v.y));
}
uint4 Sort(uint4 v) {
    v.xyzw = uint4(min(v.xy, v.zw), max(v.xy, v.zw));
    v.xyzw = uint4(min(v.xz, v.yw), max(v.xz, v.yw));
    v.yz = uint2(min(v.y, v.z), max(v.y, v.z));
    return v;
}

#endif
