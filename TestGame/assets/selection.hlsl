// A standard PBR shader (according to various examples online)

#define PI 3.14159265359

cbuffer WorldCB : register(b0)
{
    float3 _LightColor0;
    float3 _ViewSpaceLightDir0;
    float3 _ViewSpaceUpVector;
    float4 _PlayerColors[4];
}
cbuffer ConstantBuffer : register(b1)
{
    matrix ModelView;
    matrix ModelViewProjection;
    //float4 InstanceData2[256];
    //float4 InstanceData[256];
    float Time;
};

#include "include/lighting.hlsl"
#include "include/noise.hlsl"

struct VSInput
{
    //uint instanceId : SV_InstanceID;
    float4 position : POSITION;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD0;
    float4 posSize : INST_POSSIZE;
    float4 playerId : INST_PLAYERID;
};

struct PSInput
{
    float4 position : SV_POSITION;
    float3 viewPos : TEXCOORD1;
    float4 data : TEXCOORD2;
    float4 uv : TEXCOORD0;
};

PSInput VSMain(VSInput input)
{
    const float Scale = 15;
    PSInput result;

    float3 worldPos = input.position.xyz;
    float3 worldNrm = input.normal.xyz;
    // Each instance has its own offset
    float4 data = input.posSize; //InstanceData[input.instanceId];
    worldPos.xyz *= data.w / 2.0;
    worldPos.xyz += data.xyz;
    worldPos.y += 0.1;
    
    result.data = input.playerId; //InstanceData2[input.instanceId];

    result.uv = float4(input.uv, data.w, 0.0);
    result.position = mul(ModelViewProjection, float4(worldPos, 1.0));
    result.viewPos = mul(ModelView, float4(worldPos, 1.0));
    
#if defined(VULKAN)
    result.position.y = -result.position.y;
#endif

    return result;
}

float4 PSMain(PSInput input) : SV_TARGET
{
    float2 uv = input.uv;
    uv *= input.uv.z;
    uv -= clamp(uv, 0.5, input.uv.z - 0.5);
    float dst = distance(uv, 0.0);
    float a = 1.0 - abs(dst - 0.4) / 0.1;
    float4 r = float4(1, 1, 1, saturate(a / max(fwidth(a), 0.001)));
    r *= _PlayerColors[input.data.w + 0.5];
    return r;
}
