#ifndef __BLUR_HLSL__
#define __BLUR_HLSL__

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

template<int Count = 2>
void ComputeGaussianSamples(out float3 samples[Count * Count], float radius = 1.0) {
    float4 sum = 0;
    float sumWeight = 0;
    [unroll]
    for (int y = 0; y < Count; ++y) {
        [unroll]
        for (int x = 0; x < Count; ++x) {
            float3 weight3 = GetGaussianSample(2.0 * float2(x, y) - (Count - 1.0), radius);
            if (weight3.z < 1.0) weight3.z = 0.0;
            samples[x + y * Count] = weight3;
            sumWeight += weight3.z;
        }
    }
    for (int s = 0; s < Count * Count; ++s) samples[s].z /= sumWeight;
}

template<int Count = 2>
float4 GaussianSample(Texture2D<float4> tex, SamplerState samp, float2 uv, float2 texel, float radius) {
    const int SampleCount = Count * Count;
    float3 weights[SampleCount];
    ComputeGaussianSamples<Count>(weights, radius);
    
    float4 sum = 0;
    for (int s = 0; s < SampleCount; ++s) {
        sum += tex.Sample(samp, uv + weights[s].xy * texel) * weights[s].z;
    }
    return sum;
    
    /*float sumWeight = 0;
    for (int y = 0; y < Count; ++y) {
        for (int x = 0; x < Count; ++x) {
            float3 weight3 = GetGaussianSample(2.0 * float2(x, y) - (Count - 1.0), 1.0);
            float2 localUv = uv + weight3.xy * texel;
            sum += tex.Sample(samp, localUv) * weight3.z;
            sumWeight += weight3.z;
        }
    }
    sum /= sumWeight;
    return sum;*/
}
template<int Count = 2>
float4 GaussianSampleLevel(Texture2D<float4> tex, SamplerState samp, float2 uv, float2 texel, float radius = 1.0, float level = 0.0) {
    float4 sum = 0;
    
    const int SampleCount = Count * Count;
    float3 weights[SampleCount];
    ComputeGaussianSamples<Count>(weights, radius);
    for (int s = 0; s < SampleCount; ++s) {
        sum += tex.SampleLevel(samp, uv + weights[s].xy * texel, level) * weights[s].z;
    }
    return sum;
}
#endif
