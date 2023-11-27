using GameEngine23.Interop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.Utility;

namespace Weesals.UI {

    public struct CanvasVertices {
        private CanvasMeshBuffer builder;
        private CanvasMeshBuffer.CanvasRange range;
        public int VertexOffset => range.mVertexRange.Start;
	    public CanvasVertices(CanvasMeshBuffer _builder, CanvasMeshBuffer.CanvasRange _range) {
            range = _range;
            builder = _builder;
        }
        public int GetVertexCount() { return range.mVertexRange.Length; }
	    public int GetIndexCount() { return range.mIndexRange.Length; }
        public TypedBufferView<Vector3> GetPositions() { return builder.GetPositions(range.mVertexRange); }
        public TypedBufferView<Vector2> GetTexCoords() { return builder.GetTexCoords(range.mVertexRange); }
        public TypedBufferView<Color> GetColors() { return builder.GetColors(range.mVertexRange); }
        public TypedBufferView<uint> GetIndices() { return builder.GetIndices(range.mIndexRange); }
        public void MarkChanged() {
            builder.MarkIndicesChanged(range.mIndexRange);
            builder.MarkVerticesChanged(range.mVertexRange);
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
		};
        protected SparseArray<CanvasRange> Ranges = new();

        public int VertexRevision => mVertices.BufferLayout.revision;

        public CanvasMeshBuffer() {
            mVertices = new BufferLayoutPersistent(0, BufferLayoutPersistent.Usages.Vertex, 0);
            mPositionEl = mVertices.AppendElement(new CSBufferElement("POSITION", BufferFormat.FORMAT_R32G32B32_FLOAT));
            mTexCoordEl = mVertices.AppendElement(new CSBufferElement("TEXCOORD", BufferFormat.FORMAT_R16G16_UNORM));
            mColorEl = mVertices.AppendElement(new CSBufferElement("COLOR", BufferFormat.FORMAT_R8G8B8A8_UNORM));
            mIndices = new BufferLayoutPersistent(0, BufferLayoutPersistent.Usages.Index, 0);
            mIndices.AppendElement(new CSBufferElement("INDEX", BufferFormat.FORMAT_R32_UINT));
            mFreeVertices = new(true);
            mFreeIndices = new(true);
        }
        public void Dispose() {
            mIndices.Dispose();
            mVertices.Dispose();
        }

        public TypedBufferView<Vector3> GetPositions(RangeInt range) {
            return new TypedBufferView<Vector3>(mVertices.Elements[mPositionEl], range);
        }
        public TypedBufferView<Vector2> GetTexCoords(RangeInt range) {
            return new TypedBufferView<Vector2>(mVertices.Elements[mTexCoordEl], range);
        }
        public TypedBufferView<Color> GetColors(RangeInt range) {
            return new TypedBufferView<Color>(mVertices.Elements[mColorEl], range);
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
            RangeInt range = mFreeVertices.Allocate(vcount);
            if (range.Start >= 0) return range;
            mVertices.BufferLayout.mCount -= mFreeVertices.Compact(mVertices.Count);
            range = new RangeInt(mVertices.Count, vcount);
            if (range.End * mVertices.BufferStride >= mVertices.BufferLayout.size) {
                int newSize = mVertices.BufferCapacityCount + 1024;
                newSize = Math.Max(newSize, range.End);
                if (!mVertices.AllocResize(newSize)) return default;
            }
            mVertices.BufferLayout.mCount += vcount;
            return range;
        }
        public RangeInt RequireIndices(int icount) {
            RangeInt range = mFreeIndices.Allocate(icount);
            if (range.Start >= 0) return range;
            mIndices.BufferLayout.mCount -= mFreeIndices.Compact(mIndices.Count);
            range = new RangeInt(mIndices.Count, icount);
            if (range.End * mIndices.BufferStride >= mIndices.BufferLayout.revision) {
                int newSize = mIndices.BufferLayout.revision + 1024 * mIndices.BufferStride;
                newSize = Math.Max(newSize, range.End * mIndices.BufferStride);
                if (!mIndices.AllocResize(newSize)) return default;
            }
            mIndices.BufferLayout.mCount += icount;
            return range;
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
            mFreeVertices.Return(ref range.mVertexRange);
	        mFreeIndices.Return(ref range.mIndexRange);
	        Ranges.Return(id);
        }
        public CanvasVertices MapVertices(int id) {
	        var range = Ranges[id];
	        return new CanvasVertices(this, range);
        }
	}


    public struct CanvasBinding {
        public Canvas mCanvas;
        public CanvasRenderable? mParent;
        public CanvasBinding(Canvas canvas) { mCanvas = canvas; mParent = null; }
        public CanvasBinding(CanvasRenderable parent) { mCanvas = parent.Canvas; mParent = parent; }
    }

