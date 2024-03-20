#include <common.hlsl>
#include <noise.hlsl>

#define PI 3.1415927

matrix InvProjection;
matrix Projection;
float4 ViewToProj;
float2 NearFar;
float2 TexelSize;
float HalfProjScale;
float2 ZBufferParams;

SamplerState BilinearClampedSampler : register(s6);
Texture2D<float4> SceneDepth : register(t0);

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
float3 GetPosition(float2 uv) {
    return float3(uv * ViewToProj.xy + ViewToProj.zw, 1.0)
        * DepthToLinear(SceneDepth.Sample(BilinearClampedSampler, uv).r);
}
inline half3 GetNormal(float3 pos) {
	return normalize(cross(ddy(pos), ddx(pos)));
}

float FastACos(float c) {
    float z = (-0.166*abs(c) + PI/2) * sqrt(1.0 - abs(c));
    return select(c >= 0, z, PI - z);
}
float2 FastACos(float2 c) {
    float2 z = (-0.166*abs(c) + PI/2) * sqrt(1.0 - abs(c));
    return select(c >= 0, z, PI - z);
}

half4 PSMain(PSInput input) : SV_Target {
    const int NumArc = 3, NumRad = 4,
        NumRadMin = NumRad - 1;
    const float Radius = 2.5;
    
    half3 positionCS = GetPosition(input.uv);
	half3 viewNormal = GetNormal(positionCS);
    half3 viewDir = -normalize(positionCS.xyz);
        
    float stepRadius = max(Radius * HalfProjScale / positionCS.z, NumRad);
    float2 texelStep = TexelSize * (stepRadius / NumRad);
    float falloffFactor = 1.0 * rcp(Radius * Radius);
    
    half3 BentNormal = 0.0;
    half Occlusion = 0.0;
    
    float arcAng = IGN(input.position.xy) * (3.1415 / NumArc);
    float radBias = PermuteV2(input.position.xy, TemporalFrame * 0.3178);
    half2 arcStep = float2(cos(arcAng), sin(arcAng)) * texelStep;
    
    [unroll]
    for (int i = 0; i < NumArc; ++i) {
        half2 horizons = -1.0;

        [unroll]
        for (int j = min(NumRad - 1 - i, NumRadMin); j >= 0; --j) {
            half2 radOff = arcStep * (j + radBias);
            half3 posL = GetPosition(input.uv + radOff) - positionCS;
            half3 posR = GetPosition(input.uv - radOff) - positionCS;
            
            half2 dst2LR = half2(dot(posL, posL), dot(posR, posR));
            half2 sliceLR = half2(dot(posL, viewDir), dot(posR, viewDir)) * rsqrt(dst2LR);
            sliceLR -= dst2LR * falloffFactor;
            horizons = max(sliceLR, horizons);
        }
        
        half3 planeNrm = normalize(cross(half3(arcStep, 0), viewDir));
        half3 planeTan = cross(viewDir, planeNrm);
        half3 projectedNrm = (viewNormal - planeNrm * dot(viewNormal, planeNrm));
        
        half cosineGamma = dot(projectedNrm, viewDir) * rsqrt(dot(projectedNrm, projectedNrm));
        half gamma = (dot(projectedNrm, planeTan) < 0 ? 1 : -1) * FastACos(cosineGamma);
                
        horizons = FastACos(horizons);
        horizons.x = -min(horizons.x, PI / 2.0 - gamma);
        horizons.y = +min(horizons.y, PI / 2.0 + gamma);
        
        half bentAngle = dot(horizons, 0.5);
        BentNormal += viewDir * cos(bentAngle) - planeTan * sin(bentAngle);
        
        half2 horizons2 = horizons * 2.0;
        half2 innterIntegral = (-cos(horizons2 - gamma) + cosineGamma + horizons2 * sin(gamma));
        Occlusion += dot(innterIntegral, 1.0);
        
        float2x2 DirRot = {
            +cos(PI / NumArc), -sin(PI / NumArc),
            +sin(PI / NumArc), +cos(PI / NumArc),
        };
        arcStep = mul(arcStep, DirRot);
    }
    
    BentNormal = normalize(BentNormal - viewDir * (0.5 * NumArc));
    Occlusion = saturate(pow(Occlusion * (0.25 / NumArc), 1));
    
    //return float4(Occlusion.rrr, 1.0);
    return float4(0, 0, 0, (1.0 - Occlusion) * 0.8);
    //return float4(BentNormal * 0.5 + 0.5, Occlusion);
}
