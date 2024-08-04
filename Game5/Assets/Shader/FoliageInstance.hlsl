#include <common.hlsl>
#include <temporal.hlsl>
#include <basepass.hlsl>
#include <noise.hlsl>

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
    float weight : TEXCOORD2;
};


PSInput VSMain(VSInput input) {
    PSInput result;
    
    result.instanceId = input.instanceId;
    float3 worldPos = input.position.xyz;
    float3 worldNrm = input.normal.xyz;
    float4 instanceData = Instances[input.instanceId];
    float random = frac(instanceData.w);
    float scale = floor(instanceData.w) / 1024;
    float2 sc = float2(cos(random * 1234), sin(random * 1234));
    float2x2 rot = float2x2(sc.x, -sc.y, sc.y, sc.x);
    worldPos.xz = mul(rot, worldPos.xz);
    worldNrm.xz = mul(rot, worldNrm.xz);
    worldPos *= scale * pow(0.5, random - 0.5);
    
    float localY = worldPos.y;
    worldPos.xz += instanceData.xz;
    
    float3 windDirection = float3(0.1, 0.08, 0.2);
    SimplexSample3D noiseSamp0 = CreateSimplex3D(float3(worldPos.xz / 10.0, 0) + (windDirection) * Time);
    SimplexSample3D noiseSamp1 = CreateSimplex3D(float3(worldPos.xz / 30.0, 0) + (windDirection * 0.4) * Time);
    float windTime = Time * 2.0 + worldPos.x * 0.5;
    sc = float2(cos(windTime), sin(windTime * 1.3) + 0.5);
    float3 windDelta = noiseSamp0.Sample3();
    windDelta += noiseSamp1.Sample3();
    windDelta *= float3(2.5, 0.5, 2.5);
    windDelta.xy *= 1 + sin(windDelta.z * 5);
    float2 displacementXZ = windDelta.xz * (localY * localY * 0.4);
    worldPos.xz += displacementXZ;
    worldPos.y -= saturate(dot(displacementXZ, displacementXZ)) * localY;
    float3 windNrm = normalize(float3(windDelta.xz * 0.1, 1).xzy);
    worldNrm = lerp(windNrm, worldNrm, saturate(localY));
    
    worldPos.y += instanceData.y;
    
    result.positionCS = mul(ViewProjection, float4(worldPos, 1.0));
    result.viewPos = mul(View, float4(worldPos, 1.0)).xyz;
    result.normal = mul((float3x3)View, worldNrm);
    result.uv = input.uv;
    result.weight = scale;
        
    return result;
}

void PSMain(PSInput input, bool frontFace : SV_IsFrontFace, out BasePassOutput result) {
    //if (!frontFace) input.normal *= -1;
    //TemporalAdjust(input.uv);
    
    float4 tex = Texture.Sample(BilinearSampler, input.uv);
    float3 Color1 = float3(0.6, 0.9, 0.2);
    float3 Color2 = float3(0.8, 0.9, 0.25);
    float4 instanceData = Instances[input.instanceId];
    float noise = SimplexNoise(instanceData.xz / 5.0);
    noise += SimplexNoise(instanceData.xz / 20.0);
    float3 color = lerp(Color1, Color2, noise * 0.5 + 0.5);
    tex.rgb *= color;
    PBRInput pbrInput = PBRDefault();
    pbrInput.Albedo = tex.rgb;
    pbrInput.Alpha = tex.a;
    pbrInput.Specular = 0.06;
    pbrInput.Roughness = 0.7;
    pbrInput.Normal = (input.normal);
    
    clip(pbrInput.Alpha - 0.5);

    result = PBROutput(pbrInput, normalize(input.viewPos));
    //result.BaseColor.xyz = input.weight * LuminanceFactor;
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
