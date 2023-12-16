#ifndef __RETAINED__
#define __RETAINED__

#include "include/common.hlsl"

struct InstanceData
{
    matrix Model;
    matrix Unused;
    float4 Highlight;
    float4 V10;
};
StructuredBuffer<InstanceData> instanceData : register(t1);

#endif
