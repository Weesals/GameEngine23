#include "RetainedRenderer.h"

#include "RenderQueue.h"

RetainedScene::RetainedScene()
	: mGPUBuffer(1024)
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


RetainedRenderer::RetainedRenderer(const std::shared_ptr<GraphicsDeviceBase>& graphics)
	: mGraphics(graphics)
{
	mInstanceBufferLayout = BufferLayoutPersistent(-1, 0, BufferLayout::Usage::Instance, -1, 1);
	mInstanceBufferLayout.AppendElement(
		BufferLayout::Element("INSTANCE", BufferFormat::FORMAT_R32_UINT, sizeof(int), sizeof(int), nullptr)
	);
}


int RetainedRenderer::RequireRetainedCB(const ShaderBase::ConstantBuffer* constantBuffer, std::span<const Material*> materials) {
	mMaterialCollector.Clear();
	auto& values = constantBuffer->mValues;
	// Find the source of each parameter, generate hash
	MaterialCollectorContext context(materials, mMaterialCollector);
	for (int v = 0; v < values.size(); ++v)
		context.GetUniformSource(values[v].mNameId, context);
	auto hash = GenericHash({ mMaterialCollector.GenerateSourceHash(), constantBuffer->GenerateHash() });

	// Check if we have a setup for this cb set and source set
	auto cb = mCBBySourceHash.find(hash);
	if (cb != mCBBySourceHash.end()) return cb->second;

	// Force the correct output layout
	mMaterialCollector.FinalizeAndClearOutputOffsets();
	for (int v = 0; v < values.size(); ++v)
		mMaterialCollector.SetItemOutputOffset(values[v].mNameId, values[v].mOffset);
	mMaterialCollector.RepairOutputOffsets();

	// Add the resource evaluator
	RetainedCBuffer cbuffer;
	mMaterialCollector.BuildEvaluator(cbuffer.mMaterialEval);
	cbuffer.mFinalSize = constantBuffer->mSize;
	auto cbid = mRetainedCBuffers.Add(std::move(cbuffer));
	mCBBySourceHash.emplace(std::make_pair(hash, cbid));
	return cbid;
}
int RetainedRenderer::RequireRetainedResources(std::span<const ShaderBase::ResourceBinding* const> resources, std::span<const Material*> materials) {
	mMaterialCollector.Clear();
	size_t hash = 0;
	// Find the source of each resource, generate hash
	MaterialCollectorContext context(materials, mMaterialCollector);
	for (int v = 0; v < resources.size(); ++v) {
		const auto* resource = resources[v];
		context.GetUniformSource(resource->mNameId, context);
		hash = AppendHash(((int)resource->mNameId << 16) | v, hash);
	}
	hash = AppendHash(mMaterialCollector.GenerateSourceHash(), hash);

	// Check if we have a setup for this resource set and source set
	auto cb = mCBBySourceHash.find(hash);
	if (cb != mCBBySourceHash.end()) return cb->second;

	// Force the correct output layout
	mMaterialCollector.FinalizeAndClearOutputOffsets();
	for (int v = 0; v < resources.size(); ++v)
		mMaterialCollector.SetItemOutputOffset(resources[v]->mNameId, v * sizeof(std::shared_ptr<Texture>));
	mMaterialCollector.RepairOutputOffsets();

	// Add the resource evaluator
	RetainedCBuffer cbuffer;
	mMaterialCollector.BuildEvaluator(cbuffer.mMaterialEval);
	cbuffer.mFinalSize = cbuffer.mMaterialEval.mDataSize;
	auto cbid = mRetainedCBuffers.Add(std::move(cbuffer));
	cb = mCBBySourceHash.emplace(std::make_pair(hash, cbid)).first;
	return cbid;
}
int RetainedRenderer::CalculateBatchId(const Mesh* mesh, std::span<const Material*> materials) {
	// Need to calculate a hash for required batch state
	size_t batchHash = 0;

	// Find vertex bindings
	std::vector<const BufferLayout*> bindings;
	mesh->CreateMeshLayout(bindings);
	bindings.push_back(&mInstanceBufferLayout);
	auto pso = mGraphics->RequirePipeline(+bindings, materials);
	// ###############################
	//				TODO
	// ###############################
	batchHash = AppendHash(pso, batchHash);
	batchHash = AppendHash(mesh, batchHash);

	// Find relevant constant buffer sources
	std::vector<int> retainedCBs(pso->mConstantBuffers.size());
	for (int i = 0; i < retainedCBs.size(); ++i) {
		auto& cb = pso->mConstantBuffers[i];
		auto cbid = cb != nullptr ? RequireRetainedCB(cb, materials) : -1;
		batchHash = AppendHash(cbid, batchHash);
		retainedCBs[i] = cbid;
	}

	// Find relevant resource sources
	int retainedResId = RequireRetainedResources(pso->mResources, materials);
	AppendHash(retainedResId, batchHash);

	// Create the batch if it doesnt exist
	auto batchKV = mBatchByHash.find(batchHash);
	if (batchKV == mBatchByHash.end()) {
		auto batchId = mBatches.Add({
			.mMesh = mesh,
			.mPipelineLayout = pso,
			.mBufferLayout = std::move(bindings),
			.mRetainedCBs = std::move(retainedCBs),
			.mRetainedResourcesId = retainedResId,
		});
		batchKV = mBatchByHash.insert(std::make_pair(batchHash, batchId)).first;
	}
	return batchKV->second;
}

