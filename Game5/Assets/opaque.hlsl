#include "include/retained.hlsl"
#include "include/lighting.hlsl"
#include "include/shadowreceive.hlsl"

SamplerState BilinearSampler : register(s1);
Texture2D<float4> Texture : register(t0);

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
    float2 velocity : VELOCITY;
};


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

    float3 prevWorldPos = mul(instance.PreviousModel, float4(input.position.xyz, 1.0)).xyz;
    float4 previousVPos = mul(PreviousViewProjection, float4(prevWorldPos, 1.0));
    result.velocity = result.position.xy / result.position.w - previousVPos.xy / previousVPos.w;
    // Add a slight amount to avoid velocity being 0 (special case)
    result.velocity.x += 0.0000001;
        
#if defined(VULKAN)
    result.position.y = -result.position.y;
#endif

    return result;
}

void PSMain(PSInput input
, out float4 OutColor : SV_Target0
, out float4 OutVelocity : SV_Target1
) 
{
    InstanceData instance = instanceData[input.primitiveId];
    
    TemporalAdjust(input.uv);
    
    float3 viewDir = normalize(input.viewPos);
    input.normal = normalize(input.normal);
    float4 tex = Texture.Sample(BilinearSampler, input.uv);
    float3 Albedo = tex.rgb;
    float3 Specular = 0.06;
    float Roughness = 0.7;
    float Metallic = 0.0;

    float3 shadowPos = ViewToShadow(input.viewPos);
    float shadow = ShadowMap.SampleCmpLevelZero(ShadowSampler, shadowPos.xy, shadowPos.z).r;
    
    // The light
    float3 o = ComputeLight(
        Albedo,
        Specular,
        input.normal,
        Roughness,
        _LightColor0 * shadow,
        _ViewSpaceLightDir0,
        -viewDir,
        Metallic
    );
    
    // Indirect
    o += ComputeIndiret(Albedo, Specular, input.normal, Roughness, Metallic, -viewDir);
    
    o.rgb *= 1.0f - instance.Highlight.a;
    o.rgb += instance.Highlight.rgb;
    //o.rgb = pow(o.rgb, 4) * 5.0;

    OutColor = float4(o, tex.a);
    OutVelocity = float4(input.velocity * 16.0, instance.Selected, 1);
    //OutColor.rg = OutVelocity.rg * 10.0 + 0.5;
}

//#include "include/shadowcast.hlsl"

struct ShadowCast_VSInput {
    uint primitiveId : INSTANCE;
    float4 position : POSITION;
};
struct ShadowCast_PSInput {
    float4 position : SV_POSITION;
};

ShadowCast_PSInput ShadowCast_VSMain(ShadowCast_VSInput input) {
    ShadowCast_PSInput result;
    InstanceData instance = instanceData[input.primitiveId];
    float3 worldPos = mul(instance.Model, float4(input.position.xyz, 1.0)).xyz;
    result.position = mul(ViewProjection, float4(worldPos, 1.0));
#if defined(VULKAN)
    result.position.y = -result.position.y;
#endif
    return result;
}

float4 ShadowCast_PSMain(ShadowCast_PSInput input) : SV_TARGET {
    return 1.0;
}
