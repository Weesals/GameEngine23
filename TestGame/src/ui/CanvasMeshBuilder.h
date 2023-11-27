#pragma once

#include <Mesh.h>
#include <Containers.h>
#include <algorithm>

class CanvasMeshBuffer {
protected:
	int mAllocatedVertices;
	BufferLayoutPersistent mVertices;
	SparseIndices mFreeVertices;
	int mVertexBufferStrideCache;
	int mIndexBufferStrideCache;

	int mPositionEl;
	int mTexCoordEl;
	int mColorEl;

	int mAllocatedIndices;
	BufferLayoutPersistent mIndices;
	SparseIndices mFreeIndices;
public:
	struct CanvasRange {
		RangeInt mVertexRange;
		RangeInt mIndexRange;
	};

	TypedBufferView<Vector3> GetPositions(RangeInt range) const;
	TypedBufferView<Vector2> GetTexCoords(RangeInt range) const;
	TypedBufferView<ColorB4> GetColors(RangeInt range) const;
	TypedBufferView<uint32_t> GetIndices(RangeInt range) const;
	void MarkVerticesChanged(RangeInt range);
	void MarkIndicesChanged(RangeInt range);

	const BufferLayoutPersistent* GetVertices() const { return &mVertices; }
};

struct CanvasVertices : public CanvasMeshBuffer::CanvasRange {
	CanvasMeshBuffer* mBuilder;
	CanvasVertices(CanvasMeshBuffer* builder, CanvasMeshBuffer::CanvasRange range)
		: CanvasMeshBuffer::CanvasRange(range), mBuilder(builder) { }
	int GetVertexCount() const { return mVertexRange.length; }
	int GetIndexCount() const { return mIndexRange.length; }
	TypedBufferView<Vector3> GetPositions();
	TypedBufferView<Vector2> GetTexCoords();
	TypedBufferView<ColorB4> GetColors();
	TypedBufferView<uint32_t> GetIndices();
	void MarkChanged();
};

class CanvasMeshBuilder : public CanvasMeshBuffer {
protected:
	RangeInt RequireVertices(int vcount);
	RangeInt RequireIndices(int icount);

	SparseArray<CanvasRange> mRanges;

public:
	CanvasMeshBuilder();
	int Allocate(int vcount, int icount);
	void Deallocate(int id);

	CanvasVertices MapVertices(int id);

};

