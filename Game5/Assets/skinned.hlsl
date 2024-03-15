#include <retained.hlsl>
#include <lighting.hlsl>
#include <shadowreceive.hlsl>

#include <module_common.hlsl>
#include <module_skinned.hlsl>
#include <module_retained.hlsl>
#include <module_temporal.hlsl>

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

void PSMain(PSInput input, out PSOutput result) {
    ShaderModule module = (ShaderModule)0;
    module.SetupPixelIntermediates(input);
    result = (PSOutput)0;

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

    result.color = float4(o, tex.a);
    //result.velocity = float4(input.velocity, instance.Selected, 1);
    //module.PixelOutput(input, result);
    result.velocity.xy = module.GetClipVelocity(input);
    result.velocity.z = instance.Selected;
    //OutColor.rg = OutVelocity.rg * 10.0 + 0.5;
}

//#include "include/shadowcast.hlsl"

struct ShadowCast_PSInput {
    float4 position : SV_POSITION;
};

using ShadowModule =
    Module<ModuleCommon>
    ::Then<ModuleObject>
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
