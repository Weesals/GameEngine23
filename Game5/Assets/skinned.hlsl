#include <retained.hlsl>
#include <lighting.hlsl>
#include <shadowreceive.hlsl>
#include <basepass.hlsl>

#include <module_common.hlsl>
#include <module_skinned.hlsl>
#include <module_retained.hlsl>
#include <module_temporal.hlsl>
// Force Change
SamplerState BilinearSampler : register(s1);
Texture2D<float4> Texture : register(t0);

using ShaderModule =
    Module<ModuleCommon>
    ::Then<ModuleObject>
    ::Then<ModuleVertexNormals>
    ::Then<ModuleSkinned>
    ::Then<ModuleRetained>
    ::Then<ModuleClipSpace>
    ::Then<ModuleVelocity>
    ;
        
struct VSInput : ShaderModule::VSInput {
    float2 uv : TEXCOORD0;
};
struct PSInput : ShaderModule::PSInput {
    uint primitiveId : SV_InstanceID;
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 viewPos : TEXCOORD1;
    float3 normal : NORMAL;
    //float2 velocity : VELOCITY;
};
struct PSOutput : ShaderModule::PSOutput {
    float4 color : SV_Target0;
};

PSInput VSMain(VSInput input) {
    ShaderModule module = (ShaderModule)0;
    module.SetupVertexIntermediates(input);
    PSInput result = (PSInput)0;
    InstanceData instance = module.GetInstanceData();
    result.primitiveId = input.primitiveId;
    
    float3 worldPos = module.GetWorldPosition();
    float3 worldNrm = module.GetWorldNormal();
    result.position = module.GetClipPosition();
    result.viewPos = module.GetViewPosition();
    result.normal = mul(View, float4(worldNrm, 0.0)).xyz;
    result.uv = input.uv;

    result.velocity = module.GetClipVelocity();

    return result;
}

void PSMain(PSInput input, out BasePassOutput result) {
    ShaderModule module = (ShaderModule)0;
    module.SetupPixelIntermediates(input);

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

struct ShadowCast_PSInput {
    float4 position : SV_POSITION;
    float3 normal : NORMAL;
};

template<class ModuleBase> struct ModuleNormalBias : ModuleBase {
    using VSInput = typename ModuleBase::VSInput;
    void SetupVertexIntermediates(VSInput input) {
        ModuleBase::SetupVertexIntermediates(input);
        ModuleBase::vertexPosition.xyz += input.normal * -0.03;
    }
};


using ShadowModule =
    Module<ModuleCommon>
    ::Then<ModuleObject>
    ::Then<ModuleVertexNormals>
    ::Then<ModuleNormalBias>
    ::Then<ModuleSkinned>
    ::Then<ModuleRetained>
    ::Then<ModuleClipSpace>
    ;

ShadowCast_PSInput ShadowCast_VSMain(ShadowModule::VSInput input) {
    ShadowModule module = (ShadowModule)0;
    module.SetupVertexIntermediates(input);
    ShadowCast_PSInput result = (ShadowCast_PSInput)0;
    result.position = module.GetClipPosition();
    return result;
}

void ShadowCast_PSMain(ShadowCast_PSInput input) {
    //return 1.0;
}
