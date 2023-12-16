using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.Utility;

namespace Weesals.UI {
    public struct CanvasBinding {
        public Canvas mCanvas;
        public CanvasRenderable? mParent;
        public CanvasBinding(Canvas canvas) { mCanvas = canvas; mParent = null; }
        public CanvasBinding(CanvasRenderable parent) { mCanvas = parent.Canvas; mParent = parent; }
    }

    public struct SizingParameters {
        public Vector2 MinimumSize;
        public Vector2 PreferredSize;
        public Vector2 MaximumSize;
        public static readonly SizingParameters Default = new SizingParameters() { MinimumSize = Vector2.Zero, PreferredSize = new Vector2(80f), MaximumSize = new Vector2(float.MaxValue), };

        public void SetFixedXSize(float size) {
            MinimumSize.X = MaximumSize.X = PreferredSize.X = size;
        }
        public void SetFixedYSize(float size) {
            MinimumSize.Y = MaximumSize.Y = PreferredSize.Y = size;
        }

        public SizingParameters SetPreferredSize(Vector2 size) {
            PreferredSize = size;
            return this;
        }
    }

    // An item that forms a part of the UI
    public class CanvasRenderable : IWithParent<CanvasRenderable> {
        public enum DirtyFlags : byte { None = 0, Transform = 1, Layout = 2, Children = 4, Compose = 8, };
        public enum StateFlags : byte { None = 0, HasCullParent = 1, HasCustomTransformApplier = 2, };

        public ref struct TransformerContext {
            public ref CanvasLayout Layout;
            public bool IsComplete;
            public TransformerContext(ref CanvasLayout layout) { Layout = ref layout; IsComplete = false; }
        }
        public interface ICustomTransformer {
            void Apply(CanvasRenderable renderable, ref TransformerContext context);
        }

        protected CanvasBinding mBinding;
        protected CanvasTransform mTransform = CanvasTransform.MakeDefault();
        protected CanvasLayout mLayoutCache;
        protected HittestGrid.Binding hitBinding;
        protected List<CanvasRenderable> mChildren = new();
        protected List<ICustomTransformer>? customTransformers;
        protected int mOrderId = -1;
        protected DirtyFlags dirtyFlags;
        protected StateFlags stateFlags;

