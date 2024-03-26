using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Weesals.ECS;
using Weesals.Editor;
using Weesals.Engine;
using Weesals.Utility;

namespace Weesals.UI
{
    public struct Tween : ITimedEvent {
        public float Time0;
        public float Time1;
        public float Time => Time1;
        public float DeltaTime => Time1 - Time0;
        float ITimedEvent.TimeSinceEvent => Time;

        public static readonly Tween Complete = new Tween() { Time0 = 1000000f, Time1 = float.MaxValue, };
    }
    public interface ITweenable {
        // Return true while the tween is complete
        bool UpdateTween(Tween tween);
    }
    public class Image : CanvasRenderable {

        public enum AspectModes { None, PreserveAspectContain, PreserveAspectClip, };

        public CanvasImage Element;
        public AspectModes AspectMode = AspectModes.PreserveAspectContain;
        public Vector2 ImageAnchor = new Vector2(0.5f, 0.5f);

        public CSTexture Texture { get => Element.Texture; set { Element.SetTexture(value); MarkLayoutDirty(); } }
        public Color Color { get => Element.Color; set => Element.Color = value; }
        public CanvasBlending.BlendModes BlendMode { get => Element.BlendMode; set => Element.SetBlendMode(value); } 

        public Image(CSTexture texture = default) {
            Element = new CanvasImage(texture, new RectF(0f, 0f, 1f, 1f));
        }
        public Image(Sprite? sprite) {
            if (sprite != null)
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
            Element.MarkLayoutDirty();
        }
        public override void Compose(ref CanvasCompositor.Context composer) {
            var layout = mLayoutCache;
            if (Element.Texture.IsValid) {
                if (AspectMode == AspectModes.PreserveAspectContain) {
                    Element.PreserveAspect(ref layout, ImageAnchor);
                }
            }
            Element.UpdateLayout(Canvas, layout);
            Element.Append(ref composer);
            base.Compose(ref composer);
        }
    }
    public class TextBlock : CanvasRenderable {
        public CanvasText TextElement;

        public string Text { get => TextElement.Text; set { if (TextElement.Text == value) return; TextElement.Text = value; MarkComposeDirty(); MarkTransformDirty(); } }
        public Color TextColor { get => TextElement.Color; set { TextElement.Color = value; MarkComposeDirty(); } }
        public float FontSize { get => TextElement.FontSize; set { TextElement.FontSize = value; MarkComposeDirty(); MarkTransformDirty(); } }
        public Font Font { get => TextElement.Font; set { TextElement.Font = value; MarkComposeDirty(); MarkTransformDirty(); } }
        public TextAlignment Alignment { get => TextElement.Alignment; set { TextElement.Alignment = value; MarkComposeDirty(); } }
        public TextDisplayParameters DisplayParameters { get => TextElement.DisplayParameters; set { TextElement.DisplayParameters = value; MarkComposeDirty(); } }

