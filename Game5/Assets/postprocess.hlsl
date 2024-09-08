#include <common.hlsl>
#include <blur.hlsl>

SamplerState BilinearSampler : register(s1);
SamplerState BilinearClampedSampler : register(s6);
Texture2D<float4> SceneColor : register(t0);
Texture2D<float4> BloomChain : register(t1);

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

float3 ACESFilm(float3 x) {
    float a = 2.51f;
    float b = 0.03f;
    float c = 2.43f;
    float d = 0.59f;
    float e = 0.14f;
    return saturate((x*(a*x+b))/(x*(c*x+d)+e));
}
float3 Tonemap_Uchimura(float3 x, float P, float a, float m, float l, float c, float b) {
    // Uchimura 2017, "HDR theory and practice"
    // Math: https://www.desmos.com/calculator/gslcdxvipg
    // Source: https://www.slideshare.net/nikuque/hdr-theory-and-practicce-jp
    float l0 = ((P - m) * l) / a;
    float L0 = m - m / a;
    float L1 = m + (1.0 - m) / a;
    float S0 = m + l0;
    float S1 = m + a * l0;
    float C2 = (a * P) / (P - S1);
    float CP = -C2 / P;

    float3 w0 = 1.0 - smoothstep(0.0, m, x);
    float3 w2 = step(m + l0, x);
    float3 w1 = 1.0 - w0 - w2;

    float3 T = m * pow(x / m, c) + b;
    float3 S = P - (P - S1) * exp(CP * (x - S0));
    float3 L = m + a * (x - m);

    return T * w0 + L * w1 + S * w2;
}
float3 Tonemap_Uchimura(float3 x) {
    const float P = 1.0;  // max display brightness
    const float a = 1.0;  // contrast
    const float m = 0.22; // linear section start
    const float l = 0.4;  // linear section length
    const float c = 1.33; // black
    const float b = 0.0;  // pedestal
    return Tonemap_Uchimura(x, P, a, m, l, c, b);
}

float3 Evaluate(float3 x, float midIn, float midOut, float hdrMax) {
    float a = 1.2;  // contrast
    float d = 0.97; // shoulder

    float ad = a * d;
    float midi_pow_a  = pow(midIn, a);
    float midi_pow_ad = pow(midIn, ad);
    float hdrm_pow_a  = pow(hdrMax, a);
    float hdrm_pow_ad = pow(hdrMax, ad);
    float u = hdrm_pow_ad * midOut - midi_pow_ad * midOut;
    float v = midi_pow_ad * midOut;

    float b = -((-midi_pow_a + (midOut * (hdrm_pow_ad * midi_pow_a - hdrm_pow_a * v)) / u) / v);
    float c = (hdrm_pow_ad * midi_pow_a - hdrm_pow_a * v) / u;

    x = min(x, hdrMax);
    float3 z = pow(x, a);
    float3 y = z / (pow(z, d) * b + c);

    return y;
}


float4 PSMain(PSInput input) : SV_TARGET {
    float4 sceneColor = SceneColor.SampleLevel(BilinearSampler, input.uv, 0);
    //return float4(sceneColor.rgb / LuminanceFactor, 1);
    
    float4 blur = 0.0;
#if defined(ENABLEBLOOM) && ENABLEBLOOM
    //blur = float4(BloomChain.SampleLevel(BilinearClampedSampler, input.uv, 0.0).rgb, 1.0);
    blur = GaussianSampleLevel<2>(BloomChain, BilinearClampedSampler, input.uv, 1.0 / Resolution, 1.0, 1.0);
    blur /= 2.0;        // Roughly the series of 1.0 + 0.5 + 0.25 + 0.125
#endif
        
    sceneColor *= 1.0 / LuminanceFactor;
    //blur *= 1.0 / LuminanceFactor;
    
    const float Intensity = 0.0;
    //return float4(blur.rgb * 1.0, Intensity);
    sceneColor.rgb += blur.rgb;
    sceneColor.rgb = Tonemap_Uchimura(sceneColor.rgb * 1.2);
    //sceneColor.rgb = Evaluate(sceneColor.rgb, 0.3, 0.18, 1.0);
    input.uv -= 0.5;
    input.uv *= 2.0;
    //sceneColor.rgb = abs((1 - abs(input.uv.x)) * (1 - abs(input.uv.y)));
    //float d = saturate(1 - dot(input.uv, input.uv));
    //sceneColor.rgb = d * d;
    //sceneColor.rgb = exp(-dot(input.uv, input.uv));
    return float4(sceneColor.rgb, 1.0);
}
