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

    public enum CanvasAxes : byte { Horizontal, Vertical, };

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
        public void RequireMinimumSize(Vector2 size) {
            MinimumSize = Vector2.Max(MinimumSize, size);
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

        public Vector2 ClampSize(Vector2 size) {
            size.X = ClampWidth(size.X);
            size.Y = ClampHeight(size.Y);
            return size;
        }

        public void SetFixedSize(CanvasAxes axis, float itemSize) {
            switch (axis) {
                case CanvasAxes.Horizontal: SetFixedXSize(itemSize); break;
                case CanvasAxes.Vertical: SetFixedYSize(itemSize); break;
                default: throw new NotImplementedException();
            }
        }
        public void SetClampedPreferredSize(CanvasAxes axis, float size) {
            size = Math.Clamp(size, MinimumSize[(int)axis], MaximumSize[(int)axis]);
            PreferredSize[(int)axis] = size;
        }
    }
    public struct SizingResult {
        public Vector2 Size;
        public Vector4 Margins;
        public Vector2 TotalSize => Size + new Vector2(Margins.X + Margins.Z, Margins.Y + Margins.W);

        public float X { get => Size.X; set => Size.X = value; }
        public float Y { get => Size.Y; set => Size.Y = value; }
        public static implicit operator SizingResult(Vector2 s) => new() { Size = s, };
        public static implicit operator Vector2(SizingResult r) => r.Size;
    }
    public interface ICanvasLayout { }

    // An item that forms a part of the UI
    public class CanvasRenderable : IWithParent<CanvasRenderable> {
        public enum DirtyFlags : byte {
            None = 0,
            Transform = 1,  // Our mTransform was changed
            Layout = 2,     // Our mLayoutCache was changed
            Children = 4,   // Our children require layout (our mLayoutCache is unchanged)
            Compose = 8,    // We require a compose pass
        };
        public enum StateFlags : byte {
            None = 0,
            HasCullParent = 1,
            HasCustomTransformApplier = 2,
            HasCanvasLayout = 4,
        };

        public ref struct TransformerContext {
            public ref CanvasLayout Layout;
            public bool IsComplete;
            public TransformerContext(ref CanvasLayout layout) { Layout = ref layout; IsComplete = false; }
        }
        public interface ICustomTransformer {
            void Apply(CanvasRenderable renderable, ref TransformerContext context);
        }
        public string? Name { get; set; }

        protected CanvasBinding mBinding;
        protected CanvasTransform mTransform = CanvasTransform.MakeDefault();
        protected CanvasLayout mLayoutCache;
        protected HittestGrid.Binding hitBinding;
        protected List<CanvasRenderable>? mChildren;
        protected byte mDepth;
        protected DirtyFlags dirtyFlags;
        protected StateFlags stateFlags;

        public Canvas Canvas => mBinding.mCanvas;
        public CanvasRenderable? Parent => mBinding.mParent;
        public IReadOnlyList<CanvasRenderable> Children => mChildren ?? (IReadOnlyList<CanvasRenderable>)Array.Empty<CanvasRenderable>();
        public CanvasTransform Transform {
            get => mTransform;
            set => SetTransform(value);
        }
        public bool HitTestEnabled {
            get => hitBinding.IsEnabled;
            set => SetHitTestEnabled(value);
        }
        public virtual void Initialise(CanvasBinding binding) {
            mBinding = binding;
            if (mBinding.mCanvas != null) {
                mDepth = (byte)(binding.mParent == null ? 0 : (binding.mParent.mDepth + 1));
                stateFlags = StateFlags.None;
                // Should this default to on?
                //if (this is ICustomTransformer) SetCustomTransformEnabled(true);
                if (Parent != null) {
                    if (FindParent<IHitTestGroup>() != null) stateFlags |= StateFlags.HasCullParent;
                    if (this is ICanvasLayout || Parent.HasStateFlag(StateFlags.HasCanvasLayout)) {
                        stateFlags |= StateFlags.HasCanvasLayout;
                    }
                }
                if (hitBinding.IsEnabled) UpdateHitBinding();
                if (mChildren != null) {
                    foreach (var child in mChildren) if (child.Parent == null) child.Initialise(new CanvasBinding(this));
                }
                MarkComposeDirty();
            }
            MarkTransformDirty();
            //MarkSizingDirty();
        }
        public virtual void Uninitialise(CanvasBinding binding) {
            if (mBinding.mCanvas != null) {
                stateFlags = StateFlags.None;
                mBinding.mCanvas.MarkComposeDirty();
                if (mChildren != null) {
                    foreach (var child in mChildren) if (child.Parent == this) child.Uninitialise(new CanvasBinding(this));
                }
                RemoveHitBinding();
            }
            mBinding = default;
        }
        public void SetHitTestEnabled(bool enable) {
            if (hitBinding.IsEnabled == enable) return;
            if (enable) {
                Debug.Assert(!hitBinding.IsValid);
                hitBinding = default;
            } else {
                if (Canvas != null) RemoveHitBinding();
                hitBinding = HittestGrid.Binding.Disabled;
            }
        }
        public void SetCustomTransformEnabled(bool enable) {
            if (enable && this is ICustomTransformer) stateFlags |= StateFlags.HasCustomTransformApplier;
            else stateFlags &= ~StateFlags.HasCustomTransformApplier;
        }
        public void AppendChild(CanvasRenderable child) {
            Debug.Assert(child.Canvas == null, "Element is already added");
            mChildren ??= new();
            InsertChild(mChildren.Count, child);
        }
        public virtual void InsertChild(int index, CanvasRenderable child) {
            mChildren ??= new();
            if (index == -1) index = mChildren.Count;
            mChildren.Insert(index, child);
            if (mBinding.mCanvas != null && child.Parent == null) {
                child.Initialise(new CanvasBinding(this));
            }
            MarkSizingDirty();
        }
        public void ClearChildren() {
            if (mChildren == null) return;
            while (mChildren.Count > 0) RemoveChild(mChildren[^1]);
        }
        public virtual void RemoveChild(CanvasRenderable child) {
            if (mChildren == null) return;
            if (mBinding.mCanvas != null && child.Parent == this)
                child.Uninitialise(new CanvasBinding(this));
            mChildren.Remove(child);
            MarkSizingDirty();
        }
        public T? FindParent<T>() {
            for (var parent = Parent; parent != null; parent = parent.Parent) {
                if (parent is T tvalue) return tvalue;
            }
            return default;
        }
        public void SetTransform(in CanvasTransform transform) {
            mTransform = transform;
            MarkTransformDirty();
        }
        public bool UpdateLayout(in CanvasLayout parent) {
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
                if (mChildren != null) MarkChildrenDirty();
                MarkComposeDirty();
                NotifyTransformChanged();
                return true;
            }
            if (!hitBinding.IsValid && hitBinding.IsEnabled) {
                UpdateHitBinding();
            }
            return false;
        }
        public void RequireLayout() {
            if (HasDirtyFlag(DirtyFlags.Children)) {
                UpdateChildLayouts();
                ClearDirtyFlag(DirtyFlags.Children);
            }
            if (mChildren != null) {
                foreach (var child in mChildren) child.RequireLayout();
            }
        }
        public virtual void UpdateChildLayouts() {
            //if (!hitBinding.IsValid) UpdateHitBinding();
            if (mChildren != null) {
                foreach (var child in mChildren) child.UpdateLayout(mLayoutCache);
            }
        }
        public virtual void Compose(ref CanvasCompositor.Context composer) {
            ClearDirtyFlag(DirtyFlags.Compose | DirtyFlags.Layout);
            if (mChildren != null) {
                foreach (var child in mChildren) {
                    var childContext = composer.InsertChild(child);
                    child.Compose(ref childContext);
                    childContext.ClearRemainder();
                }
            }
        }

        public T? FindChild<T>() where T : CanvasRenderable {
            if (mChildren != null) {
                foreach (var child in mChildren) if (child is T typed) return typed;
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
        private void RemoveHitBinding() {
            Canvas.HitTestGrid.UpdateItem(this, ref hitBinding, new RectI(0, 0, -10000, -10000));
        }
        protected bool HasStateFlag(StateFlags flag) {
            return (stateFlags & flag) != 0;
        }
        // Transform has changed, need to 
        protected void MarkTransformDirty() {
            dirtyFlags |= DirtyFlags.Transform;
            //if (Parent != null) Parent.dirtyFlags |= DirtyFlags.Children;
            if (Parent != null) Parent.MarkChildrenDirty();
            //if (Canvas != null && Canvas != this) Canvas.MarkChildrenDirty();
        }
        // mLayoutCache has changed, need to update Element layouts and recompose
        protected void MarkLayoutDirty() {
            dirtyFlags |= DirtyFlags.Layout;
            if (Canvas != null && Canvas != this) Canvas.MarkLayoutDirty();
        }
        // Children need to be rearranged
        protected void MarkChildrenDirty() {
            Debug.Assert(mChildren != null, "Cannot mark children dirty if no children");
            dirtyFlags |= DirtyFlags.Children;
            if (Canvas != null && Canvas != this) Canvas.MarkChildrenDirty();
        }
        // Our desired size has changed, notify the parent
        private void MarkSizingDirty() {
            if (HasStateFlag(StateFlags.HasCanvasLayout)) {
                MarkChildrenDirty();
                Parent!.MarkSizingDirty();
            }
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

        public virtual SizingResult GetDesiredSize(SizingParameters sizing) {
            var childSizing = sizing;
            childSizing.Unapply(Transform);
            var childSize = Vector2.Zero;
            if (mChildren != null) {
                for (int i = 0; i < mChildren.Count; i++) {
                    childSize = Vector2.Max(childSize, mChildren[i].GetDesiredSize(childSizing));
                }
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
                if (item2 == null) return 1;
                if (item1.Parent == item2.Parent) {
                    var parent = item1.Parent!;
                    return parent.mChildren!.IndexOf(item2).CompareTo(parent.mChildren.IndexOf(item1));
                }
                item1 = item1.Parent!;
                item2 = item2.Parent!;
            }
            return -1;
        }

        public override string? ToString() {
            return Name ?? GetType().Name;
        }

    }
}
