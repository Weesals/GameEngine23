
SamplerState BilinearClampedSampler : register(s6);
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
    float2 dudx = ddx(input.uv);
    float2 dudy = ddy(input.uv);
    float4 sum = 0;
    const int Count = 4;
    for (int y = 0; y < Count; ++y) {
        for (int x = 0; x < Count; ++x) {
            float2 uvoff = 2.0 * (float2(x, y) - (Count - 1.0) / 2.0) * float2(dudx.x, dudy.y);
            sum += Texture.SampleBias(BilinearClampedSampler, input.uv + uvoff, -1.0);
        }
    }
    sum /= Count * Count;
#if CopyPass
    sum = Texture.SampleLevel(BilinearClampedSampler, input.uv, 0);
    sum -= 1.0;
    sum = max(sum, 0.0);
#endif
    return sum;
}
