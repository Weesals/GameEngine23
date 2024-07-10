#include <common.hlsl>
#include <temporal.hlsl>

SamplerState BilinearSampler : register(s1);
Texture2D<float4> Texture : register(t0);

cbuffer WorldCB2 : register(b1)
{
    matrix Projection;
}

struct VSInput
{
    float4 position : POSITION;
    float3 tangent : TANGENT0;
    float2 uv : TEXCOORD0;
    float4 color : COLOR0;
};

struct VSInputLine
{
    float4 position : POSITION;
    float2 uv : TEXCOORD0;
    float4 start : LINEPOSITION;
    float4 tangent : LINEDELTA;
    float4 color : LINECOLOR;
};

struct PSInput
{
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
    float4 color : COLOR0;
};


PSInput VSLine(VSInputLine input)
{
    PSInput result;
    
    float thickness = input.tangent.w;

    float3 worldPos = 0;
    worldPos += input.start.xyz;
    worldPos += input.tangent.xyz * input.position.z;
    result.position = mul(ViewProjection, float4(worldPos, 1.0));
    float4 clipPos2 = mul(ViewProjection, float4(worldPos + input.tangent.xyz * (0.01 / thickness), 1.0));
    float2 clipTangent = normalize(clipPos2.xy / clipPos2.w - result.position.xy / result.position.w);
    clipTangent = float2(clipTangent.y, -clipTangent.x);
    result.position.xy += clipTangent * (thickness * 4.0 * (input.uv.x - 0.5) * result.position.w / Resolution);
    result.uv = input.uv;
    result.color = input.color;
        
#if defined(VULKAN)
    result.position.y = -result.position.y;
#endif

    return result;
}

void PSLine(PSInput input
, out float4 OutColor : SV_Target0
, out float4 OutVelocity : SV_Target1
) 
{
    TemporalAdjust(input.uv);
    float bright = input.uv.x * (1.0 - input.uv.x) / 0.25;
    OutColor = input.color * float4(1, 1, 1, bright);
    OutVelocity = 0;
}

PSInput VSMain(VSInput input)
{
    PSInput result;

    float3 worldPos = input.position.xyz;
    result.position = mul(ViewProjection, float4(worldPos, 1.0));
    result.uv = input.uv;
    result.color = input.color;
        
#if defined(VULKAN)
    result.position.y = -result.position.y;
#endif

    return result;
}

void PSMain(PSInput input
, out float4 OutColor : SV_Target0
, out float4 OutVelocity : SV_Target1
) 
{
    TemporalAdjust(input.uv);
    OutColor = input.color;
    OutVelocity = 0;
}



PSInput VSText(VSInput input)
{
    PSInput result;

    input.position.xy /= Resolution;
    result.position = mul(Projection,
        mul(View, float4(input.tangent.xyz, 1.0))// + float4(input.position.xyz, 0.0) * float4(1, -1, 1, 1)
    );
    result.position.xy += input.position.xy * result.position.w * float2(2, -2);
    result.uv = input.uv;
    result.color = input.color;
        
#if defined(VULKAN)
    result.position.y = -result.position.y;
#endif

    return result;
}

void PSText(PSInput input
, out float4 OutColor : SV_Target0
, out float4 OutVelocity : SV_Target1
) 
{
    TemporalAdjust(input.uv);
    OutColor = input.color;
    OutColor.a *= saturate(0.5 + (Texture.Sample(BilinearSampler, input.uv).a - 0.475) * 10);
    OutVelocity = 0;
}

