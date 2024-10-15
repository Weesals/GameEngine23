#include "include/retained.hlsl"
#include "include/lighting.hlsl"
#include "include/shadowcast.hlsl"
#include "include/shadowreceive.hlsl"

SamplerState BilinearSampler : register(s1);
Texture2D<float4> Texture : register(t0);
Texture2D<float4> SceneDepth : register(t2);

struct VSInput
{
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


PSInput VSMain(VSInput input)
{
    PSInput result;
    
    InstanceData instance = GetInstanceData(input.primitiveId);
    
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
    return float4(frac(SceneDepth[input.position.xy].rrr * 1000), 1.0);
    InstanceData instance = GetInstanceData(input.primitiveId);
    
    float3 viewDir = normalize(input.viewPos);
    input.normal = normalize(input.normal);
    float4 tex = Texture.Sample(BilinearSampler, input.uv);
    float3 Albedo = tex.rgb;
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

    return float4(o, tex.a);
}

