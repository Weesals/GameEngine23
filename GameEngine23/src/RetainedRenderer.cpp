#include "RetainedRenderer.h"

#include "RenderQueue.h"

/*
* Each retained renderable keeps a RetainedMaterialSet (span<Material>)
* When rendering, add instances with matset id
* Resolve matsets
* Resolve PSO and resources
* Combine matsets based on PSO/res
* Combine instances with same mesh/matset
* Issue render calls
* 
* Need PSO before can resolve CBs
* Should match individual CBs
* TODO: Partial CBs? Where one CB contains another with some offset/size
* 
* RenderPassList::AddInstance(Mesh, []{ Material })
* RetainedRenderer::AppendMesh(Mesh, []{ Pass.OverrideMaterial, Material }, SceneId)
* -> Get a new matset ID with Pass.OverrideMaterial
* -> Add to batch with key { Mesh, MatSetID } => { SceneId }
* OnRender()
* -> Iterate all (dirty) batches
*   -> Generate MaterialEvaluator
*   -> Generate SourceHash
*   -> Append to SourceHashCache { size_t Hash, MatEval } => { batch_id[] }
* -> Iterate all SourceHashCache x batch_id
*   -> Generate instance (culling)
*   -> If no instances, continue
*   -> Generate CB/res
*   -> Append draw call
*/

size_t ResolvedMaterialSets::GenerateHash(CommandBuffer& cmdBuffer, size_t valueHash, int matSetId) {
	size_t hash = (size_t)cmdBuffer.GetGraphics();
	// TODO: Find subregions of CBs
	hash = AppendHash(valueHash, hash);
	hash = AppendHash(matSetId, hash);
	return hash;
}
int ResolvedMaterialSets::RequireResolved(CommandBuffer& cmdBuffer, std::span<const ShaderBase::UniformValue> values, int matSetId) {
	size_t valueHash = 0;
	for (auto& value : values) valueHash += value.GenerateHash() * 1234567;
	size_t hash = GenerateHash(cmdBuffer, valueHash, matSetId);
	auto item = mResolvedByHash.find(hash);
	if (item == mResolvedByHash.end()) {
		item = mResolvedByHash.insert(std::make_pair(hash, (int)mResolved.size())).first;
		mResolved.push_back(ResolvedMaterialSet());
	}
	auto& resolved = mResolved[item->second];
	if (!resolved.mEvaluator.IsValid()) {
		mMaterialCollector.Clear();
		auto materials = mMatCollection->GetMaterials(matSetId);
		MaterialCollectorContext context(materials, mMaterialCollector);
		for (int v = 0; v < values.size(); ++v)
			context.GetUniformSource(values[v].mName, context);

		// Force the correct output layout
		mMaterialCollector.FinalizeAndClearOutputOffsets();
		for (int v = 0; v < values.size(); ++v)
			mMaterialCollector.SetItemOutputOffset(values[v].mName, values[v].mOffset, values[v].mSize);
		mMaterialCollector.RepairOutputOffsets();

		// Add the resource evaluator
		mMaterialCollector.BuildEvaluator(resolved.mEvaluator);

		resolved.mSourceHash = GenericHash({ mMaterialCollector.GenerateSourceHash(), valueHash });
	}
	return item->second;
}
const ResolvedMaterialSets::ResolvedMaterialSet& ResolvedMaterialSets::GetResolved(int id) {
	return mResolved[id];
}


RetainedScene::RetainedScene()
	: mGPUBuffer(1024)
	, mResolvedMats(&mMatCollection)
{
	mFreeGPUBuffer.Return(0, mGPUBuffer.GetCount());
}

std::span<const Vector4> RetainedScene::GetInstanceData(int instanceId) const {
	return mGPUBuffer.GetValues(mInstances[instanceId].mData);
}

int RetainedScene::AllocateInstance(int instanceDataSize) {
	// Round up to the next Vector4
	int instanceDataCount = (instanceDataSize + sizeof(Vector4) - 1) / sizeof(Vector4);
	// Allocate an instance in the batch
	int id = mInstances.Add(Instance{ });
	auto& instance = mInstances[id];
	// Allocate the required amount of data
	for (int t = 0; t < 10; ++t) {
		instance.mData = mFreeGPUBuffer.Allocate(instanceDataCount);
		if (instance.mData.start >= 0) break;
		// Allocate failed, try to resize
		int oldCount = mGPUBuffer.SetCount(std::max(mGPUBuffer.GetCount() * 2, 1024));
		mFreeGPUBuffer.Return(oldCount, mGPUBuffer.GetCount() - oldCount);
	}
	return id;
}
// Add user data for a mesh instance
bool RetainedScene::UpdateInstanceData(int instanceId, std::span<const uint8_t> tdata) {
	auto& instance = mInstances[instanceId];
	auto data = mGPUBuffer.GetValues(instance.mData);
	if (std::memcmp(data.data(), tdata.data(), tdata.size()) == 0) return false;
	std::memcpy(data.data(), tdata.data(), tdata.size());
	mGPUBuffer.MarkChanged(instance.mData);
	mGPUDelta.AppendRegion(instance.mData);
	return true;
}
// Remove a mesh instance
void RetainedScene::RemoveInstance(int instanceId) {
	auto& instance = mInstances[instanceId];
	mFreeGPUBuffer.Return(instance.mData);
	mInstances.Return(instanceId);
}

