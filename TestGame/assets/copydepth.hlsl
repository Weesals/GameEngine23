
SamplerState PointSampler : register(s1);
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

float PSMain(PSInput input) : SV_Depth {
    float2 dd = float2(ddx(input.uv.x), ddy(input.uv.y));
#if ODD
    int count = 3;
#else
    int count = 2;
#endif
    float ret = 1.0;
    [unroll]
    for (int y = 0; y < count; ++y) {
        [unroll]
        for (int x = 0; x < count; ++x) {
            float2 dduv = 1.95 * (float2(x, y) / (count - 1.0) - 0.5);
            dduv *= dd;
            float d = Texture.SampleBias(PointSampler, input.uv + dd * dduv, -1.0).r;
            ret = min(ret, d);
        }
    }
    return ret;
}
