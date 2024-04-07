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
#define ControlCount 9
#define SampleCount 5

cbuffer ConstantBuffer : register(b1) {
    matrix ModelView;
    matrix InvModelView;
    matrix ModelViewProjection;
}

struct VSInput {
    uint instanceId : SV_InstanceID;
    float4 position : POSITION;
    float3 normal : NORMAL;
    int2 offset : INSTANCE;
};

struct PSInput {
    float3 positionOS : TEXCOORD0;
    half3 normalOS : NORMAL;
};

PSInput VSMain(VSInput input, out float4 positionCS : SV_POSITION) {
    PSInput result;

    float3 worldPos = input.position.xyz;
    float3 worldNrm = input.normal.xyz;
    TransformLandscapeVertex(worldPos, worldNrm, input.offset);
    
    // Sample from the heightmap and offset the vertex
    float4 hcell = HeightMap.Load(int3(worldPos.xz, 0), 0);
    float terrainHeight = _LandscapeScaling.z + (hcell.x) * _LandscapeScaling.w;
    worldPos.y += terrainHeight;
    worldNrm.xz = hcell.yz * 2.0 - 1.0;
    worldNrm.y = sqrt(1.0 - dot(worldNrm.xz, worldNrm.xz));

    result.positionOS = worldPos;
    result.normalOS = (half3)worldNrm;
    positionCS = mul(ModelViewProjection, float4(worldPos, 1.0));
        
    return result;
}

struct ControlPoints3x3 {
    uint4 QuadData;
    uint3x3 IdMap;
    uint PrimaryId;
    void Initialise(float2 uv, out half2 l) {
        uv += 0.5;
        float2 quadUv = floor(QuadReadLaneAt(uv, 0));
        l = (half2)(float2)(uv - quadUv);
        uv = quadUv - 0.5;
        uint quadIndex = WaveGetLaneIndex();
        bool2 quadOdd = bool2((quadIndex & 0x01) != 0, (quadIndex & 0x02) != 0);
        float2 gatherUv = uv + select(quadOdd, 1.0, 0.0);
    
        uint4 cpD = ControlMap.Gather(AnisotropicSampler, gatherUv * _LandscapeSizing1.xy + _LandscapeSizing1.zw);
        cpD = Sort(cpD);
        if (frac(uv.x * 0.5) > 0.5) cpD = cpD.yxwz;
        if (frac(uv.y * 0.5) > 0.5) cpD = cpD.zwxy;
        
        QuadData = cpD & 0x00ffffff;
        PrimaryId = QuadData.w;
    }
    void BuildMap() {
        // Build id map
        IdMap._22 = QuadData.w;
        IdMap._11_12_21 = QuadReadLaneAt(QuadData.xyz, 0);
        IdMap._13_23 = QuadReadLaneAt(QuadData.xz, 1);
        IdMap._31_32 = QuadReadLaneAt(QuadData.xy, 2);
        IdMap._33 = QuadReadLaneAt(QuadData.x, 3);
    }
    half GetMask(uint id, half2 l) {
        half3 col = half3(1.0 - l.x, 1.0, l.x);
        return dot(1.0,
            select(IdMap[0] == id, col * (1.0 - l.y), 0) +
            select(IdMap[1] == id, col * (1.0), 0) +
            select(IdMap[2] == id, col * (l.y), 0)
        );
    }
};
struct ControlReducer {
    uint2 OrigItems;
    uint2 Items;
    void Initialise(uint4 cpD) {
        uint quadIndex = WaveGetLaneIndex();
        bool2 quadOdd = bool2((quadIndex & 0x01) != 0, (quadIndex & 0x02) != 0);
        OrigItems = quadOdd.x != quadOdd.y ? cpD.xy : cpD.xz;
        Items = select(OrigItems == cpD.w, 0xffffffff, OrigItems);
        Items.x |= 0x80000000;
    }
    uint TakeItem() {
        uint next = min(Items.x, Items.y);
        next = min(next, QuadReadAcrossX(next));
        next = min(QuadReadLaneAt(next, 0), QuadReadLaneAt(next, 2));
        next &= 0x00ffffff;
        if (OrigItems.x == next) Items.x = 0xffffffff;
        if (OrigItems.y == next) Items.y = 0xffffffff;
        return next;
    }
};
void BlendTerrain(inout TerrainSample result, TerrainSample ter, half amount) {
    result.Albedo += ter.Albedo * amount;
    result.Normal += ter.Normal * amount;
    result.Metallic += ter.Metallic * amount;
    result.Roughness += ter.Roughness * amount;
    result.Height += ter.Height * amount;
}

