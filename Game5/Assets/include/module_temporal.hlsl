#ifndef __MODULE_TEMPORAL__
#define __MODULE_TEMPORAL__

template<class ModuleBase> struct ModuleVelocity : ModuleBase {
    using VSInput = typename ModuleBase::VSInput;
    struct PSInput : ModuleBase::PSInput {
        float2 velocity : VELOCITY;
    };
    struct PSOutput : ModuleBase::PSOutput {
        float4 velocity : SV_Target1;
    };
    float2 GetClipVelocity() {
        ModuleBase prevBase = this;
        prevBase.model = ModuleBase::GetInstanceData().PreviousModel;
        prevBase.viewProjection = PreviousViewProjection;
        float4 currentVPos = ModuleBase::GetClipPosition();
        float4 previousVPos = prevBase.GetClipPosition();
        float2 velocity = currentVPos.xy / currentVPos.w - previousVPos.xy / previousVPos.w;
        // Add a slight amount to avoid velocity being 0 (special case)
        velocity.x += 0.0000001;
        return velocity;
    }
    float2 GetClipVelocity(PSInput input) {
        return input.velocity;
    }
};
            
#endif
