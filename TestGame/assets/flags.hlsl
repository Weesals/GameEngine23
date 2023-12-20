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
    //float4 InstanceData[256];
    float Time;
};

#include "include/lighting.hlsl"
#include "include/noise.hlsl"

Texture2D<float4> Texture : register(t0);
SamplerState g_sampler : register(s1);

struct VSInput
{
    //uint instanceId : SV_InstanceID;
    float4 position : POSITION;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD0;
    float4 posSize : INST_POSSIZE;
    float4 playerId : INST_PLAYERID;
};

struct PSInput
{
    float4 position : SV_POSITION;
    float3 viewPos : TEXCOORD1;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD0;
};

PSInput VSMain(VSInput input)
{
    const float Scale = 15;
    PSInput result;

    float3 worldPos = input.position.xyz;
    float3 worldNrm = input.normal.xyz;
    // Each instance has its own offset
    worldPos.xyz += input.posSize.xyz; //InstanceData[input.instanceId].xyz;

    result.uv = input.uv;
    result.position = mul(ModelViewProjection, float4(worldPos, 1.0));
    result.viewPos = mul(ModelView, float4(worldPos, 1.0));
    result.normal = mul(ModelView, float4(worldNrm, 0.0));
    
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
    float3 Albedo = Texture.Sample(g_sampler, input.uv);
    float3 Specular = 0.06;
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
    
    // Indirect
    o += ComputeIndiret(Albedo, Specular, input.normal, Roughness, Metallic, -viewDir);

    return float4(o, 1);
}
