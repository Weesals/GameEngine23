// A standard PBR shader (according to various examples online)

#define PI 3.14159265359

#include <common.hlsl>
#include <temporal.hlsl>
#include <lighting.hlsl>
#include <noise.hlsl>
#include <landscapecommon.hlsl>
#include <shadowreceive.hlsl>

cbuffer ConstantBuffer : register(b1) {
    matrix Model;
    matrix ModelView;
    matrix ModelViewProjection;
}

SamplerState BilinearSampler : register(s1);
SamplerState AnisotropicSampler : register(s2);
#if EDGE
    Texture2D<float4> EdgeTex : register(t2);
#else
    Texture2DArray<float4> BaseMaps : register(t2);
    Texture2DArray<float4> BumpMaps : register(t3);
#endif

Texture3D<float4> Tex3D : register(t5);

struct SampleContext {
    Texture2DArray<float4> BaseMaps;
    Texture2DArray<float4> BumpMaps;
    float3 WorldPos;
};
struct ControlPoint {
    float4 ControlPoint;
    float2 TerrainUV;
    float Layer;
    float Rotation;
};
struct TerrainSample {
    float4 Albedo;
    float4 Normal;
    float Height;
};
ControlPoint SampleControlMap(float2 uv) {
    ControlPoint o;
    o.ControlPoint = ControlMap.Load(int3(uv, 0));
    o.TerrainUV = uv;
    o.Layer = o.ControlPoint.r * 255;
    o.Rotation = o.ControlPoint.g * (3.1415 * 2.0 * 255.0 / 256.0);
    return o;
}
TerrainSample SampleTerrain(SampleContext context, ControlPoint cp) {
    TerrainSample o;
    half4 data1 = _LandscapeLayerData1[cp.Layer];
    half4 data2 = _LandscapeLayerData2[cp.Layer];
    float2 uv = context.WorldPos.xz;
    float2 sc;
    sincos(cp.Rotation, sc.x, sc.y);
    float2x2 rot = float2x2(sc.y, -sc.x, sc.x, sc.y);
    uv = mul(rot, uv);
    uv *= data1.x;
    uv.y += data1.y * Time;
    o.Albedo = context.BaseMaps.Sample(AnisotropicSampler, float3(uv, cp.Layer));
    o.Normal = context.BumpMaps.Sample(AnisotropicSampler, float3(uv, cp.Layer));
    o.Normal.xyz = o.Normal.xyz * 2.0 - 1.0;
    o.Normal.y *= -1;
    o.Normal.xy = mul(rot, o.Normal.xy);    //._11_12_21_22
    o.Height = o.Albedo.a;
    //if (o.Height == 0) o.Height = 0.5;
    return o;
}


half3 ApplyHeightBlend(half3 bc, half3 heights) {
    //heights -= min(heights.x, min(heights.y, heights.z));
    //heights /= max(heights.x, max(heights.y, heights.z)) + 0.01;
    
    float minBC = saturate(abs(min(bc.x, min(bc.y, bc.z)) - 0.01) * 100.0);
    //bc = bc.yzx;
    bc += heights * 5.0 * saturate(bc * 5.0);
    float maxBC = max(max(bc.x, max(bc.y, bc.z)), 1.0);
    bc -= (maxBC - 1.0);
    bc = saturate(bc);
    bc /= dot(bc, 1.0);
    //bc *= minBC;
    return bc;

    bc += bc * heights * 3;
    bc += 1 - max(bc.x, max(bc.y, bc.z));
    bc = saturate(bc);
    bc /= dot(bc, 1);
    return bc;
}



struct VSInput {
    uint instanceId : SV_InstanceID;
    float4 position : POSITION;
    float3 normal : NORMAL;
    int2 offset : INSTANCE;
};

struct PSInput {
    float4 position : SV_POSITION;
    float3 normal : NORMAL;
    float3 localPos : TEXCOORD0;
    float3 viewPos : TEXCOORD1;
#if EDGE
    float3 uv : TEXCOORD2;
#endif
};

PSInput VSMain(VSInput input) {
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

    result.localPos = worldPos;
    result.normal = mul(Model, float4(worldNrm, 0.0)).xyz;
    result.viewPos = mul(ModelView, float4(worldPos, 1.0)).xyz;
    result.position = mul(ModelViewProjection, float4(worldPos, 1.0));
        
    return result;
}

float2 PermuteUV(float2 uv, float rnd) {
    rnd *= 3.14 * 2.0;
    return uv.xy * cos(rnd) + uv.yx * sin(rnd) * float2(1, -1);
}
void CalculateTerrainTBN(float3 normal, out float3 tangent, out float3 bitangent) {
    half3 n = normal;
    half4 bc = n.xzxz;
    bc.xy *= (n.z / (n.y + 1.0));
    tangent.x = n.y + bc.y;
    tangent.yz = -bc.zx;
    bitangent = bc.xwy;
    bitangent.z -= 1;
}

