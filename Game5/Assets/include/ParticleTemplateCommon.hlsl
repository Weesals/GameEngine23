#include <common.hlsl>
#include <noise.hlsl>

SamplerState BilinearSampler : register(s1);
Texture2D<float4> PositionTexture : register(t0);
Texture2D<float4> VelocityTexture : register(t1);
Texture2D<float4> Texture : register(t5);

cbuffer ParticleCB : register(b1)
{
    float3 Gravity;
    float DeltaTime;
    float Lifetime;
    float LocalTime;
    float LocalTimeZ;
    matrix Projection;
    uint PoolSize;
    uint BlockSizeBits;
    float RandomMul;
    float RandomAdd;
    float3 AvoidPoint;
}

static struct {
    uint id;
} GlobalParticle;

struct Emitter {
    float3 Position;
};

StructuredBuffer<uint> ActiveBlocks;
StructuredBuffer<Emitter> Emitters;

struct BlockSpawn {
    uint Offset;
    uint Count;
};
StructuredBuffer<BlockSpawn> BlockBegins;



struct VSBlankInput {
    float4 position : POSITION;
};

struct PSBlankInput {
    float4 position : SV_POSITION;
};

PSBlankInput VSBlank(VSBlankInput input) {
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
) {
    OutPosition = 0;
    OutVelocity = 0;
}


void KillParticle() {
}