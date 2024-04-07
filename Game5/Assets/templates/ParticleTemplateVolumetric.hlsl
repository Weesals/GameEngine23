#include <ParticleTemplateCommon.hlsl>

%Bootstrap%

struct VSSpawnInput {
    float4 position : POSITION;
    float2 uv : TEXCOORD0;
    float4 params : COLOR0;
};

struct PSSpawnInput {
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
    float4 params : TEXCOORD1;
};

PSSpawnInput VSSpawn(VSSpawnInput input)
{
    PSSpawnInput result;
    
    result.position = float4(input.position.xy, LocalTimeZ, 1.0);
    result.uv = input.uv;
    result.params = input.params;
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
    uint blockItem = (uint)input.position.x % 4 + ((uint)input.position.y % 4) * 4;
    // Out of range, should not spawn
    if (blockItem < blockBegin || blockItem >= blockEnd) discard;
    
    float3 Position = float3(0, 0, 0);
    //float Seed = frac(sin(frac(dot(input.position.xy, float2(0.34731, 0.7656))) * 651.5216) * 651.5216);
    float Seed = frac(frac(dot(input.position.xy, float2(0.6796, 0.4273))) * 188.1);
    float3 Velocity = float3(0, 0, 0);
    float Age = 0.0 - DeltaTime * frac(Seed * 123.45);
    
    Position += Emitters[input.params.x * 255].Position;
    
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
    float _global_DeltaTime = DeltaTime;
    float DeltaTime = _global_DeltaTime;
    if (Age < 0) {
        float toApply = min(DeltaTime, -Age);
        Age += DeltaTime;
        DeltaTime -= toApply;
    }
        
%ParticleStep%

    {   // 16-bit precision fix
        Position = asfloat((asuint(Position) + ((asuint(Position).yzx >> 3) & 0x00001fff)) & 0xffffe000);
    }
    if (Age > Lifetime) { Age = 10000000; }
    
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
    float4 attributes : TEXCOORD1;
};

PSInput VSMain(VSInput input)
{
    PSInput result;
    
    uint quadId = input.instanceId;
    uint blockId = quadId / (4 * 4);
    uint localId = quadId & (4 * 4 - 1);
    
    blockId = ActiveBlocks[blockId];
    
    uint2 blockAddr;
    //blockAddr.y = blockId >> BlockSizeBits;
    //blockAddr.x = blockId - (blockAddr.y << BlockSizeBits);
    blockAddr.x = blockId & 0xffff;
    blockAddr.y = blockId >> 16;
        
    uint2 localAddr = uint2(localId, localId >> 1) & 0x55;
    localAddr = (localAddr | (localAddr >> 1)) & 0x33;
    localAddr.x = (localAddr.x | (localAddr.x >> 2)) & 0x0f;
    uint2 address = blockAddr * 4 + localAddr;
    
    float4 PositionData = PositionTexture[address];
    float4 VelocityData = VelocityTexture[address];
    //PositionData = float4(0, 3, 0, 1);
    float3 Position = PositionData.xyz;
    float Age = VelocityData.w;
    float NormalizedAge = Age / Lifetime;
    float2 UV = float2(input.vertexId % 2, input.vertexId / 2);
    float Seed = frac(frac(dot(address.xy, float2(0.6796, 0.4273))) * 188.1);
    float SpriteSize = 1.0;
    
%ParticleVertex%

    result.position = mul(ViewProjection, float4(Position, 1.0));
    
    result.position.xy += (float2(input.vertexId % 2, input.vertexId / 2) - 0.5) *
        float2(Projection._11, Projection._22) * SpriteSize;
    result.uv = UV;
    result.attributes = float4(Age, Seed, 0, 0);
    //result.position.y += input.id * 0.02;
    //result.position.y += age * 0.05;
    
#if defined(VULKAN)
    result.position.y = -result.position.y;
#endif
    
    if (Age == 0.0 || Age > Lifetime) {
        result.position.z = -1.0;
    }

    return result;
}

void PSMain(PSInput input
, out float4 OutColor : SV_Target0
, out float4 OutAttr : SV_Target1
) 
{
    float2 UV = input.uv;
    float4 Color = float4(1.0, 1.0, 1.0, 1.0);
    float Age = input.attributes.x;
    float NormalizedAge = Age / Lifetime;
    float Seed = input.attributes.y;
    
%ParticlePixel%
    
    Color.rgb *= LuminanceFactor;
    OutColor = Color;//float4(1.0, 1.0, 1.0, 0.01 + PermuteV2(input.uv) * 0.02);
    float depth = input.position.z * input.position.w;
    OutAttr = float4(depth, depth * depth, 1.0, 1.0) * Color.a;
}