// Update only the changed regions
void RetainedScene::SubmitGPUMemory(CommandBuffer& cmdBuffer) {
	auto regions = mGPUDelta.GetRegions();
	if (regions.empty()) return;
	cmdBuffer.CopyBufferData(&mGPUBuffer, regions);
	mGPUDelta.Clear();
}




RetainedRenderer::RetainedRenderer() {
	mInstanceBufferLayout = BufferLayoutPersistent(-1, 0, BufferLayout::Usage::Instance, -1, 1);
	mInstanceBufferLayout.AppendElement(
		BufferLayout::Element("INSTANCE", BufferFormat::FORMAT_R32_UINT, sizeof(int), nullptr)
	);
}
void RetainedRenderer::SetScene(const std::shared_ptr<RetainedScene>& scene) {
	mGPUScene = scene;
	mInstanceMaterial.SetUniformTexture("instanceData", &mGPUScene->GetGPUBuffer());
}
const std::shared_ptr<RetainedScene>& RetainedRenderer::GetScene() const { return mGPUScene; }
int RetainedRenderer::AppendInstance(const Mesh* mesh, std::span<const Material*> materials, int sceneId) {
	InplaceVector<const Material*, 10> mats(materials);
	mats.push_back(&mInstanceMaterial);
	int matSetId = mGPUScene->GetMatCollection().Require(mats);
	auto key = StateKey(mesh, matSetId);
	auto bucket = std::partition_point(mBatches.begin(), mBatches.end(), [&](auto& item) { return item < key; });
	if (bucket == mBatches.end() || key != *bucket) {
		bucket = mBatches.insert(bucket, Batch(mesh, matSetId));
		std::vector<const BufferLayout*> bindings;
		mesh->CreateMeshLayout(bindings);
		bindings.push_back(&mInstanceBufferLayout);
		bucket->OverwriteBufferLayout(bindings);
	}
	auto instance = std::partition_point(bucket->mInstances.begin(), bucket->mInstances.end(), [&](auto& item) { return item < sceneId; });
	bucket->mInstances.insert(instance, sceneId);
	mInstanceBatches.insert(std::make_pair(sceneId, key));
	return sceneId;
}
void RetainedRenderer::SetVisible(int sceneId, bool visible) {
	auto key = mInstanceBatches.find(sceneId)->second;
	auto bucket = std::partition_point(mBatches.begin(), mBatches.end(), [&](auto& item) { return item < key; });
	auto instance = std::partition_point(bucket->mInstances.begin(), bucket->mInstances.end(), [&](auto& item) { return item < sceneId; });
	if (visible) {
		if (instance == bucket->mInstances.end() || *instance != sceneId)
			bucket->mInstances.insert(instance, sceneId);
	}
	else {
		if (instance != bucket->mInstances.end() && *instance == sceneId)
			bucket->mInstances.erase(instance);
	}
}
void RetainedRenderer::RemoveInstance(int sceneId) {
	auto key = mInstanceBatches.find(sceneId)->second;
	mInstanceBatches.erase(sceneId);
	auto bucket = std::partition_point(mBatches.begin(), mBatches.end(), [&](auto& item) { return item < key; });
	auto instance = std::partition_point(bucket->mInstances.begin(), bucket->mInstances.end(), [&](auto& item) { return item < sceneId; });
	bucket->mInstances.erase(instance);
}
void RetainedRenderer::SubmitToRenderQueue(CommandBuffer& cmdBuffer, RenderQueue& queue, const Frustum& frustum) {
	auto& gpuBuffer = mGPUScene->GetGPUBuffer();
	for (auto& batch : mBatches) {
		if (batch.mInstances.empty()) continue;

		auto* mesh = batch.mMesh;
		auto instBegin = queue.mInstancesBuffer.size();

		// Calculate visible instances
		auto& bbox = mesh->GetBoundingBox();
		auto bboxCtr = bbox.Centre();
		auto bboxExt = bbox.Extents();
		for (auto& instance : batch.mInstances) {
			auto data = mGPUScene->GetInstanceData(instance);
			auto& matrix = *(Matrix*)(data.data());
			if (!frustum.GetIsVisible(Vector3::Transform(bboxCtr, matrix), bboxExt)) continue;
			queue.mInstancesBuffer.push_back(instance);
		}
		// If no instances were created, quit
		if (queue.mInstancesBuffer.size() == instBegin) continue;

		// Compute and cache CB and resource data
		auto* graphics = cmdBuffer.GetGraphics();
		auto meshMatHash = VariadicHash(batch.mMesh, batch.mMaterialSetId, cmdBuffer.GetGraphics());
		auto resolvedKV = mPipelineCache.find(meshMatHash);
		if (resolvedKV == mPipelineCache.end()) {
			auto materials = mGPUScene->GetMatCollection().GetMaterials(batch.mMaterialSetId);
			auto pso = graphics->RequirePipeline(batch.mBufferLayout, materials);
			resolvedKV = mPipelineCache.insert(std::make_pair(meshMatHash, ResolvedPipeline{
				.mPipeline = pso,
			})).first;
			auto& resolved = resolvedKV->second;
			for (auto* cb : pso->mConstantBuffers) {
				auto resolvedId = mGPUScene->mResolvedMats.RequireResolved(cmdBuffer, cb->mValues, batch.mMaterialSetId);
				resolved.mResolvedCBs.push_back(resolvedId);
			}
			std::vector<ShaderBase::UniformValue> resources;
			for (size_t i = 0; i < pso->mResources.size(); ++i) {
				auto* res = pso->mResources[i];
				resources.push_back(ShaderBase::UniformValue{
					.mName = IdentifierWithName(res->mName),
					.mOffset = (int)(i * sizeof(void*)),
					.mSize = (int)sizeof(void*),
				});
			}
			resolved.mResolvedResources = mGPUScene->mResolvedMats.RequireResolved(cmdBuffer, resources, batch.mMaterialSetId);
		}

		auto& resolved = resolvedKV->second;
		auto* pipeline = resolved.mPipeline;
		auto resources = cmdBuffer.RequireFrameData<const void*>(pipeline->GetResourceCount());
		int r = 0;

		// Get constant buffer data for this batch
		for (int i = 0; i < resolved.mResolvedCBs.size(); ++i) {
			auto cbid = resolved.mResolvedCBs[i];
			void* resource = nullptr;
			if (cbid >= 0) {
				auto& resolved = mGPUScene->mResolvedMats.GetResolved(cbid);
				auto cbData = resolved.mEvaluator.EvaluateAppend(queue.mFrameData, pipeline->mConstantBuffers[i]->mSize);
				resource = cmdBuffer.RequireConstantBuffer(cbData);
			}
			resources[r++] = resource;
		}
		// Get other resource data for this batch
		{
			auto& resCB = mGPUScene->mResolvedMats.GetResolved(resolved.mResolvedResources);
			int count = (int)pipeline->mResources.size();
			resCB.mEvaluator.EvaluateSafe(std::span<uint8_t>((uint8_t*)&resources[r], sizeof(void*) * count));
			r += count;
		}

		// Need to force this to use queues instance buffer
		// (so that the range can be adjusted by RenderQueue)
		// TODO: A more generic approach
		batch.mBufferLayout.back() = &queue.mInstanceBufferLayout;
		// Add the draw command
		queue.AppendMesh(
			mesh->GetName().c_str(),
			pipeline,
			batch.mBufferLayout.data(),
			resources.data(),
			RangeInt::FromBeginEnd((int)instBegin, (int)queue.mInstancesBuffer.size())
		);
	}
}


