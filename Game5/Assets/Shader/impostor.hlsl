#include <retained.hlsl>
#include <lighting.hlsl>
#include <shadowreceive.hlsl>
#include <basepass.hlsl>

static const float FrameCount = 8;

SamplerState BilinearSampler : register(s1);
Texture2D<float4> Albedo : register(t0);
Texture2D<float4> Normal : register(t2);
cbuffer ConstantBuffer : register(b1) {
    float3 Offset;
    float Scale;
    matrix Projection;
}

struct VSInput
{
    uint primitiveId : INSTANCE;
    float4 position : POSITION;
};

struct ImpostorData {
    float3 rayPos : LOCALPOS;
    float3 rayTan : CAMDELTA;
    float2 frame : FRAME;
};

struct PSInput {
    uint primitiveId : SV_InstanceID;
    //float3 velocity : VELOCITY;
    ImpostorData impostor;
};

float3 GetCameraPosition() { return -mul(View._m03_m13_m23, (float3x3)View); }
float2 FrameFromVector(float3 axis) {
    float2 frame = HemiOctahedralEncode(axis.xzy) * 0.5 + 0.5;
    return (floor(frame * FrameCount) + 0.5) / FrameCount;
}
float3 FrameToVector(float2 frame) {
    return normalize(HemiOctahedralDecode(frame * 2 - 1).xzy);
}
float3x3 CreateTransform(float3 u) {
    half2 bc = u.xz * (u.z * rcp(u.y + 1.0));
    float3x3 tform = float3x3(
        half3(u.y + bc.y, -u.x, -bc.x),
        u,
        half3(-bc.x, -u.z, 1.0 - bc.y)
    );
    return tform;
}

PSInput VSMain(VSInput input, out float4 positionCS : SV_POSITION)
{

    PSInput result;
    
    InstanceData instance = GetInstanceData(input.primitiveId);
    
    result.primitiveId = input.primitiveId;

    float3 worldPos = mul(instance.Model, float4(input.position.xyz, 1.0)).xyz;
    positionCS = mul(ViewProjection, float4(worldPos, 1.0));

    matrix modelView = mul(View, instance.Model);
    float3 camFwd = normalize(modelView._m20_m21_m22);
    camFwd = View._m20_m21_m22;
    if (camFwd.y > 0) camFwd = -camFwd;
    camFwd = mul(camFwd, (float3x3)instance.Model);

    result.impostor.frame = FrameFromVector(-camFwd);
    result.impostor.rayPos = mul((worldPos - instance.Model._m03_m13_m23), (float3x3)instance.Model).xyz;
    result.impostor.rayPos += Offset;
    result.impostor.rayPos *= Scale;
    result.impostor.rayTan = mul(worldPos - GetCameraPosition(), (float3x3)instance.Model);

    float3x3 tform = CreateTransform(FrameToVector(result.impostor.frame));
    float3 frameCountScalar = float2(1.0 / FrameCount, 1).xyx;
    result.impostor.rayPos = mul(tform, result.impostor.rayPos) * frameCountScalar;
    result.impostor.rayTan = mul(tform, result.impostor.rayTan);
        
    return result;
}

void PSMain(PSInput input, out BasePassOutput result
    , linear centroid noperspective float4 positionCS : SV_POSITION
    , out float depth : SV_DepthGreaterEqual0
) {
    InstanceData instance = GetInstanceData(input.primitiveId);

    ImpostorData impostor = input.impostor;

    float3 rayPos = input.impostor.rayPos;
    float3 rayTan = input.impostor.rayTan;
    float2 frame = input.impostor.frame;

    float2 uv = rayPos.xz + frame;
    uv.y = 1.0 - uv.y;

    float2 uvTangent = -rayTan.xz / (rayTan.y * FrameCount);
    uvTangent.y *= -1;

    float4 tex = 0.5 - rayPos.y;
    for(int i = 0; i < 2; ++i) {
        float prevH = tex.a;
        tex = Normal.Sample(BilinearSampler, uv);
        uv += uvTangent * (tex.a - prevH);
    }

    float viewDp = -normalize(rayTan).y;
    float depthScale = 1.0 / (Scale * viewDp);
    float depthOffset = ((tex.a - 0.5) - -rayPos.y) * depthScale;
    //depth = positionCS.z;// + max(0, depthMod * depthScale) / positionCS.w;
    depth = (positionCS.z * positionCS.w + depthOffset * Projection._33) / (positionCS.w + depthOffset * Projection._43);

    float4 albedo = Albedo.Sample(BilinearSampler, uv);
    clip(albedo.a - 0.5);
    PBRInput pbrInput = PBRDefault();
    pbrInput.Albedo = albedo.rgb;
    pbrInput.Alpha = tex.a;
    pbrInput.Specular = 0.06;
    pbrInput.Roughness = 0.7;
    pbrInput.Emissive = instance.Highlight;
    pbrInput.Normal = normalize(tex.rgb * 2 - 1);
    pbrInput.Normal = pbrInput.Normal.xzy * float3(1, -1, 1);
    pbrInput.Normal = mul((float3x3)instance.Model, pbrInput.Normal);
    //pbrInput.Normal = mul((float3x3)View, float3(0, 1, 0));
    pbrInput.Normal = mul((float3x3)View, pbrInput.Normal);

    result = PBROutput(pbrInput, normalize(input.impostor.rayPos));
    OutputSelected(result, instance.Selected);
}

PSInput ShadowCast_VSMain(VSInput input, out float4 positionCS : SV_POSITION) {
    return VSMain(input, positionCS);
}

void ShadowCast_PSMain(PSInput input
    , linear centroid noperspective float4 positionCS : SV_POSITION
    , out float depth : SV_DepthGreaterEqual0
) {
    float3 rayPos = input.impostor.rayPos;
    float3 rayTan = input.impostor.rayTan;
    float2 frame = input.impostor.frame;

    float2 uv = rayPos.xz + frame;
    uv.y = 1.0 - uv.y;

    float4 tex = Normal.Sample(BilinearSampler, uv);
    float4 bc = Albedo.Sample(BilinearSampler, uv);
    clip(bc.a - 0.5);

    float viewDp = 1.0;//-normalize(rayTan).y;
    float depthScale = 1.0 / (Scale * viewDp);
    float depthOffset = ((tex.a - 0.5) - -rayPos.y) * depthScale * -1;
    depth = (positionCS.z * positionCS.w + depthOffset * Projection._33)
          / (positionCS.w + depthOffset * Projection._43);
}
