#if 0
#include "landscape.hlsl"
#else
#define PI 3.14159265359

#include <common.hlsl>
#include <temporal.hlsl>
#include <landscapecommon.hlsl>
#include <landscapesampling.hlsl>
#include <basepass.hlsl>

static const half HeightBlend = 6.0;
static const half WeightBlend = 1.0;

cbuffer ConstantBuffer : register(b1) {
    float4x4 ModelView;
    float4x4 ModelViewProjection;
}
struct ControlPoints3x3 {
    uint3x3 Data;
    uint4 Ids1, Ids2;
};

struct VSInput {
    uint instanceId : SV_InstanceID;
    float4 position : POSITION;
    float3 normal : NORMAL;
    int2 offset : INSTANCE;
};

struct PSInput {
    float3 positionOS : TEXCOORD0;
    half3 normalOS : NORMAL;
#if EDGE
    float3 uv : TEXCOORD1;
#endif
};

PSInput VSMain(VSInput input, out float4 positionCS : SV_POSITION) {
    PSInput result;

    float3 worldPos = input.position.xyz;
    float3 worldNrm = input.normal.xyz;
    TransformLandscapeVertex(worldPos, worldNrm, input.offset);
    
    // Sample from the heightmap and offset the vertex
    float4 hcell = HeightMap.Load(int3(worldPos.xz, 0), 0);
    float terrainHeight = _LandscapeScaling.z + (hcell.r) * _LandscapeScaling.w;
    worldPos.y += terrainHeight;
#if EDGE
    if (input.position.y > 0.5) worldPos.y = -5;
    result.uv = float3(
        float2(input.position.x, worldPos.y) * 0.1,
        terrainHeight
    );
#else
    worldNrm.xz = hcell.yz * 2.0 - 1.0;
    worldNrm.y = sqrt(1.0 - dot(worldNrm.xz, worldNrm.xz));
#endif

    result.positionOS = worldPos;
    result.normalOS = (half3)worldNrm;
    positionCS = mul(ModelViewProjection, float4(worldPos, 1.0));
        
    return result;
}

ControlPoints3x3 SampleControl(float2 uv, out half2 l) {
    uv += 0.5;
    float2 quadUv = floor(QuadReadLaneAt(uv, 0));
    l = (half2)(float2)(uv - quadUv);
    uv = quadUv - 0.5;
    //l = l * 0.5 + 0.5;
    
    ControlPoints3x3 cp = (ControlPoints3x3)0;
    
    uint quadIndex = WaveGetLaneIndex();
    bool2 isOdd = bool2((quadIndex & 0x01) != 0, (quadIndex & 0x02) != 0);
    float2 localUv = uv + select(isOdd, 1.0, 0.0);
    uint4 cpD = ControlMap.Gather(AnisotropicSampler, localUv * _LandscapeSizing1.xy + _LandscapeSizing1.zw);
    cpD = Sort(cpD) & 0x00ffffff;
    if (frac(uv.x * 0.5) > 0.5) cpD = cpD.yxwz;
    if (frac(uv.y * 0.5) > 0.5) cpD = cpD.zwxy;

    // Get all unique items
    cp.Ids1.x = cpD.w;
    uint2 items = isOdd.x != isOdd.y ? cpD.xz : cpD.xy;
    if (items.x == cpD.w) items.x = 0xffffffff;
    if (items.y == cpD.w) items.y = 0xffffffff;
    [unroll]
    for (int i = 1; i < 8; ++i) {
        uint next = items.y == 0xffffffff ? items.x : items.y;
        uint qX = QuadReadAcrossX(next);
        if (next == 0xffffffff) next = qX;
        uint qY = QuadReadAcrossY(next);
        if (next == 0xffffffff) next = qY;
        next = QuadReadLaneAt(next, 0);
        if (i < 4) cp.Ids1[i % 4] = next;
        else cp.Ids2[i % 4] = next;
        
        if (next == 0xffffffff) break;

        if (items.x == next) items.x = 0xffffffff;
        if (items.y == next) items.y = 0xffffffff;
    }
    
    // Build id map
    cp.Data._22 = cpD.w;
    cp.Data._11_12_21 = QuadReadLaneAt(cpD.xyz, 0);
    cp.Data._13_23 = QuadReadLaneAt(cpD.xz, 1);
    cp.Data._31_32 = QuadReadLaneAt(cpD.xy, 2);
    cp.Data._33 = QuadReadLaneAt(cpD.x, 3);
    
    return cp;
}

