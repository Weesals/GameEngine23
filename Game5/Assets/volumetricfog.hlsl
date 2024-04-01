#include <volumetrics.hlsl>
#include <noise.hlsl>

#define E 2.71828182846

Texture2D<float4> SceneDepth : register(t0);
float2 ZBufferParams;

static const int cloudSampleCount = 6;
static const float cloudShadowSampleCount = 2;
static const float finalCloudShadowVarianceCount = 3;
static const float finalCloudShadowSampleCount = 1;
static const float finalCloudSkySampleCount = 1;
static const float cloudShadowJitterBias = 0.4;
static const bool enableShadowReceive = false;

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

float DepthToLinear(float d) {
    return 1.0 / (ZBufferParams.x * d + ZBufferParams.y);
}

float4 PSMain(PSInput input) : SV_TARGET {
    float2 viewUV = (input.uv * 2.0 - 1.0) * float2(1.0, -1.0);
    if (false) {
        float2 pos = floor(input.position.xy / 10.0);
        //pos.x += TemporalFrame * 2.588238;
        pos.y = pos.y * 1.32;
        float r = frac(52.9829189f * frac(dot(float2(0.06711056f, 0.00583715f), pos)));
        //r = frac(dot(pos, float2(0.5, 0.25)));
        return float4(r, 0, 0, 1);
    }
    float deviceDepth = SceneDepth[input.position.xy].x;
    
    float3 rayStart = mul(InvViewProjection, float4(viewUV, 0.0, 1.0)).xyz;
    float3 rayEnd = rayStart + transpose(InvViewProjection)[2].xyz * deviceDepth;
    rayStart *= 1.0 / InvViewProjection._44;
    rayEnd *= 1.0 / (InvViewProjection._43 * deviceDepth + InvViewProjection._44);
    float3 groundPos = rayEnd.xyz;
    //return float4(Tex3D.Sample(BilinearSampler, rayEnd.xyz / 5.0).rgb, 1);
    //return float4(frac(rayEnd.xyz / 5.0), 1);
    float3 rayDelta = (rayEnd - rayStart).xyz;
    float rayLength = length(rayDelta);
    float3 viewDir = normalize(rayDelta);
    
    float VdotL = dot(viewDir, _WorldSpaceLightDir0);
    
    float3 Scattering = _LightColor0
        * lerp(HenyeyGreenstein(VdotL, .6), HenyeyGreenstein(VdotL, -.2), 0.5)
        / 3.14;
    float3 SkyColor = (float3(149., 167., 200.)*(1.0/255.0)) * 0.5;
    float3 HorizonColor = (float3(200., 167., 149.)*(1.0/255.0)) * 0.5;
    //float3 AmbientTop = (float3(149., 167., 200.)*(1.0/255.0)) * 0.1;
    //float3 AmbientBot = (float3(39., 67., 87.)*(1.0/255.0)) * 0.1;
    
    float3 scattering = 0.0;
    float transmittance = 1.0;
    bool newWeight = true;
    //input.position.xy = floor(input.position.xy / 10.0);
    float jitter = IGN(input.position.xy, 0.0, false);
    //float jitter = frac(dot(input.position.xy, float2(0.2513, 0.4369)) + TemporalFrame * 0.766);
    float jitter2 = frac(jitter * 10);//IGN(input.position.xy, 1.2, false);
    float jitter3 = frac(jitter2 * 7);//IGN(input.position.xy, 2.2, false);
    //return float4(jitter, 0, jitter3, 1.0);
    
    float2 cloudLimits = float2(cloudMinY, cloudMaxY);
    cloudLimits = viewDir.y >= 0 ? cloudLimits.xy : cloudLimits.yx;
    cloudLimits -= rayStart.y;
    
    [branch] if (cloudLimits.y * (viewDir.y >= 0 ? 1 : -1) > 0)
    {
        cloudLimits = max(cloudLimits * (1.0 / viewDir.y), 0);
        float startDst = cloudLimits.x;
        float endDst = min(cloudLimits.y, rayLength);
        float3 cloudStart = rayStart.xyz + viewDir * startDst;
        float3 cloudEnd = rayStart.xyz + viewDir * endDst;

        PrimeClouds(cloudStart, cloudEnd - cloudStart);
        GlobalMipBias = log2(startDst / 500.0f);
        //return float4(GlobalMipBias / 5.0, 0, 0, 1);
        
        float stepLength = min(dot(cloudEnd - cloudStart, viewDir) * (1.0 / cloudSampleCount), 20.0);
        float3 rayStep = viewDir * stepLength;
        float4 result = 0;
        {
            float rayD = jitter;
        
            float3 cloudUv = cloudStart + rayStep * rayD;
            cloudUv = ApplyWind(cloudUv);
            cloudUv = WorldToCloudUVs(cloudUv);
            float3 cloudStep = WorldToCloudUVs(rayStep);
        
            [unroll]
            for(int d = 0; d < cloudSampleCount; ++d) {
                float3 rayPos = cloudStart + rayStep * rayD;
                float density = SampleClouds(rayPos, cloudUv);
                if (density > 0) {
                    density *= stepLength;
                    float opacity = exp(-density);
                    float weight = (transmittance - transmittance * opacity);
                    float rayShadow = GetVolumeShadow(rayPos, jitter3 * cloudShadowJitterBias, cloudShadowSampleCount, enableShadowReceive);
                    result += float4(rayD, rayD * rayD, rayShadow, 1) * weight;
                    transmittance *= opacity;
                }
                rayD += 1.0;
                cloudUv += cloudStep;
            }
        }
        //result += ddx_fine(result) * 0.5 * ((input.position.x % 2) > 0.5 ? -1 : 1);
        //result += ddy_fine(result) * 0.5 * ((input.position.y % 2) > 0.5 ? -1 : 1);
        [branch]
        if (result.w > 0.001) {
            result.xy *= rcp(result.w);
            float rayD = result.x;
            float rayV = 2 * sqrt(abs(result.y - rayD * rayD));
            float3 rayPos = cloudStart + rayStep * (rayD + (jitter2 - 0.5) * rayV);
            float directLight = result.z;
            float directSky = saturate((rayPos.y - cloudMinY) / (cloudMaxY - cloudMinY));
            //return float4(directSky, 1 - directSky, rayV / 10.0, 1);
            //return float4(rayV / 10.0, 0, 0, 1.0 - transmittance);
            if (finalCloudShadowVarianceCount > 0) {    // && ((input.position.x / 250) % 2) < 1.0
                directLight = 0.0;
                for (int v = 0; v < finalCloudShadowVarianceCount; ++v) {
                    float rayOffset = (v + jitter2) / finalCloudShadowVarianceCount;
                    // Reduce noise for low sample counts
                    rayOffset *= finalCloudShadowVarianceCount / (finalCloudShadowVarianceCount + 0.4);
                    //rayOffset *= (rayOffset + 1.0) * 0.5;
                    rayOffset -= 0.5;
                    //rayOffset = pow(rayOffset, 2.0) - 0.5;
                    float3 shadowPos = cloudStart + rayStep * (rayD + rayOffset * rayV);
                    //shadowPos += _ViewSpaceLightDir0 * 0.5;
                    directLight += GetVolumeShadow(shadowPos, jitter3 * cloudShadowJitterBias, finalCloudShadowSampleCount, enableShadowReceive);
                    jitter3 = frac(jitter3 * 4);
                }
                directLight *= (1.0 / finalCloudShadowVarianceCount) * result.w;
            }
            directSky = GetCloudRaycast(rayPos, float3(0, 1, 0), jitter3 * cloudShadowJitterBias, finalCloudSkySampleCount);
            scattering =
                + Scattering * directLight
                + lerp(HorizonColor, SkyColor, directSky) * result.w;
        }
    } else {
        //scattering = float3(0.5, 0, 0);
    }
    float depth = DepthToLinear(deviceDepth) - 5;
    const float HeightFogLimit = 40.0;
    float4 clipZ = transpose(InvViewProjection)[3];
    float4 projZ = transpose(InvViewProjection)[2];
    float3 cameraVector = normalize(projZ.xyz * clipZ.w - clipZ.xyz * projZ.w);
    depth /= dot(viewDir, cameraVector);
    float2x3 fogAABB = float2x3(float3(0, -5, 0), float3(128, HeightFogLimit, 128));
    float3 heightBeginDepth3 = (select(viewDir >= 0, fogAABB[0], fogAABB[1]) - rayStart) / viewDir;
    float3 heightEndDepth3 = (select(viewDir < 0, fogAABB[0], fogAABB[1]) - rayStart) / viewDir;
    float heightBeginDepth = max(heightBeginDepth3.x, max(heightBeginDepth3.y, heightBeginDepth3.z));
    float heightEndDepth = min(heightEndDepth3.x, min(heightEndDepth3.y, heightEndDepth3.z));
    heightBeginDepth = max(heightBeginDepth, 0);
    heightEndDepth = min(heightEndDepth, depth);
    //return saturate(float4((depth - heightBeginDepth) / 10.0, 0, 0, 1));
    if (heightEndDepth > heightBeginDepth) {
        const float Something = 1.0;
        const float Density = 0.2;
        const float DensityFalloff = 0.5;
        float fogBeginY = rayStart.y + viewDir.y * heightBeginDepth;
        float fogEndY = rayStart.y + viewDir.y * heightEndDepth;
        float fogDistance = (heightEndDepth - heightBeginDepth);
        float opticalDensity = fogDistance
            * (abs(exp(-fogEndY * DensityFalloff) - exp(-fogBeginY * DensityFalloff)))
            / abs(fogEndY - fogBeginY);
        float fogDensity = 1.0 - exp2(-opticalDensity * Density);
        
        float2 shadow = 0.0;
        const int HeightFogShadowCount = 1;
        for (int i = 0; i < HeightFogShadowCount; ++i) {
            float invCount = rcp(HeightFogShadowCount);
            float3 worldPos = rayStart.xyz + viewDir * (heightEndDepth - fogDistance * (i * invCount + jitter * invCount));
            float density = 1.0;//exp(-worldPos.y * DensityFalloff);
            float3 shadowPos = ViewToShadow(worldPos);
            shadow += float2(ShadowMap.SampleCmpLevelZero(ShadowSampler, shadowPos.xy, shadowPos.z).r, 1) * density;
        }
        shadow.x *= 1.0 / shadow.y;
        scattering += Scattering * (fogDensity * shadow.x);
        transmittance *= 1.0 - fogDensity;
    }
    
    float4 r = float4(scattering, 1.0 - transmittance);
    //r += ddx_fine(r) * 0.25 * ((input.position.x % 2) > 0.5 ? -1 : 1);
    //r += ddy_fine(r) * 0.25 * ((input.position.y % 2) > 0.5 ? -1 : 1);
    return r;
}
