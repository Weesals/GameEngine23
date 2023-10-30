#pragma once

#include <vector>
#include <span>

#include "GraphicsUtility.h"
#include "GraphicsDeviceBase.h"
#include "RenderTarget2D.h"

class RenderPass;
class RetainedRenderer;

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
	std::span<const BufferLayout*> ImmortalizeBufferLayout(CommandBuffer& cmdBuffer, std::span<const BufferLayout*> bindings);
	void AppendMesh(const char* name,
		const PipelineLayout* pipeline, const BufferLayout** buffers,
		const void** resources, RangeInt instances);
	void AppendMesh(const char* name,
		CommandBuffer& cmdBuffer, const Mesh* mesh, const Material* material);

	void Render(CommandBuffer& cmdBuffer);

};

class MeshDraw {
	friend class RetainedRenderer;
protected:
	const Mesh* mMesh;
	std::vector<const Material*> mMaterials;
	std::vector<const BufferLayout*> mBufferLayout;
	struct RenderPassCache {
		Identifier mRenderPass;
		const PipelineLayout* mPipeline;
	};
	std::vector<RenderPassCache> mPassCache;
public:
	MeshDraw();
	MeshDraw(const Mesh* mesh, const Material* material);
	MeshDraw(const Mesh* mesh, std::span<const Material*> materials);
	virtual ~MeshDraw();
	const Mesh* GetMesh() const { return mMesh; }
	virtual void InvalidateMesh();
	const RenderPassCache* GetPassCache(CommandBuffer& cmdBuffer, const IdentifierWithName& renderPass);
	void Draw(CommandBuffer& cmdBuffer, const DrawConfig& config);
};

class MeshDrawInstanced : public MeshDraw {
protected:
	BufferLayoutPersistent mInstanceBuffer;
public:
	MeshDrawInstanced();
	MeshDrawInstanced(const Mesh* mesh, const Material* material);
	MeshDrawInstanced(const Mesh* mesh, std::span<const Material*> materials);
	~MeshDrawInstanced();
	virtual void InvalidateMesh() override;
	int GetInstanceCount();
	int AddInstanceElement(const char* name = "INSTANCE", BufferFormat fmt = FORMAT_R32_UINT, int stride = sizeof(uint32_t));
	void SetInstanceData(void* data, int count, int elementId = 0, bool markDirty = true);
	void Draw(CommandBuffer& cmdBuffer, const DrawConfig& config);
	void Draw(CommandBuffer& cmdBuffer, RenderQueue* queue, const DrawConfig& config);
	void Draw(CommandBuffer& cmdBuffer, RenderPass& pass, const DrawConfig& config);
};

