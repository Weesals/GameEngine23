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
static const bool ParallaxTransform = true;
static const int ParallaxCount = 1;
static const float ParallaxIntensity = 0.3;
static const bool Var2 = true;
#define ControlCount 9
#define SampleCount 6

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
    result.dHeightDxz = h.DerivativeOS;//(half2)worldNrm.xz / worldNrm.y;
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
    uint4 Inner;
    uint4 Outer;
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
        if (Var2) {
            l += 0.5;
        } else {
            l = 0.5 + select(quadOdd, -l, l);
        }
        
        QuadData = cpD;
        IsQuadOdd = quadOdd.x != quadOdd.y;
        Interp = l;
    }
    uint GetPrimary() { return QuadData.w; }
    void BuildMap() {
        // Build id map
        if (Var2) {
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
        } else{
            IdMap._11_12_21_22 = QuadData;
            IdMap._13_23 = QuadReadAcrossX(QuadData.xz);
            IdMap._31_32 = QuadReadAcrossY(QuadData.xy);
            IdMap._33 = QuadReadAcrossDiagonal(QuadData.x);
        }
        Inner = IdMap._12_23_32_21;
        Outer = IdMap._11_13_33_31;
    }
    bool3 MaskBool(int3 i, bool3 b, int3 l) {
        if (Var2) return select(i >= l, false, b);
        return b;
    }
    half GetMask(int i, uint id, bool isPrimary = false) {
        if (Var2) {
            /*if (!isPrimary) {
                half2 scaleBias = 0;
                if (i < 2) {
                    scaleBias += select(Inner.x == id, WeightBlend, 0.0) * half2(-1, 1);
                }
                if (i < 3) {
                    scaleBias.x += select(Inner.z == id, WeightBlend, 0.0);
                }
                half colV = WeightBlend - Interp.x * WeightBlend;
                scaleBias += select(Outer.x == id, colV, 0.0) * half2(-1, 1);
                scaleBias.x += select(Outer.w == id, colV, 0.0);
            
                colV = Interp.x * WeightBlend;
                scaleBias += select(Outer.y == id, colV, 0.0) * half2(-1, 1);
                scaleBias.x += select(Outer.z == id, colV, 0.0);
                if (i < 4) {
                    scaleBias.y += select(Inner.y == id, colV, 0.0);
                }
                if (i < 1) {
                    scaleBias.y += select(Inner.w == id, colV, 0.0);
                    scaleBias.y += (isPrimary ? WeightBlend : 0);
                }
                return scaleBias.x * Interp.y + scaleBias.y;
            }*/
            half3 col = half3(WeightBlend - Interp.x * WeightBlend, WeightBlend, Interp.x * WeightBlend);
            half bSum = dot(1, select(MaskBool(i, IdMap[0] == id, int3(9, 2, 9)), col, 0));
            half ySum = dot(1, select(MaskBool(i, IdMap[2] == id, int3(9, 3, 9)), col, 0)) - bSum;
            bSum += dot(1, select(MaskBool(i, IdMap[1] == id, int3(0, 0, 4)), col, 0).xz);
            bSum += (isPrimary ? WeightBlend : 0);
            return ySum * Interp.y + bSum;
        } else {
            half3 col = half3(WeightBlend - Interp.x * WeightBlend, WeightBlend, Interp.x * WeightBlend);
            half sum = 0;
            sum += dot(1, select(IdMap[2] == id, col * Interp.y, 0));
            sum += dot(1, select(IdMap[0] == id, col - col * Interp.y, 0));
            sum += dot(1, select(IdMap[1] == id, col, 0).xz);
            sum += isPrimary ? WeightBlend : 0;
            return sum;
        }
    }
};
struct ControlReducer {
    static const uint InvalidId = 0x8000ffff;
    uint2 Items;
    void Initialise(ControlPoints3x3 cp3x3) {
        uint2 localItems = cp3x3.IsQuadOdd ? cp3x3.QuadData.xy : cp3x3.QuadData.xz;
        Items = localItems;
        Items.y |= (WaveGetLaneIndex() & 0x03) << 16;
        Items = select(localItems == cp3x3.GetPrimary(), uint2(InvalidId, Items.x | 0x08000000), Items);
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
        ter.UnpackNrm();
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
        blend.masks[1] = primaryWeight;
        //blend.masks[1] = cp3x3.GetMask(1, materialIds[1], false);
        [unroll]
        for (int i = 2; i < SampleCount; ++i) {
            materialIds[i] = reducer.TakeItem(i - 1);
            //if (id == ControlReducer::InvalidId) break;
            blend.masks[i] = cp3x3.GetMask(i - 1, materialIds[i], false);
            blend.masks[1] += blend.masks[i];
        }
        blend.masks[1] = 4 * WeightBlend - blend.masks[1];
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

BasePassOutput PSMain(PSInput input, float4 positionCS : SV_POSITION) {
    uint packedDHDXZ = (f32tof16(input.dHeightDxz.x) << 16) | f32tof16(input.dHeightDxz.y);
    TemporalAdjust(input.positionOS.xz);
        
    ControlPoints3x3 cp3x3;
    cp3x3.Initialise(input.positionOS.xz);
        
    SampleContext context = { BaseMaps, BumpMaps, input.positionOS, positionCS.xy, RegenerateNormalY(input.dHeightDxz) };
                
    ControlReducer reducer;
    reducer.Initialise(cp3x3);
    cp3x3.BuildMap();
    float primaryWeight = cp3x3.GetMask(-1, cp3x3.GetPrimary(), true);
    float complexity = 0.0;
        
    if (ParallaxCount > 0) {
        float refHeight = 0.5;
        float3 localViewDir = mul(InvModelView, float4(0, 0, 0, 1)).xyz - context.WorldPos;
        if (ParallaxTransform) {
            float3 nrm = float3(input.dHeightDxz.x, 1, input.dHeightDxz.y);
            //nrm = normalize(nrm); // Technically required for TBN, but seems fine without
            half3 t, b; CalculateTerrainTBN(nrm, t, b);
            localViewDir = mul(float3x3(t, nrm, b), localViewDir);
        } else {
            // Hack
            localViewDir.y = max(localViewDir.y, 4);
        }
        float rateDiv = saturate(primaryWeight - (WeightBlend * 4.0 + 1.0) / 2.0);
        [branch]
        if (rateDiv > 0.01)
        {
            complexity++;
            rateDiv *= ParallaxIntensity * rcp(localViewDir.y);
            
            const float TextureSize = 256;
            
            ControlPoint cp = DecodeControlMap(cp3x3.GetPrimary());
            LayerData data = _LandscapeLayerData[cp.Layer];
            float3 worldPos = context.WorldPos * data.Scale;
            float2 dyuv = ddy(worldPos.xz);
            float mipLevel = log2(dot(dyuv, dyuv) * pow(TextureSize, 2)) / 2;
            
            float parHeight = SampleTerrainHeightLevel(context, cp, max(mipLevel, 3.0));
            context.WorldPos.xz += localViewDir.xz * ((parHeight - refHeight) * rateDiv);
            refHeight = parHeight;

            if (ParallaxCount > 1) {
                for (int r = ParallaxCount - 2; r >= 0; --r) {
                    float bias = max(r + 1, 0) * (3.0 / max(ParallaxCount, 1.5));
                    float height = SampleTerrainHeightLevel(context, cp, mipLevel + bias);
                    parHeight = lerp(parHeight, height, (r + 1) / (float)max(ParallaxCount, 1));
                    context.WorldPos.xz += localViewDir.xz * ((parHeight - refHeight) * rateDiv);
                    refHeight = parHeight;
                }
            }
        }
    }
    TerrainSample terResult = SampleTerrain(context, cp3x3, reducer, primaryWeight, complexity);

#if defined(COMPLEXITY) && COMPLEXITY
    float complexityN = max(0, complexity) / (float)(SampleCount + 1);
    terResult.Albedo.r += 0.5 * complexityN;
    terResult.Albedo.g += 0.2 * (complexityN * (1 - complexityN) / 0.25);
#endif
#if VARIANT == 0
    terResult.Albedo.g -= 0.1;
#endif

    PBRInput pbrInput = PBRDefault();
    pbrInput.Albedo = terResult.Albedo;
    pbrInput.Normal = terResult.Normal;
    pbrInput.Normal.z = sqrt(1.0 - dot(pbrInput.Normal.xy, pbrInput.Normal.xy));
    pbrInput.Metallic = terResult.Metallic;
    pbrInput.Roughness = terResult.Roughness;

    float2 dHeightDxz = half2(f16tof32(packedDHDXZ >> 16), f16tof32(packedDHDXZ));
    float3 normalOS = RegenerateNormalY(dHeightDxz);
    normalOS.y += 1.0;
    pbrInput.Normal = pbrInput.Normal.xzy * float3(-1, 1, 1);
    pbrInput.Normal = normalOS * dot(normalOS, pbrInput.Normal) * rcp(normalOS.y) - pbrInput.Normal;
    
    pbrInput.Normal = mul((float3x3)ModelView, pbrInput.Normal);
    pbrInput.Normal = normalize(pbrInput.Normal);

    float3 viewPos = mul(ModelView, float4(input.positionOS, 1.0)).xyz;
    float3 viewDir = normalize(viewPos);
    return PBROutput(pbrInput, viewDir);
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