#define PI 3.14159265359

#include <common.hlsl>
#include <temporal.hlsl>
#include <landscapecommon.hlsl>
#include <landscapesampling.hlsl>
#include <basepass.hlsl>

cbuffer ConstantBuffer : register(b1) {
    matrix ModelView;
    matrix InvModelView;
    matrix ModelViewProjection;
}

half3 ApplyHeightBlend(half3 bc, half3 heights) {
    bc += heights * 4.0 * saturate(bc * 4.0);
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
    result.normalOS = worldNrm;
    positionCS = mul(ModelViewProjection, float4(worldPos, 1.0));
        
    return result;
}

void PSMain(PSInput input, out BasePassOutput result) {    
    PBRInput pbrInput = PBRDefault();
        
    [loop] for(int i = 0; i < 50; ++i)
    {
    TemporalAdjust(input.positionOS.xz);
    SampleContext context = { BaseMaps, BumpMaps, input.positionOS, float2(0, 0), float3(0, 1, 0), };
    Triangle tri = ComputeTriangle(context.WorldPos.xz);
    
    // Dont know why this requires /(size+1)
    uint4 cp = ControlMap.Gather(AnisotropicSampler, context.WorldPos.xz * _LandscapeSizing1.xy + _LandscapeSizing1.zw);
    cp = Sort(cp);
    cp.xyz = uint3(cp[0], cp[tri.TriSign ? 2 : 1], cp[3]) & 0x3fffffff;
        
    TerrainSample t0 = (TerrainSample)0, t1 = (TerrainSample)0, t2 = (TerrainSample)0;
    float complexity = 0.0;
    if (tri.BC.x > 0.01) {
        t0 = SampleTerrain(context, DecodeControlMap(cp.x));
        if (cp.x == cp.y) { tri.BC.x += tri.BC.y; tri.BC.y = 0.0; }
        if (cp.x == cp.z) { tri.BC.x += tri.BC.z; tri.BC.z = 0.0; }
        ++complexity;
    }
    if (tri.BC.y > 0.01) {
        t1 = SampleTerrain(context, DecodeControlMap(cp.y));
        if (cp.y == cp.z) { tri.BC.y += tri.BC.z; tri.BC.z = 0.0; }
        ++complexity;
    }
    if (tri.BC.z > 0.01) {
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
    }

    pbrInput.Normal.z = sqrt(1.0 - dot(pbrInput.Normal.xy, pbrInput.Normal.xy));
        
    float3x3 tbn = { float3(0, 0, 0), float3(0, 0, 0), normalize(input.normalOS), };
    CalculateTerrainTBN(tbn[2], tbn[0], tbn[1]);
    pbrInput.Normal = mul(pbrInput.Normal, tbn);
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
