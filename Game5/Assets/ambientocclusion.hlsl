
SamplerState BilinearSampler : register(s1);
Texture2D<float4> SceneDepth : register(t0);
Texture2D<float4> HighZ : register(t1);

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
    float sceneDepth = SceneDepth[input.position.xy];
    float2 hiz = HighZ.SampleBias(BilinearSampler, input.uv, 5.0).rg;
    float depth = sceneDepth.r - hiz.r;
    depth *= 5.0;
    //depth *= saturate(1.0 - depth);
    return float4(0.0, 0.0, 0.0, saturate(depth) * 0.8);
}
