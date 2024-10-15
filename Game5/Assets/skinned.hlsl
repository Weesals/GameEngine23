#include <retained.hlsl>
#include <lighting.hlsl>
#include <shadowreceive.hlsl>
#include <basepass.hlsl>

// Force Change
SamplerState BilinearSampler : register(s1);
Texture2D<float4> Texture : register(t0);

cbuffer SkinCB {
    matrix BoneTransforms[64];
}

struct VSInput {
    uint primitiveId : INSTANCE;
    float3 positionOS : POSITION;
    float3 normalOS : NORMAL;
    float2 uv : TEXCOORD0;
    uint4 boneIds : BLENDINDICES;
    float4 boneWeights : BLENDWEIGHT;
};
struct PSInput {
    uint primitiveId : SV_InstanceID;
    float4 positionCS : SV_POSITION;
    float3 normalVS : NORMAL;
    float3 viewPos : TEXCOORD1;
    float2 uv : TEXCOORD0;
    float3 velocity : VELOCITY;
};
struct PSOutput {
    float4 color : SV_Target0;
    float4 velocity : SV_Target1;
};

matrix GetSkinTransform(uint4 boneIds, float4 boneWeights) {
    matrix boneTransform = 0;
    for (int i = 0; i < 4; ++i) {
        boneTransform += BoneTransforms[boneIds[i]] * boneWeights[i];
    }
    return boneTransform;
}

PSInput VSMain(VSInput input) {
    PSInput result = (PSInput)0;

    InstanceData instance = GetInstanceData(input.primitiveId);
    result.primitiveId = input.primitiveId;

    float3 localPos = input.positionOS.xyz;
    float3 localNrm = input.normalOS.xyz;
    localPos = mul(GetSkinTransform(input.boneIds, input.boneWeights), float4(localPos, 1.0)).xyz;
    localNrm = mul(GetSkinTransform(input.boneIds, input.boneWeights), float4(localNrm, 0.0)).xyz;
    float3 worldPos = mul(instance.Model, float4(localPos, 1.0)).xyz;
    float3 worldNrm = mul(instance.Model, float4(localNrm, 0.0)).xyz;
    result.positionCS = mul(ViewProjection, float4(worldPos, 1.0));
    result.viewPos = mul(View, float4(worldPos, 1.0));
    result.normalVS = mul((float3x3)View, worldNrm);
    result.uv = input.uv;

    float3 prevPos = mul(instance.PreviousModel, float4(localPos, 1.0)).xyz;
    float4 prevPositionCS = mul(PreviousViewProjection, float4(prevPos, 1));
    result.velocity = result.positionCS.xyz / result.positionCS.w - prevPositionCS.xyz / prevPositionCS.w;
    // Add a slight amount to avoid velocity being 0 (special case)
    result.velocity.x += 0.0000001;

    return result;
}

void PSMain(PSInput input, out BasePassOutput result) {
    InstanceData instance = GetInstanceData(input.primitiveId);
    
    TemporalAdjust(input.uv);
    
    float4 tex = Texture.Sample(BilinearSampler, input.uv);
    PBRInput pbrInput = PBRDefault();
    pbrInput.Albedo = tex.rgb;
    pbrInput.Alpha = tex.a;
    pbrInput.Specular = 0.06;
    pbrInput.Roughness = 0.7;
    pbrInput.Emissive = instance.Highlight;
    pbrInput.Normal = normalize(input.normalVS);

    float3 shadowPos = ViewToShadow(input.viewPos);
    float shadow = ShadowMap.SampleCmpLevelZero(ShadowSampler, shadowPos.xy, shadowPos.z).r;
    //pbrInput.Occlusion *= shadow;
    
    result = PBROutput(pbrInput, normalize(input.viewPos));
    OutputVelocity(result, input.velocity);
    OutputSelected(result, instance.Selected);
}

struct ShadowCast_VSInput {
    uint primitiveId : INSTANCE;
    float3 positionOS : POSITION;
    float3 normalOS : NORMAL;
    uint4 boneIds : BLENDINDICES;
    float4 boneWeights : BLENDWEIGHT;
};
struct ShadowCast_PSInput {
    float4 positionCS : SV_POSITION;
};

ShadowCast_PSInput ShadowCast_VSMain(ShadowCast_VSInput input) {
    ShadowCast_PSInput result = (ShadowCast_PSInput)0;
    InstanceData instance = GetInstanceData(input.primitiveId);
    float3 worldPos = input.positionOS.xyz;
    float3 worldNrm = input.normalOS.xyz;
    worldPos = mul(GetSkinTransform(input.boneIds, input.boneWeights), float4(worldPos, 1.0)).xyz;
    worldNrm = mul(GetSkinTransform(input.boneIds, input.boneWeights), float4(worldNrm, 0.0)).xyz;
    worldPos = mul(instance.Model, float4(worldPos, 1.0)).xyz;
    worldNrm = mul(instance.Model, float4(worldNrm, 0.0)).xyz;
    worldPos.xyz += worldNrm * -0.02;
    result.positionCS = mul(ViewProjection, float4(worldPos, 1.0));
    return result;
}

void ShadowCast_PSMain(ShadowCast_PSInput input) { }
