#ifndef __LANDSCAPE_COMMON__
#define __LANDSCAPE_COMMON__

static const float HeightScale = 512.0;

Texture2D<float4> HeightMap : register(t0);
Texture2D<uint> ControlMap : register(t1);

cbuffer LandscapeBuffer : register(b2) {
    float4 _LandscapeSizing;
    float4 _LandscapeSizing1;
    float4 _LandscapeScaling;
};

struct LayerData {
    float Scale, UVScrollY, HeightBlend, Roughness;
    float Metallic, Pad1, Pad2, Pad3;
};
StructuredBuffer<LayerData> _LandscapeLayerData : register(t2);

struct ControlPoint {
    uint4 Data;
    uint Layer;
    float Rotation;
    float2x2 RotationMatrix;
};
ControlPoint DecodeControlMap(uint cp) {
    ControlPoint o;
    o.Data = ((cp >> uint4(8, 0, 16, 24)) & 0xff);
    o.Layer = o.Data.r;
    o.Rotation = o.Data.g * (3.1415 * 2.0 / 256.0);
    float2 sc; sincos(o.Rotation, sc.x, sc.y);
    o.RotationMatrix = float2x2(sc.y, -sc.x, sc.x, sc.y);
    return o;
}
struct HeightPoint {
    float HeightOS;
    float2 DerivativeOS;
    float3 NormalOS;
};
HeightPoint DecodeHeightMap(float4 hcell) {
    HeightPoint o;
    o.HeightOS = _LandscapeScaling.z + (hcell.r) * _LandscapeScaling.w;
    float DerivativeScale = (256.0 / 16.0);
    o.DerivativeOS = hcell.yz * DerivativeScale - 0.5 * DerivativeScale;
    //o.NormalOS.xz = hcell.yz * 2.0 - 1.0;
    //o.NormalOS.y = sqrt(1.0 - dot(o.NormalOS.xz, o.NormalOS.xz));
    o.NormalOS = normalize(float3(o.DerivativeOS, 1).xzy);
    return o;
}


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
    half4 bc = half4(n.xz, n.xz * (n.z * rcp(n.y + 1.0)));
    tangent = half3(n.y + bc.w, -bc.xz);
    bitangent = -half3(bc.zy, bc.w - 1.0);
}

#endif
