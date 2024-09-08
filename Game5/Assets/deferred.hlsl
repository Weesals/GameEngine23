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

SamplerState MinSampler : register(s4);
SamplerState PointSamplerClamped : register(s7);

cbuffer DeferredCB : register(b2) {
    float2 ZBufferParams;
    float4 ViewToProj;
    matrix Projection;
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
    float2 px = pos.xy * size + 0.5;
    float2 ctr = floor(px);
    float2 l = px - ctr;
    float2 w0 = 1.0 - pow(0 + l, 2) * 0.5;
    float2 w1 = 1.0 - pow(1 - l, 2) * 0.5;
    float2 texel = 1.0 / size;
    float2 p0 = ctr * texel - w0 * texel;
    float2 p1 = ctr * texel + w1 * texel;
    float4 weights = float4(w0.x * w0.y, w1.x * w0.y, w0.x * w1.y, w1.x * w1.y);
    float4 samples = float4(
        ShadowMap.SampleCmpLevelZero(ShadowSampler, float2(p0.x, p0.y), pos.z),
        ShadowMap.SampleCmpLevelZero(ShadowSampler, float2(p1.x, p0.y), pos.z),
        ShadowMap.SampleCmpLevelZero(ShadowSampler, float2(p0.x, p1.y), pos.z),
        ShadowMap.SampleCmpLevelZero(ShadowSampler, float2(p1.x, p1.y), pos.z)
    );
    float shadow = dot(weights, samples) / dot(weights, 1);

    float2 minUv = float2(
        dot(samples.xz, 1) < dot(samples.yw, 1) ? p0.x : p1.x,
        dot(samples.xy, 1) < dot(samples.zw, 1) ? p0.y : p1.y
    );

    float minDst = ShadowMap.Sample(MinSampler, minUv);
    float fringe = 10 * abs(pos.z - minDst);
    shadow = saturate((shadow - 0.5f) / saturate(fringe) + 0.5f);

    return shadow;
}
float PCFSample3(Texture2D<float> tex, SamplerComparisonState samp, float3 pos, float2 size) {
    float2 px = pos.xy * size;
    float2 ctr = floor(px);
    float2 l = px - ctr;
    float2 w0 = 1.0 - pow(0 + l, 2) * 0.5;
    float2 w1 = 1.0 - pow(1 - l, 2) * 0.5;
    float2 texel = 1.0 / size;
    float2 p[3] = {
        ctr * texel - (1.0 + w0) * texel,
        ctr * texel,
        ctr * texel + (1.0 + w1) * texel
    };
    float2 w[3] = {
        w0, float2(1, 1), w1
    };
    float shadow = 0.0;
    float totWeight = 0;
    float2 minUv = 0;
    float minShad = 1;
    for(int y = 0; y < 3; ++y) {
        for(int x = 0; x < 3; ++x) {
            float weight = w[x].x * w[y].y;
            float2 uv = float2(p[x].x, p[y].y);
            float compareZ = pos.z - 0.002f;//(x == 1 && y == 1 ? 0.0f : 0.002f);
            float sample = ShadowMap.SampleCmpLevelZero(ShadowSampler, uv, compareZ);
            shadow += weight * sample;
            totWeight += weight;
            if (sample < minShad) {
                minShad = sample;
                minUv = uv;
            }
        }
    }
    shadow /= totWeight;

    float kernelSize = 50 * pos.z;

    float minDst = ShadowMap.Sample(MinSampler, minUv);
    float shadowBlend = kernelSize * abs(pos.z - minDst);
    shadow = saturate((shadow - 0.5f) / saturate(shadowBlend) + 0.5f);

    return shadow;
}

float4 PSMain(PSInput input) : SV_Target {
    float2 viewUv = input.uv; viewUv.y = 1.0 - viewUv.y;
    float deviceDepth = SceneDepth.Sample(BilinearSampler, input.uv).x;
    
    float3 viewDir = (float3(viewUv * ViewToProj.xy + ViewToProj.zw, 1.0));
    float depth = DepthToLinear(deviceDepth);
    
    float3 Albedo = SceneColor.Sample(BilinearSampler, input.uv).rgb * (1.0 / LuminanceFactor);
    //return float4(Albedo * LuminanceFactor, 1);
    float4 Attributes = SceneAttri.Sample(BilinearSampler, input.uv);
    float3 normal = -OctahedralDecode(Attributes.xy * 2.0 - 1.0);
    //return float4(normal * 0.5 + 0.5, 1) * LuminanceFactor;
        
    float specular = 0.06;
    float metallic = 0.0;//Attributes.z * rcp(1024.0);
    float roughness = frac(Attributes.z) * 2.0;
    float occlusion = Attributes.w;
    
    float4 AmbientOcclusion = SceneAO.Sample(BilinearSampler, input.uv);
    float3 bentNormal = normal;//OctahedralDecode(AmbientOcclusion.xy * 2.0 - 1.0);
#if ENABLEAO
    occlusion *= AmbientOcclusion.a;
#endif
    
    float3 o = 0.0;
    
    float NdL = dot(normal, _ViewSpaceLightDir0);
    [branch]
    if (NdL > 0) {
        float shadow = 1.0;
        
        float3 viewPos = viewDir * depth;
        float3 shadowPos = ViewToShadow(viewPos);
        //shadow = ShadowMap.SampleCmpLevelZero(ShadowSampler, shadowPos.xy, shadowPos.z);
        shadow = PCFSample2(ShadowMap, ShadowSampler, shadowPos, 512);
        float ssShadThick = depth * 0.01;     //1.08
        float3 ssShadPos = viewPos + _ViewSpaceLightDir0 * (depth * 0.02);
        float4 ssVPos = mul(Projection, float4(ssShadPos, 1));
        ssVPos.xyz /= ssVPos.w;
        ssVPos.y = -ssVPos.y;
        float jitter = IGN(input.position.xy);
        const int SSShadowCount = 3;
        [unroll]
        for(int i = 0; i < SSShadowCount; ++i) {
            float3 samplePos = float3(ssVPos.xy * 0.5 + 0.5, ssVPos.z);
            samplePos = lerp(float3(input.uv, deviceDepth), samplePos, (jitter + i) / SSShadowCount);
            samplePos.z = DepthToLinear(samplePos.z);
            float newDepth = DepthToLinear(SceneDepth.Sample(PointSamplerClamped, samplePos.xy));
            if (newDepth < samplePos.z && newDepth > samplePos.z - ssShadThick) {
                shadow = 0;
                //return float4(1, 0, 0, 1) * LuminanceFactor;
                break;
            }
        }
        //return float4(newDepth * 0.1, ssVPos.z * 0.1, 0, 1);
        
#if ENABLEFOG
        [branch]
        if (deviceDepth < 0.999 && shadow > 0.01) {
            float4 farCS = float4((viewUv * 2.0 - 1.0), deviceDepth, 1.0);
            farCS = mul(InvViewProjection, farCS);
            float3 groundPos = farCS.xyz / farCS.w;
        
            GlobalMipBias = 2.0;
            PrimeClouds(groundPos, _WorldSpaceLightDir0);
        
            float jitter = IGN(input.position.xy);
            shadow *= GetGroundShadow(groundPos, jitter, groundCloudShadowSampleCount);
        }
#endif
        
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
