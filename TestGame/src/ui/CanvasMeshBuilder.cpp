/*

Element: (32 bytes)
- Position(v3), Size(v2), Scale(v2), Rot(v3/quat)
Vertex: (28 bytes)
- Anchor(v2), Offset(v2), UV(v2), Color(v4)

*/

#include "CanvasMeshBuilder.h"
#include <GraphicsDeviceBase.h>
#include <MaterialEvaluator.h>
#include <span>

TypedBufferView<Vector3> CanvasMeshBuffer::GetPositions(RangeInt range) const {
	return TypedBufferView<Vector3>(&mVertices.mElements[mPositionEl], range);
}
TypedBufferView<Vector2> CanvasMeshBuffer::GetTexCoords(RangeInt range) const {
	return TypedBufferView<Vector2>(&mVertices.mElements[mTexCoordEl], range);
}
TypedBufferView<ColorB4> CanvasMeshBuffer::GetColors(RangeInt range) const {
	return TypedBufferView<ColorB4>(&mVertices.mElements[mColorEl], range);
}
TypedBufferView<uint32_t> CanvasMeshBuffer::GetIndices(RangeInt range) const {
	return TypedBufferView<uint32_t>(&mIndices.mElements[0], range);
}
void CanvasMeshBuffer::MarkVerticesChanged(RangeInt range) {
	mVertices.mRevision++;
}
void CanvasMeshBuffer::MarkIndicesChanged(RangeInt range) {
	mIndices.mRevision++;
}


TypedBufferView<Vector3> CanvasVertices::GetPositions() { return mBuilder->GetPositions(mVertexRange); }
TypedBufferView<Vector2> CanvasVertices::GetTexCoords() { return mBuilder->GetTexCoords(mVertexRange); }
TypedBufferView<ColorB4> CanvasVertices::GetColors() { return mBuilder->GetColors(mVertexRange); }
TypedBufferView<uint32_t> CanvasVertices::GetIndices() { return mBuilder->GetIndices(mIndexRange); }
void CanvasVertices::MarkChanged() {
	mBuilder->MarkVerticesChanged(mVertexRange);
	mBuilder->MarkIndicesChanged(mIndexRange);
}

CanvasMeshBuilder::CanvasMeshBuilder() {
	mVertices = BufferLayoutPersistent((size_t)this, 0, BufferLayout::Usage::Vertex, 0);
	mPositionEl = mVertices.AppendElement(BufferLayout::Element("POSITION", BufferFormat::FORMAT_R32G32B32_FLOAT));
	mTexCoordEl = mVertices.AppendElement(BufferLayout::Element("TEXCOORD", BufferFormat::FORMAT_R16G16_UNORM));
	mColorEl = mVertices.AppendElement(BufferLayout::Element("COLOR", BufferFormat::FORMAT_R8G8B8A8_UNORM));
	mIndices = BufferLayoutPersistent((size_t)this + 1, 0, BufferLayout::Usage::Index, 0);
	mIndices.AppendElement(BufferLayout::Element("INDEX", BufferFormat::FORMAT_R32_UINT));
	mVertexBufferStrideCache = mVertices.CalculateBufferStride();
	mIndexBufferStrideCache = mIndices.CalculateBufferStride();
}

RangeInt CanvasMeshBuilder::RequireVertices(int vcount) {
	RangeInt range = mFreeVertices.Allocate(vcount);
	if (range.start >= 0) return range;
	mVertices.mCount -= mFreeVertices.Compact(mVertices.mCount);
	range = RangeInt(mVertices.mCount, vcount);
	if (range.end() * mVertexBufferStrideCache >= mVertices.mSize) {
		int newSize = mVertices.mSize + 1024 * mVertexBufferStrideCache;
		newSize = std::max(newSize, range.end() * mVertexBufferStrideCache);
		if (!mVertices.AllocResize(newSize)) return RangeInt(0, 0);
	}
	mVertices.mCount += vcount;
	return range;
}
RangeInt CanvasMeshBuilder::RequireIndices(int icount) {
	RangeInt range = mFreeIndices.Allocate(icount);
	if (range.start >= 0) return range;
	mIndices.mCount -= mFreeIndices.Compact(mIndices.mCount);
	range = RangeInt(mIndices.mCount, icount);
	if (range.end() * mIndexBufferStrideCache >= mIndices.mSize) {
		int newSize = mIndices.mSize + 1024 * mIndexBufferStrideCache;
		newSize = std::max(newSize, range.end() * mIndexBufferStrideCache);
		if (!mIndices.AllocResize(newSize)) return RangeInt(0, 0);
	}
	mIndices.mCount += icount;
	return range;
}

int CanvasMeshBuilder::Allocate(int vcount, int icount) {
	return mRanges.Add(CanvasRange{
		.mVertexRange = RequireVertices(vcount),
		.mIndexRange = RequireIndices(icount),
	});
}
void CanvasMeshBuilder::Deallocate(int id) {
	// TODO: Remove directly from buffer if at end
	mFreeVertices.Return(mRanges[id].mVertexRange);
	mFreeIndices.Return(mRanges[id].mIndexRange);
	mRanges.Return(id);
}
CanvasVertices CanvasMeshBuilder::MapVertices(int id) {
	auto& range = mRanges[id];
	return CanvasVertices(this, range);
}
