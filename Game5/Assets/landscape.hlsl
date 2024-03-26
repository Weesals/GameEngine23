#define PI 3.14159265359

#include <common.hlsl>
#include <temporal.hlsl>
#include <landscapecommon.hlsl>
#include <basepass.hlsl>

cbuffer ConstantBuffer : register(b1) {
    matrix Model;
    matrix ModelView;
    matrix ModelViewProjection;
}

SamplerState AnisotropicSampler : register(s2);
#if EDGE
    Texture2D<float4> EdgeTex : register(t3);
#else
    Texture2DArray<float4> BaseMaps : register(t3);
    Texture2DArray<float4> BumpMaps : register(t4);
#endif

struct SampleContext {
    Texture2DArray<float4> BaseMaps;
    Texture2DArray<float4> BumpMaps;
    float3 WorldPos;
};
struct ControlPoint {
    uint4 Data;
    uint Layer;
    float Rotation;
};
struct TerrainSample {
    float3 Albedo;
    float3 Normal;
    float Height;
    float Metallic;
    float Roughness;
};
ControlPoint DecodeControlMap(uint cp) {
    ControlPoint o;
    o.Data = ((cp >> uint4(0, 8, 16, 24)) & 0xff);
    o.Layer = o.Data.r;
    o.Rotation = o.Data.g * (3.1415 * 2.0 / 256.0);
    return o;
}
TerrainSample SampleTerrain(SampleContext context, ControlPoint cp) {
    TerrainSample o;
    LayerData data = _LandscapeLayerData[cp.Layer];
    
    float2 uv = context.WorldPos.xz;
    float2 sc; sincos(cp.Rotation, sc.x, sc.y);
    float2x2 rot = float2x2(sc.y, -sc.x, sc.x, sc.y);
    uv = mul(rot, uv);
    uv *= data.Scale;
    uv.y += data.UVScrollY * Time;
    
    float3 bumpSample = context.BumpMaps.Sample(AnisotropicSampler, float3(uv, cp.Layer));
    o.Normal = bumpSample.rgb * 2.0 - 1.0;
    o.Normal.y *= -1;
    o.Normal.xy = mul(rot, o.Normal.xy);
    o.Metallic = data.Metallic;
    o.Roughness = data.Roughness;
    
    float4 baseSample = context.BaseMaps.Sample(AnisotropicSampler, float3(uv, cp.Layer));
    o.Albedo = baseSample.rgb;
    o.Height = baseSample.a;
    
    return o;
}

half3 ApplyHeightBlend(half3 bc, half3 heights) {
    bc += heights * 4.0 * saturate(bc * 4.0);
    float maxBC = max(max(bc.x, bc.y), max(bc.z, 1.0));
    bc = saturate(bc - (maxBC - 1.0));
    bc *= rcp(dot(bc, 1.0));
    return bc;
}

void CalculateTerrainTBN(float3 n, out float3 tangent, out float3 bitangent) {
    half4 bc = n.xzxz;
    bc.xy *= n.z * rcp(n.y + 1.0);
    tangent.x = n.y + bc.y;
    tangent.yz = -bc.zx;
    bitangent = bc.xwy;
    bitangent.z -= 1;
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
    
#if EDGE
    TemporalAdjust(input.uv.xy);
    pbrInput.Albedo = EdgeTex.Sample(AnisotropicSampler, input.uv.xy).rgb;
    pbrInput.Albedo = lerp(pbrInput.Albedo, 1, pow(1 - saturate(input.uv.z - input.positionOS.y), 4) * 0.25);
#else
    TemporalAdjust(input.positionOS.xz);
    SampleContext context = { BaseMaps, BumpMaps, input.positionOS };
    Triangle tri = ComputeTriangle(context.WorldPos.xz);
    
    // Dont know why this requires /(size+1)
    uint4 cp = ControlMap.Gather(AnisotropicSampler, context.WorldPos.xz * _LandscapeSizing1.xy + _LandscapeSizing1.zw);
    cp.xyzw = uint4(min(cp.xy, cp.zw), max(cp.xy, cp.zw));
    cp.xzyw = uint4(min(cp.xz, cp.yw), max(cp.xz, cp.yw));
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
                
    pbrInput.Normal.z = sqrt(1.0 - dot(pbrInput.Normal.xy, pbrInput.Normal.xy));
        
    float3x3 tbn = { float3(0, 0, 0), float3(0, 0, 0), normalize(input.normalOS), };
    CalculateTerrainTBN(tbn[2], tbn[0], tbn[1]);
    pbrInput.Normal = mul(pbrInput.Normal, tbn);
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
