using GameEngine23.Interop;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;

namespace Weesals.UI {
    public class Image : CanvasRenderable {

        public enum AspectModes { None, PreserveAspectContain, PreserveAspectClip, };

        public CanvasImage Element;
        public AspectModes AspectMode = AspectModes.PreserveAspectContain;
        public Vector2 ImageAnchor = new Vector2(0.5f, 0.5f);

        public Image(CSTexture texture = default) {
            Element = new CanvasImage(texture, new RectF(0f, 0f, 1f, 1f));
        }
        public Image(Sprite sprite) {
            Element = new CanvasImage(sprite.Texture, sprite.UVRect);
        }
        public override void Initialise(CanvasBinding binding) {
            base.Initialise(binding);
            Element.Initialize(Canvas);
        }
        public override void Uninitialise(CanvasBinding binding) {
            Element.Dispose(Canvas);
            base.Uninitialise(binding);
        }
        protected override void NotifyTransformChanged() {
            base.NotifyTransformChanged();
            if (Element.Texture.IsValid()) {
                if (AspectMode == AspectModes.PreserveAspectContain) {
                    var size = mLayoutCache.GetSize();
                    var imgSize = (Vector2)Element.Texture.GetSize() * Element.UVRect.Size;
                    var ratio = new Vector2(size.X * imgSize.Y, size.Y * imgSize.X);
                    if (ratio.X != ratio.Y) {
                        var osize = size;
                        if (ratio.X > ratio.Y) {
                            size.X = ratio.Y / imgSize.Y;
                            mLayoutCache.Position += mLayoutCache.AxisX.toxyz() * ((osize.X - size.X) * ImageAnchor.X);
                        } else {
                            size.Y = ratio.X / imgSize.X;
                            mLayoutCache.Position += mLayoutCache.AxisY.toxyz() * ((osize.Y - size.Y) * ImageAnchor.Y);
                        }
                        mLayoutCache.SetSize(size);
                    }
                }
            }
            Element.UpdateLayout(Canvas, mLayoutCache);
        }
        public override void Compose(ref CanvasCompositor.Context composer) {
            Element.Append(ref composer);
            base.Compose(ref composer);
        }
    }
    public class GridLayout : CanvasRenderable {
        public Int2 CellCount = new Int2(4, 4);
        public override void UpdateChildLayouts() {
            base.UpdateChildLayouts();
            var cellSize = mLayoutCache.GetSize() / (Vector2)CellCount;
            var layout = mLayoutCache;
            layout.SetSize(cellSize);
            Int2 g = Int2.Zero;
            foreach (var child in mChildren) {
                layout.Position = mLayoutCache.TransformPosition2D((Vector2)g * cellSize);
                child.UpdateLayout(layout);
                if (++g.X >= CellCount.X) {
                    g.X = 0;
                    g.Y++;
                }
            }
        }
    }
    public class ListLayout : CanvasRenderable {
        public enum Axes { Horizontal, Vertical, };
        public float ItemSize = 0f;
        public Axes Axis = Axes.Vertical;
        public override void UpdateChildLayouts() {
            base.UpdateChildLayouts();
            var layout = mLayoutCache;
            ref var axisVec4 = ref (Axis == Axes.Horizontal ? ref layout.AxisX : ref layout.AxisY);
            if (ItemSize != 0f) axisVec4.W = ItemSize;
            else axisVec4.W /= mChildren.Count;
            var axisVec = axisVec4.toxyz();
            int i = 0;
            foreach (var child in mChildren) {
                child.UpdateLayout(layout);
                layout.Position += axisVec * axisVec4.W;
                ++i;
            }
        }
    }
    public class FlexLayout : CanvasRenderable {
        public struct Division {
            public float Size;
            public int Children;
            public override string ToString() { return Size.ToString("0.00") + " x" + Children; }
        }
        List<Division> divisions = new();

        public FlexLayout() {
            divisions.Add(new Division() { Size = 1.0f, Children = 0, });
            Debug.Assert(IsHorizontal(GetDepth(0)));
        }

        private int SkipChildren(int d) {
            var division = divisions[d];
            if (division.Children > 0) {
                int end = d + division.Children;
                for (++d; d < end; ++d) end += divisions[d].Children;
            }
            return d;
        }
        private int GetParent(int d) {
            int children = 0;
            for (--d; d >= 0; --d) {
                ++children;
                children -= divisions[d].Children;
                if (children <= 0) return d;
            }
            return -1;
        }
        private int GetDepth(int d) {
            int depth = -1;
            while (d >= 0) {
                ++depth;
                d = GetParent(d);
            }
            return depth;
        }
        private static bool IsHorizontal(int depth) {
            return (depth & 0x01) == 0;
        }