        public TextBlock(string text = "") {
            TextElement = new CanvasText(text);
        }
        public override void Initialise(CanvasBinding binding) {
            base.Initialise(binding);
            TextElement.Initialize(Canvas);
        }
        public override void Uninitialise(CanvasBinding binding) {
            TextElement.Dispose(Canvas);
            base.Uninitialise(binding);
        }
        protected override void NotifyTransformChanged() {
            base.NotifyTransformChanged();
            TextElement.MarkLayoutDirty();
        }
        public override void Compose(ref CanvasCompositor.Context composer) {
            var layout = mLayoutCache;
            TextElement.UpdateLayout(Canvas, layout);
            TextElement.Append(ref composer);
            base.Compose(ref composer);
        }
        public override Vector2 GetDesiredSize(SizingParameters sizing) {
            var width = TextElement.GetPreferredWidth();
            var height = TextElement.GetPreferredHeight(width);
            var size = new Vector2(width, height);
            size -= Transform.OffsetMax - Transform.OffsetMin;
            var anchorDelta = Transform.AnchorMax - Transform.AnchorMin;
            if (anchorDelta.X > 0f) size.X /= anchorDelta.X;
            if (anchorDelta.Y > 0f) size.Y /= anchorDelta.Y;
            return size;
        }
    }
    public abstract class Selectable : CanvasRenderable, ISelectable {
        protected bool selected;
        public bool IsSelected => selected;
        public virtual void OnSelected(ISelectionGroup group, bool _selected) { selected = _selected; }
        public void OnPointerDown(PointerEvent events) {
            if (events.GetIsButtonDown(0)) this.Select();
        }
        protected virtual void DrawSelectionFrame(ref CanvasCompositor.Context composer) {
            if (IsSelected) {
                ref var background = ref composer.CreateTransient<CanvasSelection>(Canvas);
                if (HasDirtyFlag(DirtyFlags.Layout)) background.MarkLayoutDirty();
                if (background.IsDirty) MarkComposeDirty();
                if (background.IsDirty) background.UpdateLayout(Canvas, mLayoutCache.Inset(-1));
                background.Append(ref composer);
            }
        }
        public override void Compose(ref CanvasCompositor.Context composer) {
            DrawSelectionFrame(ref composer);
            base.Compose(ref composer);
        }
    }
    public abstract class Button : Selectable
        , IPointerEnterHandler, IPointerExitHandler
        , IPointerDownHandler, IPointerUpHandler
        , IPointerClickHandler
        , ITweenable {

        public static readonly Color NormalColor = new Color(0xff888888);
        public static readonly Color SelectedColor = new Color(0xffaaaaaa);
        public static readonly Color HoverColor = new Color(0xffaaaaaa);
        public static readonly Color PressColor = new Color(0xff777777);
        public static readonly Color ClickColor = new Color(0xffffffff);

        public CanvasImage Background = new();

        [Flags]
        private enum States {
            None = 0,
            Hover = 1,
            Press = 2,
            Active = 4,
            Selected = 8,
        }
        private States state = States.None;

        public event Action? OnClick;

        public Button() {
            Background.SetSprite(Resources.TryLoadSprite("ButtonBG"));
            Background.SetBlendMode(CanvasBlending.BlendModes.Overlay);
            Background.Color = NormalColor;
        }

        public override void Initialise(CanvasBinding binding) {
            base.Initialise(binding);
            Background.Initialize(Canvas);
        }
        public override void Uninitialise(CanvasBinding binding) {
            Background.Dispose(Canvas);
            base.Uninitialise(binding);
        }
        protected override void NotifyTransformChanged() {
            base.NotifyTransformChanged();
            Background.MarkLayoutDirty();
        }
        public void DrawBackground(ref CanvasCompositor.Context composer) {
            if (Background.HasDirtyFlags)
                Background.UpdateLayout(Canvas, mLayoutCache);
            Background.Append(ref composer);
            if ((state & States.Hover) != 0 && (state & (States.Press | States.Active)) == 0) {
                ref var border = ref composer.CreateTransient<CanvasImage>(Canvas);
                border.SetSprite(Resources.TryLoadSprite("ButtonFrame"));
                if (HasDirtyFlag(DirtyFlags.Layout)) border.MarkLayoutDirty();
                border.UpdateLayout(Canvas, mLayoutCache.Inset(-2));
                border.Append(ref composer);
            }
        }
        public void OnPointerEnter(PointerEvent events) {
            SetState(States.Hover, true);
        }
        public void OnPointerExit(PointerEvent events) {
            SetState(States.Hover, false);
        }
        public new void OnPointerDown(PointerEvent events) {
            base.OnPointerDown(events);
            SetState(States.Press, true);
        }
        public void OnPointerUp(PointerEvent events) {
            SetState(States.Press, false);
        }
        public void OnPointerClick(PointerEvent events) {
            SetState(States.Active, true);
            InvokeClick();
        }
        public virtual void InvokeClick() {
            OnClick?.Invoke();
        }

        private bool SetState(States flag, bool enable) {
            if (enable == ((state & flag) != 0)) return false;
            if (enable) state |= flag; else state &= ~flag;
            if (Canvas != null) Canvas.Tweens.RegisterTweenable(this);
            return true;
        }

        public bool UpdateTween(Tween tween) {
            var delay = 0f;
            var hold = (state & States.Active) != 0 ? 0.02f : 0f;
            var rate = (state & States.Active) != 0 ? 0.01f : 0.1f;
            var ease = Easing.StatefulPowerInOut(rate, 2f).WithDelay(delay);
            Background.Color = Color.Lerp(Background.Color,
                (state & States.Active) != 0 ? ClickColor :
                (state & States.Press) != 0 ? PressColor :
                (state & States.Selected) != 0 ? SelectedColor :
                (state & States.Hover) != 0 ? HoverColor :
                NormalColor,
                ease.Evaluate(tween)
            );
            var tform = Transform;
            tform.Scale = Vector3.Lerp(tform.Scale,
                (state & States.Active) != 0 ? new Vector3(0.99f) :
                (state & States.Press) != 0 ? new Vector3(1.0f) :
                (state & States.Hover) != 0 ? new Vector3(1.01f) :
                new Vector3(1f),
                ease.Evaluate(tween)
            );
            Transform = tform;
            MarkComposeDirty();
            if (!ease.GetIsComplete(tween, hold)) return false;
            if (SetState(States.Active, false)) return false;
            return true;
        }
        public override void OnSelected(ISelectionGroup group, bool selected) {
            SetState(States.Selected, selected);
        }
    }
    public class TextButton : Button {
        public CanvasText TextElement = new();
        public string Text { get => TextElement.Text; set => TextElement.Text = value; }
        public float FontSize { get => TextElement.FontSize; set => TextElement.FontSize = value; }
        public Color TextColor { get => TextElement.Color; set => TextElement.Color = value; }
        public TextDisplayParameters TextDisplay { get => TextElement.DisplayParameters; set => TextElement.DisplayParameters = value; }
        public TextAlignment TextAlignment { get => TextElement.Alignment; set => TextElement.Alignment = value; }
        public Color BackgroundColor { get => Background.Color; set => Background.Color = value; }
        public bool EnableBackground { get => Background.EnableDraw; set => Background.EnableDraw = value; }

