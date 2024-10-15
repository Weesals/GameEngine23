#ifndef __RETAINED__
#define __RETAINED__

#include <common.hlsl>
#include <temporal.hlsl>

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
StructuredBuffer<float4> instanceData : register(t1);

InstanceData GetInstanceData(uint instanceId) {
    instanceId *= 10;
    InstanceData data = (InstanceData)0;
    data.Model = transpose(matrix(
        instanceData[instanceId + 0],
        instanceData[instanceId + 1],
        instanceData[instanceId + 2],
        instanceData[instanceId + 3]
    ));
    data.PreviousModel = transpose(matrix(
        instanceData[instanceId + 4],
        instanceData[instanceId + 5],
        instanceData[instanceId + 6],
        instanceData[instanceId + 7]
    ));
    data.Highlight = instanceData[instanceId + 8];
    data.Selected = instanceData[instanceId + 9].x;
    return data;
}

#endif
