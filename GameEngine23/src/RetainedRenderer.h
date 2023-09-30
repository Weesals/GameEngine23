#pragma once

#include <vector>
#include <span>
#include "GraphicsBuffer.h"
#include "GraphicsUtility.h"
#include "GraphicsDeviceBase.h"
#include "Mesh.h"
#include "Material.h"
#include "MaterialEvaluator.h"

// Collects rendered objects into batches and caches per-instance material parameters
// into a large buffer.
class RetainedRenderer
{
protected:
	struct Instance
	{
		int mBatchId;
		RangeInt mData;
	};
	struct Batch
	{
		const Mesh* mMesh;
		const PipelineLayout* mPipelineLayout;
		std::vector<BufferLayout*> mBufferLayout;
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
	struct DrawList
	{
		std::vector<uint8_t> mCBData;
		std::vector<void*> mResourceData;
		std::vector<uint32_t> mInstancesBuffer;
		std::vector<DrawBatch> mDraws;
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
	BufferLayout mInstanceBufferLayout;

	// Used to store per-instance data
	GraphicsBuffer<Vector4> mGPUBuffer;
	// Track which regions of the buffer are free
	SparseIndices mFreeGPUBuffer;

	// Used to generate MaterialEvaluators
	// (to extract named parameters from material stacks efficiently)
	MaterialCollector mMaterialCollector;

	// Used to create PSOs
	std::shared_ptr<GraphicsDeviceBase> mGraphics;

	// Determine the source materials required to fill out this CBs parameters
	// and cache a MaterialEvaluator to extract them
	int RequireRetainedCB(const ShaderBase::ConstantBuffer* constantBuffer, const Material* material);
	// Same as above, but for other bound resources (textures, etc.)
	int RequireRetainedResources(std::span<const ShaderBase::ResourceBinding* const> resources, const Material* material);

	// Find out which batch an instance should be added to
	int CalculateBatchId(const Mesh* mesh, const Material* material);
	// Allocate an instance in that batch
	int AllocateInstance(int batchId, int instanceDataSize);

public:
	RetainedRenderer(const std::shared_ptr<GraphicsDeviceBase>& graphics);

	// Add an instance to be drawn each frame
	int AppendInstance(const Mesh* mesh, const Material* material, int instanceDataSize);
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

	// Generate a drawlist for rendering currently visible objects
	void CreateDrawList(DrawList& drawList, Matrix vp);
	// Render the generated drawlist
	void RenderDrawList(CommandBuffer& cmdBuffer, DrawList& drawList);

};
