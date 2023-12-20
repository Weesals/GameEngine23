
SamplerState MinSampler : register(s4);
SamplerState MaxSampler : register(s5);
Texture2D<float4> Texture : register(t0);

cbuffer ConstantBuffer : register(b1) {
    float2 TextureSize;
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

float2 FirstPassPS(PSInput input) : SV_Target0 {
    float2 minmax = 0.0;
    minmax.x = Texture.SampleBias(MinSampler, input.uv, -1.0).r;
    minmax.y = 1.0 - Texture.SampleBias(MaxSampler, input.uv, -1.0).r;
    return minmax;
}
float2 HighZPassPS(PSInput input) : SV_Target0 {
    return Texture.SampleBias(MinSampler, input.uv, -1.0).rg;
}