half GetMask(ControlPoints3x3 cp, uint id, half2 l) {
    half3x3 weights;
    weights[0] = select(cp.Data[0] == id, 1.0 - l.y, 0);
    weights[1] = select(cp.Data[1] == id, 1.0, 0);
    weights[2] = select(cp.Data[2] == id, l.y, 0);
    weights._11_21_31 *= 1.0 - l.x;
    weights._13_23_33 *= l.x;
    return dot(weights[0] + weights[1] + weights[2], 1.0) * WeightBlend;
}

void BlendTerrain(inout TerrainSample result, TerrainSample ter, half amount) {
    result.Albedo += ter.Albedo * amount;
    result.Normal += ter.Normal * amount;
    result.Metallic += ter.Metallic * amount;
    result.Roughness += ter.Roughness * amount;
    result.Height += ter.Height * amount;
}

void PSMain(PSInput input, out BasePassOutput result) {
#if EDGE
    PBRInput pbrInput = PBRDefault();
    TemporalAdjust(input.uv.xy);
    pbrInput.Albedo = EdgeTex.Sample(AnisotropicSampler, input.uv.xy).rgb;
    pbrInput.Albedo = lerp(pbrInput.Albedo, 1, pow(1 - saturate(input.uv.z - input.positionOS.y), 4) * 0.25);
#else
    TerrainSample terResult = (TerrainSample)0;
    uint quadIndex = WaveGetLaneIndex();
    float2 uv = input.positionOS.xz;
    //[loop] for (int i = 0; i < 100; ++i)
    {
    TemporalAdjust(uv);
        
    /*
    // Performance test int to float
    for (int i = 0; i < 20000; ++i) {
        //result.BaseColor.rg += asfloat((quadIndex << uint2(30, 29)) & 0x40000000) * 0.5;
        //result.BaseColor.rg += float2((quadIndex & uint2(0x01, 0x02)) * uint2(asuint(1.0), asuint(1.0) / 2));
        //result.BaseColor.rg += float2(uint2(quadIndex & 0x01, (quadIndex >> 1) & 0x02));
        result.BaseColor.rg += float2(quadIndex & 0x01 ? 1.0 : 0.0, (quadIndex >> 1) & 0x02 ? 1.0 : 0.0);
        quadIndex += asuint(result.BaseColor.r);
    }
    result.BaseColor *= 0.5;
    return;//*/
    
    half2 l;
    ControlPoints3x3 cp3x3 = SampleControl(uv, l);
    
    // Print out control point interpolation
    /*if (true) {
        float3 row0 = (float3)((cp3x3.Data[0] >> 8) & 0xff) / 256.0;
        float3 row1 = (float3)((cp3x3.Data[1] >> 8) & 0xff) / 256.0;
        float3 row2 = (float3)((cp3x3.Data[2] >> 8) & 0xff) / 256.0;

        float3 r0 = lerp(row0, row1, l.y);
        float3 r1 = lerp(row1, row2, l.y);

        float2 c0 = lerp(float2(r0.x, r1.x), float2(r0.y, r1.y), l.x);
        float2 c1 = lerp(float2(r0.y, r1.y), float2(r0.z, r1.z), l.x);
    
        result.BaseColor += float4(dot(float4(c0, c1), 0.25).rrr, 1);
        result.BaseColor.r += (1 - saturate(l.x * (1 - l.x) * l.y * (1 - l.y) * 200)) * 0.1;
        return;
    }//*/
    
    SampleContext context = { BaseMaps, BumpMaps, input.positionOS };
                
    float complexity = 0.0;
    terResult = SampleTerrain(context, DecodeControlMap(cp3x3.Ids1.x));
    
    [branch]
    if (cp3x3.Ids1.y != 0xffffffff) {
        const int SampleCount = 5;
            
        TerrainSample samples[SampleCount];
        half masks[SampleCount];
        half maxWeight = 1.0;
            
        {
            int i = 0;
            uint id = cp3x3.Ids1.x;
            samples[i] = terResult;
            masks[i] = GetMask(cp3x3, id, l);
            masks[i] += samples[i].Height * HeightBlend * saturate(masks[i]);
            maxWeight = max(maxWeight, masks[i]);
            ++complexity;
            Pack(samples[i]);
        }
        [unroll]
        for (int i = 2; i < SampleCount; ++i) {
            masks[i] = 0;
            samples[i] = (TerrainSample)0;
        }
        [unroll]
        for (int i = 1; i < SampleCount; ++i){
            uint id = (i < 4 ? cp3x3.Ids1 : cp3x3.Ids2)[i % 4];
            if (id == 0xffffffff) break;
            samples[i] = SampleTerrain(context, DecodeControlMap(id));
            masks[i] = GetMask(cp3x3, id, l);
            masks[i] += samples[i].Height * HeightBlend * saturate(masks[i]);
            maxWeight = max(maxWeight, masks[i]);
            ++complexity;
            Pack(samples[i]);
        }
            
        maxWeight -= 1.0;
        [unroll] for(int i = 0; i < SampleCount; ++i) masks[i] = saturate(masks[i] - maxWeight);
            
        half maskMul = 0.0;
        [unroll] for(int i = 0; i < SampleCount; ++i) maskMul += masks[i];
        maskMul = rcp(maskMul);
            
        terResult = (TerrainSample)0;
        BlendTerrain(terResult, samples[0], masks[0] * maskMul);
        BlendTerrain(terResult, samples[1], masks[1] * maskMul);
        [branch]
        if (complexity >= 3) {
            [unroll]
            for (int i = 2; i < SampleCount; ++i) {
                //if (i >= complexity) break;
                //Unpack(samples[i]);
                BlendTerrain(terResult, samples[i], masks[i] * maskMul);
            }
        }
        //terResult.Albedo.r = complexity / (float)SampleCount;
        //terResult.Albedo.rg = l;
    }
    //pbrInput.Albedo.rgb = terResult.Height;
    }
    PBRInput pbrInput = PBRDefault();
    pbrInput.Albedo = terResult.Albedo;
    pbrInput.Normal = terResult.Normal;
    pbrInput.Metallic = terResult.Metallic;
    pbrInput.Roughness = terResult.Roughness;
    pbrInput.Normal.z = sqrt(1.0 - dot(pbrInput.Normal.xy, pbrInput.Normal.xy));
    
    //if (cp3x3.Ids1.r == 0xffffffff) cp3x3.Ids1.r = 0;
    //if (cp3x3.Ids1.g == 0xffffffff) cp3x3.Ids1.g = 0;
    //if (cp3x3.Ids1.b == 0xffffffff) cp3x3.Ids1.b = 0;
    //pbrInput.Albedo = cp3x3.Ids1.rgb;
    //if (cp3x3.Ids1.y == 0xffffffff) pbrInput.Albedo = 0;
        
    half3x3 tbn = { half3(0, 0, 0), half3(0, 0, 0), normalize(input.normalOS), };
    CalculateTerrainTBN(tbn[2], tbn[0], tbn[1]);
    pbrInput.Normal = (float3)((half3)mul(pbrInput.Normal, tbn));
#endif
    pbrInput.Normal = mul((float3x3)ModelView, pbrInput.Normal);
    pbrInput.Normal = normalize(pbrInput.Normal);
        
    float3 viewPos = mul(ModelView, float4(input.positionOS, 1.0)).xyz;
    float3 viewDir = normalize(viewPos);
    result = PBROutput(pbrInput, viewDir);
}

void ShadowCast_VSMain(VSInput input, out float4 positionCS : SV_POSITION) {
    VSMain(input, positionCS);
}
void ShadowCast_PSMain() { }

#endif
