#include <common.hlsl>
#include <temporal.hlsl>
#include <basepass.hlsl>

SamplerState BilinearSampler : register(s1);
Texture2D<float4> Texture : register(t0);

StructuredBuffer<float4> Instances : register(t1);

struct VSInput {
    uint instanceId : SV_InstanceID;
    float4 position : POSITION;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD0;
};

struct PSInput {
    uint instanceId : SV_InstanceID;
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    float3 viewPos : TEXCOORD1;
    float3 normal : NORMAL;
};


PSInput VSMain(VSInput input) {
    PSInput result;
    
    result.instanceId = input.instanceId;
    float3 worldPos = input.position.xyz;
    float3 worldNrm = input.normal.xyz;
    float4 instanceData = Instances[input.instanceId];
    float2 sc = float2(cos(instanceData.w * 1234), sin(instanceData.w * 1234));
    float2x2 rot = float2x2(sc.x, -sc.y, sc.y, sc.x);
    worldPos.xz = mul(rot, worldPos.xz);
    worldNrm.xz = mul(rot, worldNrm.xz);
    worldPos *= pow(0.5, instanceData.w - 0.5);
    float localY = worldPos.y;
    worldPos.xyz += instanceData.xyz;
    
    float windTime = Time * 2.0 + worldPos.x * 0.5;
    sc = float2(cos(windTime), sin(windTime * 1.3) + 0.5);
    worldPos.xz += sc * (worldPos.y * worldPos.y * sin(windTime * 2.4) * 0.1);
    
    result.positionCS = mul(ViewProjection, float4(worldPos, 1.0));
    result.viewPos = mul(View, float4(worldPos, 1.0)).xyz;
    result.normal = mul((float3x3)View, worldNrm);
    result.uv = input.uv;
        
    return result;
}

void PSMain(PSInput input, out BasePassOutput result) {
    TemporalAdjust(input.uv);
    
    float4 tex = Texture.Sample(BilinearSampler, input.uv);
    tex.rgb *= float3(0.9, 1.0, 0.5);
    PBRInput pbrInput = PBRDefault();
    pbrInput.Albedo = tex.rgb;
    pbrInput.Alpha = tex.a;
    pbrInput.Specular = 0.06;
    pbrInput.Roughness = 0.7;
    pbrInput.Normal = normalize(input.normal);
    
    clip(pbrInput.Alpha - 0.5);

    result = PBROutput(pbrInput, normalize(input.viewPos));
}

struct ShadowCast_VSInput {
    uint instanceId : SV_InstanceID;
    float4 position : POSITION;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD0;
};
struct ShadowCast_PSInput {
    float2 uv : TEXCOORD0;
};

void ShadowCast_VSMain(ShadowCast_VSInput input, out ShadowCast_PSInput output, out float4 positionCS : SV_POSITION) {
    VSInput vsinput = (VSInput)0;
    vsinput.instanceId = input.instanceId;
    vsinput.position = input.position;
    vsinput.normal = input.normal;
    output.uv = input.uv;
    PSInput vsout = VSMain(vsinput);
    positionCS = vsout.positionCS;
}

void ShadowCast_PSMain(ShadowCast_PSInput input) {
    float4 tex = Texture.Sample(BilinearSampler, input.uv);
    clip(tex.a - 0.7);
}
