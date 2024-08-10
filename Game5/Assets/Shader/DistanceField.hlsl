
SamplerState PointSampler : register(s0);
SamplerState BilinearSampler : register(s1);
Texture2D<float4> Texture : register(t0);
Texture2D<float4> SDF : register(t1);
Texture2D<float4> Mask : register(t2);

float Spread;
float2 Resolution;

struct VSInput {
    float4 position : POSITION;
};

struct PSInput {
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
};

PSInput VSMain(VSInput input) {
    PSInput result;

    result.position = float4(input.position.xy * 2.0 - 1.0, 0.5, 1);
    result.position.y *= -1;
    result.uv = input.position.xy;
    return result;
}

float4 PSSeed(PSInput input) : SV_TARGET {
    float2 duv = float2(ddx(input.uv.x), ddy(input.uv.y));
    float4 ctr = Texture.Sample(PointSampler, input.uv);
    if (ctr.a > 0.5) {
        float4 o = min(
            min(
                Texture.Sample(PointSampler, input.uv + duv * float2(0, +1)),
                Texture.Sample(PointSampler, input.uv + duv * float2(0, -1))
            ), min(
                Texture.Sample(PointSampler, input.uv + duv * float2(+1, 0)),
                Texture.Sample(PointSampler, input.uv + duv * float2(-1, 0))
            )
        );
        if (o.a < 0.5) return float4(input.uv, 0, 1);
    }
    return float4(frac(input.uv + 0.5), 0, 1);
}

float4 PSSpread(PSInput input) : SV_TARGET {
    float4 best = float4(10000.5, 10000.5, 0, 0);
    float bestL2 = dot(best.xy, best.xy);
    float2 duv = float2(ddx(input.uv.x), ddy(input.uv.y)) * Spread;
    for (int y = -1; y <= 1; ++y) {
        for (int x = -1; x <= 1; ++x) {
            float2 sampleUv = input.uv + float2(x, y) * duv;
            float2 delta = Texture.Sample(PointSampler, sampleUv).xy - input.uv;
            float deltaL2 = dot(delta, delta);
            if (deltaL2 < bestL2) {
                bestL2 = deltaL2;
                best.xy = delta.xy;
            }
        }
    }
    best.xy += input.uv;
    best.xy = frac(best.xy);
    return best;
}

float4 PSApply(PSInput input) : SV_TARGET {
    float4 m = Mask.Sample(PointSampler, input.uv);
    float2 uvSample = SDF.Sample(PointSampler, input.uv).xy;
    float2 uv = m.a > 0.5 ? input.uv : uvSample;
    float4 r = Texture.Sample(PointSampler, uv);
#if APPLYGRADIENT
    r.a = 0.5 + distance(input.uv, uvSample) * (m.a > 0.5 ? 1.0 : -1.0) * 20.0;
#endif
    return r;
}
