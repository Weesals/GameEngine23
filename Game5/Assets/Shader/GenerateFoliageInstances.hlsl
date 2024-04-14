#include <common.hlsl>
#include <noise.hlsl>
#include <landscapecommon.hlsl>

float2 BoundsMin;
AppendStructuredBuffer<float4> Instances;
//RWTexture2D<float4> Texture : register(u0);
//RWStructuredBuffer<float4> Instances2;
//StructuredBuffer<float4> Instances2;

[numthreads(8, 8, 1)]
void CSGenerateFoliage(uint3 gtid : SV_DispatchThreadID) {
    uint3 globalId = gtid + uint3(BoundsMin, 0);
    ControlPoint c = DecodeControlMap(ControlMap.Load(globalId));
    HeightPoint h = DecodeHeightMap(HeightMap.Load(globalId));
    float3 wpos = float3(globalId.x, h.HeightOS, globalId.y);
    float rnd = dot(wpos.xz, float2(1, 1.3763191));
    rnd = frac(sin(rnd) * 12345.67);
    
    if (c.Layer == 0) {
        for (int i = 0; i < 3; ++i) {
            float2 offset = frac(rnd * float2(123, 12345)) - 0.5;
            float3 instancePos = wpos;
            instancePos.xz += offset;
        
            float4 cpos = mul(ViewProjection, float4(instancePos, 1.0));
            cpos.xy /= cpos.w;
            if (any(cpos.xy < -1.05) || any(cpos.xy > 1.05)) continue;
        
            Instances.Append(float4(instancePos, rnd));
            rnd *= frac(rnd * 123.456);
        }
    }
}
