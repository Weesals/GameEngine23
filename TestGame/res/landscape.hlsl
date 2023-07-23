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
    float3 viewPos : TEXCOORD1;
    float3 normal : NORMAL;
    float3 localPos : TEXCOORD0;
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
    result.position = mul(float4(worldPos, 1.0), ModelViewProjection);
    result.viewPos = mul(float4(worldPos, 1.0), ModelView);
    result.normal = mul(float4(worldNrm, 0.0), ModelView);
    
#if defined(VULKAN)
    result.position.y = -result.position.y;
#endif

    return result;
}

float4 PSMain(PSInput input) : SV_TARGET
{    
    float3 viewDir = normalize(input.viewPos);
    input.normal = normalize(input.normal);
    
    // TODO: Should be sampled from textures
    float3 Albedo = 0.9;
    float3 Specular = 0.5;
    float Roughness = 0.7;
    float Metallic = 0.0;
    
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
    
    float3 envFresnel = Specular_F_Roughness(
        Specular.xyz,
        Roughness * Roughness,
        dot(input.normal, -viewDir)
    ).xyz;
    
    float3 Kd = (1.0f - envFresnel) * (1.0f - Metallic);
    
    // Indirect
    o += SampleAmbientLight(input.normal) * Kd;
    o += SampleEnvironment(reflect(viewDir, input.normal), Roughness) * envFresnel;

    return float4(o, 1);
}
