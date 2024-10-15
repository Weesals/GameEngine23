#include <retained.hlsl>
#include <lighting.hlsl>
#include <shadowreceive.hlsl>
#include <basepass.hlsl>

SamplerState BilinearSampler : register(s1);
Texture2D<float4> Texture : register(t0);

struct VSInput {
    uint primitiveId : SV_InstanceID;
    float4 position : POSITION;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD0;
};

struct PSInput {
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
    float3 worldNrm = input.normal.xyz;//mul(instance.Model, float4(input.normal.xyz, 0.0)).xyz;
    result.position = mul(ViewProjection, float4(worldPos, 1.0));
    result.viewPos = mul(View, float4(worldPos, 1.0)).xyz;
    result.normal = mul((float3x3)View, worldNrm);
    result.uv = input.uv;
        
    return result;
}

void PSMain(PSInput input
    , out float4 albedo : SV_Target0
    , out float4 normalDepth : SV_Target1
) {
    InstanceData instance = GetInstanceData(input.primitiveId);
    
    TemporalAdjust(input.uv);
    
    float4 tex = Texture.Sample(BilinearSampler, input.uv);
    PBRInput pbrInput = PBRDefault();
    pbrInput.Albedo = tex.rgb;
    pbrInput.Alpha = 1.0f;
    pbrInput.Specular = 0.06;
    pbrInput.Roughness = 0.7;
    pbrInput.Emissive = instance.Highlight;
    pbrInput.Normal = normalize(input.normal);

    albedo = float4(pbrInput.Albedo.rgb, 1);
    normalDepth = float4(pbrInput.Normal.rgb * 0.5 + 0.5, input.position.z);
}
