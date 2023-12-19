matrix PreviousVP;
matrix CurrentVP;

SamplerState BilinearSampler : register(s0);

Texture2D<float4> CurrentFrame : register(t0);
Texture2D<float4> PreviousFrame : register(t1);

Texture2D<float4> SceneDepth : register(t2);
Texture2D<float4> SceneVelocity : register(t3);

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
    float depth = SceneDepth[input.position.xy].x;
    float2 velocity = SceneVelocity[input.position.xy].xy;

    float4 curVPos = float4((input.uv * 2.0 - 1.0) * float2(1.0, -1.0), depth, 1.0);
    float2 previousUV = curVPos.xy - velocity;
    
    if (all(velocity.xy == 0)) {
        float4 wpos = mul(CurrentVP, curVPos);
        wpos.xyz /= wpos.w;
        //return float4(frac(wpos.xyz), 1.0);
        float4 prevVPos = mul(PreviousVP, float4(wpos.xyz, 1.0));
        prevVPos.xyz /= prevVPos.w;
        previousUV = prevVPos.xy;
    }
    
    float2 offsets[] = { float2(-1.0, 0.0), float2(1.0, 0.0), float2(0.0, -1.0), float2(0.0, 1.0), };
    float4 sceneColor = CurrentFrame[input.position.xy];
    float4 colorMin = sceneColor;
    float4 colorMax = sceneColor;
    for (int i = 0; i < 4; ++i) {
        float4 otherColor = CurrentFrame[input.position.xy + offsets[i]];
        colorMin = min(colorMin, otherColor);
        colorMax = max(colorMax, otherColor);
    }
    float4 scenePrev = PreviousFrame.Sample(BilinearSampler, previousUV * float2(1.0, -1.0) / 2.0 + 0.5);
    scenePrev = clamp(scenePrev, colorMin, colorMax);
    sceneColor = lerp(sceneColor, scenePrev, 0.85);
    return sceneColor;
}