        public TextButton() : this("Button") { }
        public TextButton(string label) {
            TextElement.Text = label;
            TextElement.SetFont(Resources.LoadFont("./Assets/Roboto-Regular.ttf"));
        }
        public override void Initialise(CanvasBinding binding) {
            base.Initialise(binding);
            TextElement.Initialize(Canvas);
        }
        public override void Uninitialise(CanvasBinding binding) {
            TextElement.Dispose(Canvas);
            base.Uninitialise(binding);
        }
        public override void Compose(ref CanvasCompositor.Context composer) {
            DrawBackground(ref composer);
            if (HasDirtyFlag(DirtyFlags.Layout)) TextElement.MarkLayoutDirty();
            TextElement.UpdateLayout(Canvas, mLayoutCache);
            TextElement.Append(ref composer);
            base.Compose(ref composer);
        }
        public override Vector2 GetDesiredSize(SizingParameters sizing) {
            sizing.PreferredSize.X = sizing.ClampWidth(TextElement.GetPreferredWidth());
            sizing.PreferredSize.Y = sizing.ClampHeight(TextElement.GetPreferredHeight(sizing.PreferredSize.X) + 4);
            return base.GetDesiredSize(sizing);
        }
        public override string ToString() { return $"Button<{TextElement}>"; }
    }
    public class ImageButton : Button {
        public CanvasImage Icon;
        protected bool drawIcon = true;

        public ImageButton(CSTexture texture) {
            Icon = new CanvasImage(texture, new RectF(0f, 0f, 1f, 1f));
        }
        public ImageButton(Sprite sprite) {
            Icon = new CanvasImage(sprite.Texture, sprite.UVRect);
        }

