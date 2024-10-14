#include "include/common.hlsl"
#include "include/lighting.hlsl"
#include "include/noise.hlsl"

cbuffer LocalCB : register(b1) {
    float4 _PlayerColors[32];
}

struct VSInput {
    float4 position : POSITION;
    float3 normal : NORMAL;
    float4 uv : TEXCOORD0;
    float4 posTime : INST_POSSIZE;
    float4 sizeOth : INST_PLAYERID;
};

struct PSInput {
    float4 position : SV_POSITION;
    float3 viewPos : TEXCOORD1;
    float4 data : TEXCOORD2;
    float4 uv : TEXCOORD0;
};

PSInput VSMain(VSInput input) {
    const float Scale = 15;
    PSInput result;

    float3 worldPos = input.position.xyz;
    float3 worldNrm = input.normal.xyz;
    // Each instance has its own offset
    float3 offset = input.posTime.xyz;
    float2 size = input.sizeOth.xy;
    float playerId = input.sizeOth.z;
    float type = input.sizeOth.w;

    float timeN = saturate((Time - input.posTime.w) / 0.05);
    float Width = 0.1;

	float npush =
		lerp(-0.2, 0.0, sqrt(timeN)) * input.uv.z +
		//(lerp(0.4, 0.8, v.uv.z) * timeN * (1 - timeN) * (1 - timeN)) +
		input.uv.z * Width;

	if (type < 0.5) {
		//npush += dot(size.xy, abs(v.uv - 0.5)) / 1.5;
		//position.xz += v.normal.xz * dot(abs(v.normal.xz), size.xy / 0.5);
		worldPos.xz += input.normal.xz * (size.xy / 2);
	} else {
		size.xy -= 0.25;
		worldPos.xz += (input.uv.xy - 0.5) * size.xy;
	}
    worldPos.xz += worldNrm.xz * npush;
    worldPos.xyz += offset;
    worldPos.y += 0.1;
    
    result.data = playerId + 0.5;

    result.uv = input.uv;//float4(input.uv, data.w, 0.0);
    result.position = mul(ViewProjection, float4(worldPos, 1.0));
    result.viewPos = mul(View, float4(worldPos, 1.0)).xyz;
    
#if defined(VULKAN)
    result.position.y = -result.position.y;
#endif

    return result;
}

float4 PSMain(PSInput input) : SV_TARGET {
    half a = saturate((0.5 - distance(input.uv.z, 0.5)) / fwidth(input.uv.z));
    a = lerp(0.5 + a * 0.5, a, step(0.5, input.uv.z));
    half4 c = 1.0;
    c.rgb *= LuminanceFactor;
    //c.rgb *= float3(sin(input.data.x), cos(input.data.x), 1) * 0.5 + 0.5;
    c.rgb *= _PlayerColors[input.data.x];
    c.a *= a;
	return c;
}
