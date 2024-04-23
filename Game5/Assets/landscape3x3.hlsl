#if defined(VARIANT) && VARIANT == 0
# include "landscape3x3_og.hlsl"
#else
#define PI 3.14159265359

#if __INTELLISENSE__
# define VARIANT 1
#endif
#if !defined(VARIANT)
# define VARIANT 1
#endif

#include <common.hlsl>
#include <temporal.hlsl>
#include <landscapecommon.hlsl>
#include <landscapesampling.hlsl>
#include <basepass.hlsl>

static const half HeightBlend = 5.0;
static const half WeightBlend = 2.0;
static const bool EnableTriplanar = false;
static const bool EnableParallax = false;
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
    half2 dHeightDxz : NORMAL;
};

PSInput VSMain(VSInput input, out float4 positionCS : SV_POSITION) {
    PSInput result;

    float3 worldPos = input.position.xyz;
    float3 worldNrm = input.normal.xyz;
    TransformLandscapeVertex(worldPos, worldNrm, input.offset);
    
    // Sample from the heightmap and offset the vertex
    HeightPoint h = DecodeHeightMap(HeightMap.Load(int3(worldPos.xz, 0), 0));
    worldPos.y += h.HeightOS;
    worldNrm = h.NormalOS;

    result.positionOS = worldPos;
    result.dHeightDxz = (half2)worldNrm.xz / worldNrm.y;
    positionCS = mul(ModelViewProjection, float4(worldPos, 1.0));
        
    return result;
}


struct MSVertex {
    float4 position : POSITION;
    float3 normal : NORMAL;
};
struct MSInstance {
    int2 offset : INSTANCE;
};
struct VertexOut {
    PSInput input;
    float4 positionCS : SV_POSITION;
};
StructuredBuffer<MSVertex> Vertices : register(t5);
StructuredBuffer<MSInstance> Instances : register(t6);
MSVertex GetVertex(uint id) {
    return Vertices[id];
}

[NumThreads(128, 1, 1)]
[OutputTopology("triangle")]
void MSMain(
    uint gtid : SV_GroupIndex,
    uint gid : SV_GroupID,
    out vertices VertexOut verts[64],
    out indices uint3 tris[128]
) {
    uint vertCount = 64;
    uint primCount = 128;
    SetMeshOutputCounts(vertCount, primCount);
    
    uint instanceId = gid;
    uint vertexId = gtid % vertCount;
    uint primId = (gtid % primCount);

    if (vertexId < vertCount) {
        //MSVertex vert = GetVertex(gtid);
        VSInput input = (VSInput)0;
        input.instanceId = instanceId;
        input.position = float4(vertexId % 9, 0, vertexId / 9, 1);//vert.position;
        input.normal = float3(0, 1, 0);//vert.normal;
        input.offset = int2(instanceId, 0);//Instances[instanceId].offset;
        verts[gtid].input = VSMain(input, verts[gtid].positionCS);
    }
    
    uint quadId = primId / 2;
    uint column = quadId % 8;
    uint row = quadId / 8;
    uint3 tri = uint3(
        (column + 0) + (row + 0) * 9,
        (primId % 2) == 0 ? (column + 1) + (row + 0) * 9 : (column + 0) + (row + 1) * 9,
        (column + 1) + (row + 1) * 9
    );
    tris[primId] = min(tri, vertCount);
}

