#include <common.hlsl>
#include <noise.hlsl>
#include <landscapecommon.hlsl>

float2 BoundsMin;
float2 BoundsMax;
matrix InvView;
AppendStructuredBuffer<float4> Instances;
SamplerState BilinearClampedSampler : register(s6);
//RWTexture2D<float4> Texture : register(u0);
//RWStructuredBuffer<float4> Instances2;
//StructuredBuffer<float4> Instances2;

half GetMask(uint3x3 IdMap, uint id, float2 Interp) {
    half3 col = half3(1.0 - Interp.x, 1.0, Interp.x * 1.0);
    half bSum = dot(1, select(IdMap[0] == id, col, 0));
    half ySum = dot(1, select(IdMap[2] == id, col, 0)) - bSum;
    bSum += dot(1, select(IdMap[1] == id, col, 0));
    return ySum * Interp.y + bSum;
}

[numthreads(8, 8, 1)]
void CSGenerateFoliage(uint3 gtid : SV_DispatchThreadID) {
    uint quadIndex = WaveGetLaneIndex();
    bool2 quadOdd = bool2((quadIndex & 0x01) != 0, (quadIndex & 0x02) != 0);
    uint3 globalId = gtid;
    
    globalId.x = (gtid.x & 0xfffd) | ((gtid.y << 1) & 0x02);
    globalId.y = (gtid.y & 0xfffe) | ((gtid.x >> 1) & 0x01);
    
    globalId += uint3(BoundsMin, 0);
    float2 gatherUv = globalId.xy;
    gatherUv *= _LandscapeSizing1.xy;
    //gatherUv = QuadReadLaneAt(gatherUv, 0);
    //gatherUv += select(quadOdd, _LandscapeSizing1.xy, 0);
    gatherUv += _LandscapeSizing1.zw;
    
    uint3x3 IdMap;

    uint4 QuadData1 = ControlMap.Gather(BilinearClampedSampler, gatherUv);
    uint4 QuadData2 = ControlMap.Gather(BilinearClampedSampler, gatherUv - _LandscapeSizing1.xy);
    QuadData1 &= 0xff00;
    QuadData2 &= 0xff00;
    IdMap._31_32_33 = uint3(ControlMap[globalId.xy + uint2(1, -1)], QuadData2.zw);
    IdMap._21_22_23 = uint3(QuadData1.zw, QuadData2.x);
    IdMap._11_12_13 = uint3(QuadData1.yx, ControlMap[globalId.xy + uint2(-1, 1)]);
    
    //IdMap._22 = ControlMap[globalId.xy];
    
    ControlPoint c = DecodeControlMap(IdMap._22);
    HeightPoint h = DecodeHeightMap(HeightMap.Load(globalId));
    float3 wpos = float3(globalId.x, h.HeightOS, globalId.y);
    float rnd = dot(wpos.xz, float2(1, 1.3763191));
    rnd = frac(sin(rnd) * 12345.67);
    
    float2 boundsSize = BoundsMax - BoundsMin;
    float area = boundsSize.x * boundsSize.y;
    
    float3 cameraPos = mul(InvView, float4(0, 0, 0, 1)).xyz;
    float cameraDst = distance(wpos, cameraPos);
    
    float countScale = min(1, 40 / cameraDst);
    float scaleModifier = 1.0 / pow(countScale, 0.5);
    
    if (c.Layer == 0) {
        float count = 7 * countScale;
        for (float i = 0; i < count; ++i) {
            float2 offset = frac(rnd * float2(123, 12345));
            
            float3 instancePos = wpos;
            instancePos.xz += 0.5 - offset;
        
            float4 cpos = mul(ViewProjection, float4(instancePos, 1.0));
            const float FrustBorder = 1.0;
            if (any(cpos.xy < -cpos.w - FrustBorder) || any(cpos.xy > cpos.w + FrustBorder)) continue;
        
            float weight = GetMask(IdMap, IdMap._22, offset) / 4.0;
            if (weight <= 0.01f) continue;
            weight *= saturate(count - i) * scaleModifier;
                        
            Instances.Append(float4(instancePos, rnd + round(weight * 1024)));
            rnd *= frac(rnd * 123.456);
        }
    }
}