        public override void Initialise(CanvasBinding binding) {
            base.Initialise(binding);
            Icon.Initialize(Canvas);
        }
        public override void Uninitialise(CanvasBinding binding) {
            Icon.Dispose(Canvas);
            base.Uninitialise(binding);
        }
        protected override void NotifyTransformChanged() {
            base.NotifyTransformChanged();
            Icon.MarkLayoutDirty();
        }
        public override void Compose(ref CanvasCompositor.Context composer) {
            DrawBackground(ref composer);

            if (drawIcon) {
                if (Icon.HasDirtyFlags) {
                    var imglayout = mLayoutCache;
                    Icon.PreserveAspect(ref imglayout, new Vector2(0.5f, 0.5f));
                    Icon.UpdateLayout(Canvas, imglayout);
                }
                Icon.Append(ref composer);
            }
            base.Compose(ref composer);
        }
    }
    public interface IBindableValue {
        void BindValue(PropertyPath path);
    }
    public class ToggleButton : ImageButton, IBindableValue {
        private bool state = true;
        public bool State {
            get => state;
            set => SetState(value);
        }
        public Action<bool>? OnStateChanged;
        public ToggleButton() : this(Resources.TryLoadSprite("Tick")!) { }
        public ToggleButton(CSTexture texture) : base(texture) { }
        public ToggleButton(Sprite sprite) : base(sprite) { }
        public override void InvokeClick() {
            base.InvokeClick();
            State = !State;
        }
        protected virtual void SetState(bool _state) {
            if (state == _state) return;
            state = _state;
            drawIcon = state;
            OnStateChanged?.Invoke(state);
            MarkComposeDirty();
        }
        public void BindValue(PropertyPath path) {
            State = path.GetValueAs<bool>();
            OnStateChanged = (newValue) => path.SetValueAs<bool>(newValue);
        }
        public void BindValue(object owner, FieldInfo field) {
            Debug.Assert(field.FieldType == typeof(bool));
            State = (bool)field.GetValue(owner)!;
            OnStateChanged = (newValue) => field.SetValue(owner, newValue);
        }
        public override Vector2 GetDesiredSize(SizingParameters sizing) {
            return new Vector2(20f, 20f);
        }
    }
    public class FixedGridLayout : CanvasRenderable {
        public Int2 CellCount = new Int2(4, 4);
        public Vector2 Spacing = new Vector2(10f, 10f);
        public override Vector2 GetDesiredSize(SizingParameters sizing) {
            if (mChildren.Count == 0) return Vector2.Zero;
            var childSizing = GetChildSizing(sizing);
            int xCount = Math.Max(1, (int)((sizing.PreferredSize.X + Spacing.X + 0.001f) / (childSizing.PreferredSize.X + Spacing.X)));
            int yCount = Math.Max(1, (mChildren.Count + xCount - 1) / xCount);
            return new Vector2(
                xCount * childSizing.PreferredSize.X + (xCount - 1) * Spacing.X,
                yCount * childSizing.PreferredSize.Y + (yCount - 1) * Spacing.Y
            );
        }
        private SizingParameters GetChildSizing(SizingParameters sizing) {
            var availableSize = sizing.PreferredSize;
            var childSizing = SizingParameters.Default;
            if (CellCount.X != 0) {
                childSizing.SetFixedXSize((availableSize.X - (CellCount.X - 1) * Spacing.X) / CellCount.X);
            }
            if (CellCount.Y != 0) {
                childSizing.SetFixedYSize((availableSize.Y - (CellCount.Y - 1) * Spacing.Y) / CellCount.Y);
            }
            if (CellCount.X == 0 || CellCount.Y == 0) {
                Vector2 maxSize = Vector2.Zero;
                foreach (var child in mChildren) {
                    maxSize = Vector2.Max(maxSize, child.GetDesiredSize(childSizing));
                }
                if (CellCount.X == 0) childSizing.PreferredSize.X = maxSize.X;
                if (CellCount.Y == 0) childSizing.PreferredSize.Y = maxSize.Y;
            }
            return childSizing;
        }
        public override void UpdateChildLayouts() {
            //base.UpdateChildLayouts();
            var layout = mLayoutCache;
            var childSizing = GetChildSizing(SizingParameters.Default.SetPreferredSize(layout.GetSize()));
            Int2 g = Int2.Zero;
            Vector2 offset = Vector2.Zero;
            float maxHeight = 0f;
            foreach (var child in mChildren) {
                var childSize = child.GetDesiredSize(childSizing);
                if (CellCount.X != 0) childSize.X = childSizing.PreferredSize.X;
                if (CellCount.Y != 0) childSize.Y = childSizing.PreferredSize.Y;
                maxHeight = MathF.Max(maxHeight, childSize.Y);
                layout.SetSize(childSize);
                layout.Position = mLayoutCache.TransformPosition2D(offset);
                child.UpdateLayout(layout);
                offset.X += childSize.X + Spacing.X;
                ++g.X;
                if (CellCount.X == 0 ? offset.X > mLayoutCache.GetWidth() : g.X >= CellCount.X) {
                    g.X = 0;
                    g.Y++;
                    offset.X = 0f;
                    offset.Y += maxHeight + Spacing.Y;
                    maxHeight = 0f;
                }
            }
        }
    }
    public class GridLayout : CanvasRenderable {
        public struct ElementCells {
            public Int2 CellBegin;
            public Int2 CellCount;
            public Int2 CellEnd => CellBegin + CellCount;
        }
        public struct CellSize {
            public float MinimumSize = 0f;
            public float PreferredSize = 80.0f;
            public float MaximumSize = float.MaxValue;
            public bool IsValid => PreferredSize != -1.0f;
            public CellSize(float size = 80.0f) { PreferredSize = size; }
            public static readonly CellSize Default = new(80.0f);
            public static readonly CellSize Invalid = new(-1.0f);
        }
        public int ColumnCount => columns.Length;
        public int RowCount => rows.Length;
        private ElementCells[] elementCells = Array.Empty<ElementCells>();
        private CellSize[] columns = new CellSize[] { CellSize.Default };
        private CellSize[] rows = new CellSize[] { CellSize.Default };

        private float[]? gridXs;
        private float[]? gridYs;

