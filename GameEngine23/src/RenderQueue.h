#pragma once

#include <vector>
#include <span>

#include "GraphicsUtility.h"
#include "GraphicsDeviceBase.h"

class RenderQueue
{
public:
	struct DrawBatch
	{
		const PipelineLayout* mPipelineLayout;
		const BufferLayout** mBufferLayouts;
		RangeInt mResourceRange;
		RangeInt mInstanceRange;
	};

	// Data which is erased each frame
	std::vector<uint8_t> mFrameData;
	std::vector<void*> mResourceData;
	std::vector<uint32_t> mInstancesBuffer;
	std::vector<DrawBatch> mDraws;

	// Passes the typed instance buffer to a CommandList
	BufferLayout mInstanceBufferLayout;

	RenderQueue();

	void Clear();
	RangeInt RequireMaterialResources(CommandBuffer& cmdBuffer,
		const PipelineLayout* pipeline, const Material* material);
	void AppendMesh(const PipelineLayout* pipeline, const BufferLayout** buffers,
		RangeInt resources, RangeInt instances);
	void AppendMesh(CommandBuffer& cmdBuffer, const Mesh* mesh, const Material* material);

	void Flush(CommandBuffer& cmdBuffer);

};

class MeshDraw
{
protected:
	std::vector<const BufferLayout*> mBufferLayout;
	const PipelineLayout* mPipeline;
	std::vector<void*> mResources;
public:
	const Mesh* mMesh;
	const Material* mMaterial;
	MeshDraw();
	MeshDraw(Mesh* mesh, Material* material);
	~MeshDraw();
	void InvalidateMesh();
	void Draw(CommandBuffer& cmdBuffer, const DrawConfig& config);
};

class MeshDrawInstanced : public MeshDraw
{
protected:
	BufferLayout mInstanceBuffer;
public:
	MeshDrawInstanced();
	MeshDrawInstanced(Mesh* mesh, Material* material);
	~MeshDrawInstanced();
	void InvalidateMesh();
	int AddInstanceElement(const std::string_view& name = "INSTANCE", BufferFormat fmt = FORMAT_R32_UINT, int stride = sizeof(uint32_t));
	void SetInstanceData(void* data, int count, int elementId = 0, bool markDirty = true);
	void Draw(CommandBuffer& cmdBuffer, const DrawConfig& config);
	void Draw(CommandBuffer& cmdBuffer, RenderQueue* queue, const DrawConfig& config);
};

struct RenderPass
{
	RenderQueue* mRenderQueue;
	Matrix mView;
	Matrix mProjection;
	Frustum mFrustum;
	RenderPass(RenderQueue* queue, Matrix view, Matrix proj);
};
struct RenderPassList
{
	std::span<RenderPass> mPasses;
	RenderPassList(std::span<RenderPass> passes) : mPasses(passes) { }
	std::span<RenderPass>::iterator begin() { return mPasses.begin(); }
	std::span<RenderPass>::iterator end() { return mPasses.end(); }
};
