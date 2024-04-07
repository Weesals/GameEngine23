#define PI 3.14159265359

#include <common.hlsl>
#include <temporal.hlsl>
#include <landscapecommon.hlsl>
#include <landscapesampling.hlsl>
#include <basepass.hlsl>

int i <
    string name = "Shadows";
>;

cbuffer ConstantBuffer : register(b1) {
    matrix ModelView;
    matrix ModelViewProjection;
}

half3 ApplyHeightBlend(half3 bc, half3 heights) {
    bc += heights * 4.0 * saturate(bc * 4.0);
    float maxBC = max(max(bc.x, bc.y), max(bc.z, 1.0));
    bc = saturate(bc - (maxBC - 1.0));
    bc *= rcp(dot(bc, 1.0));
    return bc;
}

struct VSInput {
    uint instanceId : SV_InstanceID;
    float4 position : POSITION;
    float3 normal : NORMAL;
    int2 offset : INSTANCE;
};

struct PSInput {
    float3 positionOS : TEXCOORD0;
    float3 normalOS : NORMAL;
    float3 uv : TEXCOORD1;
};

PSInput VSMain(VSInput input, out float4 positionCS : SV_POSITION) {
    PSInput result;

    float3 worldPos = input.position.xyz;
    float3 worldNrm = input.normal.xyz;
    TransformLandscapeVertex(worldPos, worldNrm, input.offset);
    
    // Sample from the heightmap and offset the vertex
    float4 hcell = HeightMap.Load(int3(worldPos.xz, 0), 0);
    float terrainHeight = _LandscapeScaling.z + (hcell.r) * _LandscapeScaling.w;
    worldPos.y += terrainHeight;
    if (input.position.y > 0.5) worldPos.y = -5;
    result.uv = float3(
        float2(input.position.x, worldPos.y) * 0.1,
        terrainHeight
    );

    result.positionOS = worldPos;
    result.normalOS = worldNrm;
    positionCS = mul(ModelViewProjection, float4(worldPos, 1.0));
        
    return result;
}

void PSMain(PSInput input, out BasePassOutput result) {    
    PBRInput pbrInput = PBRDefault();
        
    TemporalAdjust(input.uv.xy);
    pbrInput.Albedo = EdgeTex.Sample(AnisotropicSampler, input.uv.xy).rgb;
    pbrInput.Albedo = lerp(pbrInput.Albedo, 1, pow(1 - saturate(input.uv.z - input.positionOS.y), 4) * 0.25);

    pbrInput.Normal = mul((float3x3)ModelView, pbrInput.Normal);
    pbrInput.Normal = normalize(pbrInput.Normal);
        
    float3 viewPos = mul(ModelView, float4(input.positionOS, 1.0)).xyz;
    float3 viewDir = normalize(viewPos);
    result = PBROutput(pbrInput, viewDir);
}

void ShadowCast_VSMain(VSInput input, out float4 positionCS : SV_POSITION) {
    VSMain(input, positionCS);
}
void ShadowCast_PSMain() { }