struct ControlPoints3x3 {
    uint4 QuadData;
    bool IsQuadOdd;
    uint3x3 IdMap;
    half2 Interp;
    void Initialise(float2 uv) {
        //uv += 0.5;
        float2 quadUv = QuadReadLaneAt(uv, 0);
        
        uint4 cpD = 0;
#if 1
        quadUv = round(quadUv);
        //uv += 0.5;
        uint2 quadCtr = (uint2)quadUv;
        cpD.w = ControlMap[quadCtr];
        uint quadIndex = WaveGetLaneIndex();
        bool2 quadOdd = bool2((quadIndex & 0x01) != 0, (quadIndex & 0x02) != 0);
        uint4 quadOff = uint4(0, 0, select(quadOdd, 1, -1));
        cpD.z = ControlMap[quadCtr + quadOff.zy];
        cpD.y = ControlMap[quadCtr + quadOff.xw];
        cpD.x = ControlMap[quadCtr + quadOff.zw];
#else
        uint quadIndex = WaveGetLaneIndex();
        bool2 quadOdd = bool2((quadIndex & 0x01) != 0, (quadIndex & 0x02) != 0);
        quadUv = floor(quadUv);
        float2 gatherUv = quadUv + select(quadOdd, 0.5, -0.5);
        cpD = ControlMap.Gather(AnisotropicSampler, gatherUv * _LandscapeSizing1.xy + _LandscapeSizing1.zw).wzxy;
        if (quadOdd.x) cpD = cpD.yxwz;
        if (quadOdd.y) cpD = cpD.zwxy;
#endif
        
        half2 l = (half2)(float2)(uv - quadUv);
        l = 0.5 + select(quadOdd, -l, l);
        
        QuadData = cpD;
        IsQuadOdd = quadOdd.x != quadOdd.y;
        Interp = l;
    }
    uint GetPrimary() { return QuadData.w; }
    void BuildMap() {
        // Build id map
        IdMap._11_12_21_22 = QuadData;
        IdMap._13_23 = QuadReadAcrossX(QuadData.xz);
        IdMap._31_32 = QuadReadAcrossY(QuadData.xy);
        IdMap._33 = QuadReadAcrossDiagonal(QuadData.x);
    }
    half GetMask(uint id, bool isPrimary = false) {
        half3 col = half3(WeightBlend - Interp.x * WeightBlend, WeightBlend, Interp.x * WeightBlend);
        half bSum = dot(1, select(IdMap[0] == id, col, 0));
        half ySum = dot(1, select(IdMap[2] == id, col, 0)) - bSum;
        bSum += dot(1, select(IdMap[1] == id, col, 0).xz);
        bSum += (isPrimary ? WeightBlend : 0);
        return ySum * Interp.y + bSum;
        half sum = 0;
        sum += dot(1, select(IdMap[2] == id, col * Interp.y, 0));
        sum += dot(1, select(IdMap[0] == id, col - col * Interp.y, 0));
        sum += dot(1, select(IdMap[1] == id, col, 0).xz);
        sum += isPrimary ? WeightBlend : 0;
        return sum;
    }
};
struct ControlReducer {
    static const uint InvalidId = 0x0000ffff;
    uint2 OrigItems;
    uint2 Items;
    uint TakenCount;
    void Initialise(ControlPoints3x3 cp3x3) {
        TakenCount = 0;
        OrigItems = cp3x3.IsQuadOdd ? cp3x3.QuadData.xy : cp3x3.QuadData.xz;
        Items = select(OrigItems == cp3x3.GetPrimary(), InvalidId, OrigItems);
    }
    uint TakeItem() {
        uint next = min(Items.x | 0x08000000, Items.y);
        if (TakenCount >= 4) next = Items.x;
        next = min(next, QuadReadAcrossX(next));
        next = min(next, QuadReadAcrossY(next));
        if (TakenCount < 4) next &= InvalidId;
        if (Items.x == next) Items.x = InvalidId;
        if (Items.y == next) Items.y = InvalidId;
        ++TakenCount;
        return next;
    }
    bool HasAnyLeft() {
        return QuadAny((Items.x & Items.y) != InvalidId);
    }
};

