#include <volumetrics.hlsl>
#include <noise.hlsl>

#define E 2.71828182846

Texture2D<float4> SceneDepth : register(t0);
//Texture2D<float4> VolAlbedo : register(t1);
//Texture2D<float4> VolAttr : register(t2);
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

float miePhase(float cosine) {
    return pow(cosine - 0.3, 2) * 0.31 + 0.07;
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
        
    float3 Scattering = _LightColor0 * lerp(SchlickPhase(VdotL, 0.3) * 2, SchlickPhase(VdotL, -0.6), 0.7);
    float3 SkyColor = (float3(135., 167., 230.)*(0.6/255.0));
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
    
    float4 result = 0;
    [branch] if (cloudLimits.y * (viewDir.y >= 0 ? 1 : -1) > 0)
    {
        cloudLimits = max(cloudLimits * (1.0 / viewDir.y), 0);
        float cloudStartDst = cloudLimits.x;
        float cloudEndDst = min(cloudLimits.y, rayLength);
        float3 cloudStart = rayStart.xyz + viewDir * cloudStartDst;
        float3 cloudEnd = rayStart.xyz + viewDir * cloudEndDst;

        PrimeClouds(cloudStart, cloudEnd - cloudStart);
        GlobalMipBias = log2(cloudStartDst / 500.0f);
    
        float stepLength = min(dot(cloudEnd - cloudStart, viewDir) * (1.0 / cloudSampleCount), 20.0);
        float3 rayStep = viewDir * stepLength;
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
        [branch]
        if (result.w > 0.001) {
            result.xy *= rcp(result.w);
            float rayD = result.x;
            float rayV = 2 * sqrt(abs(result.y - rayD * rayD));
            rayD = rayD * stepLength + cloudStartDst;
            rayV = rayV * stepLength;
            float3 rayPos = rayStart + viewDir * (rayD + (jitter2 - 0.5) * rayV);
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
                    float3 shadowPos = rayStart + viewDir * (rayD + rayOffset * rayV);
                    //shadowPos += _ViewSpaceLightDir0 * 0.5;
                    directLight += GetVolumeShadow(shadowPos, jitter3 * cloudShadowJitterBias, finalCloudShadowSampleCount, enableShadowReceive);
                    jitter3 = frac(jitter3 * 4);
                }
                directLight *= (1.0 / finalCloudShadowVarianceCount) * result.w;
            }
            directSky = GetCloudRaycast(rayPos, float3(0, 1, 0), jitter3 * cloudShadowJitterBias, finalCloudSkySampleCount);
            scattering =
                + Scattering * directLight
                + lerp(HorizonColor, SkyColor, directSky * 0.7) * result.w;
        }
        //result += ddx_fine(result) * 0.5 * ((input.position.x % 2) > 0.5 ? -1 : 1);
        //result += ddy_fine(result) * 0.5 * ((input.position.y % 2) > 0.5 ? -1 : 1);
    } else {
        //scattering = float3(0.5, 0, 0);
    }
    /*{
        float4 attr = VolAttr.Sample(BilinearSampler, input.uv);
        result.xyw += attr.xyz;
        float2 normal = float2(ddy_fine(attr.y), ddy_fine(attr.y));
        result.z += attr.z * dot(viewDir.xy, normal);
    }*/
    //return float4(result.rgb, 1);
    float depth = DepthToLinear(deviceDepth) - 5;
    const float HeightFogLimit = 40.0;
    const float HeightFogMidLimit = HeightFogLimit / 10.0;
    float4 clipZ = transpose(InvViewProjection)[3];
    float4 projZ = transpose(InvViewProjection)[2];
    float3 cameraVector = normalize(projZ.xyz * clipZ.w - clipZ.xyz * projZ.w);
    {
        //depth = min(depth, (0 - rayStart.y) / viewDir.y);
    }
    depth /= dot(viewDir, cameraVector);
    float2x3 fogAABB = float2x3(float3(0, -0, 0), float3(127, HeightFogLimit, 127));
    float3 heightBeginDepth3 = (select(viewDir >= 0, fogAABB[0], fogAABB[1]) - rayStart) / viewDir;
    float3 heightEndDepth3 = (select(viewDir < 0, fogAABB[0], fogAABB[1]) - rayStart) / viewDir;
    float heightBeginDepth = max(heightBeginDepth3.x, max(heightBeginDepth3.y, heightBeginDepth3.z));
    float heightEndDepth = min(heightEndDepth3.x, min(heightEndDepth3.y, heightEndDepth3.z));
    heightBeginDepth = max(heightBeginDepth, 0);
    heightEndDepth = min(heightEndDepth, depth);
    //return float4(frac(rayEnd / 100) * 0.1, 1);
    //return saturate(float4((depth - heightBeginDepth) / 10.0, 0, 0, 1));
    [branch]
    if (heightEndDepth > heightBeginDepth) {
        const float Something = 1.0;
        const float Density = 0.3;
        const float DensityFalloff = 0.5;
        //if (viewDir.y < 0) heightMidDepth = clamp((HeightFogMidLimit - rayStart.y) / viewDir.y, heightBeginDepth, heightEndDepth);
        float fogBeginY = rayStart.y + viewDir.y * heightBeginDepth;
        float fogEndY = rayStart.y + viewDir.y * heightEndDepth;
        float fogDistance = (heightEndDepth - heightBeginDepth);
        float effectiveDensity = Density
            * (exp2(-fogEndY * DensityFalloff) - exp2(-fogBeginY * DensityFalloff))
            / -(fogEndY - fogBeginY);

        float opticalDensity = fogDistance * effectiveDensity;

        float heightMidDepth = heightBeginDepth;//(heightBeginDepth + heightEndDepth) * 0.5;
        float fogMidDistance = (heightEndDepth - heightMidDepth);
        //return float4(fogMidDistance / 1000.0, 0, 0, 1);

        PrimeClouds(rayStart + viewDir * heightEndDepth, 0);
        GlobalMipBias = 2.0;
        
        float2 shadow = 0.0;
        const int HeightFogShadowCount = 2;
        if (HeightFogShadowCount == 0) shadow = 1.0;
        for (int i = 0; i < HeightFogShadowCount; ++i) {
            const float invCount = rcp(HeightFogShadowCount);
            float sampleDepth = (i * invCount + jitter * invCount);
            #if 0
            sampleDepth = log2(lerp(1, exp2(-fogMidDistance * effectiveDensity), sampleDepth));
            sampleDepth *= 1.0 / (-fogMidDistance * effectiveDensity);
            #endif
            //return float4(sampleDepth * fogDistance / 10.0, 0, 0, 1);
            sampleDepth = sampleDepth * fogMidDistance;
            float3 worldPos = rayStart.xyz + viewDir * (heightMidDepth + sampleDepth);
            float density = exp2(-worldPos.y * DensityFalloff) * exp2(-sampleDepth * Density);
            float3 shadowPos = ViewToShadow(worldPos);
            float localShadow = ShadowMap.SampleCmpLevelZero(ShadowSampler, shadowPos.xy, shadowPos.z).r;
            localShadow *= GetGroundShadow(worldPos, 0.5, 1);
            shadow += float2(localShadow, 1) * density;
        }
        shadow.x *= 1.0 / shadow.y;
        shadow = shadow * fogMidDistance + (heightMidDepth - heightBeginDepth);
        shadow *= 1.0 / fogDistance;
        
        const int HeightFogCloudCount = 0;
        for (int i = 0; i < HeightFogCloudCount; ++i) {
            const float invCount = 1.0 / HeightFogCloudCount;
            float sampleDepth = (i * invCount + jitter * invCount);
            sampleDepth = 1.0 - sampleDepth * sampleDepth;
            sampleDepth = fogMidDistance * sampleDepth;
            float3 worldPos = rayStart.xyz + viewDir * (heightMidDepth + sampleDepth);
            //worldPos.y = 0.0;
            float3 uv = worldPos / 200.0;
    
            //uv = ApplyWind(uv);
            //uv = WorldToCloudUVs(uv);
            float3 time3 = Time * 0.002 + uv.x - float3(1.0, 2.0, 0.0) / 3.0;
            uv.x -= Time * 0.002;
            float3 cloud = dot(Noise2D.SampleLevel(BilinearSampler, uv.xz, 0).rgb,
                saturate(1.0 - abs(frac(time3) * 3.0 - 1.0)));
            cloud += 0.5;
            cloud = exp2(-cloud * fogMidDistance * invCount * Density);
            opticalDensity *= cloud;
            //opticalDensity *= SampleCloudsRaw(worldPos / 40.0, true, false) + 0.5;
        }
                
        float fogDensity = 1.0 - exp2(-opticalDensity);
        float fogScatter = fogDensity;
        if (viewDir.y < 0) fogScatter *= transmittance;
        
        scattering += (
            _LightColor0 * (SchlickPhase(VdotL, -0.1) + SchlickPhase(viewDir.y, 0.5))
            + SkyColor * 0.2
            + HorizonColor * 0.2
        ) * (fogScatter * shadow.x);
        transmittance *= 1.0 - fogDensity;
    }
    
    float4 r = float4(scattering, 1.0 - transmittance);
    //r.rgb = SkyColor;
    //r.a = 1.0;
    r.rgb *= LuminanceFactor;
    //r += ddx_fine(r) * 0.25 * ((input.position.x % 2) > 0.5 ? -1 : 1);
    //r += ddy_fine(r) * 0.25 * ((input.position.y % 2) > 0.5 ? -1 : 1);
    return r;
}