RenderPass::RenderPass(std::string_view name)
	: mName(name), mFrustum(Matrix::Identity)
{
	mRetainedRenderer = std::make_shared<RetainedRenderer>();
}
void RenderPass::UpdateViewProj(const Matrix& view, const Matrix& proj)
{
	mView = view;
	mProjection = proj;
	mFrustum = Frustum(view * proj);
}

int RenderPassList::GetPassInstanceId(int sceneId, int passId) {
	int instPassIdOffset = sceneId * (int)mPasses.size();
	return mInstancePassIds[instPassIdOffset + passId];
}
int RenderPassList::AddInstance(const Mesh* mesh, std::span<const Material*> materials, int dataSize) {
	auto sceneId = mScene->AllocateInstance(dataSize);
	int reqInstPassIdC = (sceneId + 1) * (int)mPasses.size();
	if (mInstancePassIds.size() < reqInstPassIdC) mInstancePassIds.resize(reqInstPassIdC);
	int instPassIdOffset = sceneId * (int)mPasses.size();
	for (int i = 0; i < (int)mPasses.size(); ++i) {
		auto pass = mPasses[i];
		InplaceVector<const Material*, 10> renMaterials;
		renMaterials.push_back_if_not_null(pass->mOverrideMaterial.get());
		for (auto* mat : materials) renMaterials.push_back(mat);
		mInstancePassIds[instPassIdOffset + i] =
			mPasses[i]->mRetainedRenderer->AppendInstance(mesh, renMaterials, sceneId);
	}
	return sceneId;
}
void RenderPassList::RemoveInstance(int sceneId) {
	int instPassIdOffset = sceneId * (int)mPasses.size();
	for (int i = 0; i < mPasses.size(); ++i) {
		auto instanceId = mInstancePassIds[instPassIdOffset + i];
		if (instanceId < 0) continue;
		mPasses[i]->mRetainedRenderer->RemoveInstance(instanceId);
	}
	mScene->RemoveInstance(sceneId);
}
