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

MeshDraw::MeshDraw() : mMesh(nullptr), mMaterial(nullptr), mPipeline(nullptr) { }
MeshDraw::MeshDraw(Mesh* mesh, Material* material)
    : mMesh(mesh), mMaterial(material), mPipeline(nullptr)
{
}
MeshDraw::~MeshDraw() {
}
void MeshDraw::InvalidateMesh() {
    mBufferLayout.clear();
    mMesh->CreateMeshLayout(mBufferLayout);
    mPipeline = nullptr;
}
void MeshDraw::Draw(CommandBuffer& cmdBuffer, const DrawConfig& config) {
    if (mBufferLayout.empty()) InvalidateMesh();
    if (mPipeline == nullptr)
        mPipeline = cmdBuffer.GetGraphics()->RequirePipeline(mBufferLayout, mMaterial);
    mMaterial->ResolveResources(cmdBuffer, mResources, mPipeline);
    cmdBuffer.DrawMesh(mBufferLayout, mPipeline, mResources, config, mMaterial->GetInstanceCount());
    mResources.clear();
}

MeshDrawInstanced::MeshDrawInstanced() : MeshDraw() { }
MeshDrawInstanced::MeshDrawInstanced(Mesh* mesh, Material* material) : MeshDraw(mesh, material) {
    mInstanceBuffer = BufferLayout(rand(), 0, BufferLayout::Usage::Instance, 0);
}
MeshDrawInstanced::~MeshDrawInstanced() { }
void MeshDrawInstanced::InvalidateMesh() {
    MeshDraw::InvalidateMesh();
    mBufferLayout.push_back(&mInstanceBuffer);
}
int MeshDrawInstanced::AddInstanceElement(const std::string_view& name, BufferFormat fmt, int stride) {
    mInstanceBuffer.mElements.push_back(BufferLayout::Element(name, fmt, stride, stride, nullptr));
    mPipeline = nullptr;
    return (int)mInstanceBuffer.mElements.size() - 1;
}
void MeshDrawInstanced::SetInstanceData(void* data, int count, int elementId, bool markDirty) {
    mInstanceBuffer.mElements[elementId].mData = data;
    if (mInstanceBuffer.mCount != count) {
        mInstanceBuffer.mCount = count;
        mPipeline = nullptr;
    }
    if (markDirty)
        mInstanceBuffer.mBuffer.mRevision++;
}

void MeshDrawInstanced::Draw(CommandBuffer& cmdBuffer, const DrawConfig& config) {
    auto instanceCount = mInstanceBuffer.mElements.empty() ? mMaterial->GetInstanceCount() : mInstanceBuffer.mCount;
    if (instanceCount == 0) return;
    if (mBufferLayout.empty()) InvalidateMesh();
    if (mPipeline == nullptr) {
        mInstanceBuffer.CalculateImplicitSize();
        mPipeline = cmdBuffer.GetGraphics()->RequirePipeline(mBufferLayout, mMaterial);
    }
    mMaterial->ResolveResources(cmdBuffer, mResources, mPipeline);
    cmdBuffer.DrawMesh(mBufferLayout, mPipeline, mResources, config, instanceCount);
    mResources.clear();
}
