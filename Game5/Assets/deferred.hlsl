#include <common.hlsl>
#include <noise.hlsl>
#include <shadowreceive.hlsl>
#include <lighting.hlsl>
#include <volumetrics.hlsl>

static const float groundCloudShadowSampleCount = 2;

Texture2D<float4> SceneColor : register(t0);
Texture2D<float1> SceneDepth : register(t1);
Texture2D<float4> SceneAttri : register(t2);
Texture2D<float4> SceneAO : register(t3);

float2 ZBufferParams;
float4 ViewToProj;

struct VSInput {
    float4 position : POSITION;
    float2 uv : TEXCOORD0;
};

struct PSInput {
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
};

PSInput VSMain(VSInput input) {
    PSInput result;
    result.position = input.position;
    result.uv = input.uv;
    return result;
}

float DepthToLinear(float d) {
    return 1.0 / (ZBufferParams.x * d + ZBufferParams.y);
}

float4 PSMain(PSInput input) : SV_Target {
    float2 viewUv = input.uv; viewUv.y = 1.0 - viewUv.y;
    float deviceDepth = SceneDepth.Sample(BilinearSampler, input.uv).x;
    
    float3 viewDir = (float3(viewUv * ViewToProj.xy + ViewToProj.zw, 1.0));
    //viewDir = SceneColor.Sample(BilinearSampler, input.uv).rgb * 2.0 - 1.0;
    float depth = DepthToLinear(deviceDepth);
    float3 viewPos = viewDir * depth;
    float3 shadowPos = ViewToShadow(viewPos);
    //return float4(shadowPos, 1);
    float shadow = ShadowMap.SampleCmpLevelZero(ShadowSampler, shadowPos.xy, shadowPos.z).r;
    //return float4(shadow.xxx, 1);
    
    if (deviceDepth < 0.999) {
        float4 farCS = float4((viewUv * 2.0 - 1.0), deviceDepth, 1.0);
        farCS = mul(InvViewProjection, farCS);
        farCS.xyz /= farCS.w;
        float3 groundPos = farCS.xyz;
        //return float4(frac(groundPos / 10), 1);
        
        float3 midCloud = groundPos + _WorldSpaceLightDir0 *
            ((cloudMinY + cloudMaxY) / 2.0 - groundPos.y) / _WorldSpaceLightDir0.y;
        PrimeClouds(midCloud, _WorldSpaceLightDir0);
        
        float jitter = IGN(input.position.xy);
        shadow *= GetGroundShadow(groundPos, jitter, groundCloudShadowSampleCount);
    }

    
    float3 Albedo = SceneColor.Sample(BilinearSampler, input.uv).rgb;
    float4 Attributes = SceneAttri.Sample(BilinearSampler, input.uv);
    float3 normal = OctahedralDecode(Attributes.xy * 2.0 - 1.0);
    //return float4(Albedo, 1);
    //normal = pow(normal, 0.45);
    //normal = normal * 2.0 - 1.0;
    //normal = normalize(normal);
    //return float4(Albedo, 1);
    //normal = OctahedralDecode(OctahedralEncode(normal));
    //normal = OctahedralDecode(OctahedralEncode(normal));
    //normal = OctahedralDecode(OctahedralEncode(normal));
    //normal = OctahedralDecode(OctahedralEncode(normal));
    //normal = OctahedralDecode(OctahedralEncode(normal));
    //return float4(normal * 0.5 + 0.5, 1);
    //return float4(abs(Albedo - frac(viewDir)) * 10.0, 1.0);
    //return float4(frac(viewDir), 1.0);
    //viewDir = Albedo * 2.0 - 1.0;
    //Albedo = 1.0;
        
    float specular = 0.06;
    float metallic = 0.0;
    float roughness = Attributes.z;
    float occlusion = Attributes.w;
    
    float4 AmbientOcclusion = SceneAO.Sample(BilinearSampler, input.uv);
    occlusion *= AmbientOcclusion.a;
    float3 bentNormal = OctahedralDecode(AmbientOcclusion.xy * 2.0 - 1.0);
    
    //if(input.uv.x < 0.5) return float4(bentNormal * 0.5 + 0.5, 1.0);
    //return float4(normal * 0.5 + 0.5, 1.0);
    bentNormal = normal;
    
    // The light
    float3 o = ComputeLight(
        Albedo,
        specular,
        normal,
        roughness,
        _LightColor0 * shadow,
        _ViewSpaceLightDir0,
        -viewDir,
        metallic
    );
    
    // Indirect
    o += ComputeIndiret(Albedo, specular,
        bentNormal, roughness, metallic, -viewDir) * occlusion;
        
    //o.rgb *= 1.0f - input.Emissive.a;
    //o.rgb += input.Emissive.rgb;
    //o.rgb = pow(o.rgb, 4) * 5.0;
    
    //return float4(Albedo, 1);
        
    return float4(o, 1.0);
}