        public override void AppendChild(CanvasRenderable child) {
            var childId = Children.Count;
            AppendChild(child, new Int2(childId % ColumnCount, childId % RowCount), new Int2(1, 1));
        }
        public int AppendChild(CanvasRenderable child, Int2 cell) { return AppendChild(child, cell, Int2.One); }
        public int AppendChild(CanvasRenderable child, Int2 cell, Int2 span) {
            var childId = Children.Count;
            base.AppendChild(child);
            if (Children.Count > elementCells.Length)
                Array.Resize(ref elementCells, (int)BitOperations.RoundUpToPowerOf2((uint)Children.Count));
            elementCells[childId] = new ElementCells() { CellBegin = cell, CellCount = span, };
            return childId;
        }
        public override void RemoveChild(CanvasRenderable child) {
            base.RemoveChild(child);
        }
        public int GetChildIndex(CanvasRenderable child) {
            for (int i = 0; i < Children.Count; i++) if (Children[i] == child) return i;
            return -1;
        }
        public void SetChildCell(int childId, Int2 cell) { SetChildCell(childId, cell, Int2.One); }
        public void SetChildCell(int childId, Int2 cell, Int2 span) {
            var elCells = new ElementCells() { CellBegin = cell, CellCount = span, };
            elementCells[childId] = elCells;
        }

        public void SetColumnSize(int columnId, CellSize size) {
            SetSizeElement(ref columns, columnId, size);
        }
        public void SetRowSize(int rowId, CellSize size) {
            SetSizeElement(ref rows, rowId, size);
        }
        public void SetSizeElement(ref CellSize[] array, int index, CellSize size) {
            if (index >= array.Length) {
                int oldCount = array.Length;
                Array.Resize(ref array, (int)BitOperations.RoundUpToPowerOf2((uint)index + 4));
                array.AsSpan(oldCount).Fill(CellSize.Invalid);
            }
            array[index] = size;
        }
        public override void UpdateChildLayouts() {
            if (gridXs == null) ComputeLayout();
            for (int c = 0; c < mChildren.Count; c++) {
                var child = mChildren[c];
                var cell = elementCells[c];
                var left = cell.CellBegin.X == 0 ? 0f : gridXs[cell.CellBegin.X - 1];
                var top = cell.CellBegin.Y == 0 ? 0f : gridYs[cell.CellBegin.Y - 1];
                var right = gridXs[cell.CellEnd.X - 1];
                var bot = gridYs[cell.CellEnd.Y - 1];
                var layout = mLayoutCache;
                layout.AxisX.W = (right - left);
                layout.AxisY.W = (bot - top);
                layout.Position = layout.TransformPosition2D(new Vector2(left, top));
                child.UpdateLayout(layout);
            }
        }
        protected override void NotifyTransformChanged() {
            gridXs = default;
            base.NotifyTransformChanged();
        }
        private void ComputeLayout() {
            var size = ComputeGridSize();
            using var tmpXSizes = new PooledArray<CellSize>(size.X);
            using var tmpYSizes = new PooledArray<CellSize>(size.Y);
            if (columns.Length < tmpXSizes.Count) SetSizeElement(ref columns, tmpXSizes.Count - 1, CellSize.Default);
            if (rows.Length < tmpYSizes.Count) SetSizeElement(ref rows, tmpYSizes.Count - 1, CellSize.Default);
            columns.AsSpan(0, tmpXSizes.Count).CopyTo(tmpXSizes);
            rows.AsSpan(0, tmpYSizes.Count).CopyTo(tmpYSizes);
            foreach (ref var item in tmpXSizes.AsSpan()) if (!item.IsValid) item = new();
            foreach (ref var item in tmpYSizes.AsSpan()) if (!item.IsValid) item = new();
            using var elements = PooledArray<ElementCells>.FromEnumerator(elementCells);
            elements.AsSpan().Sort((c1, c2) => c1.CellCount.X.CompareTo(c2.CellCount.X));
            for (int i = 0; i < elements.Count; i++) {
                var cell = elements[i];
                var child = Children[i];
                var spanSizeX = GetSumSizing(tmpXSizes, cell.CellBegin.X, cell.CellEnd.X);
                var spanSizeY = GetSumSizing(tmpYSizes, cell.CellBegin.Y, cell.CellEnd.Y);
                var desiredSize = child.GetDesiredSize(new SizingParameters() {
                    MinimumSize = new Vector2(spanSizeX.MinimumSize, spanSizeY.MinimumSize),
                    PreferredSize = new Vector2(spanSizeX.PreferredSize, spanSizeY.PreferredSize),
                    MaximumSize = new Vector2(spanSizeX.MaximumSize, spanSizeY.MaximumSize),
                });
                ApplySizing(tmpXSizes, cell.CellBegin.X, cell.CellEnd.X, spanSizeX, new CellSize() { MinimumSize = spanSizeX.MinimumSize, PreferredSize = desiredSize.X, MaximumSize = spanSizeX.MaximumSize, });
                ApplySizing(tmpYSizes, cell.CellBegin.Y, cell.CellEnd.Y, spanSizeY, new CellSize() { MinimumSize = spanSizeY.MinimumSize, PreferredSize = desiredSize.Y, MaximumSize = spanSizeY.MaximumSize, });
            }
            if (gridXs == null) {
                foreach (ref var item in tmpXSizes.AsSpan()) item.MaximumSize = Math.Min(item.MaximumSize, 10000);
                foreach (ref var item in tmpYSizes.AsSpan()) item.MaximumSize = Math.Min(item.MaximumSize, 10000);
                CellSize sumSizeX = default;
                CellSize sumSizeY = default;
                for (int i = 0; i < tmpXSizes.Count; i++) AddSizes(ref sumSizeX, tmpXSizes[i]);
                for (int i = 0; i < tmpYSizes.Count; i++) AddSizes(ref sumSizeY, tmpYSizes[i]);
                var levelX = GetSizingLevel(mLayoutCache.GetWidth(), sumSizeX);
                var levelY = GetSizingLevel(mLayoutCache.GetHeight(), sumSizeY);
                gridXs = new float[size.X];
                gridYs = new float[size.Y];
                for (int i = 0; i < tmpXSizes.Count; i++)
                    gridXs[i] = (i == 0 ? 0 : gridXs[i - 1]) + GetSizing(tmpXSizes[i], levelX);
                for (int i = 0; i < tmpYSizes.Count; i++)
                    gridYs[i] = (i == 0 ? 0 : gridYs[i - 1]) + GetSizing(tmpYSizes[i], levelY);
            }
        }

