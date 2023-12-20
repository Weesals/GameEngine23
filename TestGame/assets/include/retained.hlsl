#ifndef __RETAINED__
#define __RETAINED__

#include "include/common.hlsl"
#include "include/temporal.hlsl"

struct InstanceData
{
    matrix Model;
    matrix PreviousModel;
    float4 Highlight;
    float Selected;
    float Dummy1;
    float Dummy2;
    float Dummy3;
};
StructuredBuffer<InstanceData> instanceData : register(t1);

#endif
