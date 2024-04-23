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

cbuffer DeferredCB : register(b2) {
    float2 ZBufferParams;
    float4 ViewToProj;
}

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

float PCFSample(Texture2D<float> tex, SamplerComparisonState samp, float3 pos, float2 size) {
    //return ShadowMap.SampleCmpLevelZero(ShadowSampler, pos.xy, pos.z).r;
    const float2 offset = float2(0.5, 0.5);
    float2 uv = pos.xy * size;
    float2 texel = 1.0 / size;
    float2 base_uv = (floor(uv) - offset) * texel;
    float2 st = frac(uv);

    float2 uw = float2(3 - 2 * st.x, 1 + 2 * st.x);
    float2 u = float2((2 - st.x) / uw.x - 1, (st.x) / uw.y + 1);
    u = u * texel.x + base_uv.x;

    float2 vw = float2(3 - 2 * st.y, 1 + 2 * st.y);
    float2 v = float2((2 - st.y) / vw.x - 1, (st.y) / vw.y + 1);
    v = v * texel.y + base_uv.y;

    float shadow = 0.0;
    shadow += uw.x * vw.x * ShadowMap.SampleCmpLevelZero(ShadowSampler, float2(u.x, v.x), pos.z);
    shadow += uw.y * vw.x * ShadowMap.SampleCmpLevelZero(ShadowSampler, float2(u.y, v.x), pos.z);
    shadow += uw.x * vw.y * ShadowMap.SampleCmpLevelZero(ShadowSampler, float2(u.x, v.y), pos.z);
    shadow += uw.y * vw.y * ShadowMap.SampleCmpLevelZero(ShadowSampler, float2(u.y, v.y), pos.z);
    shadow /= 16;
    return shadow;
}    
float PCFSample2(Texture2D<float> tex, SamplerComparisonState samp, float3 pos, float2 size) {
    float2 texel = 1.0 / size;
    float2 uv = pos.xy * size;
    float2 ctr = floor(uv);
    float2 l = (uv - ctr);
    float2 o0 = 0 + pow(0 + l, 2);//saturate(l * 2 - 1.0);
    float2 o1 = 1 - pow(1 - l, 2);//saturate(l * 2 - 0.0);
    float2 p0 = ctr - 1.0 + (0 + o0) * 0.5;
    float2 p1 = ctr + 1.0 - (1 - o1) * 0.5;
    p0 *= texel; p1 *= texel;
    float shadow = 0.0;
    float2 w0 = 1.0 - o0 * 0.5;
    float2 w1 = 0.5 + o1 * 0.5;
    shadow += w0.x * w0.y * ShadowMap.SampleCmpLevelZero(ShadowSampler, float2(p0.x, p0.y), pos.z);
    shadow += w1.x * w0.y * ShadowMap.SampleCmpLevelZero(ShadowSampler, float2(p1.x, p0.y), pos.z);
    shadow += w0.x * w1.y * ShadowMap.SampleCmpLevelZero(ShadowSampler, float2(p0.x, p1.y), pos.z);
    shadow += w1.x * w1.y * ShadowMap.SampleCmpLevelZero(ShadowSampler, float2(p1.x, p1.y), pos.z);
    shadow /= (w0.x * w0.y + w0.x * w1.y + w1.x * w0.y + w1.x * w1.y);
    return shadow;
}

float4 PSMain(PSInput input) : SV_Target {
    float2 viewUv = input.uv; viewUv.y = 1.0 - viewUv.y;
    float deviceDepth = SceneDepth.Sample(BilinearSampler, input.uv).x;
    
    float3 viewDir = (float3(viewUv * ViewToProj.xy + ViewToProj.zw, 1.0));
    //viewDir = SceneColor.Sample(BilinearSampler, input.uv).rgb * 2.0 - 1.0;
    float depth = DepthToLinear(deviceDepth);
    
    float3 Albedo = SceneColor.Sample(BilinearSampler, input.uv).rgb * (1.0 / LuminanceFactor);
    //return float4(Albedo * LuminanceFactor, 1);
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
    float3 bentNormal = normal;//OctahedralDecode(AmbientOcclusion.xy * 2.0 - 1.0);
    occlusion *= AmbientOcclusion.a;
    
    float3 o = 0.0;
    
    float NdL = dot(normal, _ViewSpaceLightDir0);
    [branch]
    if (NdL > 0) {
        float shadow = 1.0;
        float3 viewPos = viewDir * depth;
        float3 shadowPos = ViewToShadow(viewPos);
        //return float4(shadowPos, 1);
        //shadow = ShadowMap.SampleCmpLevelZero(ShadowSampler, shadowPos.xy, shadowPos.z);
        shadow = PCFSample2(ShadowMap, ShadowSampler, shadowPos, 512);
        
        [branch]
        if (deviceDepth < 0.999 && shadow > 0.0) {
            float4 farCS = float4((viewUv * 2.0 - 1.0), deviceDepth, 1.0);
            farCS = mul(InvViewProjection, farCS);
            farCS.xyz /= farCS.w;
            float3 groundPos = farCS.xyz;
            //return float4(frac(groundPos), 1);
        
            float3 midCloud = groundPos + _WorldSpaceLightDir0 *
                ((cloudMinY + cloudMaxY) / 2.0 - groundPos.y) / _WorldSpaceLightDir0.y;
            PrimeClouds(midCloud, _WorldSpaceLightDir0);
        
            float jitter = IGN(input.position.xy);
            shadow *= GetGroundShadow(groundPos, jitter, groundCloudShadowSampleCount);
        }
        
        // The light
        o = ComputeLight(
            Albedo,
            specular,
            normal,
            roughness,
            _LightColor0,
            _ViewSpaceLightDir0,
            -viewDir,
            metallic,
            shadow
        );
    }
    
    // Indirect
    o += ComputeIndiret(Albedo, specular,
        bentNormal, roughness, metallic, -viewDir) * occlusion;
    
    o *= LuminanceFactor;
        
    return float4(o, 1.0);
}
