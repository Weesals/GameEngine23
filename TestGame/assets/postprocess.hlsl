
SamplerState BilinearSampler : register(s0);
SamplerState AnisotropicSampler : register(s3);
Texture2D<float4> BloomChain : register(t0);

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

float4 PSMain(PSInput input) : SV_TARGET {
    float4 blur = 0.0;
    for (int i = 0; i < 3; ++i) {
        //blur *= 1.2;
        blur += float4(BloomChain.SampleBias(BilinearSampler, input.uv, 1.0 + i * 2.0 + 0.5).rgb, 1.0);
    }
    //blur /= blur.a;
    blur /= 2.0;        // Roughly the series of 1.0 + 0.5 + 0.25 + 0.125
    const float Intensity = 0.0;
    return float4(blur.rgb * 1.0, Intensity);
}