        private float GetSizingLevel(float width, CellSize sumSize) {
            return width < sumSize.MinimumSize ? width / sumSize.MinimumSize
                : width < sumSize.PreferredSize ? 1.0f + (width - sumSize.MinimumSize) / (sumSize.PreferredSize - sumSize.MinimumSize)
                : width < sumSize.MaximumSize ? 2.0f + (width - sumSize.PreferredSize) / (sumSize.MaximumSize - sumSize.PreferredSize)
                : 3.0f + width / sumSize.MaximumSize;
        }
        private float GetSizing(CellSize cellSize, float level) {
            return level <= 1f ? cellSize.MinimumSize * level
                : level <= 2f ? cellSize.MinimumSize + (cellSize.PreferredSize - cellSize.MinimumSize) * (level - 1f)
                : level <= 3f ? cellSize.PreferredSize + (cellSize.MaximumSize - cellSize.PreferredSize) * (level - 2f)
                : cellSize.MaximumSize * (level - 3f);
        }

        private void AddSizes(ref CellSize sum, CellSize s2) {
            sum.MinimumSize += s2.MinimumSize;
            sum.PreferredSize += s2.PreferredSize;
            sum.MaximumSize += s2.MaximumSize;
        }
        private CellSize GetSumSizing(PooledArray<CellSize> sizes, int begin, int end) {
            CellSize sum = default;
            for (int i = begin; i < end; i++) AddSizes(ref sum, sizes[i]);
            return sum;
        }
        private void ApplySizing(PooledArray<CellSize> sizes, int begin, int end, CellSize current, CellSize apply) {
            if (apply.MinimumSize > current.MinimumSize) {
                var ratio = current.MinimumSize == 0f ? 0f : apply.MinimumSize / current.MinimumSize;
                var bias = current.MinimumSize == 0f ? apply.MinimumSize / (end - begin) : 0f;
                for (int i = begin; i < end; i++) sizes[i].MinimumSize = sizes[i].MinimumSize * ratio + bias;
            }
            if (apply.PreferredSize > current.PreferredSize) {
                var ratio = current.PreferredSize == 0f ? 0f : apply.PreferredSize / current.PreferredSize;
                var bias = current.PreferredSize == 0f ? apply.PreferredSize / (end - begin) : 0f;
                for (int i = begin; i < end; i++) sizes[i].PreferredSize = sizes[i].PreferredSize * ratio + bias;
            }
            if (apply.MaximumSize < current.MaximumSize) {
                var ratio = apply.MaximumSize / current.MaximumSize;
                for (int i = begin; i < end; i++) sizes[i].MaximumSize = sizes[i].MaximumSize * ratio;
            }
        }

        private Int2 ComputeGridSize() {
            Int2 size = Int2.Zero;
            for (int i = 0; i < Children.Count; i++) {
                var el = elementCells[i];
                size = Int2.Max(size, el.CellEnd);
            }
            return size;
        }
    }
    public class ListLayout : CanvasRenderable, ICanvasLayout {
        public enum Axes : byte { Horizontal, Vertical, };
        public enum ScaleModes : byte { None, Clamp, StretchOrClamp, };
        public ScaleModes ScaleMode = ScaleModes.Clamp;
        public Axes Axis = Axes.Vertical;
        public float ItemSize = 0f;

