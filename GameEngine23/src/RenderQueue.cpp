#include "RenderQueue.h"

RenderQueue::RenderQueue() {
	mInstanceBufferLayout = BufferLayout(-1, 0, BufferLayout::Usage::Instance, -1);
	mInstanceBufferLayout.mElements.push_back(
		BufferLayout::Element("INSTANCE", BufferFormat::FORMAT_R32_UINT, sizeof(int), sizeof(int), nullptr)
	);
}
void RenderQueue::Clear()
{
	// Clear previous data
	mFrameData.clear();
	mResourceData.clear();
	mInstancesBuffer.clear();
	mDraws.clear();
	mFrameData.reserve(2048);
}
RangeInt RenderQueue::RequireMaterialResources(CommandBuffer& cmdBuffer,
	const PipelineLayout* pipeline, const Material* material)
{
	int offset = (int)mResourceData.size();
	//int count = (int)resources.size();
	//mResourceData.resize(offset + count);
	material->ResolveResources(cmdBuffer, mResourceData, pipeline);
	return RangeInt(offset, (int)mResourceData.size() - offset);
}
void RenderQueue::AppendMesh(const PipelineLayout* pipeline, const BufferLayout** buffers,
	RangeInt resources, RangeInt instances)
{
	mDraws.push_back(DrawBatch{
		.mPipelineLayout = pipeline,
		.mBufferLayouts = buffers,
		.mResourceRange = resources,
		.mInstanceRange = instances,
	});
}
void RenderQueue::AppendMesh(CommandBuffer& cmdBuffer, const Mesh* mesh, const Material* material)
{
	std::vector<const BufferLayout*> bufferLayout;
	mesh->CreateMeshLayout(bufferLayout);
	auto pbuffLayout = cmdBuffer.RequireFrameData<const BufferLayout*>((int)bufferLayout.size());
	std::copy(bufferLayout.begin(), bufferLayout.end(), pbuffLayout.data());
	auto* pipeline = cmdBuffer.GetGraphics()->RequirePipeline(bufferLayout, material);
	auto resourcesRange = RequireMaterialResources(cmdBuffer, pipeline, material);
	AppendMesh(pipeline, &pbuffLayout.front(), resourcesRange, RangeInt(0, 1));
}

void RenderQueue::Flush(CommandBuffer& cmdBuffer)
{
	// Setup the instance buffer
	mInstanceBufferLayout.mElements[0].mData = mInstancesBuffer.data();
	mInstanceBufferLayout.mBuffer.mSize = mInstanceBufferLayout.mElements[0].mItemSize * (int)mInstancesBuffer.size();
	mInstanceBufferLayout.mBuffer.mRevision++;
	// Submit daw calls
	for (auto& draw : mDraws) {
		// The subregion of this draw calls instances
		mInstanceBufferLayout.mOffset = draw.mInstanceRange.start;
		mInstanceBufferLayout.mCount = draw.mInstanceRange.length;

		// Submit
		DrawConfig config = DrawConfig::MakeDefault();
		auto* pipeline = draw.mPipelineLayout;
		cmdBuffer.DrawMesh(
			std::span<const BufferLayout*>(draw.mBufferLayouts, pipeline->mBindings.size()),
			pipeline,
			std::span<void*>(mResourceData.begin() + draw.mResourceRange.start, draw.mResourceRange.length),
			config,
			(int)draw.mInstanceRange.length
		);
	}
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
	assert(mPipeline->mBindings.size() == mBufferLayout.size());
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
	assert(mPipeline->mBindings.size() == mBufferLayout.size());
	mMaterial->ResolveResources(cmdBuffer, mResources, mPipeline);
	cmdBuffer.DrawMesh(mBufferLayout, mPipeline, mResources, config, instanceCount);
	mResources.clear();
}
void MeshDrawInstanced::Draw(CommandBuffer& cmdBuffer, RenderQueue* queue, const DrawConfig& config) {
	auto instanceCount = mInstanceBuffer.mElements.empty() ? mMaterial->GetInstanceCount() : mInstanceBuffer.mCount;
	if (instanceCount == 0) return;
	if (mBufferLayout.empty()) InvalidateMesh();
	if (mPipeline == nullptr) {
		mInstanceBuffer.CalculateImplicitSize();
		mPipeline = cmdBuffer.GetGraphics()->RequirePipeline(mBufferLayout, mMaterial);
	}
	assert(mPipeline->mBindings.size() == mBufferLayout.size());
	if (queue != nullptr) {
		auto resourcesRange = queue->RequireMaterialResources(cmdBuffer, mPipeline, mMaterial);
		queue->AppendMesh(mPipeline, &mBufferLayout.front(), resourcesRange, RangeInt(0, instanceCount));
	}
	else {
		mMaterial->ResolveResources(cmdBuffer, mResources, mPipeline);
		cmdBuffer.DrawMesh(mBufferLayout, mPipeline, mResources, config, instanceCount);
		mResources.clear();
	}
}

RenderPass::RenderPass(RenderQueue* queue, Matrix view, Matrix proj)
	: mRenderQueue(queue), mView(view), mProjection(proj), mFrustum(view* proj)
{
}
