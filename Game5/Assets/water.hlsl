// A standard PBR shader (according to various examples online)

#define PI 3.14159265359


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
    

#include "include/common.hlsl"
#include "include/temporal.hlsl"
#include "include/lighting.hlsl"
#include "include/noise.hlsl"
#include "include/landscapecommon.hlsl"

cbuffer ConstantBuffer : register(b1) {
    matrix Model;
    matrix ModelView;
    matrix ModelViewProjection;
    matrix InvModelViewProjection;
    matrix Projection;
}

SamplerState BilinearSampler : register(s1);
SamplerState AnisotropicSampler : register(s2);
Texture2D<float4> NoiseTex : register(t1);
Texture2D<float4> FoamTex : register(t2);
Texture2D<float4> ShadowMap : register(t4);
Texture2D<float4> SceneDepth : register(t5);
SamplerComparisonState ShadowSampler : register(s3);



struct VSInput {
    uint instanceId : SV_InstanceID;
    float4 position : POSITION;
    uint2 offset : INSTANCE;
};

struct PSInput {
    float4 position : SV_POSITION;
    float3 normal : NORMAL;
    float3 worldPos : TEXCOORD0;
    float4 waterPos : TEXCOORD1;
    float3 viewPos : TEXCOORD2;
    float3 viewDirWS : TEXCOORD3;
};

