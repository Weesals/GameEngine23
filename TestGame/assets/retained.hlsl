// A standard PBR shader (according to various examples online)

#define PI 3.14159265359

#include "include/common.hlsl"
#include "include/lighting.hlsl"

cbuffer Testing : register(b1)
{
    float4 TestValue;
}
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

SamplerState BilinearSampler : register(s0);
Texture2D<float4> Texture : register(t0);
Texture2D<float4> ShadowMap : register(t1);

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
    result.viewPos = mul(View, float4(worldPos, 1.0)).xyz;
    result.normal = mul(View, float4(worldNrm, 0.0)).xyz;
    result.uv = input.uv;
        
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
    float4 t = Texture.Sample(BilinearSampler, input.uv);
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

struct ShadowCast_VSInput
{
    uint primitiveId : INSTANCE;
    float4 position : POSITION;
};
struct ShadowCast_PSInput
{
    float4 position : SV_POSITION;
};

ShadowCast_PSInput ShadowCast_VSMain(ShadowCast_VSInput input)
{
    ShadowCast_PSInput result;
    InstanceData instance = instanceData[input.primitiveId];
    float3 worldPos = mul(instance.Model, float4(input.position.xyz, 1.0)).xyz;
    result.position = mul(ViewProjection, float4(worldPos, 1.0));  
#if defined(VULKAN)
    result.position.y = -result.position.y;
#endif
    return result;
}

float4 ShadowCast_PSMain(ShadowCast_PSInput input) : SV_TARGET
{
    return 1.0;
}