        public Canvas Canvas => mBinding.mCanvas;
        public CanvasRenderable? Parent => mBinding.mParent;
        public IReadOnlyList<CanvasRenderable> Children => mChildren;
        public CanvasTransform Transform {
            get => mTransform;
            set => SetTransform(value);
        }
        internal int GetOrderId() { return mOrderId; }
        public virtual void Initialise(CanvasBinding binding) {
            mBinding = binding;
            if (mBinding.mCanvas != null) {
                var next = Parent?.FindNext(this);
                var nextOrderId = next != null ? next.mOrderId : mOrderId + 0x1000000;
                int step = (nextOrderId - mOrderId) / (mChildren.Count + 1);
                int orderId = mOrderId;
                foreach (var child in mChildren) { orderId += step; child.mOrderId = orderId; }
                foreach (var child in mChildren) if (child.Parent == null) child.Initialise(new CanvasBinding(this));
                stateFlags = StateFlags.None;
                if (FindParent<IHitTestGroup>() != null) stateFlags |= StateFlags.HasCullParent;
                if (this is ICustomTransformer) stateFlags |= StateFlags.HasCustomTransformApplier;
                MarkComposeDirty();
            }
            MarkTransformDirty();
        }
        public virtual void Uninitialise(CanvasBinding binding) {
            if (mBinding.mCanvas != null) {
                mBinding.mCanvas.MarkComposeDirty();
                foreach (var child in mChildren) if (child.Parent == this) child.Uninitialise(new CanvasBinding(this));
                SetHitTestEnabled(false);
            }
            mBinding = default;
        }
        public void SetHitTestEnabled(bool enable) {
            if (hitBinding.IsEnabled == enable) return;
            if (enable) {
                Debug.Assert(!hitBinding.IsValid);
                hitBinding = default;
            } else {
                if (Canvas != null)
                    Canvas.HitTestGrid.UpdateItem(this, ref hitBinding, new RectI(0, 0, -10000, -10000));
                hitBinding = HittestGrid.Binding.Disabled;
            }
        }
        public void SetCustomTransformEnabled(bool enable) {
            if (enable && this is ICustomTransformer) stateFlags |= StateFlags.HasCustomTransformApplier;
            else stateFlags &= ~StateFlags.HasCustomTransformApplier;
        }
        public virtual void AppendChild(CanvasRenderable child) {
            InsertChild(mChildren.Count, child);
        }
        protected void InsertChild(int index, CanvasRenderable child) {
            mChildren.Insert(index, child);
            if (mBinding.mCanvas != null && child.Parent == null) {
                var prev = index > 0 ? mChildren[index - 1] : this;
                var next = index + 2 < mChildren.Count ? mChildren[index + 2] : Parent?.FindNext(this);
                child.mOrderId = next != null ? (next.mOrderId + prev.mOrderId) / 2 : prev.mOrderId + 0x1000000;
                child.Initialise(new CanvasBinding(this));
            }
            MarkChildrenDirty();
        }
        public void ClearChildren() {
            while (mChildren.Count > 0) RemoveChild(mChildren[^1]);
        }
        public virtual void RemoveChild(CanvasRenderable child) {
            if (mBinding.mCanvas != null && child.Parent == this)
                child.Uninitialise(new CanvasBinding(this));
            mChildren.Remove(child);
            MarkChildrenDirty();
        }
        public T? FindParent<T>() {
            for (var parent = Parent; parent != null; parent = parent.Parent) {
                if (parent is T tvalue) return tvalue;
            }
            return default;
        }
        private CanvasRenderable FindPrev(CanvasRenderable from) {
            var child = mChildren.IndexOf(from);
            Debug.Assert(child >= 0, "Child does not exist in self");
            --child;
            if (child >= 0) return mChildren[child];
            return this;
        }
        private CanvasRenderable? FindNext(CanvasRenderable from) {
            var child = mChildren.IndexOf(from);
            Debug.Assert(child >= 0, "Child does not exist in self");
            ++child;
            if (child < mChildren.Count) return mChildren[child];
            return Parent?.FindNext(this);
        }
        public void SetTransform(in CanvasTransform transform) {
            mTransform = transform;
            MarkTransformDirty();
            if (Parent != null) Parent.MarkChildrenDirty();
        }
        public void UpdateLayout(in CanvasLayout parent) {
            ClearDirtyFlag(DirtyFlags.Transform);
            int oldHash = mLayoutCache.GetHashCode();
            mTransform.Apply(parent, out mLayoutCache);
            if ((stateFlags & StateFlags.HasCustomTransformApplier) != 0) {
                TransformerContext context = new(ref mLayoutCache);
                ((ICustomTransformer)this).Apply(this, ref context);
                if (!context.IsComplete) MarkTransformDirty();
            }
            if (oldHash != mLayoutCache.GetHashCode()) {
                MarkLayoutDirty();
                MarkChildrenDirty();
                MarkComposeDirty();
                NotifyTransformChanged();
            }
        }
        public void RequireLayout() {
            if (HasDirtyFlag(DirtyFlags.Children)) {
                ClearDirtyFlag(DirtyFlags.Children);
                UpdateChildLayouts();
            }
            foreach (var child in mChildren) child.RequireLayout();
        }
        public virtual void UpdateChildLayouts() {
            foreach (var child in mChildren) {
                child.UpdateLayout(mLayoutCache);
            }
        }
        public virtual void Compose(ref CanvasCompositor.Context composer) {
            ClearDirtyFlag(DirtyFlags.Compose | DirtyFlags.Layout);
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
            if (hitBinding.IsEnabled) {
                var p0 = mLayoutCache.TransformPosition2DN(new Vector2(0.0f, 0.0f));
                var p1 = mLayoutCache.TransformPosition2DN(new Vector2(1.0f, 0.0f));
                var p2 = mLayoutCache.TransformPosition2DN(new Vector2(0.0f, 1.0f));
                var p3 = mLayoutCache.TransformPosition2DN(new Vector2(1.0f, 1.0f));
                RectI bounds = new RectI((int)p0.X, (int)p0.Y, 0, 0);
                bounds = bounds.ExpandToInclude(new Int2((int)p1.X, (int)p1.Y));
                bounds = bounds.ExpandToInclude(new Int2((int)p2.X, (int)p2.Y));
                bounds = bounds.ExpandToInclude(new Int2((int)p3.X, (int)p3.Y));
                Canvas.HitTestGrid.UpdateItem(this, ref hitBinding, bounds);
            }
        }
        protected void MarkTransformDirty() {
            dirtyFlags |= DirtyFlags.Transform;
            if (Parent != null) Parent.dirtyFlags |= DirtyFlags.Children;
            if (Canvas != null && Canvas != this) Canvas.MarkChildrenDirty();
        }
        protected void MarkLayoutDirty() {
            dirtyFlags |= DirtyFlags.Layout;
            if (Canvas != null && Canvas != this) Canvas.MarkLayoutDirty();
        }
        protected void MarkChildrenDirty() {
            dirtyFlags |= DirtyFlags.Children;
            if (Canvas != null && Canvas != this) Canvas.MarkChildrenDirty();
        }
        protected void MarkComposeDirty() {
            dirtyFlags |= DirtyFlags.Compose;
            if (Canvas != null && Canvas != this) Canvas.MarkComposeDirty();
        }
        protected bool HasDirtyFlag(DirtyFlags flag) {
            return (dirtyFlags & flag) != 0;
        }
        protected void ClearDirtyFlag(DirtyFlags flag) {
            dirtyFlags &= ~flag;
        }

        public bool HitTest(Vector2 pos) {
            var lpos = mLayoutCache.InverseTransformPosition2D(pos);
            if (lpos.X < 0f || lpos.Y < 0f || lpos.X >= mLayoutCache.GetWidth() || lpos.Y >= mLayoutCache.GetHeight()) return false;
            if (this is IHitTest hittest && !hittest.HitTest(pos)) return false;
            if ((stateFlags & StateFlags.HasCullParent) != 0) {
                var cullParent = FindParent<IHitTestGroup>()!;
                if (!cullParent.HitTest(pos)) return false;
            }
            return true;
        }

        public virtual Vector2 GetDesiredSize(SizingParameters sizing) {
            return sizing.PreferredSize;
        }

        internal CanvasLayout GetComputedLayout() {
            return mLayoutCache;
        }

        public static void CopyTransform(CanvasRenderable dst, CanvasRenderable src) {
            var parent = dst.Parent?.mLayoutCache ?? CanvasLayout.MakeBox(new Vector2(0, 0));
            var source = src.mLayoutCache;
            var tform = new CanvasTransform();
            tform.Anchors = default;
            var localPos = parent.InverseTransformPosition(source.Position);
            tform.Offsets.toxy(localPos.toxy());
            tform.Offsets.tozw(localPos.toxy() + source.GetSize());
            dst.SetTransform(tform);
        }

    }
}