PSInput VSMain(VSInput input) {
    const float Scale = 15;
    PSInput result;

    float3 worldPos = input.position.xyz;
    float3 worldNrm = float3(0, 1, 0);
    TransformLandscapeVertex(worldPos, worldNrm, input.offset);

    // Sample from the heightmap and offset the vertex
    float4 hcell = HeightMap.Load(int3(worldPos.xz, 0), 0);
    float terrainHeight = _LandscapeScaling.z + (hcell.r) * _LandscapeScaling.w;
    float waterHeight = (hcell.a * 255 - 127) * 8 / 128;
    worldPos.y += waterHeight;
    if (hcell.a == 0) worldPos.y = 0.0f / 0.0f;
    
#if EDGE
    if (input.position.y > 0.5) worldPos.y = terrainHeight;
#endif

    result.position = mul(ModelViewProjection, float4(worldPos, 1.0));
    result.normal = mul(Model, float4(worldNrm, 0.0)).xyz;
    result.worldPos = mul(Model, float4(worldPos, 1.0)).xyz;
    result.waterPos = float4(worldPos, waterHeight - terrainHeight);
    result.viewPos = mul(ModelView, float4(worldPos, 1.0)).xyz;
    
    float4 vCamPos4 = result.position;
    vCamPos4.xyz /= vCamPos4.w;
    vCamPos4.z = 0.0;
    vCamPos4.w = 1.0;
    float4 camPos = mul(InvModelViewProjection, vCamPos4);
    camPos.xyz /= camPos.w;
    camPos.xyz = -float3(
        dot(View._m00_m10_m20, View._m03_m13_m23),
        dot(View._m01_m11_m21, View._m03_m13_m23),
        dot(View._m02_m12_m22, View._m03_m13_m23)
    );
    result.viewDirWS = result.worldPos - camPos.xyz;
        
#if defined(VULKAN)
    result.position.y = -result.position.y;
#endif

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
    TemporalAdjust(input.worldPos.xz);
    
    float2 normalizedScreenSpaceUV = input.position.xy / 1024;
    float3 viewDirectionWS = normalize(input.viewPos);
    float3 positionWS = input.worldPos.xyz;
    float3 normalWS = input.normal;
    float2 refrUV = normalizedScreenSpaceUV;
    float3 viewDir = normalize(input.viewDirWS);
    float waterHeight = positionWS.y;
    float waterDepth = input.waterPos.w;
    
    normalWS = normalize(normalWS);

    float4 noise = NoiseTex.Sample(BilinearSampler, input.worldPos.xz * 0.2);
    normalWS.xz = (noise.xy * 2 - 1);
    float rot = Time + dot(input.worldPos.xz, float2(0.14, 0.12) * 2);
    normalWS.xz = normalWS.xz * cos(rot) + normalWS.zx * float2(-1, 1) * sin(rot);
    normalWS.xz += sin(normalWS.zx * 20 + Time * 2) * 0.1;
    float roughness = length(normalWS.xz);
    //Normal.xz /= 1 + dot(Normal.xz, Normal.xz);
    normalWS.xz *= saturate(waterDepth / 3.0) * 0.5;
    normalWS.y = sqrt(saturate(1 - dot(normalWS.xz, normalWS.xz)));
    //WorldPos.y += (dot(abs(Normal.xz), 1) - 0.2) * 0.2;

    const float3 SkyColor = float3(135, 206, 235) / 255 * 0.5;
    const float3 CloudColor = float3(0.8, 0.8, 0.8);
    const float3 GroundColor = float3(0.25, 0.3, 0.1);
    const float3 HorizonColor = float3(.5, .35, .2);
    const half3 ExtinctCoefs = half3(0.35, 0.09, 0.03) * -15;
    //const half3 ScatterCoefs = half3(0.007, 0.021, 0.025) * -20;
    const half3 ScatterCoefs = half3(0.0007, 0.0021, 0.0025) * -15;
    const float3 FoamColor = float3(0.9, 0.9, 0.9);
        
    const float3 mainLightColor = 0.8;
    
    half3 transmittance = 1;
    half3 inscatter = 0;

    half4 color = 1;
    
    float near = Projection._34 / Projection._33;
    float far = Projection._34 / (Projection._33 - 1);
    float4 _ZBufferParams = float2(1 - far / near, far / near).xyxy;
    _ZBufferParams.zw /= far;
    float opticalDepth = max(0,
        -1.0 / (_ZBufferParams.z * SceneDepth[input.position.xy].r + _ZBufferParams.w) -
        -1.0 / (_ZBufferParams.z * input.position.z + _ZBufferParams.w)
    );
    transmittance = exp2(opticalDepth * ExtinctCoefs);

    half3 scatterLum = (ScatterCoefs / ExtinctCoefs) * mainLightColor;
    inscatter = (scatterLum - scatterLum * transmittance);

    half3 normal = normalWS;
        //normal.xz *= saturate(opticalDepth / 2);
    normal = normalize(normal);

    half3 viewRefl = reflect(viewDir, normal);
    half3 ambientRefl = SkyColor;

    float2 cloudUV = (positionWS + viewRefl / viewRefl.y * 10).xz / 40;
    cloudUV.x -= 0.01 * Time;
    float cloud = NoiseTex.SampleLevel(BilinearSampler, cloudUV, 0).r;
    cloud = sqrt(saturate(cloud * 5 - 2.5));
    ambientRefl = lerp(ambientRefl, CloudColor, cloud);
    ambientRefl = lerp(ambientRefl, HorizonColor, pow(saturate(1 - viewRefl.y), 1));
    ambientRefl = lerp(ambientRefl, GroundColor, saturate(-viewRefl.y));
    
    //return float4(cloud.xxx, 1.0);
    
#if defined(_ISEDGE)
    ambientRefl = 0;
#endif

    float fresnel = lerp(0.04, 1.0, pow(1.0 - max(0.0, dot(-viewDir, normal)), 4.0));
    inscatter += ambientRefl * (fresnel * saturate(opticalDepth * 5));

    refrUV += (refract(viewDir, normal, 1 / 1.3) - viewDir).xz * (opticalDepth * 0.03);

    float2 foamUv = positionWS.xz / 10;
    float foamI = saturate(1 - waterDepth * 3) * 1.2;
    // Swish foam in deep waters
    foamUv += sin(foamUv.yx * 20 + Time * 0.5) * 0.05 * waterDepth;

    float foam = foamI;
    // Animate by flipping 3 frames toward shore
    half3 fweights = saturate(1 - abs(
        frac(Time * 0.3 - foam * 1 - float3(0, 0.333, 0.666)) - 0.5
    ) * 3);
    // Reduce animation near shoreline
    fweights = lerp(fweights, 0.333, saturate(1 - waterDepth * 5) * 0.5);
    // Apply frames to foam intensity
    half3 foamMask = FoamTex.Sample(BilinearSampler, foamUv);
    foam += dot(foamMask, fweights) - 1;
    // Fade out foam at shore
    foam -= saturate(0.75 - opticalDepth * 15);
    foam = saturate(foam);
    //foam += (1.0 - normal.y) * 5;
    inscatter += foam * FoamColor;
    transmittance *= 1 - foam;
    //inscatter *= SampleMainLightCookie(positionWS);
    
    //color.rgb = SampleSceneColor(refrUV);

    color.rgb *= transmittance;
    color.rgb += inscatter;
    
    return float4(color.rgb, 1 - transmittance.g);
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
