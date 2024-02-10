// A simple UI textured shader

#if __INTELLISENSE__
# define OUTLINE_ON
# define UNDERLAY_ON
#endif

cbuffer ConstantBuffer : register(b0)
{
    matrix ModelViewProjection;
    float4 CullRect;
};
cbuffer TextCBuffer : register(b1)
{
    float4 _FaceColor;
    float _FaceDilate;
    
#ifdef OUTLINE_ON
    float4 _OutlineColor;
    float _OutlineWidth;
    float _OutlineSoftness;
#endif
#ifdef UNDERLAY_ON
    float4 _UnderlayColor;
    float _UnderlayDilate;
    float _UnderlaySoftness;
    float2 _UnderlayOffset;
#endif
}

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
SamplerState g_sampler : register(s1);

PSInput VSMain(VSInput input)
{
    PSInput result;
    
    result.position = mul(ModelViewProjection, float4(input.position.xy, 0.0, 1.0));
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
    half density = Texture.Sample(g_sampler, input.uv).a;
    
    half fringe = 2.0 * (127.0 / 7.0) / 256;// * ddx(input.uv.x);
    //fringe = max(fwidth(density), 0.00001);
    half bias = 0.5 - _FaceDilate / 2 - fringe * 0.125;
    
    half4 faceColor = _FaceColor;
    faceColor.rgb *= input.color.rgb;

    // Compute result color
    half4 c = faceColor * saturate((density - bias) / fringe + 0.5);
    
#ifdef OUTLINE_ON
    if (_OutlineWidth > 0) {
        half4 outlineColor = _OutlineColor;
        outlineColor.rgb *= outlineColor.a;
        
        half outlineFade = max(_OutlineSoftness, fringe / 2);
        half halfWidth = (_OutlineWidth / 2);
        half ol_from = min(1, bias + halfWidth + outlineFade / 2);
        half ol_to = max(0, bias - halfWidth - outlineFade / 2);
        c = lerp(faceColor, outlineColor, saturate((ol_from - density) / outlineFade));
        c *= saturate((density - ol_to) / outlineFade);
    }
#endif
#ifdef UNDERLAY_ON
    half ul_from = max(0, bias - _UnderlayDilate - _UnderlaySoftness / 2);
    half ul_to = min(1, bias - _UnderlayDilate + _UnderlaySoftness / 2);
    half2 offset =
        _UnderlayOffset.x +
        _UnderlayOffset.y;

    float2 underlayUV = input.uv - offset;
    half old = Texture.Sample(g_sampler, underlayUV).a;
    c += _UnderlayColor * _UnderlayColor.a * (1 - c.a) *
        saturate((old - ul_from) / max(0.00001, ul_to - ul_from));
#endif
    c.a *= input.color.a; 
    if (any(input.position.xy < CullRect.xy) || any(input.position.xy > CullRect.zw))
        c.a = 0.0;
    //c.a = max(c.a, 0.08f);
    return c;
}
