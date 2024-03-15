#include <noise.hlsl>
#include <shadowreceive.hlsl>

matrix InvVP;
float3 _WorldSpaceLightDir0;
float CloudDensity;

SamplerState BilinearSampler : register(s1);
Texture2D<float4> SceneDepth : register(t0);
Texture3D<float4> FogDensity : register(t5);

struct VSInput {
    float4 position : POSITION;
    float2 uv : TEXCOORD0;
};

struct PSInput {
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
};

PSInput VSMain(VSInput input) {
    PSInput result;
    result.position = input.position;
    result.uv = input.uv;
    return result;
}


static const float cloudMaxY = 16;
static const float cloudMinY = 12;
static const int cloudSampleCount = 2;
static const float cloudTextureDensity = 30.0;
static const float cloudShadowSampleCount = 0;
static const float finalCloudShadowVarianceCount = 1;
static const float finalCloudShadowSampleCount = 1;
static const float finalCloudSkySampleCount = 1;
static const float groundCloudShadowSampleCount = 2;

float SampleClouds(float3 p) {
    p.x += Time * 1.0;

    // Rotate texture
    const half3 n = normalize(float3(0.3, 1, 0.2));
    const half4 bc = n.xzxz * float4(n.zz / (n.y + 1.0), 1.0, 1.0);
    const float3 tangent = float3(n.y + bc.y, -bc.zx);
    const float3 bitangent = bc.xwy - float3(0, 0, 1);
    float3 uv = float3(
        dot(p, tangent),
        dot(p, n),
        dot(p, bitangent)
    ) / cloudTextureDensity;
    
    // Blend between 3 frames
    float timeN = Time * 0.02 + (p.x - p.z) * 0.008;
    float3 cloudSamples = FogDensity.SampleLevel(BilinearSampler, uv.xzy, 0).rgb;
    float3 cloudTangents = (cloudSamples.yzx - cloudSamples.zxy);
    float3 cloudTimes = frac(timeN - float3(0, 0.333, 0.666)) * 3 - 1.0;
    float3 cloudPhase = saturate(1.0 - abs(cloudTimes));
    float clouds = dot(cloudPhase, cloudSamples + (cloudTangents * cloudTimes));
    
    // Mask above/below
    clouds *= saturate((cloudMaxY + 0.5 - p.y) / 2.0);
    clouds *= saturate((p.y + cloudMinY - 0.5) / 2.0);
    
    clouds = saturate(clouds * 5.0 - 2.5);
    // Add density multiplier
    return clouds * CloudDensity;
}
float HenyeyGreenstein(float LdotV, float g) {
	float gg = g * g;
	return (1.0 - gg) / pow(1.0 + gg - 2.0 * g * LdotV, 1.5);
}
float GetCloudRaycast(float3 worldPos, float3 rayDir, float bias = 0.5, const int stepCount = 2) {
    // Determine steps to get through the layer
    float distance = clamp((cloudMaxY - worldPos.y) / rayDir.y, 1.0, 10.0);
    float stepAmount = distance / stepCount;
    float3 stepIncrement = rayDir * stepAmount;
    worldPos += stepIncrement * bias;
    
    // Accumulate density
    float density = 0.0;
    for (int i = 0; i < stepCount; i++) {
		density += SampleClouds(worldPos);
        worldPos += stepIncrement;
    }
    return exp2(-density * stepAmount);
}
float GetGroundShadow(float3 worldPos, float bias = 0.5, int stepCount = 2) {
    // Step to the bottom of the clouds
    float3 lightDir = _WorldSpaceLightDir0;
    worldPos += lightDir * max((cloudMinY - worldPos.y) / lightDir.y, 0);
    return GetCloudRaycast(worldPos, _WorldSpaceLightDir0, bias, stepCount);
}
float GetVolumeShadow(float3 p, float bias = 0.5, int stepCount = 1) {
    float3 shadowPos = ViewToShadow(p);
    float shadow = ShadowMap.SampleCmpLevelZero(ShadowSampler, shadowPos.xy, shadowPos.z).r;

    return GetCloudRaycast(p, _WorldSpaceLightDir0, bias, stepCount) * shadow;
}

