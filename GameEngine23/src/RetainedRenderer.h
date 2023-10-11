#pragma once

#include <vector>
#include <span>
#include <map>
#include "GraphicsBuffer.h"
#include "GraphicsUtility.h"
#include "GraphicsDeviceBase.h"
#include "Mesh.h"
#include "Material.h"
#include "MaterialEvaluator.h"

class RenderQueue;

class RetainedScene {
	struct Instance {
		RangeInt mData;
	};
	// All instances that currently exist
	SparseArray<Instance> mInstances;

	// Used to store per-instance data
	GraphicsBuffer<Vector4> mGPUBuffer;
	// Track which regions of the buffer are free
	SparseIndices mFreeGPUBuffer;
	GraphicsBufferDelta mGPUDelta;
public:
	RetainedScene();

	const GraphicsBuffer<Vector4>& GetGPUBuffer() { return mGPUBuffer; }
	std::span<const Vector4> GetInstanceData(int instanceId) const;

	// Allocate an instance in that batch
	int AllocateInstance(int instanceDataSize);

	// Update the user data of an instance
	// (MUST begin with World matrix)
	template<class T>
	bool UpdateInstanceData(int instanceId, const T& tdata) {
		auto& instance = mInstances[instanceId];
		assert(instance.mData.length * sizeof(Vector4) >= sizeof(T));
		return UpdateInstanceData(instanceId, std::span<const uint8_t>((const uint8_t*)&tdata, sizeof(T)));
	}
	bool UpdateInstanceData(int instanceId, std::span<const uint8_t> data);
	// Remove an instance from rendering
	void RemoveInstance(int instanceId);

	// Push updated instance data to GPU
	void SubmitGPUMemory(CommandBuffer& cmdBuffer);
};

// Collects rendered objects into batches and caches per-instance material parameters
// into a large buffer.
class RetainedRenderer
{
protected:
	struct Instance {
		int mBatchId;
		int mSceneId;
	};
	struct Batch
	{
		const Mesh* mMesh;
		const PipelineLayout* mPipelineLayout;
		std::vector<const BufferLayout*> mBufferLayout;
		std::vector<int> mRetainedCBs;
		std::vector<int> mInstances;
		int mRetainedResourcesId;
	};
	struct RetainedCBuffer
	{
		MaterialEvaluator mMaterialEval;
		int mFinalSize;
	};
public:
	struct DrawBatch
	{
		const Batch* mBatch;
		RangeInt mResourceRange;
		RangeInt mInstanceRange;
	};

protected:
	// All CBs that have been used
	SparseArray<RetainedCBuffer> mRetainedCBuffers;
	std::map<size_t, int> mCBBySourceHash;

	// All batches that currently exist
	SparseArray<Batch> mBatches;
	std::map<size_t, int> mBatchByHash;

	// All instances that currently exist
	SparseArray<Instance> mInstances;
	// Passes the typed instance buffer to a CommandList
	BufferLayoutPersistent mInstanceBufferLayout;
	// Stores per-instance data
	std::shared_ptr<RetainedScene> mGPUScene;

	// Used to generate MaterialEvaluators
	// (to extract named parameters from material stacks efficiently)
	MaterialCollector mMaterialCollector;

	// Used to create PSOs
	std::shared_ptr<GraphicsDeviceBase> mGraphics;

	// Determine the source materials required to fill out this CBs parameters
	// and cache a MaterialEvaluator to extract them
	int RequireRetainedCB(const ShaderBase::ConstantBuffer* constantBuffer, std::span<const Material*> materials);
	// Same as above, but for other bound resources (textures, etc.)
	int RequireRetainedResources(std::span<const ShaderBase::ResourceBinding* const> resources, std::span<const Material*> materials);

	// Find out which batch an instance should be added to
	int CalculateBatchId(const Mesh* mesh, std::span<const Material*> materials);
	// Allocate an instance in that batch
	int AllocateInstance(int batchId, int instanceDataSize);

public:
	RetainedRenderer(const std::shared_ptr<GraphicsDeviceBase>& graphics);

	void SetScene(const std::shared_ptr<RetainedScene>&scene) { mGPUScene = scene; }
	const std::shared_ptr<RetainedScene>& GetScene() { return mGPUScene; }

	const Instance& GetInstance(int instanceId) const { return mInstances[instanceId]; }

	// Add an instance to be drawn each frame
	int AppendInstance(const Mesh* mesh, std::span<const Material*> materials, int sceneId);
	// Remove an instance from rendering
	void RemoveInstance(int instanceId);

	// Generate a drawlist for rendering currently visible objects
	void SubmitToRenderQueue(CommandBuffer& cmdBuffer, RenderQueue& drawList, const Frustum& frustum);

};
