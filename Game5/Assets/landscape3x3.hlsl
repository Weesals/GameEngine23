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
#if !defined(ENABLEPARALLAX)
# define ENABLEPARALLAX 0
#endif

#include <common.hlsl>
#include <temporal.hlsl>
#include <landscapecommon.hlsl>
#include <landscapesampling.hlsl>
#include <basepass.hlsl>

static const half HeightBlend = 5.0;
static const half WeightBlend = 2.0;
static const bool EnableTriplanar = false;
static const bool ParallaxTransform = true;
static const int ParallaxCount = ENABLEPARALLAX ? 1 : 0;
static const float ParallaxIntensity = 0.3;
static const float DepthScale = 0.3;
#define ControlCount 9
#define SampleCount 6

cbuffer ConstantBuffer : register(b1) {
    matrix ModelView;
    matrix InvModelView;
    matrix ModelViewProjection;
    matrix InvViewProjection;
    matrix Projection;
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

PSInput VertMain(VSInput input, out float4 positionCS : SV_POSITION, float depthBias = 0.0) {
    PSInput result;

    float3 worldPos = input.position.xyz;
    float3 worldNrm = input.normal.xyz;
    TransformLandscapeVertex(worldPos, worldNrm, input.offset);
    
    // Sample from the heightmap and offset the vertex
    HeightPoint h = DecodeHeightMap(HeightMap.Load(int3(worldPos.xz, 0), 0));
    worldPos.y += h.HeightOS;
    worldNrm = h.NormalOS;

    result.positionOS = worldPos;
    result.dHeightDxz = h.DerivativeOS;//(half2)worldNrm.xz / worldNrm.y;
    positionCS = mul(ModelViewProjection, float4(worldPos, 1.0));
        
    float4 clipZ = mul(InvViewProjection, float4(positionCS.xy, 0.0, positionCS.w));
    float4 projZ = transpose(InvViewProjection)[2];
    float3 dirWS = normalize(projZ.xyz * clipZ.w - clipZ.xyz * projZ.w);
    float depthOffset = depthBias / dirWS.y;
    positionCS.z += depthOffset * Projection._33;
    positionCS.z *= positionCS.w / (positionCS.w + depthOffset * Projection._43);

    return result;
}

PSInput VSMain(VSInput input, out float4 positionCS : SV_POSITION) {
    PSInput output = VertMain(input, positionCS, 0.5 * DepthScale);

    return output;
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
        uint quadIndex = WaveGetLaneIndex();
        bool2 quadOdd = bool2((quadIndex & 0x01) != 0, (quadIndex & 0x02) != 0);
        float2 quadUv = round(QuadReadLaneAt(uv, 0));
        
#if 0
        uint2 quadCtr = (uint2)quadUv;
        QuadData.w = ControlMap[quadCtr];
        uint4 quadOff = uint4(0, 0, select(quadOdd, 1, -1));
        QuadData.z = ControlMap[quadCtr + quadOff.zy];
        QuadData.y = ControlMap[quadCtr + quadOff.xw];
        QuadData.x = ControlMap[quadCtr + quadOff.zw];
#else
        float2 gatherUv = quadUv * _LandscapeSizing1.xy + select(quadOdd, _LandscapeSizing1.xy, 0);
        QuadData = ControlMap.Gather(AnisotropicSampler, gatherUv).wzxy;
        if (quadOdd.x) QuadData = QuadData.yxwz;
        if (quadOdd.y) QuadData = QuadData.zwxy;
#endif
        
        IsQuadOdd = quadOdd.x != quadOdd.y;
        Interp = (half2)(float2)(uv - quadUv) + 0.5;
    }
    uint GetPrimary() { return QuadData.w; }
    void BuildMap() {
        // Build id map
        uint quadIndex = WaveGetLaneIndex();
        bool2 quadOdd = bool2((quadIndex & 0x01) != 0, (quadIndex & 0x02) != 0);
        uint acrossY = QuadReadAcrossY(QuadData.y);
        uint acrossX = QuadReadAcrossX(QuadData.z);
        IdMap._22 = QuadData.w;
        IdMap._12 = select(quadOdd.y, acrossY, QuadData.y);
        IdMap._32 = select(quadOdd.y, QuadData.y, acrossY);
        IdMap._21 = select(quadOdd.x, acrossX, QuadData.z);
        IdMap._23 = select(quadOdd.x, QuadData.z, acrossX);
        IdMap._11 = QuadReadLaneAt(QuadData.x, 0);
        IdMap._13 = QuadReadLaneAt(QuadData.x, 1);
        IdMap._31 = QuadReadLaneAt(QuadData.x, 2);
        IdMap._33 = QuadReadLaneAt(QuadData.x, 3);
    }
    half GetMask(int i, uint id) {
        half3 col = half3(WeightBlend - Interp.x * WeightBlend, WeightBlend, Interp.x * WeightBlend);
        half bSum = dot(1, select(select(i >= int3(9, 2, 9), false, IdMap[0] == id), col, 0));
        half ySum = dot(1, select(select(i >= int3(9, 3, 9), false, IdMap[2] == id), col, 0)) - bSum;
        bSum += dot(1, select(select(i >= int3(0, 0, 4), false, IdMap[1] == id), col, 0).xz);
        bSum += (i == -1 ? WeightBlend : 0);
        return ySum * Interp.y + bSum;
    }
};
struct ControlReducer {
    static const uint InvalidId = 0x8000ffff;
    uint2 Items;
    void Initialise(ControlPoints3x3 cp3x3) {
        uint2 localItems = cp3x3.IsQuadOdd ? cp3x3.QuadData.xy : cp3x3.QuadData.xz;
        Items = localItems;
        Items.y |= (WaveGetLaneIndex() & 0x03) << 16;
        Items.x = select(localItems.x == cp3x3.GetPrimary(), InvalidId, Items.x);
        Items.y = select(localItems.y == cp3x3.GetPrimary(), Items.x | 0x08000000, Items.y);
    }
    uint TakeItem(int TakenCount) {
        uint next = TakenCount < 4 ? Items.y : Items.x;
        next = min(next, QuadReadAcrossX(next));
        next = min(next, QuadReadAcrossY(next));
        if (TakenCount < 4) next &= InvalidId;
        if (Items.x == next) Items.x = InvalidId;
        if ((Items.y & InvalidId) == next) Items.y = Items.x | 0x08000000;
        return next;
    }
    bool HasAnyLeft(int TakenCount = 0) {
        if (TakenCount >= 4) return QuadAny(Items.x != InvalidId);
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
    }
    void FinalizeWeights() {
        half maxWeight = 1.0;
        [unroll] for(int i = 0; i < SampleCount; ++i) maxWeight = max(maxWeight, masks[i]);
        maxWeight -= 1.0;
        [unroll] for(int i = 0; i < SampleCount; ++i) masks[i] = saturate(masks[i] - maxWeight);
        half maskFactor = 0.0;
        [unroll] for(int i = 0; i < SampleCount; ++i) maskFactor += masks[i];
        maskFactor = rcp(maskFactor);
        [unroll] for(int i = 0; i < SampleCount; ++i) masks[i] *= maskFactor;
    }
    void SampleNormal(int i, SampleContext context, int id) {
        TerrainSample sampI = SampleTerrain(context, DecodeControlMap(id), EnableTriplanar);
        samples[i].Normal = sampI.Normal;
        samples[i].Height = sampI.Height;
    }
    void SampleAlbedo(int i, SampleContext context, int id) {
        TerrainSample sampI = SampleTerrain(context, DecodeControlMap(id), EnableTriplanar);
        samples[i].Albedo = sampI.Albedo;
        samples[i].Metallic = sampI.Metallic;
        samples[i].Roughness = sampI.Roughness;
    }
    void BlendAlbedo(inout TerrainSample result, int i) {
        TerrainSample ter = samples[i];
        //ter.UnpackAlbedo();
        half amount = masks[i];
        result.Albedo += ter.Albedo * amount;
        result.Metallic += ter.Metallic * amount;
        result.Roughness += ter.Roughness * amount;
        complexity += 0.5;
    }
    void BlendNrm(inout TerrainSample result, int i) {
        TerrainSample ter = samples[i];
        //ter.UnpackNrm();  // Potentially slower on intel
        half amount = masks[i];
        result.Normal += ter.Normal * amount;
        result.Height += ter.Height * amount;
        complexity += 0.5;
    }
};

