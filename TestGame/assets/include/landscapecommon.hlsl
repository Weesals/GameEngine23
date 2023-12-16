
Texture2D<float4> HeightMap : register(t0);
Texture2D<float4> ControlMap : register(t1);

cbuffer LandscapeBuffer : register(b2)
{
    float4 _LandscapeSizing;
    float4 _LandscapeScaling;
    // x:Scale y:UVScrollY z:Metallic w:Smoothness
    half4 _LandscapeLayerData1[32];
    // x:HeightBlend
    half4 _LandscapeLayerData2[32];
};


struct Triangle
{
    half2 P0, P1, P2;
    half3 BC;
};
Triangle ComputeTriangle(half2 pos)
{
    half2 quadPos = round(pos / 2) * 2;
    half2 quadBC = abs(pos - quadPos);
    Triangle t;
    half4 rect = half4(quadPos.xy, quadPos.xy + (pos > quadPos ? 1 : -1));
    t.P0 = rect.xy;
    t.P2 = rect.zw;
    t.P1 = quadBC.x < quadBC.y ? rect.xw : rect.zy;
    t.BC.z = min(quadBC.x, quadBC.y);
    t.BC.y = abs(quadBC.x - quadBC.y);
    t.BC.x = 1 - (t.BC.y + t.BC.z);
    return t;
}


void TransformLandscapeVertex(inout float3 position, inout float3 normal, float2 offset) {
    // Each instance has its own offset
#if EDGE
    float2 axes[] = { float2(1, 0), float2(0, 1), float2(1, 0), float2(0, 1), };
    float2 axis = axes[offset.y];
    position.xz = axis * (position.x + offset.x);
    normal = float3(-axis.y, 0, -axis.x);
#else
    position.xz += offset;
#endif
}