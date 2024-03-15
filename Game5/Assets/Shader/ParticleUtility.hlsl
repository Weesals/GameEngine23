cbuffer ParticleCB : register(b0) {
    float LocalTimeZ;
}

struct VSBlankInput {
    float4 position : POSITION;
};

struct PSBlankInput {
    float4 position : SV_POSITION;
};

PSBlankInput VSBlank(VSBlankInput input)
{
    PSBlankInput result;
    
    result.position = float4(input.position.xy, LocalTimeZ, 1.0);
    result.position.z = LocalTimeZ;
#if defined(VULKAN)
    result.position.y = -result.position.y;
#endif

    return result;
}

void PSBlank(PSBlankInput input
, out float4 OutPosition : SV_Target0
, out float4 OutVelocity : SV_Target1
) 
{
    OutPosition = 0;
    OutVelocity = 0;
}

