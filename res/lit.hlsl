// A standard PBR shader (according to various examples online)

#define PI 3.14159265359

cbuffer WorldCB : register(b0)
{
    float3 _LightColor0;
    float3 _ViewSpaceLightDir0;
    float3 _ViewSpaceUpVector;
}
cbuffer ConstantBuffer : register(b1)
{
    matrix ModelView;
    matrix ModelViewProjection;
};

struct VSInput
{
    float4 position : POSITION;
    float3 normal : NORMAL;
};

struct PSInput
{
    float4 position : SV_POSITION;
    float3 viewPos : TEXCOORD1;
    float3 normal : NORMAL;
};

PSInput VSMain(VSInput input)
{
    PSInput result;

    result.position = mul(float4(input.position.xyz, 1.0), ModelViewProjection);
    result.viewPos = mul(float4(input.position.xyz, 1.0), ModelView);
    result.normal = mul(float4(input.normal.xyz, 0.0), ModelView);
    
#if defined(VULKAN)
    result.position.y = -result.position.y;
#endif

    return result;
}

// Magic functions that compute PBR terms
float NormalDistribution_GGX(float a, float NdH)
{
    float a2 = a * a;
    float denominator = (NdH * NdH) * (a2 - 1.0f) + 1.0f;
    denominator *= denominator;
    return a2 / (denominator * PI);
}
float GeometrySmith_GGX(float NdV, float NdL, float k)
{
    float2 ggx12 = max(float2(NdV, NdL), 0.0);
    ggx12 = ggx12 / (ggx12 * (1.0 - k) + k);
    return ggx12.x * ggx12.y;
}
float Fresnel_Schlick(float u)
{
    return pow(saturate(1.0f - u), 4.0);
}
float3 Fresnel_Schlick(float3 specularColor, float HdV)
{
    return (specularColor + (1.0f - specularColor) * pow((1.0f - saturate(HdV)), 5.0f));
}
float3 Specular_F_Roughness(float3 specularColor, float a, float VdH)
{
    return specularColor + (max(1.0f - a, specularColor) - specularColor) * pow((1.0f - saturate(VdH)), 5.0f);
}
float3 Diffuse(float3 albedoColor)
{
    return albedoColor / PI;
}
float3 Specular(float3 specularColor, float NdV, float NdL, float NdH, float VdH, float a)
{
    float3 TotalSpecular = NormalDistribution_GGX(a, NdH) *
		Fresnel_Schlick(specularColor, VdH) *
		GeometrySmith_GGX(NdV, NdL, a * 0.5);
    return TotalSpecular / (4.0f * NdV * NdL + 0.001f);
}

// Combine all the above functions to compute the effects of a light
float3 ComputeLight(float3 albedoColor, float3 specularColor, float3 normal, float roughness, float3 lightColor, float3 lightDir, float3 viewDir, float met)
{
    ///Calculate everything.
    float NdL = saturate(dot(normal, lightDir));
    float NdV = saturate(dot(normal, viewDir));
    float3 h = normalize(lightDir + viewDir);
    float NdH = saturate(dot(normal, h));
    float VdH = saturate(dot(viewDir, h));
    float LdV = saturate(dot(lightDir, viewDir));
    float a = max(0.001f, roughness * roughness);

    ///Get the diffuse result and the specular result.
    float3 ColorDiffuse = Diffuse(albedoColor);
    float3 ColorSpecular = Specular(specularColor, NdV, NdL, NdH, VdH, a);

    ///Now we get the fresnel of our half and view. This gives us our Specular contribution depending on the angle of viewing.
    float3 F = Fresnel_Schlick(specularColor, VdH);
    float3 kS = F;
    float3 kD = float3(1.0f, 1.0f, 1.0f) - kS; ///To get our diffuse contribution we substract the specular contribution from a white color.

    kD *= 1.0f - met;

    ///Now we just multiply the NdL by the lightcolor and by the colorDiffuse and ColorSpecular
    return lightColor * NdL * (kD * ColorDiffuse + ColorSpecular);
}

// TODO: These should sample from the world
float3 SampleEnvironment(float3 normal, float roughness)
{
    float3 FloorColor = float3(148, 124, 93) / 255;
    float3 SkyColor = float3(184, 226, 255) / 255;
    float skyness = 1.0 - pow(saturate(1.0 - dot(normal, _ViewSpaceUpVector)), 1.0);
    skyness = lerp(skyness, 0.5, roughness);
    return lerp(FloorColor, SkyColor, skyness) * 0.3;
}
float3 SampleAmbientLight(float3 normal)
{
    float3 FloorColor = float3(148, 124, 93) / 255;
    float3 SkyColor = float3(184, 226, 255) / 255;
    float skyness = 1.0 - pow(saturate(1.0 - dot(normal, _ViewSpaceUpVector)), 1.0);
    return lerp(FloorColor, SkyColor, skyness) * 0.3;
}

float4 PSMain(PSInput input) : SV_TARGET
{    
    float3 viewDir = normalize(input.viewPos);
    input.normal = normalize(input.normal);
    // TODO: Should be sampled from textures
    float3 Albedo = 0.9;
    float3 Specular = 0.5;
    float Roughness = 0.7;
    float Metallic = 0.0;
    
    // The light
    float3 o = ComputeLight(
        Albedo,
        Specular,
        input.normal,
        Roughness,
        _LightColor0,
        _ViewSpaceLightDir0,
        -viewDir,
        Metallic
    );
    
    float3 envFresnel = Specular_F_Roughness(
        Specular.xyz,
        Roughness * Roughness,
        dot(input.normal, -viewDir)
    ).xyz;
    
    float3 Kd = (1.0f - envFresnel) * (1.0f - Metallic);
    
    // Indirect
    o += SampleAmbientLight(input.normal) * Kd;
    o += SampleEnvironment(reflect(viewDir, input.normal), Roughness) * envFresnel;

    return float4(o, 1);
}
