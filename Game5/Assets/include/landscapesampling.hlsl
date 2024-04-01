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
};
struct ControlPoint {
    uint4 Data;
    uint Layer;
    float Rotation;
};
struct TerrainSample {
    half3 Albedo;
    half3 Normal;
    half Height;
    half Metallic;
    half Roughness;
    //float4 Packed1;
    //uint4 Packed2;
};
void Pack(inout TerrainSample t) {
    /*t.Packed1 = float4(
        (float)floor(t.Albedo.r * 256.0) + (float)t.Normal.x * 0.5 + 0.5,
        (float)floor(t.Albedo.g * 256.0) + (float)t.Normal.y * 0.5 + 0.5,
        (float)floor(t.Albedo.b * 256.0) + (float)t.Height,
        (float)floor(t.Metallic * 256.0) + (float)t.Roughness
    );
    t.Packed2 = uint4(
        (f32tof16(t.Albedo.r) << 16) | f32tof16(t.Albedo.g),
        (f32tof16(t.Albedo.g) << 16) | f32tof16(t.Height),
        (f32tof16(t.Normal.x) << 16) | f32tof16(t.Normal.y),
        (f32tof16(t.Metallic) << 16) | f32tof16(t.Roughness)
    );*/
}
void Unpack(inout TerrainSample t) {
    /*t.Albedo = (half3)floor(t.Packed1.xyz) / 256.0;
    t.Normal.xy = (half2)frac(t.Packed1.xy) * 2.0 - 1.0;
    t.Normal.z = 0.0;
    t.Height = (half)frac(t.Packed1.z);
    t.Metallic = (half)floor(t.Packed1.w) / 256.0;
    t.Roughness = (half)frac(t.Packed1.w);
    t.Albedo = float3(f16tof32(t.Packed2.x >> 16), f16tof32(t.Packed2.x), f16tof32(t.Packed2.y >> 16));
    t.Normal = float3(f16tof32(t.Packed2.z >> 16), f16tof32(t.Packed2.z), 0);
    t.Height = f16tof32(t.Packed2.y);
    t.Metallic = f16tof32(t.Packed2.w >> 16);
    t.Roughness = f16tof32(t.Packed2.w);*/
}
ControlPoint DecodeControlMap(uint cp) {
    ControlPoint o;
    o.Data = ((cp >> uint4(0, 8, 16, 24)) & 0xff);
    o.Layer = o.Data.r;
    o.Rotation = o.Data.g * (3.1415 * 2.0 / 256.0);
    return o;
}
TerrainSample SampleTerrain(SampleContext context, ControlPoint cp) {
    TerrainSample o;
    LayerData data = _LandscapeLayerData[cp.Layer];
    
    float2 uv = context.WorldPos.xz;
    float2 sc; sincos(cp.Rotation, sc.x, sc.y);
    float2x2 rot = float2x2(sc.y, -sc.x, sc.x, sc.y);
    uv = mul(rot, uv);
    uv *= data.Scale;
    uv.y += data.UVScrollY * Time;
    
    half4 bumpSample = context.BumpMaps.Sample(AnisotropicSampler, float3(uv, cp.Layer));
    o.Normal = bumpSample.rgb * 2.0 - 1.0;
    o.Normal.y *= -1;
    o.Normal.xy = mul((half2x2)rot, o.Normal.xy);
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
