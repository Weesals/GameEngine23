#include <retained.hlsl>
#include <lighting.hlsl>
#include <shadowreceive.hlsl>
#include <basepass.hlsl>

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
    float3 velocity : VELOCITY;
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
    result.normal = mul((float3x3)View, worldNrm);
    result.uv = input.uv;

    float3 prevWorldPos = mul(instance.PreviousModel, float4(input.position.xyz, 1.0)).xyz;
    float4 previousVPos = mul(PreviousViewProjection, float4(prevWorldPos, 1.0));
    result.velocity = result.position.xyz / result.position.w - previousVPos.xyz / previousVPos.w;
    // Add a slight amount to avoid velocity being 0 (special case)
    result.velocity.x += 0.0000001;
        
#if defined(VULKAN)
    result.position.y = -result.position.y;
#endif

    return result;
}

void PSMain(PSInput input, out BasePassOutput result) {
    InstanceData instance = instanceData[input.primitiveId];
    
    TemporalAdjust(input.uv);
    
    float4 tex = Texture.Sample(BilinearSampler, input.uv);
    PBRInput pbrInput = PBRDefault();
    pbrInput.Albedo = tex.rgb;
    pbrInput.Alpha = tex.a;
    pbrInput.Specular = 0.06;
    pbrInput.Roughness = 0.7;
    pbrInput.Emissive = instance.Highlight;
    pbrInput.Normal = normalize(input.normal);

    float3 shadowPos = ViewToShadow(input.viewPos);
    float shadow = ShadowMap.SampleCmpLevelZero(ShadowSampler, shadowPos.xy, shadowPos.z).r;
    //pbrInput.Occlusion *= shadow;
    
    result = PBROutput(pbrInput, normalize(input.viewPos));
    OutputVelocity(result, input.velocity);
    OutputSelected(result, instance.Selected);
}

//#include "include/shadowcast.hlsl"

struct ShadowCast_VSInput {
    uint primitiveId : INSTANCE;
    float4 position : POSITION;
    float3 normal : NORMAL;
};
struct ShadowCast_PSInput {
    float4 position : SV_POSITION;
};

void ShadowCast_VSMain(ShadowCast_VSInput input, out float4 positionCS : SV_POSITION) {
    ShadowCast_PSInput result;
    InstanceData instance = instanceData[input.primitiveId];
    input.position.xyz += input.normal * -0.03;
    float3 worldPos = mul(instance.Model, float4(input.position.xyz, 1.0)).xyz;
    positionCS = mul(ViewProjection, float4(worldPos, 1.0));
}

void ShadowCast_PSMain() { }