        // Allow arbitrary insertion as public API for list
        public new void InsertChild(int index, CanvasRenderable child) {
            base.InsertChild(index, child);
        }

        public override void UpdateChildLayouts() {
            //base.UpdateChildLayouts();
            var layout = mLayoutCache;
            Vector2 sizeMasked = default;
            ref var sizeAxis = ref (Axis == Axes.Horizontal ? ref sizeMasked.X : ref sizeMasked.Y);
            ref var axisVec4 = ref (Axis == Axes.Horizontal ? ref layout.AxisX : ref layout.AxisY);
            float sizeScale = 1.0f;
            Span<float> itemSizes = ScaleMode == ScaleModes.None ? default : stackalloc float[mChildren.Count];
            float desiredSize = ItemSize * mChildren.Count;
            if (ItemSize == 0f) {
                var sizing = SizingParameters.Default;
                if (Axis == Axes.Horizontal) sizing.SetFixedYSize(layout.GetHeight());
                else if (Axis == Axes.Vertical) sizing.SetFixedXSize(layout.GetWidth());
                for (int c = 0; c < mChildren.Count; ++c) {
                    sizeMasked = mChildren[c].GetDesiredSize(sizing);
                    itemSizes[c] = sizeAxis;
                    desiredSize += sizeAxis;
                }
            }
            if (ScaleMode != ScaleModes.None) {
                sizeScale = axisVec4.W / desiredSize;
                if (ScaleMode == ScaleModes.Clamp) sizeScale = Math.Min(1f, sizeScale);
            }

            var axisVec = axisVec4.toxyz();
            for (int c = 0; c < mChildren.Count; ++c) {
                var child = mChildren[c]; 
                axisVec4.W = (ItemSize == 0f ? itemSizes[c] : ItemSize) * sizeScale;

                child.UpdateLayout(layout);
                layout.Position += axisVec * axisVec4.W;
            }
        }
        public override Vector2 GetDesiredSize(SizingParameters sizing) {
            if (ItemSize != 0f) {
                if (Axis == Axes.Horizontal) sizing.SetFixedXSize(ItemSize * Children.Count);
                else if (Axis == Axes.Vertical) sizing.SetFixedYSize(ItemSize * Children.Count);
            }
            //sizing.PreferredSize = new Vector2(80f, 80f);
            float minSizeO = 0f;
            float sizeA = 0f;
            var childSizing = sizing;
            if (ItemSize == 0f) {
                if (Axis == Axes.Horizontal) childSizing.PreferredSize.X = 20f;
                else if (Axis == Axes.Vertical) childSizing.PreferredSize.Y = 20f;
            }
            foreach (var child in Children) {
                var size = child.GetDesiredSize(childSizing);
                minSizeO = MathF.Max(minSizeO,
                    Axis == Axes.Horizontal ? size.Y : Axis == Axes.Vertical ? size.X : 0f);
                if (ItemSize == 0f) {
                    sizeA += Axis == Axes.Horizontal ? size.X : Axis == Axes.Vertical ? size.Y : 0f;
                }
            }
            if (ItemSize == 0f) {
                if (Axis == Axes.Horizontal) sizing.PreferredSize.X = sizing.ClampWidth(sizeA);
                else sizing.PreferredSize.Y = sizing.ClampHeight(sizeA);
            }
            // Set other axis
            if (Axis == Axes.Horizontal) sizing.PreferredSize.Y = minSizeO;
            else if (Axis == Axes.Vertical) sizing.PreferredSize.X = minSizeO;
            return base.GetDesiredSize(sizing);
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
    public class ScrollView : CanvasRenderable, ICanvasLayout, IBeginDragHandler, IDragHandler, IEndDragHandler, ITweenable, IHitTestGroup {
        protected enum Flags { None = 0, Dragging = 1, }
        public Vector2 ScrollMask = new Vector2(1f, 1f);
        public RectF Margins = new RectF(0f, 0f, 0f, 0f);
        protected Flags flags;
        private Material clipMaterial = new();

        protected bool HasFlag(Flags flag) { return (flags & flag) != 0; }
        protected void SetFlag(Flags flag) { flags |= flag; }
        protected void ClearFlag(Flags flag) { flags &= ~flag; }

        private Vector2 contentSize;
        public Vector2 ContentSize => contentSize;

        protected Vector2 velocity;
        protected Vector2 scroll;
        public Vector2 Scroll { get => scroll; set => SetScroll(value); }

        public void OnBeginDrag(PointerEvent events) {
            if (!events.GetIsButtonDown(1)) { events.Yield(); return; }
            SetFlag(Flags.Dragging);
        }
        public void OnDrag(PointerEvent events) {
            var delta = -(events.CurrentPosition - events.PreviousPosition) * ScrollMask;
            Scroll += delta;
            velocity = Vector2.Lerp(velocity, delta / Math.Max(0.0001f, events.System.DeltaTime), 0.5f);
        }
        public void OnEndDrag(PointerEvent events) {
            Canvas.Tweens.RegisterTweenable(this);
            ClearFlag(Flags.Dragging);
        }

        private void SetScroll(Vector2 value) {
            scroll = value;
            MarkChildrenDirty();
        }

        public override void UpdateChildLayouts() {
            var layout = mLayoutCache;
            layout.Position.toxy(layout.Position.toxy() - Scroll - Margins.Min);
            contentSize = default;
            var sizing = SizingParameters.Default.SetPreferredSize(layout.GetSize() - Margins.Size);
            foreach (var child in mChildren) {
                contentSize = Vector2.Max(contentSize, child.GetDesiredSize(sizing));
            }
            layout.SetSize(contentSize);
            foreach (var child in mChildren) {
                child.UpdateLayout(layout);
            }
            var min = mLayoutCache.Position.toxy();
            var max = mLayoutCache.TransformPosition2DN(new Vector2(1f, 1f));
            clipMaterial.SetValue("CullRect", new Vector4(min.X, min.Y, max.X, max.Y));
        }
        public override void Compose(ref CanvasCompositor.Context composer) {
            using var clip = composer.PushClippingArea(mLayoutCache);
            using var mat = composer.PushMaterial(clipMaterial);
            base.Compose(ref composer);
        }

        public bool UpdateTween(Tween tween) {
            if (!HasFlag(Flags.Dragging)) {
                var scrollMin = Vector2.Zero;
                var scrollMax = Vector2.Max(Vector2.Zero, contentSize + Margins.Size - mLayoutCache.GetSize());
                var velEase = Easing.PowerOut(0.5f, 2f);
                var velDelta = velocity * (velEase.Evaluate(tween.Time1) - velEase.Evaluate(tween.Time0)) * 0.25f;
                var newScroll = MoveClamped(Scroll, velDelta, scrollMin, scrollMax);

                var clampEase = Easing.StatefulPowerInOut(0.5f, 2f);
                var targetScroll = Vector2.Clamp(newScroll, scrollMin, scrollMax);
                Scroll = Vector2.Lerp(newScroll, targetScroll, clampEase.Evaluate(tween));
                return clampEase.GetIsComplete(tween);
            }
            return true;
        }
        private Vector2 MoveClamped(Vector2 scroll, Vector2 delta, Vector2 min, Vector2 max) {
            scroll.X = MoveClamped(scroll.X, delta.X, min.X, max.X);
            scroll.Y = MoveClamped(scroll.Y, delta.Y, min.Y, max.Y);
            return scroll;
        }
        private float MoveClamped(float from, float delta, float min, float max) {
            const float Power = 1.2f;
            var limit = delta < 0f ? from - min : max - from;
            if (limit < 0f) limit = 1.0f - MathF.Pow(1.0f - limit, Power);
            limit -= Math.Abs(delta);
            if (limit < 0f) limit = 1.0f - MathF.Pow(1.0f - limit, 1.0f / Power);
            return delta < 0f ? limit + min : max - limit;
        }
    }
    public class CloneView : CanvasRenderable {
        public Matrix4x4 CloneTransform = Matrix4x4.Identity;
        private Material transformMaterial = new();

        public override void Initialise(CanvasBinding binding) {
            //base.Initialise(binding);
            mBinding = binding;
        }
        public override void AppendChild(CanvasRenderable child) {
            //base.AppendChild(child);
            if (mChildren == null) mChildren = new();
            mChildren.Add(child);
        }
        public override void RemoveChild(CanvasRenderable child) {
            //base.RemoveChild(child);
            if (mChildren == null) return;
            mChildren.Remove(child);
        }
        public void RemoveFromParent() {
            if (Parent != null) Parent.RemoveChild(this);
        }
        public override void UpdateChildLayouts() {
            //base.UpdateChildLayouts();
        }
        public override Vector2 GetDesiredSize(SizingParameters sizing) {
            //return base.GetDesiredSize(sizing);
            return Vector2.Zero;
        }
        public override void Compose(ref CanvasCompositor.Context composer) {
            transformMaterial.SetValue("Model", CloneTransform);
            //using var tform = composer.PushTransformer(CloneTransform);
            using var usemat = composer.PushMaterial(transformMaterial);
            base.Compose(ref composer);
        }
    }
}
