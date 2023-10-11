#include "GraphicsDeviceBase.h"

void CommandBuffer::DrawMesh(const Mesh* mesh, const Material* material, const DrawConfig& config)
{
    std::vector<const BufferLayout*> bindingLayout;
    mesh->CreateMeshLayout(bindingLayout);
    auto* pipeline = GetGraphics()->RequirePipeline(bindingLayout, material);

    void* resources[32];
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
    DrawMesh(bindingLayout, pipeline,
        std::span<void*>(resources, i),
        config, material->GetInstanceCount());
}

