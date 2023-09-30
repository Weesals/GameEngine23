// A standard PBR shader (according to various examples online)

#define PI 3.14159265359

cbuffer WorldCB : register(b0)
{
    float3 _LightColor0;
    float3 _ViewSpaceLightDir0;
    float3 _ViewSpaceUpVector;
}
cbuffer ConstantBuffer : register(b1)
{
    matrix ModelView;
    matrix ModelViewProjection;
    float2 Offsets[256];
    float Time;
    float4 HeightRange;
};

Texture2D<float4> HeightMap : register(t0);
SamplerState g_sampler : register(s0);
Texture2D<float4> GrassTexture : register(t1);

#include "include/lighting.hlsl"
#include "include/noise.hlsl"

struct VSInput
{
    uint instanceId : SV_InstanceID;
    float4 position : POSITION;
    float3 normal : NORMAL;
};

struct PSInput
{
    float4 position : SV_POSITION;
    float3 normal : NORMAL;
    float3 localPos : TEXCOORD0;
    float3 viewPos : TEXCOORD1;
};

PSInput VSMain(VSInput input)
{
    const float Scale = 15;
    PSInput result;

    float3 worldPos = input.position.xyz;
    float3 worldNrm = input.normal.xyz;
    // Each instance has its own offset
    worldPos.xz += Offsets[input.instanceId];

#if defined(VULKAN)
    // Textures are not currently supported in Vulkan
    // so use this noise instead
    float2 dd;
    worldPos.y += SimplexNoise(worldPos.xz / Scale, dd);
    worldNrm.xz -= dd / Scale;
    worldNrm = normalize(worldNrm);
#else
    // Sample from the heightmap and offset the vertex
    float4 hcell = HeightMap.Load(int3(worldPos.xz, 0), 0);
    worldPos.y += HeightRange.x + (hcell.r) * (HeightRange.y - HeightRange.x);
    worldNrm.xz = hcell.yz * 2.0 - 1.0;
    worldNrm.y = sqrt(1.0 - dot(worldNrm.xz, worldNrm.xz));
#endif
    
    result.localPos = worldPos;
    result.position = mul(ModelViewProjection, float4(worldPos, 1.0));
    result.viewPos = mul(ModelView, float4(worldPos, 1.0));
    result.normal = mul(ModelView, float4(worldNrm, 0.0));
    
#if defined(VULKAN)
    result.position.y = -result.position.y;
#endif

    return result;
}

float2 PermuteUV(float2 uv, float rnd)
{
    rnd *= 3.14 * 2.0;
    return uv.xy * cos(rnd) + uv.yx * sin(rnd) * float2(1, -1);
}

float4 PSMain(PSInput input) : SV_TARGET
{    
    float3 viewDir = normalize(input.viewPos);
    input.normal = normalize(input.normal);
    
    // TODO: Should be sampled from textures
    float3 Albedo = 0.6;
    float3 Specular = 0.06;
    float Roughness = 0.7;
    float Metallic = 0.0;
    
    
    {
        float2 p = input.localPos.xz / 12.0;
        float2 i = floor(p + dot(p, 1.0) * SF2);
        float2 L = i - dot(i, 1.0) * SG2;
    
        float2 d = p - L;
        float2 o = (d.x > d.y ? float2(1.0, 0.0) : float2(0.0, 1.0));
        float2 p0 = L;
        float2 p1 = L + o - SG2;
        float2 p2 = L + 1.0 - 2.0 * SG2;

        float2 RndVec = float2(10.15, 23.78);
        float3 rnd = float3(dot(i, RndVec), dot(i + o, RndVec), dot(i + 1, RndVec));
        rnd = frac(sin(rnd) * 100);
        Albedo = 0;
        float3 w = float3(dot(p - p0, p - p0), dot(p - p1, p - p1), dot(p - p2, p - p2));
        w = max(0.5 - w, 0.0);
        w = pow(w, 4.0);
        w /= dot(w, 1);
        [unroll]
        for (int j = 0; j < 3; ++j)
        {
            Albedo += w[j] *
                GrassTexture.Sample(g_sampler, PermuteUV(input.localPos.xz * 0.05, rnd[j])).rgb;
        }
    }
    
    // The light
    float3 o = ComputeLight(
        Albedo,
        Specular,
        input.normal,
        Roughness,
        _LightColor0,
        _ViewSpaceLightDir0,
        -viewDir,
        Metallic
    );
    
    // Indirect
    o += ComputeIndiret(Albedo, Specular, input.normal, Roughness, Metallic, -viewDir);

    return float4(o, 1);
}
