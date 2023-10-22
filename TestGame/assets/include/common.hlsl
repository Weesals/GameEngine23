
cbuffer WorldCB : register(b0)
{
    float3 _LightColor0;
    float3 _ViewSpaceLightDir0;
    float3 _ViewSpaceUpVector;
    float Time;
    float4x4 ShadowIVViewProjection;
    float4x4 View;
    float4x4 ViewProjection;
}
