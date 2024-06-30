#ifndef __LANDSCAPE_SAMPLING__
#define __LANDSCAPE_SAMPLING__

#define PI 3.14159265359

#include <common.hlsl>
#include <landscapecommon.hlsl>

SamplerState BilinearSampler : register(s1);
SamplerState AnisotropicSampler : register(s2);
#if EDGE
    Texture2D<float4> EdgeTex : register(t4);
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
struct TerrainSample {
    half3 Albedo;
    half3 Normal;
    half Height;
    half Metallic;
    half Roughness;
    
    float4 Packed;
    uint4 PackedI;
    float Pack3(float3 v) {
        v.xy = round(255.0 * v.xy);
        return dot(v, float3(256, 1.0, 255.0 / 256.0));
    }
    float3 Unpack3(float v) {
        return float3(v / (256 * 255.0), frac(v / 256.0), frac(v));
    }
    void Pack() {
        Packed.z = Pack3(Albedo);
        uint2 packedAttr = f32tof16(float2(Metallic, Roughness));
        PackedI.y = (packedAttr.x << 16) | packedAttr.y;
        
        //Packed.w = Pack3(float3(Normal.xy * 0.5 + 0.5, Metallic));
        uint2 packedNrm = f32tof16(Normal.xy);
        PackedI.x = (packedNrm.x << 16) | packedNrm.y;
    }
    void UnpackAlbedo() {
        Albedo = Unpack3(Packed.z);
        Metallic = f16tof32(PackedI.y >> 16);
        Roughness = f16tof32(PackedI.y);
    }
    void UnpackNrm() {
        //Normal.xy = Unpack3(Packed.w).xy * 2.0 - 1.0;
        //Metallic = Unpack3(Packed.w).z;
        Normal.xy = f16tof32(uint2(PackedI.x >> 16, PackedI.x));
        Normal.z = sqrt(1.0 - dot(Normal.xy, Normal.xy));
    }
};

half4 SampleGradOptional(Texture2DArray<half4> tex, float3 uv, float2 duvdx, float2 duvdy, bool enableGrad = false) {
    if (enableGrad) {
        return tex.SampleGrad(AnisotropicSampler, uv, duvdx, duvdy);
    } else{
        return tex.Sample(AnisotropicSampler, uv);
    }
}
float2 ComputeSampleUV(SampleContext context, ControlPoint cp) {
    LayerData data = _LandscapeLayerData[cp.Layer];
    float3 worldPos = context.WorldPos * data.Scale;
    return mul(cp.RotationMatrix, worldPos.xz);
}
TerrainSample SampleTerrain(SampleContext context, ControlPoint cp, bool enableTriPlanar = false, float mipBias = 0.0, bool useGrad = false) {
    TerrainSample o;
    LayerData data = _LandscapeLayerData[cp.Layer];
    
    float3 worldPos = context.WorldPos * data.Scale;
    float2 uv = ComputeSampleUV(context, cp);
    float2 duvdx = ddx(uv) * pow(2, mipBias);
    float2 duvdy = ddy(uv) * pow(2, mipBias);
    
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
    
    half4 bumpSample = SampleGradOptional(context.BumpMaps, float3(uv, cp.Layer), duvdx, duvdy, enableTriPlanar || mipBias != 0.0 || useGrad);
    half4 baseSample = SampleGradOptional(context.BaseMaps, float3(uv, cp.Layer), duvdx, duvdy, enableTriPlanar || mipBias != 0.0 || useGrad);
    //bumpSample = half4(0, 0, 0, 1);
    o.Normal.xy = bumpSample.rg * half2(2.0, 2.0) - half2(1.0, 1.0);
    o.Normal.xy = mul(o.Normal.xy, (half2x2)cp.RotationMatrix);
    o.Normal.z = sqrt(1 - dot(o.Normal.xy, o.Normal.xy));
    o.Height = bumpSample.b;
    
    //baseSample = abs(sin(cp.Layer + half4(1, 2, 3, 4)));
    o.Albedo = baseSample.rgb;
    o.Roughness = (half)data.Roughness * baseSample.a;
    o.Metallic = (half)data.Metallic;
    
    o.Pack();
    
    return o;
}
float SampleTerrainHeightLevel(SampleContext context, ControlPoint cp, float mipLevel = 0.0) {
    float2 uv = ComputeSampleUV(context, cp);
    half4 bumpSample = context.BumpMaps.SampleLevel(BilinearSampler, float3(uv, cp.Layer), mipLevel);
    return bumpSample.b;
}
float SampleTerrainHeightBias(SampleContext context, ControlPoint cp, float mipBias = 0.0) {
    float2 uv = ComputeSampleUV(context, cp);
    half4 bumpSample = context.BumpMaps.SampleBias(BilinearSampler, float3(uv, cp.Layer), mipBias);
    return bumpSample.b;
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
