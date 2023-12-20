
SamplerState BilinearSampler : register(s1);
Texture2D<float4> Texture : register(t0);

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
    return Texture.Sample(BilinearSampler, input.uv);
}