// Add a mesh to the persistent scene
int RetainedRenderer::AppendInstance(const Mesh* mesh, std::span<const Material*> materials, int sceneId) {
	int batchId = CalculateBatchId(mesh, materials);
	auto instanceId = mInstances.Add(Instance{ .mBatchId = batchId, .mSceneId = sceneId, });
	mBatches[batchId].mInstances.push_back(instanceId);
	return instanceId;
}
// Remove a mesh instance
void RetainedRenderer::RemoveInstance(int instanceId) {
	auto& instance = mInstances[instanceId];
	auto& batch = mBatches[instance.mBatchId];
	batch.mInstances.erase(std::remove(batch.mInstances.begin(), batch.mInstances.end(), instanceId), batch.mInstances.end());
	mInstances.Return(instanceId);
}

void RetainedRenderer::SubmitToRenderQueue(CommandBuffer& cmdBuffer, RenderQueue& drawList, const Frustum& frustum) {
	auto& gpuBuffer = mGPUScene->GetGPUBuffer();
	// Generate draw commands from batches
	Identifier gInstanceDataId = "instanceData";
	for (auto& batch : mBatches) {
		if (batch.mInstances.empty()) continue;
		auto instBegin = drawList.mInstancesBuffer.size();
		auto resources = cmdBuffer.RequireFrameData<const void*>(batch.mPipelineLayout->GetResourceCount());
		int r = 0;

		// Calculate visible instances
		auto& bbox = batch.mMesh->GetBoundingBox();
		auto bboxCtr = bbox.Centre();
		auto bboxExt = bbox.Extents();
		for (auto& instance : batch.mInstances) {
			auto data = mGPUScene->GetInstanceData(mInstances[instance].mSceneId);
			auto& matrix = *(Matrix*)(data.data());
			if (!frustum.GetIsVisible(Vector3::Transform(bboxCtr, matrix), bboxExt)) continue;
			drawList.mInstancesBuffer.push_back(mInstances[instance].mSceneId);
		}
		// If no instances were created, quit
		if (drawList.mInstancesBuffer.size() == instBegin) continue;

		// Get constant buffer data for this batch
		for (auto& cbid : batch.mRetainedCBs) {
			void* resource = nullptr;
			if (cbid >= 0) {
				auto& cb = mRetainedCBuffers[cbid];
				auto cbData = cb.mMaterialEval.EvaluateAppend(drawList.mFrameData, cb.mFinalSize);
				resource = cmdBuffer.RequireConstantBuffer(cbData);
				//resource = cbData.data();
			}
			resources[r++] = resource;
		}
		// Get other resource data for this batch
		{
			uint8_t tmpData[1024];
			auto& resCB = mRetainedCBuffers[batch.mRetainedResourcesId];
			resCB.mMaterialEval.Evaluate(std::span<uint8_t>(tmpData, tmpData + 1024));
			auto& pipResources = batch.mPipelineLayout->mResources;
			for (auto i = 0; i < pipResources.size(); ++i) {
				resources[r++] =
					pipResources[i]->mNameId == gInstanceDataId ? &gpuBuffer :
					((const std::shared_ptr<void>*)tmpData)[i].get()
				;
			}
		}

		// Need to force this to use queues instance buffer
		// (so that the range can be adjusted by RenderQueue)
		// TODO: A more generic approach
		batch.mBufferLayout.back() = &drawList.mInstanceBufferLayout;
		// Add the draw command
		drawList.AppendMesh(
			batch.mMesh->GetName().c_str(),
			batch.mPipelineLayout,
			batch.mBufferLayout.data(),
			resources.data(),
			RangeInt::FromBeginEnd((int)instBegin, (int)drawList.mInstancesBuffer.size())
		);
	}
}
