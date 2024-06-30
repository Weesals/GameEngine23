#include <colorspace.hlsl>

matrix PreviousVP;
matrix CurrentVP;
matrix CurToPrevVP;
matrix PrevToCurVP;
float2 TemporalJitter;
float2 Resolution;
float TemporalFrame;

SamplerState PointSampler : register(s0);
SamplerState BilinearClampedSampler : register(s6);
SamplerState MinSampler : register(s4);
SamplerState MaxSampler : register(s5);

Texture2D<float4> CurrentFrame : register(t0);
Texture2D<float4> PreviousFrame : register(t1);

Texture2D<float4> SceneDepth : register(t2);
Texture2D<float4> SceneVelId : register(t3);

cbuffer TemporalCB : register(b0) {
    float RandomX, RandomY, TemporalFrac, TemporalFrac2, TemporalFrac3;
}

struct VSInput {
    float4 position : POSITION;
    float2 uv : TEXCOORD0;
};

struct PSInput {
    float4 position : SV_POSITION;
    float4 uv : TEXCOORD0;
};

PSInput VSMain(VSInput input) {
    PSInput result;

    result.position = input.position;
    result.uv = input.uv.xyxy;
    float2 sign = float2(TemporalJitter.x < 0 ? -1 : +1, TemporalJitter.y < 0 ? +1 : -1);
    result.uv.zw += 1.0 / Resolution * sign * 0.5;
    return result;
}

