#ifndef __MODULE_LIGHTING__
#define __MODULE_LIGHTING__

#include <lighting.hlsl>

template<class ModuleBase> struct ModuleLighting : ModuleBase {
    struct PSInput : ModuleBase::PSInput {
        float3 viewPos : VIEWPOS;
        float3 normal : NORMAL;
    };
    float3 GetWorldPosition(VSInput input) {
        return input.position.xyz;
    }
};

#endif
