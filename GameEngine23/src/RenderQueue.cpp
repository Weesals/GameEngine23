#include "RenderQueue.h"

RenderQueue::RenderQueue() {
	mInstanceBufferLayout = BufferLayout(-1, 0, BufferLayout::Usage::Instance, -1);
	mInstanceBufferLayout.mElements.push_back(
		BufferLayout::Element("INSTANCE", BufferFormat::FORMAT_R32_UINT, sizeof(int), sizeof(int), nullptr)
	);
}
void RenderQueue::Clear()
{
	// Clear previous data
	mFrameData.clear();
	mResourceData.clear();
	mInstancesBuffer.clear();
	mDraws.clear();
	mFrameData.reserve(2048);
}
void RenderQueue::AppendMesh(const PipelineLayout* pipeline, const BufferLayout** buffers,
	RangeInt resources, RangeInt instances)
{
	mDraws.push_back(DrawBatch{
		.mPipelineLayout = pipeline,
		.mBufferLayouts = buffers,
		.mResourceRange = resources,
		.mInstanceRange = instances,
	});
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
			std::span<void*>(mResourceData.begin() + draw.mResourceRange.start, draw.mResourceRange.length),
			config,
			(int)draw.mInstanceRange.length
		);
	}
}
