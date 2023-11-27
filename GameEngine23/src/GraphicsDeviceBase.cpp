#include "GraphicsDeviceBase.h"

void CommandBuffer::DrawMesh(const Mesh* mesh, const Material* material, const DrawConfig& config, const char* name)
{
    mesh->CreateMeshLayout(tBindingLayout);
    const Material* materials[]{ material };
    auto* pipeline = GetGraphics()->RequirePipeline(tBindingLayout, materials);

    const void* resources[32];
    int i = 0;
    for (auto* cb : pipeline->mConstantBuffers) {
        uint8_t tmpData[4096];
        for (auto& val : cb->mValues) {
            auto data = material->GetUniformBinaryData(val.mName);
            std::memcpy(tmpData + val.mOffset, data.data(), data.size());
        }
        resources[i++] = RequireConstantBuffer(std::span<uint8_t>(tmpData, cb->mSize));
    }
    for (auto* rb : pipeline->mResources) {
        auto* data = material->GetUniformTexture(rb->mName);
        resources[i++] = data == nullptr ? nullptr : data->get();
    }
    DrawMesh(tBindingLayout, pipeline,
        std::span<const void*>(resources, i),
        config, material->GetInstanceCount(), name);
    tBindingLayout.clear();
}


struct Getter {
    std::span<const Material*> materials;
    template<class T, typename Fn>
    T MaterialGet(const Fn& fn) {
        T out = T();
        for (auto& mat : materials) {
            if (fn(mat, out)) return out;
        }
        return out;
    };
};
const PipelineLayout* GraphicsDeviceBase::RequirePipeline(std::span<const BufferLayout*> bindings, std::span<const Material*> materials) {
    IdentifierWithName renderQueue;
    for (auto& mat : materials) {
        renderQueue = mat->GetRenderPassOverride();
        if (renderQueue.IsValid()) break;
    }
    return RequirePipeline(bindings, materials, renderQueue);
}
const PipelineLayout* GraphicsDeviceBase::RequirePipeline(std::span<const BufferLayout*> bindings, std::span<const Material*> materials, const IdentifierWithName& renderQueue) {
    Getter getter = { .materials = materials };
    // Get the relevant shaders
    const auto& sourceVS = *getter.MaterialGet<Shader*>([](const Material* mat, Shader*& out) { out = mat->GetVertexShader().get(); return out != nullptr; });
    const auto& sourcePS = *getter.MaterialGet<Shader*>([](const Material* mat, Shader*& out) { out = mat->GetPixelShader().get(); return out != nullptr; });
    const auto& materialState = materials.back()->GetMaterialState();

    return RequirePipeline(sourceVS, sourcePS, materialState, bindings, renderQueue);
}
