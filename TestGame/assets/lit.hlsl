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
    float4 Highlight;
};

#include "include/lighting.hlsl"

struct VSInput
{
    float4 position : POSITION;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD0;
};

struct PSInput
{
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 viewPos : TEXCOORD1;
    float3 normal : NORMAL;
};

Texture2D<float4> Texture : register(t0);
SamplerState g_sampler : register(s0);

PSInput VSMain(VSInput input)
{
    PSInput result;
    
    result.position = mul(float4(input.position.xyz, 1.0), ModelViewProjection);
    result.viewPos = mul(float4(input.position.xyz, 1.0), ModelView);
    result.normal = mul(float4(input.normal.xyz, 0.0), ModelView);
    result.uv = input.uv;
    
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
    float4 t = Texture.Sample(g_sampler, input.uv);
    float3 Albedo = t.rgb;
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
    
    o.rgb *= 1.0f - Highlight.a;
    o.rgb += Highlight.rgb;

    return float4(o, 1);
}