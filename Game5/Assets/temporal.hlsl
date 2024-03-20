matrix PreviousVP;
matrix CurrentVP;
float2 TemporalJitter;

SamplerState BilinearSampler : register(s1);
SamplerState MinSampler : register(s4);
SamplerState MaxSampler : register(s5);

Texture2D<float4> CurrentFrame : register(t0);
Texture2D<float4> PreviousFrame : register(t1);

Texture2D<float4> SceneDepth : register(t2);
Texture2D<float4> SceneVelId : register(t3);

cbuffer TemporalCB : register(b0)
{
    float RandomX, RandomY, TemporalFrac, TemporalFrac2, TemporalFrac3;
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

float Permute(float v) {
    return frac(v * TemporalFrac2 - TemporalFrac3);
}
float4 PSMain(PSInput input) : SV_TARGET {
    float rnd = frac(frac(dot(input.position.xy, float2(RandomX, RandomY))) * TemporalFrac);
    for(int i = 0; i < 1000; ++i) rnd = Permute(rnd);
    //return float4(rnd, Permute(rnd), Permute(Permute(rnd)), 1);
    
    float2 offsets[] = { float2(1.0, 0.0), float2(0.0, 1.0), float2(0.0, -1.0), float2(-1.0, 0.0), };
    float2 sign = float2(TemporalJitter.x < 0 ? -1 : +1, TemporalJitter.y < 0 ? +1 : -1);
    float2 texelSize = float2(ddx(input.uv.x), ddy(input.uv.y));
    
    float depth = SceneDepth[input.position.xy].x;
    float4 velId = SceneVelId[input.position.xy];
    float2 velocity = velId.xy;

    float4 otherVelId = SceneVelId.Sample(BilinearSampler, input.uv + texelSize * sign * 0.5);
    if (otherVelId.z != velId.z) return float4(1.0, 1.0, 0.0, 1.0);

    //return float4(velocity * 0.5 + 0.5, 0.0, 1.0);

    float4 curVPos = float4((input.uv * 2.0 - 1.0) * float2(1.0, -1.0), depth, 1.0);
    float2 previousUV = curVPos.xy - velocity / 16.0;
    
    if (all(velocity.xy == 0)) {
        float4 wpos = mul(CurrentVP, curVPos);
        wpos.xyz /= wpos.w;
        //return float4(frac(wpos.xyz), 1.0);
        float4 prevVPos = mul(PreviousVP, float4(wpos.xyz, 1.0));
        prevVPos.xyz /= prevVPos.w;
        previousUV = prevVPos.xy;
    }
    
    float4 sceneColor = CurrentFrame[input.position.xy];
    float4 scenePrev = PreviousFrame.Sample(BilinearSampler, previousUV * float2(1.0, -1.0) / 2.0 + 0.5);

    float4 colorMin = sceneColor;
    float4 colorMax = sceneColor;
    for (int i = 0; i < 2; ++i) {
        float4 otherColor = CurrentFrame[input.position.xy + offsets[i] * sign];
        colorMin = min(colorMin, otherColor);
        colorMax = max(colorMax, otherColor);
    }
    colorMin = CurrentFrame.Sample(MinSampler, input.uv + texelSize * sign * 0.5);
    colorMax = CurrentFrame.Sample(MaxSampler, input.uv + texelSize * sign * 0.5);
    scenePrev = clamp(scenePrev, colorMin, colorMax);
    sceneColor = lerp(sceneColor, scenePrev, 0.85);

    return sceneColor;
}
