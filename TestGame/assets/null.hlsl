// A shader that does nothing, just used for testing

struct VSInput
{
    float4 position : POSITION;
};

struct PSInput
{
    float4 position : SV_POSITION;
};

PSInput VSMain(VSInput input)
{
    PSInput result;

    result.position = float4(input.position.xyz / 10, 1.0);
    return result;
}

float4 PSMain(PSInput input) : SV_TARGET
{    
    return float4(0.5, 0.6, 0.7, 1.0);
}
