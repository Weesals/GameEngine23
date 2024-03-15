#ifndef __MODULE_RETAINED__
#define __MODULE_RETAINED__

#include <retained.hlsl>

template<class ModuleBase> struct ModuleRetained : ModuleBase {
    uint primitiveId;
    matrix model;

    struct VSInput : ModuleBase::VSInput {
        uint primitiveId : INSTANCE;
    };

    void SetupVertexIntermediates(VSInput input) {
        ModuleBase::SetupVertexIntermediates(input);
        primitiveId = input.primitiveId;
        model = GetInstanceData().Model;
    }
    InstanceData GetInstanceData() {
        InstanceData instance = instanceData[primitiveId];
        return instance;
    }
    float3 GetWorldPosition() {
        return mul(model, float4(ModuleBase::GetLocalPosition(), 1.0)).xyz;
    }
    float3 GetWorldNormal() {
        return mul(model, float4(ModuleBase::GetLocalNormal(), 0.0)).xyz;
    }
};

#endif
