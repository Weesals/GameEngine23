#include "RenderQueue.h"
#include "MaterialEvaluator.h"
#include "RetainedRenderer.h"

RenderQueue::RenderQueue() {
	mInstanceBufferLayout = BufferLayoutPersistent((size_t)this, 0, BufferLayout::Usage::Instance, -1);
	mInstanceBufferLayout.AppendElement(
		BufferLayout::Element("INSTANCE", BufferFormat::FORMAT_R32_UINT, sizeof(int), sizeof(int), nullptr)
	);
}
void RenderQueue::Clear()
{
	// Clear previous data
	mFrameData.clear();
	mInstancesBuffer.clear();
	mDraws.clear();
	mFrameData.reserve(2048);
}
std::span<const void*> RenderQueue::RequireMaterialResources(CommandBuffer& cmdBuffer,
	const PipelineLayout* pipeline, const Material* material)
{
	return MaterialEvaluator::ResolveResources(cmdBuffer, pipeline, std::span<const Material*>(&material, 1));
}
std::span<const BufferLayout*> RenderQueue::ImmortalizeBufferLayout(CommandBuffer& cmdBuffer, std::span<const BufferLayout*> bindings) {
	// Copy buffer contents
	auto renBufferLayouts = cmdBuffer.RequireFrameData<BufferLayout>(bindings,
		[](auto* buffer) { return *buffer; });
	// Copy elements from each buffer
	for (auto& buffer : renBufferLayouts)
		buffer.mElements = cmdBuffer.RequireFrameData(buffer.GetElements()).data();
	// Create array of pointers
	auto renBuffersPtrs = cmdBuffer.RequireFrameData<const BufferLayout*>(renBufferLayouts,
		[](auto& layout) { return &layout; });
	return renBuffersPtrs;
}
void RenderQueue::AppendMesh(const char* name, const PipelineLayout* pipeline, const BufferLayout** buffers,
	const void** resources, RangeInt instances)
{
	mDraws.push_back(DrawBatch{
		.mName = name,
		.mPipelineLayout = pipeline,
		.mBufferLayouts = buffers,
		.mResources = resources,
		.mInstanceRange = instances,
	});
}
void RenderQueue::AppendMesh(const char* name, CommandBuffer& cmdBuffer, const Mesh* mesh, const Material* material)
{
	std::vector<const BufferLayout*> bufferLayout;
	mesh->CreateMeshLayout(bufferLayout);
	auto pbuffLayout = cmdBuffer.RequireFrameData<const BufferLayout*>((int)bufferLayout.size());
	std::copy(bufferLayout.begin(), bufferLayout.end(), pbuffLayout.data());
	const Material* materials[]{ material };
	auto* pipeline = cmdBuffer.GetGraphics()->RequirePipeline(bufferLayout, materials);
	auto resources = RequireMaterialResources(cmdBuffer, pipeline, material);
	AppendMesh(name, pipeline, &pbuffLayout.front(), resources.data(), RangeInt(0, 1));
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
			std::span<const void*>(draw.mResources, draw.mResources + pipeline->GetResourceCount()),
			config,
			(int)draw.mInstanceRange.length,
			draw.mName
		);
	}
}


MeshDraw::MeshDraw() : mMesh(nullptr) { }
MeshDraw::MeshDraw(const Mesh* mesh, const Material* material) : MeshDraw(mesh, std::span<const Material*>(&material, 1)) { }
MeshDraw::MeshDraw(const Mesh* mesh, std::span<const Material*> materials)
	: mMesh(mesh), mMaterials(materials.begin(), materials.end()) { }
MeshDraw::~MeshDraw() {
}
void MeshDraw::InvalidateMesh() {
	mBufferLayout.clear();
	mMesh->CreateMeshLayout(mBufferLayout);
	mPassCache.clear();
}
const MeshDraw::RenderPassCache* MeshDraw::GetPassCache(CommandBuffer& cmdBuffer, const IdentifierWithName& renderPass) {
	if (mBufferLayout.empty()) InvalidateMesh();
	auto item = std::partition_point(mPassCache.begin(), mPassCache.end(), [=](auto& item) {
		return renderPass.mId < item.mRenderPass.mId;
		});
	if (item == mPassCache.end() || item->mRenderPass != renderPass) {
		item = mPassCache.emplace(item, RenderPassCache{
			.mRenderPass = renderPass,
			.mPipeline = cmdBuffer.GetGraphics()->RequirePipeline(mBufferLayout, mMaterials, renderPass),
		});
	}
	if (!item->mPipeline->IsValid()) return nullptr;
	return &*item;
}
void MeshDraw::Draw(CommandBuffer& cmdBuffer, const DrawConfig& config) {
	if (mBufferLayout.empty()) InvalidateMesh();
	auto passCache = GetPassCache(cmdBuffer, IdentifierWithName::None);
	if (passCache == nullptr) return;
	assert(passCache->mPipeline->mBindings.size() == mBufferLayout.size());
	int instanceCount = 0;
	for (auto* mat : mMaterials) instanceCount = std::max(instanceCount, mat->GetInstanceCount());
	auto resources = MaterialEvaluator::ResolveResources(cmdBuffer, passCache->mPipeline, mMaterials);
	cmdBuffer.DrawMesh(mBufferLayout, passCache->mPipeline, resources, config, instanceCount);
}

