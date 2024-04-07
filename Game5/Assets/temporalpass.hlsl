matrix PreviousVP;
matrix CurrentVP;
matrix CurToPrevVP;
float2 TemporalJitter;
float2 Resolution;

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

float3 RGBToYCoCg(float3 rgb) {
    return float3(
        //dot(rgb, float3(1.0, 2.0, 1.0)),
        //dot(rgb, float3(2.0, 0.0, -2.0)),
        //dot(rgb, float3(-1.0, 2.0, -1.0))
        dot(rgb, float3(0.25, 0.5, 0.25)),
        dot(rgb, float3(0.5 , 0.0, -0.5)),
        dot(rgb, float3(-0.25, 0.5, -0.25))
    );
}
float3 YCoCgToRGB(float3 ycocg) {
    //ycocg *= 0.25;
    return float3(
        dot(ycocg, float3(+1, +1, -1)),
        dot(ycocg, float3(+1, +0, +1)),
        dot(ycocg, float3(+1, -1, -1))
    );
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
    float2 velocity = velId.xy;
    
    float4 otherVelId = SceneVelId.Sample(BilinearClampedSampler, gatherUv);
    if (otherVelId.z != velId.z) return float4(1.0, 1.0, 0.0, 1.0);
    
    float2 previousUV = input.uv.xy - velocity * (0.5 / 16.0);
    
    if (all(velocity.xy == 0)) {
        float depth = SceneDepth[input.position.xy].x;
        float4 curVPos = float4(input.uv.xy * float2(2.0, -2.0) - float2(1.0, -1.0), depth, 1.0);
        float4 prevVPos = mul(CurToPrevVP, curVPos);
        prevVPos.xyz /= prevVPos.w;
        previousUV = prevVPos.xy * float2(0.5, -0.5) + 0.5;
    }
    
    float3 colorMin = RGBToYCoCg(CurrentFrame.Sample(MinSampler, gatherUv).rgb);
    float3 colorMax = RGBToYCoCg(CurrentFrame.Sample(MaxSampler, gatherUv).rgb);
    float3 scenePrev = RGBToYCoCg(PreviousFrame.Sample(BilinearClampedSampler, previousUV).rgb);
    scenePrev = clamp(scenePrev, colorMin, colorMax);
    
    float3 sceneColor = RGBToYCoCg(CurrentFrame[input.position.xy].rgb);
    float3 sceneDelta = scenePrev - sceneColor;
    sceneDelta -= clamp(sceneDelta, -0.0005, 0.0005);
    sceneDelta *= 0.85;
    sceneColor = sceneColor + sceneDelta;

    return float4(YCoCgToRGB(sceneColor), 1);
}
