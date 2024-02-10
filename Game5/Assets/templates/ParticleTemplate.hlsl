#include <noise.hlsl>

SamplerState BilinearSampler : register(s1);
Texture2D<float4> Texture : register(t0);
Texture2D<float4> PositionTexture : register(t1);
Texture2D<float4> VelocityTexture : register(t2);

cbuffer ParticleCB : register(b0)
{
    float3 Gravity;
    float DeltaTime;
    float Lifetime;
    float LocalTime;
    float LocalTimeZ;
    matrix ViewProjection;
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

%Bootstrap%

AppendStructuredBuffer<int> FreeParticleIds;

void KillParticle() {
    //FreeParticleIds.Append(GlobalParticle.id);
}

struct BlockSpawn
{
    uint Offset;
    uint Count;
};
StructuredBuffer<BlockSpawn> BlockBegins;

struct VSSpawnInput {
    float4 position : POSITION;
    float2 uv : TEXCOORD0;
};

struct PSSpawnInput {
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
};

PSSpawnInput VSSpawn(VSSpawnInput input)
{
    PSSpawnInput result;
    
    result.position = float4(input.position.xy, LocalTimeZ, 1.0);
    result.uv = input.uv;
#if defined(VULKAN)
    result.position.y = -result.position.y;
#endif

    return result;
}

void PSSpawn(PSSpawnInput input
, out float4 OutPosition : SV_Target0
, out float4 OutVelocity : SV_Target1
)
{
    BlockSpawn blockSpawn = BlockBegins[input.uv.x];
    uint blockBegin = (uint)input.uv.x;
    uint blockEnd = (uint)input.uv.y;
    uint blockId = (uint)input.position.x / 4 + (uint)input.position.y / 4 * 256;
    uint blockItem = (uint)input.position.x % 4 + ((uint)input.position.y % 4) * 4;
    // Out of range, should not spawn
    if (blockItem < blockBegin || blockItem >= blockEnd) discard;
    
    float3 Position = float3(0, 0, 0);
    //float Seed = frac(sin(frac(dot(input.position.xy, float2(0.34731, 0.7656))) * 651.5216) * 651.5216);
    float Seed = frac(frac(dot(input.position.xy, float2(0.6796, 0.4273))) * 188.1);
    float3 Velocity = float3(0, 0, 0);
    float Age = 0.0;
    
%ParticleSpawn%

    OutPosition = float4(Position, Seed);
    OutVelocity = float4(Velocity, Age);
}



struct VSStepInput {
    float4 position : POSITION;
};

struct PSStepInput {
    float4 position : SV_POSITION;
};

PSStepInput VSStep(VSStepInput input)
{
    PSStepInput result;
    
    result.position = float4(input.position.xy, LocalTimeZ, 1.0);
    result.position.z = LocalTimeZ;
#if defined(VULKAN)
    result.position.y = -result.position.y;
#endif

    return result;
}

void PSStep(PSStepInput input
, out float4 OutPosition : SV_Target0
, out float4 OutVelocity : SV_Target1
) 
{
    GlobalParticle.id = (uint)input.position.x + ((uint)input.position.y << 16);
    float3 Position = PositionTexture[input.position.xy].xyz;
    //float Seed = PositionTexture[input.position.xy].w;
    float Seed = frac(frac(dot(input.position.xy, float2(0.6796, 0.4273))) * 188.1);
    //float Seed = frac(frac(dot(input.position.xy, float2(0.34731, 0.7656))) * 651.5216);
    float3 Velocity = VelocityTexture[input.position.xy].xyz;
    float Age = VelocityTexture[input.position.xy].w;
    
    float3 delta = Position - AvoidPoint;
    Velocity += 1.0 * delta / pow(dot(delta, delta), 2.0);
    
%ParticleStep%

    {   // 16-bit precision fix
        Position = asfloat((asuint(Position) + ((asuint(Position).yzx >> 3) & 0x00001fff)) & 0xffffe000);
    }
    
    OutPosition = float4(Position, Seed);
    OutVelocity = float4(Velocity, Age);
}




struct VSInput {
    uint vertexId : SV_VertexID;
    uint instanceId : SV_InstanceID;
};

struct PSInput {
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
};

PSInput VSMain(VSInput input)
{
    PSInput result;
    
    uint quadId = input.instanceId;
    uint blockId = quadId / (4 * 4);
    uint localId = quadId & (4 * 4 - 1);
    
    uint2 blockAddr;
    blockAddr.y = blockId >> BlockSizeBits;
    blockAddr.x = blockId - (blockAddr.y << BlockSizeBits);
        
    uint2 localAddr = uint2(localId, localId >> 1) & 0x55;
    localAddr = (localAddr | (localAddr >> 1)) & 0x33;
    localAddr.x = (localAddr.x | (localAddr.x >> 2)) & 0x0f;
    uint2 address = blockAddr * 4 + localAddr;
    
    float4 PositionData = PositionTexture[address];
    float4 VelocityData = VelocityTexture[address];
    //PositionData = float4(0, 3, 0, 1);
    float3 Position = PositionData.xyz;
    float Age = VelocityData.w;
    float2 UV = float2(input.vertexId % 2, input.vertexId / 2);
    float Seed = frac(frac(dot(address.xy, float2(0.6796, 0.4273))) * 188.1);
    
%ParticleVertex%

    result.position = mul(ViewProjection, float4(Position, 1.0));
    
    result.position.xy += (float2(input.vertexId % 2, input.vertexId / 2) - 0.5) *
        float2(Projection._11, Projection._22);
    result.uv = UV;
    //result.position.y += input.id * 0.02;
    //result.position.y += age * 0.05;
    
#if defined(VULKAN)
    result.position.y = -result.position.y;
#endif
    
    if (Age == 0.0) {
        result.position.z = -1.0;
    }

    return result;
}

void PSMain(PSInput input
, out float4 OutColor : SV_Target0
) 
{
    float2 UV = input.uv;
    float4 Color = float4(1.0, 1.0, 1.0, 1.0);
    
%ParticlePixel%
    
    OutColor = Color;//float4(1.0, 1.0, 1.0, 0.01 + PermuteV2(input.uv) * 0.02);
}
