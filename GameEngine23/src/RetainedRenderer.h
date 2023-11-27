#pragma once

#include <vector>
#include <span>
#include <map>
#include <set>
#include "GraphicsBuffer.h"
#include "GraphicsUtility.h"
#include "GraphicsDeviceBase.h"
#include "Mesh.h"
#include "Material.h"
#include "MaterialEvaluator.h"
#include "RenderQueue.h"

struct RetainedMaterialSet {
	// Set of materials not including render pass override
	std::vector<const Material*> mMaterials;
	int mReferenceCount = 0;
	RetainedMaterialSet() = default;
	RetainedMaterialSet(RetainedMaterialSet&& other) = default;
	RetainedMaterialSet& operator =(RetainedMaterialSet&& other) = default;
	RetainedMaterialSet(std::span<const Material*> materials)
		: mMaterials(materials.begin(), materials.end()) { }
};
class RetainedMaterialCollection {
	SparseArray<RetainedMaterialSet> mMaterialSets;
	std::unordered_map<size_t, int> mSetIDByHash;
public:
	std::span<const Material*> GetMaterials(int id) const { return +mMaterialSets[id].mMaterials; }
	void AddRef(int id, int count = 1) { mMaterialSets[id].mReferenceCount += count; }
	void DeRef(int id, int count = 1) { if ((mMaterialSets[id].mReferenceCount -= count) == 0) Remove(id); }
	void Remove(int id) {
		size_t hash = ArrayHash(GetMaterials(id));
		mMaterialSets.Return(id);
		mSetIDByHash.erase(mSetIDByHash.find(hash));
	}
	int Require(std::span<const Material*> materials) {
		size_t hash = ArrayHash(materials);
		auto insert = mSetIDByHash.find(hash);
		if (insert == mSetIDByHash.end()) {
			auto id = mMaterialSets.Add(RetainedMaterialSet(materials));
			insert = mSetIDByHash.insert(std::make_pair(hash, id)).first;
		}
		return insert->second;
	}
};

class ResolvedMaterialSets {
	RetainedMaterialCollection* mMatCollection;
public:
	struct ResolvedMaterialSet {
		MaterialEvaluator mEvaluator;
		size_t mSourceHash;
	};
	ResolvedMaterialSets(RetainedMaterialCollection* matCollection)
		: mMatCollection(matCollection) { }
protected:
	std::unordered_map<size_t, int> mResolvedByHash;
	std::vector<ResolvedMaterialSet> mResolved;
	MaterialCollector mMaterialCollector;
	size_t GenerateHash(CommandBuffer& cmdBuffer, size_t valueHash, int matSetId);
public:
	int RequireResolved(CommandBuffer& cmdBuffer, std::span<const ShaderBase::UniformValue> values, int matSetId);
	const ResolvedMaterialSet& GetResolved(int id);
};

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

	RetainedMaterialCollection mMatCollection;
public:
	RetainedScene();

	ResolvedMaterialSets mResolvedMats;

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

	RetainedMaterialCollection& GetMatCollection() { return mMatCollection; }
};

// Collects rendered objects into batches and caches per-instance material parameters
// into a large buffer.
class RetainedRenderer {
public:
	struct StateKey {
		const Mesh* mMesh;
		int mMaterialSetId;
		StateKey(const Mesh* mesh, int matSetId)
			: mMesh(mesh), mMaterialSetId(matSetId) { }
		bool operator == (const StateKey& o) const {
			return mMesh == o.mMesh && mMaterialSetId == o.mMaterialSetId;
		}
		bool operator != (const StateKey& o) const { return !(*this == o); }
		bool operator <(const StateKey& o) const {
			return mMesh < o.mMesh || (mMesh == o.mMesh && mMaterialSetId < o.mMaterialSetId);
		}
	};
	struct Batch : public StateKey {
		using StateKey::StateKey;
		std::vector<int> mInstances;
		std::vector<const BufferLayout*> mBufferLayout;
		void OverwriteBufferLayout(std::span<const BufferLayout*> layout) {
			mBufferLayout.assign(layout.begin(), layout.end());
		}
	};
	struct ResolvedPipeline {
		const PipelineLayout* mPipeline;
		std::vector<int> mResolvedCBs;
		int mResolvedResources;
	};

	// All batches that currently exist
	std::vector<Batch> mBatches;
	std::unordered_map<int, StateKey> mInstanceBatches;
	// Stores cached PSOs per mesh/matset/graphics
	std::unordered_map<size_t, ResolvedPipeline> mPipelineCache;

	// Passes the typed instance buffer to a CommandList
	BufferLayoutPersistent mInstanceBufferLayout;
	// Material to inject GPU Scene buffer as 'instanceData'
	Material mInstanceMaterial;
	// Stores per-instance data
	std::shared_ptr<RetainedScene> mGPUScene;

	RetainedRenderer();

	void SetScene(const std::shared_ptr<RetainedScene>& scene);
	const std::shared_ptr<RetainedScene>& GetScene() const;

	// Add an instance to be drawn each frame
	int AppendInstance(const Mesh* mesh, std::span<const Material*> materials, int sceneId);
	// Set visibility
	void SetVisible(int sceneId, bool visible);
	// Remove an instance from rendering
	void RemoveInstance(int instanceId);

	// Generate a drawlist for rendering currently visible objects
	void SubmitToRenderQueue(CommandBuffer& cmdBuffer, RenderQueue& drawList, const Frustum& frustum);
};

class RenderPass
{
public:
	std::string mName;
	RenderQueue mRenderQueue;
	Matrix mView;
	Matrix mProjection;
	Frustum mFrustum;
	std::shared_ptr<RenderTarget2D> mRenderTarget;
	std::shared_ptr<Material> mOverrideMaterial;
	std::shared_ptr<RetainedRenderer> mRetainedRenderer;
	RenderPass(std::string_view name);
	void UpdateViewProj(const Matrix& view, const Matrix& proj);
	const IdentifierWithName& GetRenderPassOverride() const {
		return mOverrideMaterial != nullptr ? mOverrideMaterial->GetRenderPassOverride() : IdentifierWithName::None;
	}
};
class RenderPassList {
	std::shared_ptr<RetainedScene> mScene;
	std::vector<int> mInstancePassIds;
public:
	std::vector<RenderPass*> mPasses;
	RenderPassList(const std::shared_ptr<RetainedScene>& scene)
		: mScene(scene) { }
	std::vector<RenderPass*>::iterator begin() { return mPasses.begin(); }
	std::vector<RenderPass*>::iterator end() { return mPasses.end(); }
	int GetPassInstanceId(int sceneId, int passId);
	int AddInstance(const Mesh* mesh, std::span<const Material*> materials, int dataSize);
	template<class T>
	bool UpdateInstanceData(int sceneId, const T& tdata) {
		return mScene->UpdateInstanceData(sceneId, tdata);
	}
	void RemoveInstance(int sceneId);
};
