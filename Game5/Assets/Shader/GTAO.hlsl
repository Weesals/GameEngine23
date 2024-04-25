#include <common.hlsl>
#include <noise.hlsl>
#include <lighting.hlsl>

#define PI 3.1415927

matrix InvProjection;
matrix Projection;
matrix InvView;
float4 ViewToProj;
float2 NearFar;
float2 TexelSize;
float HalfProjScale;
float2 ZBufferParams;

SamplerState PointSampler : register(s0);
SamplerState BilinearClampedSampler : register(s6);
Texture2D<float4> SceneDepth : register(t0);
Texture2D<float4> SceneAttri : register(t1);

struct VSInput {
    float4 position : POSITION;
    float2 uv : TEXCOORD0;
};

struct PSInput {
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 wpos : TEXCOORD1;
};

PSInput VSMain(VSInput input) {
    PSInput result;
    result.position = input.position;
    result.position.z = 0.9999999;
    result.position.w = 1.0;
    result.uv = input.uv;
    result.wpos = input.position.xyz;
    return result;
}

float DepthToLinear(float d) {
    return 1.0 / (ZBufferParams.x * d + ZBufferParams.y);
}
float GetDeviceDepth(float2 uv) {
    return SceneDepth.Sample(BilinearClampedSampler, uv).r;
}
float3 GetPosition(float2 uv, float deviceDepth) {
    return float3((uv * ViewToProj.xy + ViewToProj.zw), 1.0)
        * DepthToLinear(deviceDepth);
}
float3 GetPosition(float2 uv) {
    return GetPosition(uv, SceneDepth.Sample(BilinearClampedSampler, uv).r);
}
inline half3 GetNormal(float2 uv) {
	//return normalize(cross(ddy(pos), ddx(pos)));
    return -OctahedralDecode(SceneAttri.Sample(BilinearClampedSampler, uv).xy * 2.0 - 1.0);
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
    const int NumArc = 2, NumRad = 3,
        NumRadMin = NumRad - 1;
    const float Radius = 2.5;
    
    float3 viewPos = GetPosition(input.uv);
    half3 viewNrm = GetNormal(input.uv);
    half3 viewDir = -normalize(viewPos);
    viewNrm.x *= -1; // Dont know why this is here.
        
    float stepRadius = max(Radius * HalfProjScale / viewPos.z, NumRad);
    float2 texelStep = TexelSize * (stepRadius / NumRad);
    float falloffFactor = 1.0 * rcp(Radius * Radius);
    
    half3 BentNormal = 0.0;
    half Occlusion = 0.0;
    
    float arcAng = IGN(input.position.xy) * (3.1415 / NumArc);
    half radBias = frac(arcAng * 125);//(half)((float)PermuteV2(input.position.xy, TemporalFrame * 0.3178));
    half2 arcStep = float2(cos(arcAng), sin(arcAng)) * texelStep;
    //return float4(0, 0, 0, radBias);
    
    [unroll]
    for (int i = 0; i < NumArc; ++i) {
        half2 horizons = -1.0;

        [unroll]
        for (int j = min(NumRad - 1 - i, NumRadMin); j >= 0; --j) {
            half2 radOff = arcStep * (j + radBias);
            float3 posL = GetPosition(input.uv - radOff) - viewPos;
            float3 posR = GetPosition(input.uv + radOff) - viewPos;
            
            half2 dst2LR = half2(dot(posL, posL), dot(posR, posR));
            half2 sliceLR = half2(dot(posL, viewDir), dot(posR, viewDir)) * rsqrt(dst2LR);
            sliceLR -= dst2LR * falloffFactor;
            horizons = max(sliceLR, horizons);
        }
        
        half3 planeNrm = normalize(cross(half3(arcStep, 0), viewDir));
        half3 planeTan = cross(viewDir, planeNrm);
        half3 projectedNrm = viewNrm - planeNrm * dot(viewNrm, planeNrm);
        float projLength = length(projectedNrm);
        
        half cosineGamma = dot(projectedNrm, viewDir) * rsqrt(dot(projectedNrm, projectedNrm));
        half gamma = (dot(projectedNrm, planeTan) < 0 ? 1 : -1) * FastACos(cosineGamma);
                
        horizons = FastACos(horizons);
        horizons.x = -min(horizons.x, PI / 2.0 - gamma);
        horizons.y = +min(horizons.y, PI / 2.0 + gamma);
        
        half bentAngle = dot(horizons, 0.5);
        BentNormal += viewDir * cos(bentAngle) - planeTan * sin(bentAngle);
        
        half2 horizons2 = horizons * 2.0;
        half2 innterIntegral = (-cos(horizons2 - gamma) + cosineGamma + horizons2 * sin(gamma));
        Occlusion += projLength * dot(innterIntegral, 1.0);
        
        half2x2 DirRot = {
            +cos(PI / NumArc), -sin(PI / NumArc),
            +sin(PI / NumArc), +cos(PI / NumArc),
        };
        arcStep = mul(arcStep, DirRot);
    }
    
    BentNormal = normalize(BentNormal - viewDir * (0.5 * NumArc));
    Occlusion = pow(saturate(Occlusion * (0.25 / NumArc)), 2);
    
    BentNormal.x *= -1;
    //BentNormal = viewDir;
    //BentNormal = mul((float3x3)InvView, BentNormal);
    //BentNormal = mul((float3x3)transpose(View), BentNormal).xzy;
    //BentNormal.z *= -1;
    //BentNormal.y *= -1;
    //return float4(Occlusion.rrr, 1.0);
    float4 r = float4(OctahedralEncode(BentNormal) * 0.5 + 0.5, 0, Occlusion);
    r.rgb = 0.0;
    //r.a = 1.0;
    return r;
    //return float4(BentNormal * 0.5 + 0.5, Occlusion);
}
