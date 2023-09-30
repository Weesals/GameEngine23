// A standard PBR shader (according to various examples online)

#define PI 3.14159265359

cbuffer WorldCB : register(b0)
{
    float3 _LightColor0;
    float3 _ViewSpaceLightDir0;
    float3 _ViewSpaceUpVector;
    float4x4 View;
    float4x4 ViewProjection;
}
cbuffer ConstantBuffer : register(b1)
{
};

#include "include/lighting.hlsl"

struct VSInput
{
    //uint instanceId : SV_InstanceID;
    uint primitiveId : INSTANCE;
    float4 position : POSITION;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD0;
};

struct PSInput
{
    uint primitiveId : SV_InstanceID;
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 viewPos : TEXCOORD1;
    float3 normal : NORMAL;
};

Texture2D<float4> Texture : register(t0);
SamplerState g_sampler : register(s0);

struct InstanceData
{
    matrix Model;
    matrix Unused;
    float4 Highlight;
    float4 V10;
};
StructuredBuffer<InstanceData> instanceData : register(t1);

PSInput VSMain(VSInput input)
{
    PSInput result;
    
    InstanceData instance = instanceData[input.primitiveId];
    
    result.primitiveId = input.primitiveId;
    float3 worldPos = mul(instance.Model, float4(input.position.xyz, 1.0)).xyz;
    float3 worldNrm = mul(instance.Model, float4(input.normal.xyz, 0.0)).xyz;
    result.position = mul(ViewProjection, float4(worldPos, 1.0));
    result.viewPos = mul(View, float4(worldPos, 1.0));
    result.normal = mul(View, float4(worldNrm, 0.0));
    result.uv = input.uv;
    
    /*result.position = lerp(
        float4(input.position.xyz / 5.0, 1.0),
        result.position,
        0.5 + 0.5 * sin(Time * 6.0)
    );*/
    //result.position.x += sin(Time);
    
#if defined(VULKAN)
    result.position.y = -result.position.y;
#endif

    return result;
}

float4 PSMain(PSInput input) : SV_TARGET
{    
    InstanceData instance = instanceData[input.primitiveId];
    
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
    
    o.rgb *= 1.0f - instance.Highlight.a;
    o.rgb += instance.Highlight.rgb;

    return float4(o, 1);
}