struct LayerBlend {
    TerrainSample samples[SampleCount];
    half masks[SampleCount];
    half maxWeight;
    half complexity;
    void SetAt(int i, ControlPoints3x3 cp3x3, TerrainSample sample, float2 l, int id) {
        samples[i] = sample;
        masks[i] = cp3x3.GetMask(id, l) * WeightBlend;
        masks[i] += samples[i].Height * saturate(masks[i]) * HeightBlend;
        maxWeight = max(maxWeight, masks[i]);
        ++complexity;
    }
    float FinalizeWeights() {
        maxWeight -= 1.0;
        [unroll] for(int i = 0; i < SampleCount; ++i) masks[i] = saturate(masks[i] - maxWeight);
        half maskMul = 0.0;
        [unroll] for(int i = 0; i < SampleCount; ++i) maskMul += masks[i];
        return rcp(maskMul);
    }
};

void PSMain(PSInput input, out BasePassOutput result, float4 positionCS : SV_POSITION) {
    const bool EnableTriplanar = false;
    const bool EnableParallax = false;
    TerrainSample terResult = (TerrainSample)0;
    
    [loop] for(int i = 0; i < 1; ++i)
    {
    TemporalAdjust(input.positionOS.xz);
        
    half2 l;
    ControlPoints3x3 cp3x3;
    cp3x3.Initialise(input.positionOS.xz, l);

    SampleContext context = { BaseMaps, BumpMaps, input.positionOS, positionCS.xy, input.normalOS };
                
    terResult = SampleTerrain(context, DecodeControlMap(cp3x3.PrimaryId), EnableTriplanar);
    if (EnableParallax) {
        float3 localViewDir = mul(InvModelView, float4(0, 0, 0, 1)).xyz - context.WorldPos;
        float rateDiv = 0.1 * (1.0 / localViewDir.y);
        context.WorldPos.xz += localViewDir.xz * ((terResult.Height - 0.5) * rateDiv);
        terResult = SampleTerrain(context, DecodeControlMap(cp3x3.PrimaryId), EnableTriplanar);
    }
    
    ControlReducer reducer;
    reducer.Initialise(cp3x3.QuadData);
    uint secondaryId = reducer.TakeItem();

    [branch]
    if (secondaryId != 0x00ffffff) {
        cp3x3.BuildMap();
        LayerBlend blend = (LayerBlend)0;
        blend.maxWeight = 1.0;
        blend.SetAt(0, cp3x3, terResult, l, cp3x3.PrimaryId);
        blend.SetAt(1, cp3x3, SampleTerrain(context, DecodeControlMap(secondaryId), EnableTriplanar), l, secondaryId);
        [unroll]
        for (int i = 2; i < SampleCount; ++i) {
            uint id = reducer.TakeItem();
            if (id == 0x00ffffff) break;
            blend.SetAt(i, cp3x3, SampleTerrain(context, DecodeControlMap(id), EnableTriplanar), l, id);
        }
        
        half maskMul = blend.FinalizeWeights();
            
        terResult = (TerrainSample)0;
        BlendTerrain(terResult, blend.samples[0], blend.masks[0] * maskMul);
        BlendTerrain(terResult, blend.samples[1], blend.masks[1] * maskMul);
        [branch]
        if (blend.complexity > 2) {
            [unroll]
            for (int i = 2; i < SampleCount; ++i) {
                BlendTerrain(terResult, blend.samples[i], blend.masks[i] * maskMul);
            }
        }
        //terResult.Albedo.r = (blend.complexity + 1) / (float)SampleCount;
    }
    }
    
    PBRInput pbrInput = PBRDefault();
    pbrInput.Albedo = terResult.Albedo;
    pbrInput.Normal = terResult.Normal;
    pbrInput.Metallic = terResult.Metallic;
    pbrInput.Roughness = terResult.Roughness;
    pbrInput.Normal.z = sqrt(1.0 - dot(pbrInput.Normal.xy, pbrInput.Normal.xy));

    half3x3 tbn = { half3(0, 0, 0), half3(0, 0, 0), normalize(input.normalOS), };
    CalculateTerrainTBN(tbn[2], tbn[0], tbn[1]);
    pbrInput.Normal = (float3)((half3)mul(pbrInput.Normal, tbn));
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
