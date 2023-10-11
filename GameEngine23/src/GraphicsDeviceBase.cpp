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
            auto data = material->GetUniformBinaryData(val.mNameId);
            std::memcpy(tmpData + val.mOffset, data.data(), data.size());
        }
        resources[i++] = RequireConstantBuffer(std::span<uint8_t>(tmpData, cb->mSize));
    }
    for (auto* rb : pipeline->mResources) {
        auto* data = material->GetUniformTexture(rb->mNameId);
        resources[i++] = data == nullptr ? nullptr : data->get();
    }
    DrawMesh(tBindingLayout, pipeline,
        std::span<const void*>(resources, i),
        config, material->GetInstanceCount(), name);
    tBindingLayout.clear();
}


const PipelineLayout* GraphicsDeviceBase::RequirePipeline(std::span<const BufferLayout*> bindings, std::span<const Material*> materials) {
    IdentifierWithName renderQueue;
    for (auto& mat : materials) {
        renderQueue = mat->GetRenderPassOverride();
        if (renderQueue.IsValid()) break;
    }
    return RequirePipeline(bindings, materials, renderQueue);
}