float4 PSMain(PSInput input) : SV_TARGET {    
    float depth = SceneDepth[input.position.xy].x;

    float4 nearCS = float4((input.uv * 2.0 - 1.0) * float2(1.0, -1.0), 0.0, 1.0);
    float4 farCS = float4((input.uv * 2.0 - 1.0) * float2(1.0, -1.0), depth, 1.0);
    float4 rayStart = mul(InvVP, nearCS);
    float4 rayEnd = mul(InvVP, farCS);
    rayStart.xyz /= rayStart.w;
    rayEnd.xyz /= rayEnd.w;
    float3 groundPos = rayEnd.xyz;
    //return float4(Tex3D.Sample(BilinearSampler, rayEnd.xyz / 5.0).rgb, 1);
    //return float4(frac(rayEnd.xyz / 5.0), 1);
    float3 viewDir = normalize((rayEnd - rayStart).xyz);
    float2 limits = float2(cloudMinY, cloudMaxY);
    limits = viewDir.y < 0 ? limits.xy : limits.yx;
    rayStart.xyz += viewDir * max((limits.y - rayStart.y) / viewDir.y, 0);
    rayEnd.xyz += viewDir * min((limits.x - rayEnd.y) / viewDir.y, 0);
    
    //return float4(SampleClouds(rayStart.xyz).rrr, 1.0);

    float VdotL = dot(viewDir, _WorldSpaceLightDir0);
    
    float3 Scattering = _LightColor0
        * lerp(HenyeyGreenstein(VdotL, .8), HenyeyGreenstein(VdotL, -.2), 0.5)
        / 3.14;
    float3 SkyColor = (float3(149., 167., 200.)*(1.0/255.)) * 0.5;
    float3 AmbientTop = (float3(149., 167., 200.)*(1.0/255.)) * 0.1;
    float3 AmbientBot = (float3(39., 67., 87.)*(1.0/255.)) * 0.1;

    float stepLength = min(dot((rayEnd - rayStart).xyz, viewDir) / cloudSampleCount, 100.0);
    float3 scattering = 0.0;
    float transmittance = 1.0;
    float jitter = IGN(input.position.xy);
    if (stepLength > 0) {
        float3 rayStep = viewDir * stepLength;
        float4 result = 0;
        for(int d = 0; d < cloudSampleCount; ++d) {
            float rayD = ((float)d + jitter);
            float3 rayPos = rayStart.xyz + rayStep * rayD;
            float density = SampleClouds(rayPos);
            if (density <= 0) continue;
            float alpha = exp(-density * stepLength);
            float weight = (transmittance - transmittance * alpha);
            result += float4(rayD, GetVolumeShadow(rayPos, jitter, cloudShadowSampleCount), 1, rayD * rayD)
                * weight;
            transmittance *= alpha;
        }
        {
            float jitter2 = IGN(input.position.xy, 5.0);
            float jitter3 = IGN(input.position.xy, 10.0);
            float rayD = result.x / result.z;
            float rayV = abs(result.w / result.z - rayD * rayD) * 0.8;
            float3 rayPos = rayStart.xyz + rayStep * (rayD + (jitter2 - 0.5) * rayV);
            float3 ambientLight = lerp(AmbientBot, AmbientTop,
                saturate((rayPos.y - cloudMinY) / (cloudMaxY - cloudMinY)))
                * result.z;
            float directLight = result.y;
            float directSky = result.y;
            if (finalCloudShadowVarianceCount > 0) {
                if (finalCloudShadowSampleCount > 0) directLight = 0.0;
                for (int v = 0; v < finalCloudShadowVarianceCount; ++v) {
                    float rayOffset = (v + jitter2) / finalCloudShadowVarianceCount - 0.5;
                    float3 shadowPos = rayStart.xyz + rayStep * (rayD + rayOffset * rayV);
                    shadowPos += _ViewSpaceLightDir0 * 0.5;
                    directLight += GetVolumeShadow(shadowPos, jitter3 * 0.4, finalCloudShadowSampleCount);
                    directSky += GetCloudRaycast(shadowPos, float3(0, 1, 0), jitter3 * 0.4, finalCloudSkySampleCount);
                }
                directLight /= finalCloudShadowVarianceCount;
                directSky /= finalCloudShadowVarianceCount;
            }
            scattering = ambientLight
                + Scattering * (directLight * result.z)
                + SkyColor * (directSky * result.z);
        }
    }
    if (depth < 0.99) {
        float shadow = GetGroundShadow(groundPos, jitter, groundCloudShadowSampleCount);
        transmittance *= lerp(0.3, 1.0, shadow);
    }
    return float4(scattering, 1.0 - transmittance);
}
