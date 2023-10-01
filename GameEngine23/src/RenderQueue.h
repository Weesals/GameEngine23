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
	void AppendMesh(const PipelineLayout* pipeline, const BufferLayout** buffers,
		RangeInt resources, RangeInt instances);

	void Flush(CommandBuffer& cmdBuffer);

};

