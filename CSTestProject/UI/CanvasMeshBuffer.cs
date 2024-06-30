using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.Utility;

namespace Weesals.UI {

    public ref struct CanvasVertices {
        private CanvasMeshBuffer builder;
        private ref CanvasMeshBuffer.CanvasRange range;
        public int VertexOffset => range.mVertexRange.Start;
        public CanvasVertices(CanvasMeshBuffer _builder, ref CanvasMeshBuffer.CanvasRange _range) {
            range = ref _range;
            builder = _builder;
        }
        public ref RectF VertexBounds => ref range.VertBoundsCache;
        public int GetVertexCount() { return range.mVertexRange.Length; }
        public int GetIndexCount() { return range.mIndexRange.Length; }
        public TypedBufferView<Vector2> GetPositions2D() { return builder.GetPositions<Vector2>(range.mVertexRange); }
        public TypedBufferView<Vector3> GetPositions() { return builder.GetPositions(range.mVertexRange); }
        public TypedBufferView<Vector2> GetTexCoords() { return builder.GetTexCoords(range.mVertexRange); }
        public TypedBufferView<SColor> GetColors() { return builder.GetColors(range.mVertexRange); }
        public TypedBufferView<uint> GetIndices() { return builder.GetIndices(range.mIndexRange); }
        public void MarkVerticesChanged() {
            builder.MarkVerticesChanged(range.mVertexRange);
            VertexBounds.Width = -1f;
        }
        public void MarkIndicesChanged() {
            builder.MarkIndicesChanged(range.mIndexRange);
        }
        public void MarkChanged() {
            MarkIndicesChanged();
            MarkVerticesChanged();
        }
    };

    public class CanvasMeshBuffer : IDisposable {
        protected int mAllocatedVertices;
        protected BufferLayoutPersistent mVertices;
        protected SparseIndices mFreeVertices;

        protected int mPositionEl;
        protected int mTexCoordEl;
        protected int mColorEl;

        protected int mAllocatedIndices;
        protected BufferLayoutPersistent mIndices;
        protected SparseIndices mFreeIndices;

        public struct CanvasRange {
            public RangeInt mVertexRange;
            public RangeInt mIndexRange;
            public RectF VertBoundsCache;
        };
        protected SparseArray<CanvasRange> Ranges = new();

        public int VertexRevision => mVertices.BufferLayout.revision;

        public CanvasMeshBuffer() {
            mVertices = new BufferLayoutPersistent(BufferLayoutPersistent.Usages.Vertex);
            mPositionEl = mVertices.AppendElement(new CSBufferElement("POSITION", BufferFormat.FORMAT_R32G32_FLOAT));
            mTexCoordEl = mVertices.AppendElement(new CSBufferElement("TEXCOORD", BufferFormat.FORMAT_R16G16_UNORM));
            mColorEl = mVertices.AppendElement(new CSBufferElement("COLOR", BufferFormat.FORMAT_R8G8B8A8_SNORM));
            mIndices = new BufferLayoutPersistent(BufferLayoutPersistent.Usages.Index);
            mIndices.AppendElement(new CSBufferElement("INDEX", BufferFormat.FORMAT_R32_UINT));
            mFreeVertices = new();
            mFreeIndices = new();
        }
        public void Dispose() {
            mIndices.Dispose();
            mVertices.Dispose();
        }

        public TypedBufferView<T> GetPositions<T>() where T : unmanaged {
            return new TypedBufferView<T>(mVertices.Elements[mPositionEl], new RangeInt(0, mVertices.Count));
        }
        public TypedBufferView<Vector3> GetPositions() {
            return new TypedBufferView<Vector3>(mVertices.Elements[mPositionEl], new RangeInt(0, mVertices.Count));
        }
        public TypedBufferView<T> GetPositions<T>(RangeInt range) where T : unmanaged {
            return new TypedBufferView<T>(mVertices.Elements[mPositionEl], range);
        }
        public TypedBufferView<Vector3> GetPositions(RangeInt range) {
            return new TypedBufferView<Vector3>(mVertices.Elements[mPositionEl], range);
        }
        public TypedBufferView<Vector2> GetTexCoords(RangeInt range) {
            return new TypedBufferView<Vector2>(mVertices.Elements[mTexCoordEl], range);
        }
        public TypedBufferView<SColor> GetColors(RangeInt range) {
            return new TypedBufferView<SColor>(mVertices.Elements[mColorEl], range);
        }
        public TypedBufferView<uint> GetIndices(RangeInt range) {
            return new TypedBufferView<uint>(mIndices.Elements[0], range);
        }
        public void MarkVerticesChanged(RangeInt range) {
            mVertices.BufferLayout.revision++;
        }
        public void MarkIndicesChanged(RangeInt range) {
            mIndices.BufferLayout.revision++;
        }

        public ref BufferLayoutPersistent GetVertices() { return ref mVertices; }

        public RangeInt RequireVertices(int vcount) {
            return RequireSlice(ref mVertices, mFreeVertices, vcount);
        }
        public RangeInt RequireIndices(int icount) {
            return RequireSlice(ref mIndices, mFreeIndices, icount);
        }
        private RangeInt RequireSlice(ref BufferLayoutPersistent items, SparseIndices unused, int count) {
            var start = unused.Take(count);
            if (start >= 0) return new RangeInt(start, count);
            items.BufferLayout.mCount -= unused.Compact(items.Count);
            var range = new RangeInt(items.Count, count);
            if (range.End >= items.BufferCapacityCount) {
                int newSize = items.BufferCapacityCount * 3 / 2 + 1024;
                newSize = Math.Max(newSize, range.End);
                if (!items.AllocResize(newSize)) return default;
            }
            items.BufferLayout.mCount += count;
            return range;
        }
        private bool Reallocate(ref BufferLayoutPersistent items, SparseIndices unused, ref RangeInt range, int count) {
            if (range.Length == count) return false;
            if (range.Length > count) {
                unused.Add(range.End, count - range.End);
                range.Length = count;
                return true;
            }
            if (unused.TryTakeAt(range.End, count - range.Length)) {
                range.Length = count;
                return true;
            }
            var oldRange = range;
            range = RequireSlice(ref items, unused, count);
            items.CopyRange(oldRange.Start, range.Start, oldRange.Length);
            unused.Add(ref oldRange);
            return true;
        }
        public int Allocate(int vcount, int icount) {
            return Ranges.Add(new CanvasRange {
                mVertexRange = RequireVertices(vcount),
                mIndexRange = RequireIndices(icount),
            });
        }
        public void Deallocate(int id) {
            // TODO: Remove directly from buffer if at end
            var range = Ranges[id];
            mFreeVertices.Add(ref range.mVertexRange);
            mFreeIndices.Add(ref range.mIndexRange);
            Ranges.Return(id);
        }
        public CanvasVertices MapVertices(int id) {
            return new CanvasVertices(this, ref Ranges[id]);
        }
        public TypedBufferView<uint> MapIndices(int id) {
            return GetIndices(Ranges[id].mIndexRange);
        }
        public bool Require(ref int elementId, int vcount, int icount) {
            if (elementId == -1) {
                elementId = Allocate(vcount, icount);
                return true;
            }
            var range = Ranges[elementId];
            bool resized = Reallocate(ref mVertices, mFreeVertices, ref range.mVertexRange, vcount) |
                Reallocate(ref mIndices, mFreeIndices, ref range.mIndexRange, icount);
            if (!resized) return false;
            Ranges[elementId] = range;
            return true;
        }
    }
}
