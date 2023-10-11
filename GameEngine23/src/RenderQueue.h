#pragma once

#include <vector>
#include <span>

#include "GraphicsUtility.h"
#include "GraphicsDeviceBase.h"
#include "RenderTarget2D.h"
#include "RetainedRenderer.h"

class RenderQueue
{
public:
	struct DrawBatch
	{
		const char* mName;
		const PipelineLayout* mPipelineLayout;
		const BufferLayout** mBufferLayouts;
		const void** mResources;
		RangeInt mInstanceRange;
	};

	// Data which is erased each frame
	std::vector<uint8_t> mFrameData;
	std::vector<uint32_t> mInstancesBuffer;
	std::vector<DrawBatch> mDraws;

	// Passes the typed instance buffer to a CommandList
	BufferLayoutPersistent mInstanceBufferLayout;

	RenderQueue();

	void Clear();
	std::span<const void*> RequireMaterialResources(CommandBuffer& cmdBuffer,
		const PipelineLayout* pipeline, const Material* material);
	void AppendMesh(const char* name,
		const PipelineLayout* pipeline, const BufferLayout** buffers,
		const void** resources, RangeInt instances);
	void AppendMesh(const char* name,
		CommandBuffer& cmdBuffer, const Mesh* mesh, const Material* material);

	void Flush(CommandBuffer& cmdBuffer);

};

struct RenderPass
{
	std::string mName;
	RenderQueue mRenderQueue;
	Matrix mView;
	Matrix mProjection;
	Frustum mFrustum;
	std::shared_ptr<RenderTarget2D> mRenderTarget;
	std::shared_ptr<Material> mOverrideMaterial;
	std::shared_ptr<RetainedRenderer> mRetainedRenderer;
	RenderPass(std::string_view name, const std::shared_ptr<GraphicsDeviceBase>& graphics);
	void UpdateViewProj(const Matrix& view, const Matrix& proj);
	const IdentifierWithName& GetRenderPassOverride() const {
		return mOverrideMaterial != nullptr ? mOverrideMaterial->GetRenderPassOverride() : IdentifierWithName::None;
	}
};
class RenderPassList {
	std::shared_ptr<RetainedScene> mScene;
	SparseArray<int> mPassIds;
	std::vector<RangeInt> mPassIdsBySceneId;
public:
	std::vector<RenderPass*> mPasses;
	RenderPassList(const std::shared_ptr<RetainedScene>& scene)
		: mScene(scene) { }
	std::vector<RenderPass*>::iterator begin() { return mPasses.begin(); }
	std::vector<RenderPass*>::iterator end() { return mPasses.end(); }
	int AddInstance(const Mesh* mesh, const Material* material, int dataSize);
	template<class T>
	bool UpdateInstanceData(int sceneId, const T& tdata) {
		return mScene->UpdateInstanceData(sceneId, tdata);
	}
	void RemoveInstance(int sceneId);
};

class MeshDraw
{
protected:
	std::vector<const BufferLayout*> mBufferLayout;
	std::vector<const void*> mResources;
	struct RenderPassCache {
		Identifier mRenderPass;
		const PipelineLayout* mPipeline;
	};
	std::vector<RenderPassCache> mPassCache;
public:
	const Mesh* mMesh;
	const Material* mMaterial;
	MeshDraw();
	MeshDraw(Mesh* mesh, Material* material);
	~MeshDraw();
	virtual void InvalidateMesh();
	const RenderPassCache* GetPassCache(CommandBuffer& cmdBuffer, const IdentifierWithName& renderPass);
	void Draw(CommandBuffer& cmdBuffer, const DrawConfig& config);
};

class MeshDrawInstanced : public MeshDraw
{
protected:
	BufferLayoutPersistent mInstanceBuffer;
public:
	MeshDrawInstanced();
	MeshDrawInstanced(Mesh* mesh, Material* material);
	~MeshDrawInstanced();
	virtual void InvalidateMesh() override;
	int GetInstanceCount();
	int AddInstanceElement(const char* name = "INSTANCE", BufferFormat fmt = FORMAT_R32_UINT, int stride = sizeof(uint32_t));
	void SetInstanceData(void* data, int count, int elementId = 0, bool markDirty = true);
	void Draw(CommandBuffer& cmdBuffer, const DrawConfig& config);
	void Draw(CommandBuffer& cmdBuffer, RenderQueue* queue, const DrawConfig& config);
	void Draw(CommandBuffer& cmdBuffer, RenderPass& pass, const DrawConfig& config);
};