float3 RegenerateNormalY(float2 ddxz) {
    return normalize(float3(ddxz, 1).xzy);
}

TerrainSample SampleTerrain(SampleContext context, ControlPoints3x3 cp3x3, ControlReducer reducer
    , float primaryWeight
    , inout float complexity
) {
    //context.WorldPos.x += Time;
    TerrainSample samp0 = SampleTerrain(context, DecodeControlMap(cp3x3.GetPrimary()), EnableTriplanar);
    [branch]
    if (SampleCount == 1 || QuadAny(primaryWeight >= (WeightBlend * 4.0 + HeightBlend * 0.55 + 1.0) / 2.0)) {
        return samp0;
    }
    LayerBlend blend = (LayerBlend)0;
            
    uint materialIds[SampleCount];
    {
        materialIds[0] = cp3x3.GetPrimary();
        blend.masks[0] = primaryWeight;
        blend.BlendHeight(0, samp0.Height);

        materialIds[1] = reducer.TakeItem(0);
        //blend.masks[1] = cp3x3.GetMask(1, materialIds[1]);
        [unroll]
        for (int i = 2; i < SampleCount; ++i) {
            materialIds[i] = reducer.TakeItem(i - 1);
            //if (id == ControlReducer::InvalidId) break;
            blend.masks[i] = cp3x3.GetMask(i - 1, materialIds[i]);
            primaryWeight += blend.masks[i];
        }
        blend.masks[1] = 4 * WeightBlend - primaryWeight;
    }

    blend.SetAt(0, samp0);
    {
        TerrainSample samp1 = SampleTerrain(context, DecodeControlMap(materialIds[1]), EnableTriplanar);
        blend.BlendHeight(1, samp1.Height);
        blend.SetAt(1, samp1);
    }
    [unroll]
    for (int i = 2; i < SampleCount; ++i) {
        uint id = materialIds[i];
        if (id == ControlReducer::InvalidId) break;
        TerrainSample sampI = SampleTerrain(context, DecodeControlMap(id), EnableTriplanar);
        blend.BlendHeight(i, sampI.Height);
        blend.SetAt(i, sampI);
    }
                    
    blend.FinalizeWeights();
            
    TerrainSample terResult = (TerrainSample)0;
    blend.BlendNrm(terResult, 0);
    blend.BlendNrm(terResult, 1);
            
    blend.SampleAlbedo(0, context, materialIds[0]);
    blend.BlendAlbedo(terResult, 0);
    blend.SampleAlbedo(1, context, materialIds[1]);
    blend.BlendAlbedo(terResult, 1);
            
    [branch]
    if (SampleCount > 2 && materialIds[2] != ControlReducer::InvalidId) {
        [unroll]
        for (int i = 2; i < SampleCount; ++i) {
            if (blend.masks[i] <= 0) continue;
            //if (blend.ids[i] == ControlReducer::InvalidId) break;
            blend.BlendNrm(terResult, i);
            //if (blend.ids[i] == ControlReducer::InvalidId) break;
            blend.SampleAlbedo(i, context, materialIds[i]);
            blend.BlendAlbedo(terResult, i);
        }
    }
    complexity += blend.complexity;
    return terResult;
}

