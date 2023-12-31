// A standard PBR shader (according to various examples online)

#define PI 3.14159265359

#include "include/common.hlsl"
#include "include/temporal.hlsl"
#include "include/lighting.hlsl"
#include "include/noise.hlsl"
#include "include/landscapecommon.hlsl"

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
Texture2D<float4> ShadowMap : register(t4);
SamplerComparisonState ShadowSampler : register(s3);


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
    o.Normal.xy = mul(rot, o.Normal.xy);
    o.Height = o.Albedo.a;
    if (o.Height == 0) o.Height = 0.5;
    return o;
}


half3 ApplyHeightBlend(half3 bc, half3 heights) {
    heights -= min(heights.x, min(heights.y, heights.z));
    heights /= max(heights.x, max(heights.y, heights.z)) + 0.1;

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
    result.position = mul(ModelViewProjection, float4(worldPos, 1.0));
    result.viewPos = mul(ModelView, float4(worldPos, 1.0)).xyz;
    result.normal = mul(Model, float4(worldNrm, 0.0)).xyz;
        
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

float IGN(float2 pos) {
    pos.x += TemporalFrame * 5.588238;
    return frac(52.9829189f * frac(dot(float2(0.06711056f, 0.00583715f), pos)));
}

float4 PSMain(PSInput input) : SV_TARGET {
    float3 viewDir = normalize(input.viewPos);
    input.normal = normalize(input.normal);
    
    // TODO: Should be sampled from textures
    float3 Specular = 0.06;
    float Roughness = 0.7;
    float Metallic = 0.0;
    float3 Albedo = 0.0;
    
    float4 shadowVPos = mul(ShadowIVViewProjection, float4(input.viewPos, 1.0));
    shadowVPos.xyz /= shadowVPos.w;
    shadowVPos.y *= -1.0;
    //float4 shadowSample = ShadowMap.Sample(BilinearSampler, 0.5 + shadowVPos.xy * 0.5);
    float2 shadowUV = 0.5 + shadowVPos.xy * 0.5;
    float noise = IGN(input.position.xy);
    //shadowUV += (float2(noise, frac(noise * 100)) - 0.5) / 512;
    //return float4(float2(noise, frac(noise * 100)), 0, 1);
    float shadow = ShadowMap.SampleCmpLevelZero(ShadowSampler, shadowUV, shadowVPos.z - 0.002).r;


#if 0
    if (false) {
        float2 p = input.localPos.xz; // / 12.0;
        float2 i = floor(p + dot(p, 1.0) * SF2);
        float2 L = i - dot(i, 1.0) * SG2;
    
        float2 d = p - L;
        float2 o = (d.x > d.y ? float2(1.0, 0.0) : float2(0.0, 1.0));
        float2 p0 = L;
        float2 p1 = L + o - SG2;
        float2 p2 = L + 1.0 - 2.0 * SG2;
        float2 points[] = { p0, p1, p2 };

        float2 RndVec = float2(10.15, 23.78);
        float3 rnd = float3(dot(i, RndVec), dot(i + o, RndVec), dot(i + 1, RndVec));
        rnd = frac(sin(rnd) * 100);
        Albedo = 0;
        float3 w = float3(dot(p - p0, p - p0), dot(p - p1, p - p1), dot(p - p2, p - p2));
        w = max(0.5 - w, 0.0);
        w = pow(w, 4.0);
        w /= dot(w, 1);
        [unroll]
        for (int j = 0; j < 3; ++j)
        {
            float4 control = ControlMap.Load(int3(points[j], 0));
            float4 tex = GrassTexture.Sample(BilinearSampler, float3(PermuteUV(input.localPos.xz * 0.05, rnd[j]), 0));
            tex.rgb = lerp(tex.rgb, tex.grb, control.r * 255);
            Albedo += w[j] * tex.rgb;
        }
    }
#endif
    
#if EDGE
    TemporalAdjust(input.uv.xy);
    Albedo = EdgeTex.Sample(BilinearSampler, input.uv.xy);
    Albedo = lerp(Albedo, 1, pow(1 - saturate(input.uv.z - input.localPos.y), 4) * 0.25);
#else
    TemporalAdjust(input.localPos.xz);
    SampleContext context = { BaseMaps, BumpMaps, input.localPos };
    Triangle tri = ComputeTriangle(context.WorldPos.xz);
    ControlPoint c0 = SampleControlMap(tri.P0);
    ControlPoint c1 = SampleControlMap(tri.P1);
    ControlPoint c2 = SampleControlMap(tri.P2);
    TerrainSample t0 = SampleTerrain(context, c0);
    TerrainSample t1 = SampleTerrain(context, c1);
    TerrainSample t2 = SampleTerrain(context, c2);

    half3 bc = tri.BC;
    bc = ApplyHeightBlend(bc, half3(t0.Height, t1.Height, t2.Height));
    Albedo = t0.Albedo * bc.x + t1.Albedo * bc.y + t2.Albedo * bc.z;
    float3 Normal = t0.Normal * bc.x + t1.Normal * bc.y + t2.Normal * bc.z;
    
    float3 tangent, binormal;
    CalculateTerrainTBN(input.normal, tangent, binormal);
    input.normal = tangent * Normal.x + binormal * Normal.y + input.normal * Normal.z;
    //if (Time % 5 < 1) return float4(input.normal.xxx * 0.5 + 0.5, 1.0);
    //if (Time % 5 < 2) return float4(input.normal.yyy * 0.5 + 0.5, 1.0);
    //if (Time % 5 < 3) return float4(input.normal.zzz * 0.5 + 0.5, 1.0);
    //return float4(input.normal.xzy * 0.5 + 0.5, 1.0);
#endif
    
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
    
    return float4(o, 1);
}

PSInput ShadowCast_VSMain(VSInput input)
{
    return VSMain(input);
}
float4 ShadowCast_PSMain(PSInput input) : SV_TARGET
{
    float d = input.position.z;
    return float4(d.xxx, 1);
}