MeshDrawInstanced::MeshDrawInstanced() : MeshDraw() { }
MeshDrawInstanced::MeshDrawInstanced(const Mesh* mesh, const Material* material) : MeshDrawInstanced(mesh, std::span<const Material*>(&material, 1)) { }
MeshDrawInstanced::MeshDrawInstanced(const Mesh* mesh, std::span<const Material*> materials) : MeshDraw(mesh, materials) {
	mInstanceBuffer = BufferLayoutPersistent(rand(), 0, BufferLayout::Usage::Instance, 0);
}
MeshDrawInstanced::~MeshDrawInstanced() { }
void MeshDrawInstanced::InvalidateMesh() {
	MeshDraw::InvalidateMesh();
	mBufferLayout.push_back(&mInstanceBuffer);
}
int MeshDrawInstanced::GetInstanceCount() {
	if (mInstanceBuffer.IsValid()) return mInstanceBuffer.mCount;
	int instanceCount = 0;
	for (auto* mat : mMaterials) instanceCount = std::max(instanceCount, mat->GetInstanceCount());
	return instanceCount;
}
int MeshDrawInstanced::AddInstanceElement(const char* name, BufferFormat fmt, int stride) {
	mInstanceBuffer.AppendElement(BufferLayout::Element(name, fmt, stride, stride, nullptr));
	mPassCache.clear();
	return (int)mInstanceBuffer.GetElements().size() - 1;
}
void MeshDrawInstanced::SetInstanceData(void* data, int count, int elementId, bool markDirty) {
	mInstanceBuffer.mElements[elementId].mData = data;
	if (mInstanceBuffer.mCount != count) {
		mInstanceBuffer.mCount = count;
		mInstanceBuffer.CalculateImplicitSize();
	}
	if (markDirty)
		mInstanceBuffer.mBuffer.mRevision++;
}

void MeshDrawInstanced::Draw(CommandBuffer& cmdBuffer, const DrawConfig& config) {
	int instanceCount = GetInstanceCount();
	if (instanceCount <= 0) return;
	auto passCache = GetPassCache(cmdBuffer, IdentifierWithName::None);
	if (passCache == nullptr) return;
	assert(passCache->mPipeline->mBindings.size() == mBufferLayout.size());
	auto resources = MaterialEvaluator::ResolveResources(cmdBuffer, passCache->mPipeline, mMaterials);
	cmdBuffer.DrawMesh(mBufferLayout, passCache->mPipeline, resources, config, instanceCount);
}
void MeshDrawInstanced::Draw(CommandBuffer& cmdBuffer, RenderQueue* queue, const DrawConfig& config) {
	int instanceCount = GetInstanceCount();
	if (instanceCount <= 0) return;
	auto passCache = GetPassCache(cmdBuffer, IdentifierWithName::None);
	if (passCache == nullptr) return;
	if (queue != nullptr) {
		auto resources = MaterialEvaluator::ResolveResources(cmdBuffer, passCache->mPipeline, mMaterials);
		queue->AppendMesh(mMesh->GetName().c_str(), passCache->mPipeline, mBufferLayout.data(), resources.data(), RangeInt(0, instanceCount));
	}
	else {
		auto resources = MaterialEvaluator::ResolveResources(cmdBuffer, passCache->mPipeline, mMaterials);
		cmdBuffer.DrawMesh(mBufferLayout, passCache->mPipeline, resources, config, instanceCount);
	}
}
void MeshDrawInstanced::Draw(CommandBuffer& cmdBuffer, RenderPass& pass, const DrawConfig& config) {
	int instanceCount = GetInstanceCount();
	if (instanceCount <= 0) return;
	auto passCache = GetPassCache(cmdBuffer, pass.GetRenderPassOverride());
	if (passCache == nullptr) return;
	auto& queue = pass.mRenderQueue;
	InplaceVector<const Material*, 10> renMaterials;
	renMaterials.push_back_if_not_null(pass.mOverrideMaterial.get());
	for (auto* mat : mMaterials) renMaterials.push_back(mat);

	auto resources = MaterialEvaluator::ResolveResources(cmdBuffer, passCache->mPipeline, renMaterials);
	auto renBufferPtrs = queue.ImmortalizeBufferLayout(cmdBuffer, mBufferLayout);

	queue.AppendMesh(mMesh->GetName().c_str(), passCache->mPipeline, renBufferPtrs.data(),
		resources.data(), RangeInt(0, instanceCount));
}
