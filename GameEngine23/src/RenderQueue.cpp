#include "RenderQueue.h"
#include "MaterialEvaluator.h"

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


MeshDraw::MeshDraw() : mMesh(nullptr), mMaterial(nullptr) { }
MeshDraw::MeshDraw(Mesh* mesh, Material* material) : mMesh(mesh), mMaterial(material) { }
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
		const Material* materials[]{ mMaterial };
		item = mPassCache.emplace(item, RenderPassCache{
			.mRenderPass = renderPass,
			.mPipeline = cmdBuffer.GetGraphics()->RequirePipeline(mBufferLayout, materials, renderPass),
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
	mMaterial->ResolveResources(cmdBuffer, mResources, passCache->mPipeline);
	cmdBuffer.DrawMesh(mBufferLayout, passCache->mPipeline, mResources, config, mMaterial->GetInstanceCount());
	mResources.clear();
}

MeshDrawInstanced::MeshDrawInstanced() : MeshDraw() { }
MeshDrawInstanced::MeshDrawInstanced(Mesh* mesh, Material* material) : MeshDraw(mesh, material) {
	mInstanceBuffer = BufferLayoutPersistent(rand(), 0, BufferLayout::Usage::Instance, 0);
}
MeshDrawInstanced::~MeshDrawInstanced() { }
void MeshDrawInstanced::InvalidateMesh() {
	MeshDraw::InvalidateMesh();
	mBufferLayout.push_back(&mInstanceBuffer);
}
int MeshDrawInstanced::GetInstanceCount() {
	auto instanceCount = mInstanceBuffer.IsValid() ? mInstanceBuffer.mCount : mMaterial->GetInstanceCount();
	if (instanceCount == 0) return 0;
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
	mMaterial->ResolveResources(cmdBuffer, mResources, passCache->mPipeline);
	cmdBuffer.DrawMesh(mBufferLayout, passCache->mPipeline, mResources, config, instanceCount);
	mResources.clear();
}
void MeshDrawInstanced::Draw(CommandBuffer& cmdBuffer, RenderQueue* queue, const DrawConfig& config) {
	int instanceCount = GetInstanceCount();
	if (instanceCount <= 0) return;
	auto passCache = GetPassCache(cmdBuffer, IdentifierWithName::None);
	if (passCache == nullptr) return;
	if (queue != nullptr) {
		auto resources = queue->RequireMaterialResources(cmdBuffer, passCache->mPipeline, mMaterial);
		queue->AppendMesh(mMesh->GetName().c_str(), passCache->mPipeline, mBufferLayout.data(), resources.data(), RangeInt(0, instanceCount));
	}
	else {
		mMaterial->ResolveResources(cmdBuffer, mResources, passCache->mPipeline);
		cmdBuffer.DrawMesh(mBufferLayout, passCache->mPipeline, mResources, config, instanceCount);
		mResources.clear();
	}
}
void MeshDrawInstanced::Draw(CommandBuffer& cmdBuffer, RenderPass& pass, const DrawConfig& config) {
	int instanceCount = GetInstanceCount();
	if (instanceCount <= 0) return;
	auto passCache = GetPassCache(cmdBuffer, pass.GetRenderPassOverride());
	if (passCache == nullptr) return;
	auto& queue = pass.mRenderQueue;
	const Material* mats[] = { pass.mOverrideMaterial.get(), mMaterial, };
	std::span<const Material*> renderMats(mats + (mats[0] == nullptr ? 1 : 0), mats + 2);
	auto resources = MaterialEvaluator::ResolveResources(cmdBuffer, passCache->mPipeline, renderMats);

	// Copy buffer contents
	auto renBufferLayouts = cmdBuffer.RequireFrameData<BufferLayout>(+mBufferLayout,
		[](auto* buffer) { return *buffer; });
	// Copy elements from each buffer
	for (auto& buffer : renBufferLayouts)
		buffer.mElements = cmdBuffer.RequireFrameData(buffer.GetElements()).data();
	// Create array of pointers
	auto renBuffersPtrs = cmdBuffer.RequireFrameData<const BufferLayout*>(renBufferLayouts,
		[](auto& layout) { return &layout; });

	queue.AppendMesh(mMesh->GetName().c_str(), passCache->mPipeline, renBuffersPtrs.data(),
		resources.data(), RangeInt(0, instanceCount));
}

RenderPass::RenderPass(std::string_view name, const std::shared_ptr<GraphicsDeviceBase>& graphics)
	: mName(name), mFrustum(Matrix::Identity)
{
	mRetainedRenderer = std::make_shared<RetainedRenderer>(graphics);
}
void RenderPass::UpdateViewProj(const Matrix& view, const Matrix& proj)
{
	mView = view;
	mProjection = proj;
	mFrustum = Frustum(view * proj);
}

int RenderPassList::AddInstance(const Mesh* mesh, const Material* material, int dataSize) {
	auto sceneId = mScene->AllocateInstance(dataSize);
	if (sceneId >= (int)mPassIdsBySceneId.size()) mPassIdsBySceneId.resize(sceneId + 1);
	mPassIdsBySceneId[sceneId] = mPassIds.Allocate((int)mPasses.size());
	auto range = mPassIdsBySceneId[sceneId];
	for (int i = 0; i < range.length; ++i) {
		auto pass = mPasses[i];
		const Material* materials[2] = { pass->mOverrideMaterial.get(), material, };
		std::span<const Material*> matsSpan(materials + (materials[0] == nullptr ? 1 : 0), materials + 2);
		mPassIds[range.start + i] =
			mPasses[i]->mRetainedRenderer->AppendInstance(mesh, matsSpan, sceneId);
	}
	return sceneId;
}
void RenderPassList::RemoveInstance(int sceneId) {
	auto range = mPassIdsBySceneId[sceneId];
	for (int i = 0; i < range.length; ++i) {
		auto instanceId = mPassIds[range.start + i];
		if (instanceId < 0) continue;
		mPasses[i]->mRetainedRenderer->RemoveInstance(instanceId);
	}
	mScene->RemoveInstance(sceneId);
}
