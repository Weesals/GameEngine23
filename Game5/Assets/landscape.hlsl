#define PI 3.14159265359

#include <common.hlsl>
#include <temporal.hlsl>
#include <landscapecommon.hlsl>
#include <landscapesampling.hlsl>
#include <basepass.hlsl>
#include <noise.hlsl>

static const half HeightBlend = 5.0;
static const half WeightBlend = 2.0;

Texture2D<half4> NoiseTex : register(t5);

cbuffer ConstantBuffer : register(b1) {
    matrix ModelView;
    matrix InvModelView;
    matrix ModelViewProjection;
}

half3 ApplyHeightBlend(half3 bc, half3 heights) {
    //bc += heights * 4.0 * saturate(bc * 4.0);
    bc = bc * WeightBlend + heights * HeightBlend * saturate(bc * 4.0);
    float maxBC = max(max(bc.x, bc.y), max(bc.z, 1.0));
    bc = saturate(bc - (maxBC - 1.0));
    bc *= rcp(dot(bc, 1.0));
    return bc;
}

struct VSInput {
    uint instanceId : SV_InstanceID;
    float4 position : POSITION;
    float3 normal : NORMAL;
    int2 offset : INSTANCE;
};

struct PSInput {
    float3 positionOS : TEXCOORD0;
    float3 normalOS : NORMAL;
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
    HeightPoint h = DecodeHeightMap(HeightMap.Load(int3(worldPos.xz, 0), 0));
    worldPos.y += h.HeightOS;
#if EDGE
    if (input.position.y > 0.5) worldPos.y = -5;
    result.uv = float3(
        float2(input.position.x, worldPos.y) * 0.1,
        h.HeightOS
    );
#else
    worldNrm = h.NormalOS;
#endif

    result.positionOS = worldPos;
    result.normalOS = worldNrm;
    positionCS = mul(ModelViewProjection, float4(worldPos, 1.0));
        
    return result;
}

BasePassOutput PSMain(PSInput input) {    
    PBRInput pbrInput = PBRDefault();
        
    TemporalAdjust(input.positionOS.xz);

    float2 controlUv = input.positionOS.xz;
    //controlUv += NoiseTex.Sample(BilinearSampler, controlUv * 1.0).xy * 0.5 - 0.25;
    //controlUv += CreateSimplex2D(controlUv * 2.0).Sample2() * 0.2;

    controlUv += ddx(controlUv) * (WaveGetLaneIndex() & 0x01 ? -0.5 : 0.5);
    controlUv += ddy(controlUv) * (WaveGetLaneIndex() & 0x02 ? -0.5 : 0.5);

    SampleContext context = { BaseMaps, BumpMaps, input.positionOS, float2(0, 0), float3(0, 1, 0), };
    Triangle tri = ComputeTriangle(controlUv);
    
    // Dont know why this requires /(size+1)
    float2 controlUvCtr = (tri.P0 + tri.P2) * 0.5;
    uint4 cp = ControlMap.Gather(AnisotropicSampler, controlUvCtr * _LandscapeSizing1.xy + _LandscapeSizing1.zw);
    cp = cp.wzxy;
    if(!tri.FlipSign.y) cp = cp.zwxy;
    if(!tri.FlipSign.x) cp = cp.yxwz;
    cp.xyz = uint3(cp[0], cp[tri.TriSign ? 2 : 1], cp[3]);

    TerrainSample t0 = (TerrainSample)0, t1 = (TerrainSample)0, t2 = (TerrainSample)0;
    float complexity = 0.0;
    if (QuadAny(tri.BC.x > 0.1)) {
        //cp.x = QuadReadLaneAt(cp.x, 0);
        t0 = SampleTerrain(context, DecodeControlMap(cp.x));
        if (cp.x == cp.y) { tri.BC.x += tri.BC.y; tri.BC.y = 0.0; }
        if (cp.x == cp.z) { tri.BC.x += tri.BC.z; tri.BC.z = 0.0; }
        ++complexity;
    }
    if (QuadAny(tri.BC.y > 0.1)) {
        //cp.y = QuadReadLaneAt(cp.y, 0);
        t1 = SampleTerrain(context, DecodeControlMap(cp.y));
        if (cp.y == cp.z) { tri.BC.y += tri.BC.z; tri.BC.z = 0.0; }
        ++complexity;
    }
    if (QuadAny(tri.BC.z > 0.1)) {
        t2 = SampleTerrain(context, DecodeControlMap(cp.z));
        ++complexity;
    }

    half3 bc = tri.BC;
    bc = ApplyHeightBlend(bc, half3(t0.Height, t1.Height, t2.Height));
    pbrInput.Albedo = (t0.Albedo * bc.x + t1.Albedo * bc.y + t2.Albedo * bc.z);
    pbrInput.Normal = (t0.Normal * bc.x + t1.Normal * bc.y + t2.Normal * bc.z);
    pbrInput.Metallic = (t0.Metallic * bc.x + t1.Metallic * bc.y + t2.Metallic * bc.z);
    pbrInput.Roughness = (t0.Roughness * bc.x + t1.Roughness * bc.y + t2.Roughness * bc.z);
    float height = (t0.Height * bc.x + t1.Height * bc.y + t2.Height * bc.z);

#if defined(COMPLEXITY) && COMPLEXITY
    pbrInput.Albedo.r = (complexity - 1) / 3.0;
#endif

    pbrInput.Normal.z = sqrt(1.0 - dot(pbrInput.Normal.xy, pbrInput.Normal.xy));
        
    float3x3 tbn = { float3(0, 0, 0), float3(0, 0, 0), normalize(input.normalOS), };
    CalculateTerrainTBN(tbn[2], tbn[0], tbn[1]);
    pbrInput.Normal = mul(pbrInput.Normal, tbn);
    pbrInput.Normal = mul((float3x3)ModelView, pbrInput.Normal);
    pbrInput.Normal = normalize(pbrInput.Normal);
    
    float3 viewPos = mul(ModelView, float4(input.positionOS, 1.0)).xyz;
    float3 viewDir = normalize(viewPos);
    return PBROutput(pbrInput, viewDir);
}

void ShadowCast_VSMain(VSInput input, out float4 positionCS : SV_POSITION) {
    VSMain(input, positionCS);
}
void ShadowCast_PSMain() { }
