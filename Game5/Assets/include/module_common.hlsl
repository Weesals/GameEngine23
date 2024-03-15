#ifndef __VERTEXFACTORY_COMMON__
#define __VERTEXFACTORY_COMMON__

template<class Class>
struct Module : Class {
    template<template<typename Ty> class Other>
	using Then = Module<Other<Class> >;
};

struct ModuleCommon {
    struct VSInput {
    };
    struct PSInput {
    };
    struct PSOutput {
    };
    void SetupVertexIntermediates(VSInput input) { }
    void SetupPixelIntermediates(PSInput input) { }
    // These approaches dont work
    //PSInput VertexOutput(VSInput input) { return (PSInput)0; }
    //void PixelOutput(PSInput input, inout PSOutput result) { }
};

template<class ModuleBase> struct ModuleObject : ModuleBase {
    float4 vertexPosition;
    struct VSInput : ModuleBase::VSInput {
        float4 position : POSITION;
    };
    void SetupVertexIntermediates(VSInput input) {
        ModuleBase::SetupVertexIntermediates(input);
        vertexPosition = input.position;
    }
    float3 GetLocalPosition() {
        return vertexPosition.xyz;
    }
};

template<class ModuleBase> struct ModuleVertexNormals : ModuleBase {
    float3 vertexNormal;
    struct VSInput : ModuleBase::VSInput {
        float3 normal : NORMAL;
    };
    void SetupVertexIntermediates(VSInput input) {
        ModuleBase::SetupVertexIntermediates(input);
        vertexNormal = input.normal;
    }
    float3 GetLocalNormal() {
        return vertexNormal.xyz;
    }
};

template<class ModuleBase> struct ModuleClipSpace : ModuleBase {
    using VSInput = typename ModuleBase::VSInput;
    matrix view;
    matrix viewProjection;
    void SetupVertexIntermediates(VSInput input) {
        ModuleBase::SetupVertexIntermediates(input);
        view = View;
        viewProjection = ViewProjection;
    }
    float4 GetClipPosition() {
        float4 positionCS = TransformWorldToClip(ModuleBase::GetWorldPosition());
#if defined(VULKAN)
        positionCS.y = -positionCS.y;
#endif
        return positionCS;
    }
    float3 GetViewPosition() {
        return TransformWorldToView(ModuleBase::GetWorldPosition()).xyz;
    }
    float4 TransformWorldToClip(float3 worldPos) {
        return mul(viewProjection, float4(worldPos, 1.0));
    }
    float4 TransformWorldToView(float3 worldPos) {
        return mul(view, float4(worldPos, 1.0));
    }
};

#endif
