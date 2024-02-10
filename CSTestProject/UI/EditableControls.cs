using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;

namespace Weesals.UI {
    public class TextField : TextBlock
        , IPointerDownHandler
        , IKeyPressHandler
        , ICharInputHandler
        , ISelectable
        , ITweenable
        {

        public CanvasImage Background = new() {
            Color = Color.White,
        };
        public CanvasImage Cursor = new() {
            Color = Color.White,
        };

        private int cursorIndex = -1;

        public bool IsEditing => cursorIndex >= 0;

        // Return null to return to the original string
        public Func<string, string?> OnValidateInput;
        public Action<string> OnTextChanged;

        public TextField() {
            TextElement.Color = Color.White;
            TextElement.DisplayParameters = TextDisplayParameters.Flat;
            TextElement.Alignment = TextAlignment.Left;
            Background.SetSprite(Resources.TryLoadSprite("TextBox"));
        }

        public override void Initialise(CanvasBinding binding) {
            base.Initialise(binding);
            Background.Initialize(Canvas);
            Cursor.Initialize(Canvas);
        }
        public override void Uninitialise(CanvasBinding binding) {
            Background.Dispose(Canvas);
            Cursor.Dispose(Canvas);
            base.Uninitialise(binding);
        }
        protected override void NotifyTransformChanged() {
            base.NotifyTransformChanged();
            Background.MarkLayoutDirty();
            Cursor.MarkLayoutDirty();
        }
        public override void Compose(ref CanvasCompositor.Context composer) {
            var layout = mLayoutCache;
            var textLayout = layout.Inset(4f);
            TextElement.UpdateLayout(Canvas, textLayout);
            Background.UpdateLayout(Canvas, layout);
            Background.Append(ref composer);
            if (cursorIndex >= 0) {
                var cursorLayout = textLayout;
                cursorLayout.SetWidth(2f);
                cursorLayout.Position = textLayout.TransformPosition2D(
                    GetCursorPosition(cursorIndex));
                Cursor.UpdateLayout(Canvas, cursorLayout);
                Cursor.Append(ref composer);
            }
            base.Compose(ref composer);
        }
        public override Vector2 GetDesiredSize(SizingParameters sizing) {
            return base.GetDesiredSize(sizing) + new Vector2(8f, 8f);
        }

        private void SetCursorIndex(int index) {
            if (cursorIndex == index) return;
            cursorIndex = index;
            Cursor.MarkLayoutDirty();
            MarkComposeDirty();
            if (IsEditing) Canvas.Tweens.RegisterTweenable(this);
        }
        private Vector2 GetCursorPosition(int cursorIndex) {
            if (cursorIndex < TextElement.ComputedGlyphCount) return TextElement.GetComputedGlyphRect(cursorIndex).Min;
            return TextElement.GetComputedGlyphRect(TextElement.ComputedGlyphCount - 1).Lerp(new Vector2(1f, 0f));
        }

        public void OnPointerDown(PointerEvent events) {
            events.System.SelectionManager.SetSelected(this);
            var localPos = mLayoutCache.InverseTransformPosition2D(events.CurrentPosition);
            float nearestDst2 = float.MaxValue;
            int nearest = -1;
            for (int i = 0; i <= TextElement.ComputedGlyphCount; i++) {
                var pos = GetCursorPosition(i);
                var dst2 = (localPos - pos).LengthSquared();
                if (dst2 >= nearestDst2) continue;
                nearest = i;
                nearestDst2 = dst2;
            }
            SetCursorIndex(nearest);
        }
        public void OnSelected(ISelectionGroup group, bool selected) {
            if (IsEditing != selected) {
                SetCursorIndex(selected ? 0 : -1);
                if (group is SelectionManager manager && manager.EventSystem != null) {
                    if (selected)
                        manager.EventSystem.KeyboardFilter.Insert(0, this);
                    else
                        manager.EventSystem.KeyboardFilter.Remove(0, this);
                }
            }
        }

        public void OnKeyPress(ref KeyEvent key) {
            if (cursorIndex >= 0) {
                var text = Text;
                if (key.Key == KeyCode.Backspace) {
                    if (cursorIndex > 0) {
                        text = text.Remove(cursorIndex - 1, 1);
                        SetCursorIndex(Math.Max(cursorIndex - 1, 0));
                    }
                } else if (key.Key == KeyCode.Delete) {
                    if (cursorIndex < text.Length) {
                        text = text.Remove(cursorIndex, 1);
                        SetCursorIndex(Math.Max(cursorIndex, 0));
                    }
                } else if (key.Key == KeyCode.LeftArrow) {
                    SetCursorIndex(Math.Max(cursorIndex - 1, 0));
                } else if (key.Key == KeyCode.RightArrow) {
                    SetCursorIndex(Math.Min(cursorIndex + 1, text.Length));
                } else if ((int)key.Key < 128 && (char.IsLetterOrDigit((char)key.Key) || char.IsWhiteSpace((char)key.Key) || char.IsSymbol((char)key.Key))) {
                    //var chr = (char)key.Key;
                    //if (!key.Shift) chr = char.ToLowerInvariant(chr);
                    //text = text.Insert(cursorIndex, "" + chr);
                    //SetCursorIndex(Math.Min(cursorIndex + 1, text.Length));
                }
                if (TrySetText(text)) {
                    key.Consume();
                }
            }
        }
        public void OnCharInput(ref CharInputEvent chars) {
            if (!IsEditing) SetCursorIndex(Text.Length);
            var text = Text;
            text = text.Insert(cursorIndex, chars);
            if (TrySetText(text)) {
                SetCursorIndex(Math.Min(cursorIndex + chars.Length, text.Length));
                chars.Consume();
            }
        }

        private bool TrySetText(string? text) {
            if (text == Text) return true;
            if (OnValidateInput != null) text = OnValidateInput(text);
            if (text == null) return false;
            Text = text;
            if (OnTextChanged != null) OnTextChanged(Text);
            return true;
        }

        public bool UpdateTween(Tween tween) {
            var timeN = (tween.Time % 1.0f);
            var newColor = timeN < 0.5f ? Color.White : Color.Clear;
            if (Cursor.Color != newColor) {
                Cursor.Color = newColor;
                MarkComposeDirty();
            }
            return false;
        }
    }
}
