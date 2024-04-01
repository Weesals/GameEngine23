#ifndef __LANDSCAPE_COMMON__
#define __LANDSCAPE_COMMON__

Texture2D<float4> HeightMap : register(t0);
Texture2D<uint> ControlMap : register(t1);

cbuffer LandscapeBuffer : register(b2)
{
    float4 _LandscapeSizing;
    float4 _LandscapeSizing1;
    float4 _LandscapeScaling;
};

struct LayerData {
    float Scale, UVScrollY, HeightBlend, Roughness;
    float Metallic, Pad1, Pad2, Pad3;
};
StructuredBuffer<LayerData> _LandscapeLayerData : register(t2);


struct Triangle {
    half2 P0, P1, P2;
    half3 BC;
    bool2 FlipSign;
    bool TriSign;
};
Triangle ComputeTriangle(half2 pos) {
    half2 quadPos = round(pos * 0.5) * 2;
    half2 quadBC = abs(pos - quadPos);
    Triangle t;
    bool2 sign = pos > quadPos;
    half4 rect = half4(quadPos.xy, quadPos.xy + select(sign, 1, -1));
    t.P0 = rect.xy;
    t.P2 = rect.zw;
    t.P1 = quadBC.x < quadBC.y ? rect.xw : rect.zy;
    t.BC.z = min(quadBC.x, quadBC.y);
    t.BC.y = abs(quadBC.x - quadBC.y);
    t.BC.x = 1 - (t.BC.y + t.BC.z);
    t.FlipSign = sign;
    t.TriSign = quadBC.x < quadBC.y;
    return t;
}


void TransformLandscapeVertex(inout float3 position, inout float3 normal, float2 offset) {
    // Each instance has its own offset
#if EDGE
    float2 axes[] = { float2(1, 0), float2(0, -1), float2(-1, 0), float2(0, 1), };
    float2 offsets[] = {
        float2(0, 0), float2(0, 0),
        float2(0, _LandscapeSizing.y),
        float2(_LandscapeSizing.x, 0)
    };
    float2 axis = axes[offset.y];
    position.xz = axis * position.x + axis * offset.x + offsets[offset.y];
    normal = float3(axis.y, 0, -axis.x);
#else
    position.xz += offset;
#endif
    position.xz = min(position.xz, _LandscapeSizing.xy);
}

void CalculateTerrainTBN(half3 n, out half3 tangent, out half3 bitangent) {
    half4 bc = n.xzxz;
    bc.xy *= n.z * rcp(n.y + 1.0);
    tangent.x = n.y + bc.y;
    tangent.yz = -bc.zx;
    bitangent = bc.xwy;
    bitangent.z -= 1;
}

#endif
