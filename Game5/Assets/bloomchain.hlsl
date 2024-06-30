#include <common.hlsl>
#include <blur.hlsl>
#include <colorspace.hlsl>

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


float4 PSThreshold(PSInput input) : SV_Target {
    float4 sum = Texture.SampleLevel(BilinearClampedSampler, input.uv, 0) * (1.0 / LuminanceFactor);
    //sum.rgb = YCoCgToRGB(sum.rgb);
    sum -= 1.0;
    sum = max(sum, 0.0);
    sum.a = 1.0;
    //if (floor(dot(input.position.xy, float2(1, 50))) == floor(frac(Time) * 5.0 + 20 * 100)) sum = 100.0;
    return sum;
}
float4 PSDownsample(PSInput input) : SV_Target {
    input.uv = input.position.xy * (2.0 * TextureMipTexel);
    
    float4 sum = GaussianSample<3>(Texture, BilinearClampedSampler, input.uv, TextureMipTexel, 1.5);
    sum.a = 1.0;
    return sum;
}
float4 PSUpsample(PSInput input) : SV_Target {
    input.uv = input.position.xy * (0.5 * TextureMipTexel);
    
    float4 sum = GaussianSample<2>(Texture, BilinearClampedSampler, input.uv, TextureMipTexel * 0.5, 1);
    sum.a = 0.0;
    return sum;
}
