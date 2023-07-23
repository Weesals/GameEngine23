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
};

#include "include/lighting.hlsl"

struct VSInput
{
    float4 position : POSITION;
    float3 normal : NORMAL;
};

struct PSInput
{
    float4 position : SV_POSITION;
    float3 viewPos : TEXCOORD1;
    float3 normal : NORMAL;
};

PSInput VSMain(VSInput input)
{
    PSInput result;
    
    result.position = mul(float4(input.position.xyz, 1.0), ModelViewProjection);
    result.viewPos = mul(float4(input.position.xyz, 1.0), ModelView);
    result.normal = mul(float4(input.normal.xyz, 0.0), ModelView);
    
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
