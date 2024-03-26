#ifndef __BASEPASS__
#define __BASEPASS__

#include <common.hlsl>
#include <lighting.hlsl>

struct BasePassOutput {
    float4 BaseColor : SV_Target0;
    float4 Velocity : SV_Target1;
    float4 Attributes : SV_Target2;
};

struct PBRInput {
    float Alpha;
    float3 Albedo;
    float3 Specular;
    float3 Normal;
    float Roughness;
    float Metallic;
    float Occlusion;
    float4 Emissive;
};

PBRInput PBRDefault() {
    PBRInput params = (PBRInput)0;
    params.Roughness = 1.0;
    params.Occlusion = 1.0;
    params.Normal = float3(0, 1, 0);
    params.Emissive = float4(0, 0, 0, 0);
    return params;
}

BasePassOutput PBROutput(PBRInput input, float3 viewDir = float3(0, 0, 1)) {
    BasePassOutput output;
        
    // The light
    float3 o = ComputeLight(
        input.Albedo,
        input.Specular,
        input.Normal,
        input.Roughness,
        _LightColor0 * input.Occlusion,
        _ViewSpaceLightDir0,
        -viewDir,
        input.Metallic
    );
    
    // Indirect
    o += ComputeIndiret(input.Albedo, input.Specular,
        input.Normal, input.Roughness, input.Metallic, -viewDir);
    
    o.rgb *= 1.0f - input.Emissive.a;
    o.rgb += input.Emissive.rgb;
    //o.rgb = pow(o.rgb, 4) * 5.0;

    output = (BasePassOutput)0;
    output.BaseColor = float4(input.Albedo, input.Alpha);
    output.Attributes = float4(OctahedralEncode(input.Normal) * 0.5 + 0.5, input.Roughness, input.Occlusion);
    return output;
}

void OutputVelocity(inout BasePassOutput result, float2 velocity) {
    result.Velocity.xy = velocity * 16.0;
}
void OutputSelected(inout BasePassOutput result, float selected) {
    result.Velocity.z = selected;
}

#endif