    // An item that forms a part of the UI
    public class CanvasRenderable {
        protected CanvasBinding mBinding;
        protected List<CanvasRenderable> mChildren = new();
        protected CanvasTransform mTransform = CanvasTransform.MakeDefault();
	    protected CanvasLayout mLayoutCache;
        protected bool requireUpdateChildren;
        public Canvas Canvas => mBinding.mCanvas;
	    public CanvasRenderable? Parent => mBinding.mParent;
        public CanvasTransform Transform {
            get => mTransform;
            set => SetTransform(value);
        }
        public virtual void Initialise(CanvasBinding binding) {
            mBinding = binding;
            if (mBinding.mCanvas != null)
                foreach (var child in mChildren) child.Initialise(new CanvasBinding(this));
        }
        public virtual void Uninitialise(CanvasBinding binding) {
            if (mBinding.mCanvas != null)
                foreach (var child in mChildren) child.Uninitialise(new CanvasBinding(this));
            mBinding = default;
        }
        public virtual void AppendChild(CanvasRenderable child) {
            InsertChild(mChildren.Count, child);
        }
        protected void InsertChild(int index, CanvasRenderable child) {
            if (mBinding.mCanvas != null)
                child.Initialise(new CanvasBinding(this));
            mChildren.Insert(index, child);
            MarkChildrenDirty();
        }
        public virtual void RemoveChild(CanvasRenderable child) {
	        if (mBinding.mCanvas != null)
		        child.Uninitialise(new CanvasBinding(this));
	        mChildren.Remove(child);
            MarkChildrenDirty();
        }
        public void SetTransform(in CanvasTransform transform) {
            mTransform = transform;
            if (Parent != null) Parent.MarkChildrenDirty();
        }
        public void RequireLayout() {
            if (requireUpdateChildren) {
                UpdateChildLayouts();
                requireUpdateChildren = false;
            }
            foreach (var child in mChildren) child.RequireLayout();
        }
        public void UpdateLayout(in CanvasLayout parent) {
            int oldHash = mLayoutCache.GetHashCode();
            mTransform.Apply(parent, out mLayoutCache);
            if (oldHash != mLayoutCache.GetHashCode()) {
                MarkChildrenDirty();
                NotifyTransformChanged();
            }
        }
        public virtual void UpdateChildLayouts() {
            foreach (var child in mChildren) {
                child.UpdateLayout(mLayoutCache);
            }
        }
        public virtual void Compose(ref CanvasCompositor.Context composer) {
            foreach (var child in mChildren) {
                var childContext = composer.InsertChild(child);
                child.Compose(ref childContext);
                childContext.ClearRemainder();
            }
        }

        public T? FindChild<T>() where T : CanvasRenderable {
		    foreach (var child in mChildren) {
                if (child is T typed) return typed;
		    }
		    return null;
	    }
        protected virtual void NotifyTransformChanged() {
        }
        protected void MarkChildrenDirty() {
            requireUpdateChildren = true;
        }
    }

    public class Canvas : CanvasRenderable, IDisposable {
        public CanvasMeshBuffer Builder { get; private set; }
        public CanvasCompositor Compositor { get; private set; }
        public Material Material;
        private Int2 mSize;

        public int Revision => Builder.VertexRevision + Compositor.GetIndices().BufferLayout.revision;

        unsafe public Canvas() {
            Builder = new();
            Compositor = new(Builder);
            Initialise(new CanvasBinding(this));
            Material = new Material("./assets/ui.hlsl");
            Material.SetBlendMode(BlendMode.MakeAlphaBlend());
            Material.SetRasterMode(RasterMode.MakeDefault().SetCull(RasterMode.CullModes.None));
            Material.SetDepthMode(DepthMode.MakeOff());
        }
        public void Dispose() {
            Builder.Dispose();
            Compositor.Dispose();
        }
        public void SetSize(Int2 size) {
            if (mSize != size) {
                mSize = size;
                Material.SetValue("Projection", Matrix4x4.CreateOrthographicOffCenter(0.0f, (float)mSize.X, (float)mSize.Y, 0.0f, 0.0f, 500.0f));
                UpdateLayout(CanvasLayout.MakeBox(size));
            }
            RequireLayout();
            var builder = Compositor.CreateBuilder();
            var compositor = Compositor.CreateRoot(ref builder);
            Compose(ref compositor);
        }
        public Int2 GetSize() {
	        return mSize;
        }

        public void Render(CSGraphics graphics, Material material) {
            Compositor.Render(graphics, material);
        }
    }

}