float Permute(float v) {
    return frac(v * TemporalFrac2 - TemporalFrac3);
}
float4 PSMain(PSInput input) : SV_TARGET {
    float2 gatherUv = input.uv.zw;
    float4 velId = SceneVelId[input.position.xy];
    //return velId.bbbb;
    float3 velocity = velId.xyz;
    float2 texelSize = abs(input.uv.xy - input.uv.zw) * 2.0;
    
    float4 otherVelId = SceneVelId.Sample(BilinearClampedSampler, gatherUv);
    if (otherVelId.w != velId.w) return float4(1.0, 1.0, 0.0, 1.0);
    
    float2 previousUV = input.uv.xy - velocity.xy * (float2(1, -1) * (0.5 / 16.0));
    
    float depth = SceneDepth[input.position.xy].x;
    float4 curVPos = float4(input.uv.xy * float2(2.0, -2.0) - float2(1.0, -1.0), depth, 1.0);
    float4 prevVPos = mul(CurToPrevVP, curVPos);
    prevVPos.xyz /= prevVPos.w;
    if (all(velocity.xy == 0)) {
        previousUV = prevVPos.xy * float2(0.5, -0.5) + 0.5;
    }
    
    float3 sceneColor = CurrentFrame[input.position.xy].rgb;

    // If out of bounds
    if (!QuadAll(all(and(previousUV >= 0, previousUV <= 1)))) return float4(sceneColor, depth);

    float4 scenePrev4 = PreviousFrame.Sample(BilinearClampedSampler, previousUV);

    // If the depth doesnt match
    const float DepthError = 0.001;
    float minDepth = depth;
    float4 ptc = mul(PrevToCurVP, float4(prevVPos.xy, scenePrev4.w, 1));
    scenePrev4.w = ptc.z / ptc.w + velocity.z / 32.0;
    //return float4((velocity.zzz * 100.0 + 0.5) / 8.0, depth);
    if (minDepth > scenePrev4.w + DepthError) {
        minDepth = SceneDepth.Sample(MinSampler, gatherUv).r;
    }
    if (minDepth > scenePrev4.w + DepthError) return float4(sceneColor, depth);

    sceneColor = RGBToYCoCg(sceneColor);

    float3 scenePrev = RGBToYCoCg(scenePrev4.rgb);
    float3 colorMin, colorMax;

    float2 gather0 = gatherUv;
    float2 gather1 = gatherUv + texelSize;
    if (frac(TemporalFrame * 0.5 + 0.25) < 0.5) {
        gather0.x += texelSize.x;
        gather1.x -= texelSize.x;
    }

    // Current frame bounds
    if (true) {
        if (false) {
            bool2 quadOdd = WaveGetLaneIndex() & uint2(0x01, 0x02);
            colorMin = RGBToYCoCg(CurrentFrame.SampleLevel(MinSampler, gatherUv + select(quadOdd, 0, -texelSize), 0).rgb);
            colorMax = RGBToYCoCg(CurrentFrame.SampleLevel(MaxSampler, gatherUv + select(quadOdd, 0, -texelSize), 0).rgb);

            float3 sceneColorX = RGBToYCoCg(CurrentFrame[input.position.xy + float2(select(quadOdd.x, 1.0, -1.0), 0)].rgb);
            float3 sceneColorY = RGBToYCoCg(CurrentFrame[input.position.xy + float2(0, select(quadOdd.y, 1.0, -1.0))].rgb);

            float3 acrossX = QuadReadAcrossX(sceneColor);
            float3 acrossY = QuadReadAcrossY(sceneColor);
            
            colorMin = acrossX; colorMax = acrossX;
            colorMin = min(colorMin, acrossY); colorMax = max(colorMax, acrossY);
            colorMin = min(colorMin, sceneColorX); colorMax = max(colorMax, sceneColorX);
            colorMin = min(colorMin, sceneColorY); colorMax = max(colorMax, sceneColorY);
        } else if (false) {
            float3 c0 = RGBToYCoCg(CurrentFrame[input.position.xy + float2(+0, +1)].rgb);
            float3 c1 = RGBToYCoCg(CurrentFrame[input.position.xy + float2(+0, -1)].rgb);
            float3 c2 = RGBToYCoCg(CurrentFrame[input.position.xy + float2(+1, +0)].rgb);
            float3 c3 = RGBToYCoCg(CurrentFrame[input.position.xy + float2(-1, +0)].rgb);
            float3 c4 = RGBToYCoCg(CurrentFrame[input.position.xy + float2(-1, -1)].rgb);
            float3 c5 = RGBToYCoCg(CurrentFrame[input.position.xy + float2(+1, +1)].rgb);
            colorMin = min(min(c0, c1), min(c2, c3));
            colorMax = max(max(c0, c1), max(c2, c3));
            colorMin = min(colorMin, min(c4, c5));
            colorMax = max(colorMax, max(c4, c5));
        } else {
            colorMin = RGBToYCoCg(CurrentFrame.SampleLevel(MinSampler, gather0, 0).rgb);
            colorMax = RGBToYCoCg(CurrentFrame.SampleLevel(MaxSampler, gather0, 0).rgb);
            colorMin = min(colorMin, RGBToYCoCg(CurrentFrame.SampleLevel(MinSampler, gather1, 0).rgb));
            colorMax = max(colorMax, RGBToYCoCg(CurrentFrame.SampleLevel(MaxSampler, gather1, 0).rgb));
        }
        scenePrev = clamp(scenePrev, colorMin, colorMax);
    }

    if (false) {
        float3 prevMin = RGBToYCoCg(PreviousFrame.SampleLevel(MinSampler, gather0, 0).rgb);
        float3 prevMax = RGBToYCoCg(PreviousFrame.SampleLevel(MaxSampler, gather0, 0).rgb);
        prevMin = min(prevMin, RGBToYCoCg(PreviousFrame.SampleLevel(MinSampler, gather1, 0).rgb));
        prevMax = max(prevMax, RGBToYCoCg(PreviousFrame.SampleLevel(MaxSampler, gather1, 0).rgb));
        scenePrev -= sceneColor;
        float3 diff = saturate(2.0 - 2.0 * abs(0.5 - (sceneColor - prevMin) / max(prevMax - prevMin, 0.001)));
        //scenePrev = select(or(sceneColor < prevMin, sceneColor > prevMax), 0, scenePrev);
        scenePrev *= diff;
        scenePrev += sceneColor;
    }

    if (true) {
        //return float4((prevVPos.xy * 0.5 + 0.5) / 8, 0, 1);
        float3 sceneDelta = scenePrev - sceneColor;
        sceneDelta -= clamp(sceneDelta, -0.0005, 0.0005);
        sceneDelta *= 0.8;
        sceneColor = sceneColor + sceneDelta;
        //return float4(float2(minDepth, scenePrev4.w).rgg / 10.0, depth);
    }

    sceneColor = YCoCgToRGB(sceneColor);
    
    return float4(sceneColor, depth);
}
