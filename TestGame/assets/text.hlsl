// A simple UI textured shader

cbuffer ConstantBuffer : register(b0)
{
    matrix Projection;
};

struct VSInput
{
    float2 position : POSITION;
    float2 uv : TEXCOORD0;
    float4 color : COLOR0;
};

struct PSInput
{
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
    float4 color : COLOR0;
};

Texture2D<float4> Texture : register(t0);
SamplerState g_sampler : register(s0);

PSInput VSMain(VSInput input)
{
    PSInput result;
    
    result.position = mul(Projection, float4(input.position.xy, 0.0, 1.0));
    result.position.z = 0.5;
    result.uv = input.uv;
    result.color = input.color;
    
#if defined(VULKAN)
    result.position.y = -result.position.y;
#endif

    return result;
}

float4 PSMain(PSInput input) : SV_TARGET
{    
    float4 t = Texture.Sample(g_sampler, input.uv);
    //t.rg = input.uv;
    t.a = saturate(0.5 + (t.a - 0.5) / max(0.001, fwidth(t.a)));
    t.rgb = 1.0;
    t *= input.color;
    return t;
}