        public override void AppendChild(CanvasRenderable child) {
            AppendRight(child);
        }
        public override void RemoveChild(CanvasRenderable child) {
            var index = mChildren.IndexOf(child);
            cache.Process(divisions);
            int divisionI = cache.Items[index].DivisionIndex;
            var parentI = GetParent(divisionI);
            var division = divisions[divisionI];
            var parent = divisions[parentI];
            parent.Children--;
            divisions[parentI] = parent;
            divisions.RemoveAt(divisionI);
            int d = parentI + 1;
            for (int c = 0; c < parent.Children; ++c) {
                var sibling = divisions[d];
                sibling.Size /= 1.0f - division.Size;
                divisions[d] = sibling;
                d = SkipChildren(d) + 1;
            }

            base.RemoveChild(child);
        }
        public void AppendRight(CanvasRenderable child, float width = 0.2f) {
            base.AppendChild(child);
            AppendDivision(0, width);
        }
        public void InsertBelow(CanvasRenderable other, CanvasRenderable child, float height = 0.5f) {
            var index = mChildren.IndexOf(other);
            base.InsertChild(index + 1, child);
            cache.Process(divisions);
            int divisionI = cache.Items[index].DivisionIndex;
            MakeOrientation(ref divisionI, false);
            InsertDivision(divisionI, height);
        }
        public void InsertRight(CanvasRenderable other, CanvasRenderable child, float width = 0.5f) {
            var index = mChildren.IndexOf(other);
            base.InsertChild(index + 1, child);
            cache.Process(divisions);
            int divisionI = cache.Items[index].DivisionIndex;
            MakeOrientation(ref divisionI, true);
            InsertDivision(divisionI, width);
        }

        private void MakeOrientation(ref int divisionI, bool horizontal) {
            // Orientation of parent is inverse of self
            if (!IsHorizontal(GetDepth(divisionI)) == horizontal) return;
            var division = divisions[divisionI];
            division.Children++;
            divisions[divisionI] = division;
            ++divisionI;
            divisions.Insert(divisionI, new Division() {
                Size = 1.0f,
                Children = 0,
            });
        }

        // Appends a division to the specified parent, scaling all other children
        // Does NOT insert a matching child!
        private void AppendDivision(int parent, float width) {
            var root = divisions[parent];
            var childSize = root.Children == 0 ? 1.0f : width;
            int d = parent + 1;
            for (int c = 0; c < root.Children; ++c) {
                var sibling = divisions[d];
                sibling.Size *= 1.0f - childSize;
                divisions[d] = sibling;
                d = SkipChildren(d) + 1;
            }
            root.Children++;
            divisions[parent] = root;
            divisions.Insert(d, new Division() { Size = childSize, Children = 0, });
        }
        // Splits a division, consuming a percentage of its size
        // Does NOT insert a matching child!
        private void InsertDivision(int divisionI, float size) {
            var parentI = GetParent(divisionI);
            var parent = divisions[parentI];
            parent.Children++;
            divisions[parentI] = parent;
            var division = divisions[divisionI];
            divisions.Insert(divisionI + 1, new Division() {
                Size = division.Size * size,
                Children = 0,
            });
            division.Size *= 1.0f - size;
            divisions[divisionI] = division;
        }

        public struct StackItem {
            public int DivisionIndex;
            public int ChildCount;
            public int AxisL, AxisR, AxisT, AxisB;
            public int AxisS;
        }
        public class FlexCache {
            public List<float> Axes = new();
            public List<StackItem> Items = new();
            Stack<StackItem> stack = new();
            public void Clear() {
                Axes.Clear();
                stack.Clear();
                Items.Clear();
            }
            public void Process(List<Division> divisions) {
                Clear();
                Axes.Add(0.0f);
                Axes.Add(0.0f);
                Axes.Add(1.0f);
                Axes.Add(1.0f);
                Debug.Assert(IsHorizontal(stack.Count));
                stack.Push(new StackItem() {
                    DivisionIndex = -1,
                    ChildCount = 1,
                    AxisL = 0,
                    AxisT = 1,
                    AxisR = 2,
                    AxisB = 3,
                    AxisS = 0,
                });
                int c = 0;
                for (int d = 0; d < divisions.Count; ++d) {
                    var division = divisions[d];
                    var top = stack.Pop();
                    --top.ChildCount;
                    Debug.Assert(top.ChildCount >= 0);
                    var horizontal = IsHorizontal(stack.Count);
                    var child = new StackItem() {
                        ChildCount = division.Children,
                        DivisionIndex = d,
                        AxisL = top.AxisL,
                        AxisT = top.AxisT,
                        AxisR = top.AxisR,
                        AxisB = top.AxisB,
                        AxisS = horizontal ? top.AxisL : top.AxisT,
                    };
                    if (top.ChildCount != 0) {
                        var parentHoriz = IsHorizontal(stack.Count - 1);
                        var axis = Axes.Count;
                        var axisMin = Axes[top.AxisS];
                        var axisMax = Axes[parentHoriz ? top.AxisR : top.AxisB];
                        var axisStep = Axes[parentHoriz ? top.AxisL : top.AxisT];
                        Axes.Add(axisStep + division.Size * (axisMax - axisMin));
                        if (parentHoriz) {
                            child.AxisR = axis;
                            top.AxisL = axis;
                        } else {
                            child.AxisB = axis;
                            top.AxisT = axis;
                        }
                    }
                    stack.Push(top);
                    if (division.Children == 0) {
                        // Leaf node, add an item here
                        Items.Add(child);
                        ++c;
                        while (stack.Count > 0 && stack.Peek().ChildCount == 0) stack.Pop();
                        continue;
                    }
                    // Branch node, divide parent and append
                    stack.Push(child);

                }
            }
        }
        FlexCache cache = new();
        public override void UpdateChildLayouts() {
            base.UpdateChildLayouts();
            cache.Process(divisions);
            var axes = cache.Axes;
            for (int c = 0; c < cache.Items.Count; ++c) {
                var child = cache.Items[c];
                var layout = mLayoutCache;
                layout = layout.MinMaxNormalized(axes[child.AxisL], axes[child.AxisT], axes[child.AxisR], axes[child.AxisB]);
                mChildren[c].UpdateLayout(layout);
            }
        }
    }
}
