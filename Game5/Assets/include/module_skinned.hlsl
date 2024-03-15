#ifndef __MODULE_SKINNED__
#define __MODULE_SKINNED__

cbuffer SkinCB {
    matrix BoneTransforms[32];
}

template<class ModuleBase> struct ModuleSkinned : ModuleBase {
    uint4 boneIds;
    float4 boneWeights;
    struct VSInput : ModuleBase::VSInput {
        uint4 boneIds : BLENDINDICES;
        float4 boneWeights : BLENDWEIGHT;
    };
    struct PSInput : ModuleBase::PSInput {
    };
    void SetupVertexIntermediates(VSInput input) {
        ModuleBase::SetupVertexIntermediates(input);
        boneIds = input.boneIds;
        boneWeights = input.boneWeights;
    }
    matrix GetSkinTransform() {
        matrix boneTransform = 0;
        for (int i = 0; i < 4; ++i) {
            boneTransform += BoneTransforms[boneIds[i]] * boneWeights[i];
        }
        return boneTransform;
    }
    float3 GetLocalPosition() {
        return mul(GetSkinTransform(), float4(ModuleBase::GetLocalPosition(), 1.0)).xyz;
    }
    float3 GetLocalNormal() {
        return mul(GetSkinTransform(), float4(ModuleBase::GetLocalNormal(), 0.0)).xyz;
    }
};

#endif