BasePassOutput PSMain(PSInput input, linear centroid noperspective float4 positionCS : SV_POSITION, out float depth : SV_DepthGreaterEqual0) {
    //uint packedDHDXZ = (f32tof16(input.dHeightDxz.x) << 16) | f32tof16(input.dHeightDxz.y);
    TemporalAdjust(input.positionOS.xz);
        
    ControlPoints3x3 cp3x3;
    cp3x3.Initialise(input.positionOS.xz);
        
    SampleContext context = { BaseMaps, BumpMaps, input.positionOS, positionCS.xy, RegenerateNormalY(input.dHeightDxz) };
                
    ControlReducer reducer;
    reducer.Initialise(cp3x3);
    cp3x3.BuildMap();
    float primaryWeight = cp3x3.GetMask(-1, cp3x3.GetPrimary());
    float complexity = 0.0;
        
    if (ParallaxCount > 0) {
        float3 localViewDir = mul(InvModelView, float4(0, 0, 0, 1)).xyz - context.WorldPos;
        if (ParallaxTransform) {
            float3 nrm = float3(input.dHeightDxz.x, 1, input.dHeightDxz.y);
            if (true) {
                nrm = normalize(nrm); // Technically required for TBN, but seems fine without
                half3 t, b; CalculateTerrainTBN(nrm, t, b);
                localViewDir = mul(float3x3(t, nrm, b), localViewDir);
            } else {
                nrm = normalize(nrm) + float3(0, 1, 0);
                //nrm *= float3(-1, 1, -1);
                localViewDir = localViewDir * float3(1, 1, 1);
                localViewDir += nrm * dot(nrm, localViewDir) * rcp(nrm.y);
            }
        } else { // Hack
            localViewDir.y = max(localViewDir.y, 4);
        }
        float rateDiv = saturate(primaryWeight - (WeightBlend * 4.0 + 1.0) / 2.0);
        [branch]
        if (rateDiv > 0.01)
        {
            complexity++;
            rateDiv *= ParallaxIntensity * (rcp(localViewDir.y));
            
            const float TextureSize = 256;
            
            ControlPoint cp = DecodeControlMap(cp3x3.GetPrimary());
            LayerData data = _LandscapeLayerData[cp.Layer];
            float3 worldPos = context.WorldPos * data.Scale;
            float2 dyuv = ddy(worldPos.xz);
            float mipLevel = log2(dot(dyuv, dyuv) * pow(TextureSize, 2)) / 2;
            mipLevel = context.BumpMaps.CalculateLevelOfDetail(BilinearSampler, worldPos.xz);
            
            float refHeight = 0.5;
            float parHeight = SampleTerrainHeightLevel(context, cp, max(mipLevel, 3.0));
            context.WorldPos.xz += localViewDir.xz * ((parHeight - refHeight) * rateDiv);
            refHeight = parHeight;

            if (ParallaxCount > 1) {
                for (int r = ParallaxCount - 2; r >= 0; --r) {
                    float bias = max(r + 1, 0) * (3.0 / max(ParallaxCount, 1.5));
                    float height = SampleTerrainHeightLevel(context, cp, mipLevel + bias);
                    height = lerp(refHeight, height, (r + 1) / (float)max(ParallaxCount, 1));
                    context.WorldPos.xz += localViewDir.xz * ((height - refHeight) * rateDiv);
                    refHeight = height;
                }
            }
        }
    }
    TerrainSample terResult = SampleTerrain(context, cp3x3, reducer, primaryWeight, complexity);

#if defined(COMPLEXITY) && COMPLEXITY
    float complexityN = max(0, complexity) / (float)(SampleCount + 1);
    terResult.Albedo.r = 0.6 * saturate(complexityN * 3 - 1);
    terResult.Albedo.g = 0.4 * saturate(min(complexityN * 3, 3 - complexityN * 3));
    terResult.Albedo.b *= 0.5;
    //terResult.Normal.xy *= 0.5;
#endif
#if VARIANT == 0
    terResult.Albedo.g -= 0.1;
#endif

    PBRInput pbrInput = PBRDefault();
    pbrInput.Albedo = terResult.Albedo;
    pbrInput.Normal = terResult.Normal;
    pbrInput.Metallic = terResult.Metallic;
    pbrInput.Roughness = terResult.Roughness;

    float3 normalOS = half3(input.dHeightDxz, 1).xzy;//half3(f16tof32(packedDHDXZ >> 16), 1, f16tof32(packedDHDXZ));
    if (false) {
        //pbrInput.Normal = pbrInput.Normal.xyz;
        normalOS = normalize(normalOS);
        half3 t, b; CalculateTerrainTBN(normalOS, t, b);
        pbrInput.Normal.z = sqrt(1.0 - dot(pbrInput.Normal.xy, pbrInput.Normal.xy));
        pbrInput.Normal = pbrInput.Normal.xzy * float3(1, 1, -1);
        pbrInput.Normal = mul(pbrInput.Normal, float3x3(t, normalOS, b));
    } else {
        normalOS = normalize(normalOS) + float3(0, 1, 0);
        pbrInput.Normal.z = sqrt(1.0 - dot(pbrInput.Normal.xy, pbrInput.Normal.xy));
        pbrInput.Normal = pbrInput.Normal.xzy * float3(1, -1, 1);
        pbrInput.Normal -= normalOS * dot(normalOS, pbrInput.Normal) * rcp(normalOS.y);
        //pbrInput.Normal.y = sqrt(1.0 - dot(pbrInput.Normal.xz, pbrInput.Normal.xz));
    }
    
    pbrInput.Normal = mul((float3x3)ModelView, pbrInput.Normal);
    pbrInput.Normal = normalize(pbrInput.Normal);

    float3 viewPos = mul(ModelView, float4(input.positionOS, 1.0)).xyz;
    float3 viewDir = normalize(viewPos);

    float3 localViewDirY = normalize(mul(InvModelView, float4(0, 0, 0, 1)).xyz - context.WorldPos).y;
    float depthOffset = (1 - terResult.Height) * (DepthScale / localViewDirY);
    depth = (positionCS.z * positionCS.w + depthOffset * Projection._33) / (positionCS.w + depthOffset * Projection._43);
    //depth = positionCS.z + max(0, depthOffset / (positionCS.w * positionCS.w));
    //pbrInput.Albedo = frac(depth * 100);

    return PBROutput(pbrInput, viewDir);
}

void ShadowCast_VSMain(VSInput input, out float4 positionCS : SV_POSITION) {
    VertMain(input, positionCS, 0.5 * DepthScale);
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