struct LayerBlend {
    TerrainSample samples[SampleCount];
    half masks[SampleCount];
    half complexity;
    void BlendHeight(int i, float heightMask) {
        masks[i] += heightMask * HeightBlend;
    }
    void SetAt(int i, TerrainSample sample) {
        samples[i] = sample;
        ++complexity;
    }
    float FinalizeWeights() {
        half maxWeight = 1.0;
        [unroll] for(int i = 0; i < SampleCount; ++i) maxWeight = max(maxWeight, masks[i]);
        maxWeight -= 1.0;
        [unroll] for(int i = 0; i < SampleCount; ++i) masks[i] = saturate(masks[i] - maxWeight);
        half maskFactor = 0.0;
        [unroll] for(int i = 0; i < SampleCount; ++i) maskFactor += masks[i];
        maskFactor = rcp(maskFactor);
        [unroll] for(int i = 0; i < SampleCount; ++i) masks[i] *= maskFactor;
        return maskFactor;
    }
    void BlendAlbedo(inout TerrainSample result, int i) {
        TerrainSample ter = samples[i];
        ter.UnpackAlbedo();
        half amount = masks[i];
        result.Albedo += ter.Albedo * amount;
        result.Height += ter.Height * amount;
    }
    void BlendNrm(inout TerrainSample result, int i) {
        TerrainSample ter = samples[i];
        ter.UnpackNrm();
        half amount = masks[i];
        result.Normal += ter.Normal * amount;
        result.Metallic += ter.Metallic * amount;
        result.Roughness += ter.Roughness * amount;
    }
};

float3 RegenerateNormalY(float2 ddxz) {
    return normalize(float3(ddxz, 1).xzy);
}

