#include <common.hlsl>

SamplerState BilinearClampedSampler : register(s6);
Texture2D<float4> Texture : register(t0);
float2 TextureSize;
float2 TextureMipTexel;

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

float GaussianWeight(float2 delta, float R) {
    return exp2(-dot(delta, delta) * (1.0 / (R * R)));
}
float3 GetGaussianSample(float2 delta, float R) {
    float4 g = float4(
        GaussianWeight(delta + float2(-0.5, -0.5), R),
        GaussianWeight(delta + float2(+0.5, -0.5), R),
        GaussianWeight(delta + float2(-0.5, +0.5), R),
        GaussianWeight(delta + float2(+0.5, +0.5), R)
    );
    float weightSum = dot(g, 1);
    float2 delta2 = float2(dot(g, float4(1, 0, 1, 0)), dot(g, float4(1, 1, 0, 0)));
    delta += 0.5 - delta2 / weightSum;
    return float3(delta, weightSum);
}

float4 PSThreshold(PSInput input) : SV_Target {
    float4 sum = Texture.SampleLevel(BilinearClampedSampler, input.uv, 0) * (1.0 / LuminanceFactor);
    sum -= 1.0;
    sum = max(sum, 0.0);
    sum.a = 1.0;
    //if (floor(dot(input.position.xy, float2(1, 50))) == floor(frac(Time) * 5.0 + 20 * 100)) sum = 100.0;
    return sum;
}
float4 PSDownsample(PSInput input) : SV_Target {
    input.uv = input.position.xy * (2.0 * TextureMipTexel);
        
    float4 sum = 0;
    const int Count = 2;
    for (int y = 0; y < Count; ++y) {
        for (int x = 0; x < Count; ++x) {
            float3 weight3 = GetGaussianSample(2.0 * float2(x, y) - (Count - 1.0), 1.0);
            float2 localUv = input.uv + weight3.xy * TextureMipTexel;
            float4 sample = float4(Texture.Sample(BilinearClampedSampler, localUv).rgb, 1);
            sample *= weight3.z;
            sum += sample;
        }
    }
    sum /= sum.a;
    sum.a = 1.0;
    return sum;
}
float4 PSUpsample(PSInput input) : SV_Target {
    input.uv = input.position.xy * (0.5 * TextureMipTexel);
    
    float4 sum = 0;
    const int Count = 3;
    for (int y = 0; y < Count; ++y) {
        for (int x = 0; x < Count; ++x) {
            float3 weight3 = GetGaussianSample(float2(x, y) - (Count - 1.0) / 2.0, 1.0);
            float4 sample = float4(Texture.Sample(BilinearClampedSampler, input.uv + weight3.xy * TextureMipTexel).rgb, 1);
            sum += sample * weight3.z;
        }
    }
    sum /= sum.a;
    sum.a = 0.0;
    return sum;
}
