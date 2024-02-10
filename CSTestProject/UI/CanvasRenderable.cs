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
        public static readonly SizingParameters Default = new SizingParameters() { MinimumSize = Vector2.Zero, PreferredSize = new Vector2(20f), MaximumSize = new Vector2(float.MaxValue), };

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

        public void Unapply(CanvasTransform transform) {
            var anchorSize = transform.AnchorMax - transform.AnchorMin;
            var offsetSize = transform.OffsetMax - transform.OffsetMin;
            if (anchorSize.X == 0f) anchorSize.X = 1.0f;
            if (anchorSize.Y == 0f) anchorSize.Y = 1.0f;
            MinimumSize = Vector2.Max(Vector2.Zero, MinimumSize * anchorSize - offsetSize);
            PreferredSize = Vector2.Max(Vector2.Zero, PreferredSize * anchorSize - offsetSize);
            MaximumSize = Vector2.Max(Vector2.Zero, MaximumSize * anchorSize - offsetSize);
        }
        public void Apply(CanvasTransform transform) {
            var anchorSize = transform.AnchorMax - transform.AnchorMin;
            var offsetSize = transform.OffsetMax - transform.OffsetMin;
            MinimumSize += offsetSize;
            PreferredSize += offsetSize;
            MaximumSize += offsetSize;
            if (anchorSize.X > 0f) { MinimumSize.X /= anchorSize.X; PreferredSize.X /= anchorSize.X; MaximumSize.X /= anchorSize.X; }
            if (anchorSize.Y > 0f) { MinimumSize.Y /= anchorSize.Y; PreferredSize.X /= anchorSize.X; MaximumSize.X /= anchorSize.X; }
        }

        public float ClampWidth(float width) {
            return Math.Clamp(width, MinimumSize.X, MaximumSize.X);
        }
        public float ClampHeight(float height) {
            return Math.Clamp(height, MinimumSize.Y, MaximumSize.Y);
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
        protected byte mDepth;
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
                mDepth = (byte)(binding.mParent == null ? 0 : (binding.mParent.mDepth + 1));
                SetHitTestEnabled(true);
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
            if (!hitBinding.IsValid) UpdateHitBinding();
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
            if (hitBinding.IsEnabled) UpdateHitBinding();
        }
        private void UpdateHitBinding() {
            var p0 = mLayoutCache.TransformPosition2DN(new Vector2(0.0f, 0.0f));
            var p1 = mLayoutCache.TransformPosition2DN(new Vector2(1.0f, 0.0f));
            var p2 = mLayoutCache.TransformPosition2DN(new Vector2(0.0f, 1.0f));
            var p3 = mLayoutCache.TransformPosition2DN(new Vector2(1.0f, 1.0f));
            RectI bounds = new RectI((int)p0.X, (int)p0.Y, 0, 0)
                .ExpandToInclude(new Int2((int)p1.X, (int)p1.Y))
                .ExpandToInclude(new Int2((int)p2.X, (int)p2.Y))
                .ExpandToInclude(new Int2((int)p3.X, (int)p3.Y));
            Canvas.HitTestGrid.UpdateItem(this, ref hitBinding, bounds);
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
            var childSizing = sizing;
            childSizing.Unapply(Transform);
            var childSize = Vector2.Zero;
            for (int i = 0; i < Children.Count; i++) {
                childSize = Vector2.Max(childSize, Children[i].GetDesiredSize(childSizing));
            }
            childSizing.PreferredSize = Vector2.Max(childSizing.PreferredSize, childSize);
            childSizing.Apply(Transform);
            return Vector2.Clamp(childSizing.PreferredSize, sizing.MinimumSize, sizing.MaximumSize);
        }

        public CanvasLayout GetComputedLayout() {
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
        public static CanvasRenderable? FindCommonAncestor(CanvasRenderable item1, CanvasRenderable item2) {
            while (item1.mDepth > item2.mDepth) item1 = item1.Parent!;
            while (item2.mDepth > item1.mDepth) item2 = item2.Parent!;
            while (item1 != null && item1 != item2) {
                item1 = item1.Parent!;
                item2 = item2.Parent!;
            }
            return item1;
        }
        public static int GetGlobalOrder(CanvasRenderable item1, CanvasRenderable item2) {
            if (item1 == item2) return 0;
            while (item1.mDepth > item2.mDepth) item1 = item1.Parent!;
            if (item1 == item2) return -1;
            while (item2.mDepth > item1.mDepth) item2 = item2.Parent!;
            if (item1 == item2) return 1;
            while (item1 != null && item1 != item2) {
                if (item1.Parent == item2.Parent) {
                    var parent = item1.Parent!;
                    return parent.mChildren.IndexOf(item2).CompareTo(parent.mChildren.IndexOf(item1));
                }
                item1 = item1.Parent!;
                item2 = item2.Parent!;
            }
            return -1;
        }

    }
}