void PSMain(PSInput input, out BasePassOutput result, float4 positionCS : SV_POSITION) {
    TerrainSample terResult = (TerrainSample)0;
    
    uint packedDHDXZ = (f32tof16(input.dHeightDxz.x) << 16) | f32tof16(input.dHeightDxz.y);
    //[loop] for(int i = 0; i < 20; ++i)
    {
    TemporalAdjust(input.positionOS.xz);
        
    ControlPoints3x3 cp3x3;
    cp3x3.Initialise(input.positionOS.xz);

    SampleContext context = { BaseMaps, BumpMaps, input.positionOS, positionCS.xy, RegenerateNormalY(input.dHeightDxz) };
                
    terResult = SampleTerrain(context, DecodeControlMap(cp3x3.GetPrimary()), EnableTriplanar);
            
    ControlReducer reducer;
    reducer.Initialise(cp3x3);
    cp3x3.BuildMap();
    float primaryWeight = cp3x3.GetMask(cp3x3.GetPrimary(), true);
        
    if (EnableParallax) {
        float3 localViewDir = mul(InvModelView, float4(0, 0, 0, 1)).xyz - context.WorldPos;
        float rateDiv = 0.01 * primaryWeight * (1.0 / localViewDir.y);
        context.WorldPos.xz += localViewDir.xz * ((terResult.Height - 0.5) * rateDiv);
        terResult = SampleTerrain(context, DecodeControlMap(cp3x3.GetPrimary()), EnableTriplanar);
    }
        
    [branch] if (QuadAny(primaryWeight < (WeightBlend * 4.0 + HeightBlend * 0.5 + 1.0) / 2.0))
    {
        LayerBlend blend = (LayerBlend)0;
            
        uint materialIds[SampleCount];
        uint packed0;
        {
            materialIds[0] = cp3x3.GetPrimary();
            blend.masks[0] = primaryWeight;
            blend.BlendHeight(0, terResult.Height);

            uint secondaryId = reducer.TakeItem();
            materialIds[1] = secondaryId;
            blend.masks[1] = primaryWeight;
            packed0 = (materialIds[1] << 16) | materialIds[0];
            [unroll]
            for (int i = 2; i < SampleCount; ++i) {
                uint id = reducer.TakeItem();
                materialIds[i] = id;
                if (id == ControlReducer::InvalidId) break;
                blend.masks[i] = cp3x3.GetMask(id, false);
                blend.masks[1] += blend.masks[i];
            }
            blend.masks[1] = 4 * WeightBlend - blend.masks[1];
        }
        //materialIds[1] = packed0 >> 16;
        //materialIds[0] = packed0 & 0xffff;

        blend.SetAt(0, terResult);
        terResult = (TerrainSample)0;
        {
            TerrainSample samp1 = SampleTerrain(context, DecodeControlMap(materialIds[1]), EnableTriplanar);
            blend.BlendHeight(1, samp1.Height);
            blend.SetAt(1, samp1);
        }
        blend.complexity = 2;
        [unroll]
        for (int i = 2; i < SampleCount; ++i) {
            uint id = materialIds[i];
            if (id == ControlReducer::InvalidId) break;
            TerrainSample sampI = SampleTerrain(context, DecodeControlMap(id), EnableTriplanar);
            blend.BlendHeight(i, sampI.Height);
            blend.SetAt(i, sampI);
        }
                    
        half maskMul = blend.FinalizeWeights();
            
        blend.BlendAlbedo(terResult, 0);
        blend.BlendNrm(terResult, 0);
        blend.BlendAlbedo(terResult, 1);
        blend.BlendNrm(terResult, 1);
        [branch]
        if (blend.complexity > 2) {
            [unroll]
            for (int i = 2; i < SampleCount; ++i) {
                //TerrainSample sampI = SampleTerrain(context, DecodeControlMap(materialIds[i]), EnableTriplanar);
                //terResult.Normal = sampI.Normal;
                //terResult.Metallic = sampI.Metallic;
                //terResult.Roughness = sampI.Roughness;
                blend.BlendAlbedo(terResult, i);
                //if (blend.ids[i] == ControlReducer::InvalidId) break;
            }
            [unroll]
            for (int i = 2; i < SampleCount; ++i) {
                TerrainSample sampI = SampleTerrain(context, DecodeControlMap(materialIds[i]), EnableTriplanar);
                blend.samples[i].Normal = sampI.Normal;
                blend.samples[i].Metallic = sampI.Metallic;
                blend.samples[i].Roughness = sampI.Roughness;
                blend.BlendNrm(terResult, i);
                //if (blend.ids[i] == ControlReducer::InvalidId) break;
            }
        }
#if defined(COMPLEXITY) && COMPLEXITY
        terResult.Albedo.r += 0.4 * pow(max(0, blend.complexity + 1) / (float)SampleCount, 2);
#endif
    }
    }
#if VARIANT == 1
    terResult.Albedo.g += 0.1;
#else
    terResult.Albedo.g -= 0.1;
#endif

    PBRInput pbrInput = PBRDefault();
    pbrInput.Albedo = terResult.Albedo;
    pbrInput.Normal = terResult.Normal;
    pbrInput.Normal.z = sqrt(1.0 - dot(pbrInput.Normal.xy, pbrInput.Normal.xy));
    pbrInput.Metallic = terResult.Metallic;
    pbrInput.Roughness = terResult.Roughness;

    //float2 dHeightDxz = half2(f16tof32(packedDHDXZ >> 16), f16tof32(packedDHDXZ));
    float3 normalOS = RegenerateNormalY(input.dHeightDxz);
    normalOS.y += 1.0;
    pbrInput.Normal = pbrInput.Normal.xzy * float3(-1, 1, 1);
    pbrInput.Normal = normalOS * dot(normalOS, pbrInput.Normal) * rcp(normalOS.y) - pbrInput.Normal;
    
    pbrInput.Normal = mul((float3x3)ModelView, pbrInput.Normal);
    pbrInput.Normal = normalize(pbrInput.Normal);

    float3 viewPos = mul(ModelView, float4(input.positionOS, 1.0)).xyz;
    float3 viewDir = normalize(viewPos);
    result = PBROutput(pbrInput, viewDir);
    //result.BaseColor.rgb /= LuminanceFactor;
    //result.BaseColor.rgb = pbrInput.Normal * 0.5 + 0.5;
}

void ShadowCast_VSMain(VSInput input, out float4 positionCS : SV_POSITION) {
    VSMain(input, positionCS);
}
[NumThreads(128, 1, 1)]
[OutputTopology("triangle")]
void ShadowCast_MSMain(
    uint gtid : SV_GroupIndex,
    uint gid : SV_GroupID,
    out vertices VertexOut verts[64],
    out indices uint3 tris[128]
) {
    MSMain(gtid, gid, verts, tris);
}
void ShadowCast_PSMain() { }
#endif