float4 PSMain(PSInput input) : SV_TARGET {
    float3 viewDir = normalize(input.viewPos);
    input.normal = normalize(input.normal);
    
    //SimplexSample3D simnoise = SimplexNoise3D(input.viewPos);
    //return float4(simnoise.Sample3(), 1.0);
    
    // TODO: Should be sampled from textures
    float3 Specular = 0.06;
    float Roughness = 0.7;
    float Metallic = 0.0;
    float3 Albedo = 0.0;
    
    float3 shadowPos = ViewToShadow(input.viewPos);
    float shadow = ShadowMap.SampleCmpLevelZero(ShadowSampler, shadowPos.xy, shadowPos.z).r;
    
    for (int i = 0; i < 1; ++i)
    {
#if EDGE
    TemporalAdjust(input.uv.xy);
    Albedo = EdgeTex.Sample(BilinearSampler, input.uv.xy).rgb;
    Albedo = lerp(Albedo, 1, pow(1 - saturate(input.uv.z - input.localPos.y), 4) * 0.25);
#else
    TemporalAdjust(input.localPos.xz);
    SampleContext context = { BaseMaps, BumpMaps, input.localPos };
    Triangle tri = ComputeTriangle(context.WorldPos.xz);
    ControlPoint c0 = SampleControlMap(tri.P0);
    ControlPoint c1 = SampleControlMap(tri.P1);
    ControlPoint c2 = SampleControlMap(tri.P2);
    TerrainSample t0 = (TerrainSample)0, t1 = (TerrainSample)0, t2 = (TerrainSample)0;
    float complexity = 0.0;
    if (tri.BC.x > 0.01) {
        t0 = SampleTerrain(context, c0);
        if (all(c0.ControlPoint.rg == c1.ControlPoint.rg)) { tri.BC.x += tri.BC.y; tri.BC.y = 0.0; }
        if (all(c0.ControlPoint.rg == c2.ControlPoint.rg)) { tri.BC.x += tri.BC.z; tri.BC.z = 0.0; }
        ++complexity;
    }
    if (tri.BC.y > 0.01) {
        t1 = SampleTerrain(context, c1);
        if (all(c1.ControlPoint.rg == c2.ControlPoint.rg)) { tri.BC.y += tri.BC.z; tri.BC.z = 0.0; }
        ++complexity;
    }
    if (tri.BC.z > 0.01) {
        t2 = SampleTerrain(context, c2);
        ++complexity;
    }

    half3 bc = tri.BC;
    bc = ApplyHeightBlend(bc, half3(t0.Height, t1.Height, t2.Height));
    Albedo = (t0.Albedo * bc.x + t1.Albedo * bc.y + t2.Albedo * bc.z).rgb;
    //float3 heights = half3(t0.Height, t1.Height, t2.Height);
    //float height = dot(heights, bc);
    //Albedo = heights;
    float3 Normal = (t0.Normal * bc.x + t1.Normal * bc.y + t2.Normal * bc.z).rgb;
                
    float3 tangent, binormal;
    CalculateTerrainTBN(input.normal, tangent, binormal);
    input.normal = tangent * Normal.x + binormal * Normal.y + input.normal * Normal.z;
    //if (Time % 5 < 1) return float4(input.normal.xxx * 0.5 + 0.5, 1.0);
    //if (Time % 5 < 2) return float4(input.normal.yyy * 0.5 + 0.5, 1.0);
    //if (Time % 5 < 3) return float4(input.normal.zzz * 0.5 + 0.5, 1.0);
    //return float4(input.normal.xzy * 0.5 + 0.5, 1.0);
#endif
    }

    input.normal = mul((float3x3)View, input.normal);

    // The light
    float3 o = ComputeLight(
        Albedo,
        Specular,
        input.normal,
        Roughness,
        _LightColor0 * shadow,
        _ViewSpaceLightDir0,
        -viewDir,
        Metallic
    );
    
    // Indirect
    o += ComputeIndiret(Albedo, Specular, input.normal, Roughness, Metallic, -viewDir);
    //o.rgb = float3(0.5, 0.5, 0.5);

    //float r = abs(frac(Time)-0.5) * 0.3;
    //float dx = sin(Time) * 0.1;
    //float dy = cos(Time) * 0.1;
    //o.rgb = Tex3D.SampleGrad(AnisotropicSampler, input.localPos / 10, float3(dx, 0, 0), float3(0, 0, dy)).rgb;
    //o.rgb += ControlMap.SampleGrad(BilinearSampler, input.localPos.xz / 10, float2(dx, 0), float2(0, dy)).rgb - 0.5;
    //o.rgb = Tex3D.Sample(AnisotropicSampler, input.localPos / 10).rgb;
    
    return float4(o, 1);
}

PSInput ShadowCast_VSMain(VSInput input) {
    return VSMain(input);
}
void ShadowCast_PSMain(PSInput input) {
    //float d = input.position.z;
    //return float4(d.xxx, 1);
}
