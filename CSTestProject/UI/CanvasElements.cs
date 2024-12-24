using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.Geometry;
using Weesals.Utility;

namespace Weesals.UI {
    public struct CanvasElement {
        public int ElementId { get; private set; }
        public Material Material { get; private set; }
        public bool IsValid => ElementId != -1;
        public void SetMaterial(Material mat) { Material = mat; }
		public void SetElementId(int id) {
			Debug.Assert(ElementId == id || ElementId == -1, "Element already has an ElementId");
			ElementId = id;
		}
        public void Dispose(Canvas canvas) {
			if (ElementId != -1) {
                canvas.Builder.Deallocate(ElementId);
				ElementId = -1;
			}
        }
		public static readonly CanvasElement Invalid = new CanvasElement() { ElementId = -1, };
    }
    public struct CanvasBlending {
        private Color color;

        public enum BlendModes : byte { Tint, Screen, Overlay };
        public BlendModes BlendMode { get; private set; }
        public Color Color { get => color; set { color = value; } }
        public SColor GetUnifiedBlendColor() {
            SColor unified = new SColor(127, 127, 127, 127);
            switch (BlendMode) {
                case BlendModes.Tint: unified = Color; break;
                case BlendModes.Screen: unified = new SColor(
                    (sbyte)Math.Min(0, -128 + Color.R * 128 / 255),
                    (sbyte)Math.Min(0, -128 + Color.G * 128 / 255),
                    (sbyte)Math.Min(0, -128 + Color.B * 128 / 255),
                    (sbyte)Math.Max(0, Color.A * 127 / 255)
                    ); break;
                case BlendModes.Overlay: unified = new SColor(
                    (sbyte)(Color.R),
                    (sbyte)(Color.G),
                    (sbyte)(Color.B),
                    (sbyte)Math.Max(0, Color.A * 127 / 255)
                    ); break;
            }
            return unified;
        }
        public void SetUnifiedBlendColor(SColor unified) {
            switch (BlendMode) {
                case BlendModes.Tint: Color = unified; break;
                case BlendModes.Screen: Color = new Color(
                    (byte)((unified.R + 128) * 255 / 128),
                    (byte)((unified.G + 128) * 255 / 128),
                    (byte)((unified.B + 128) * 255 / 128),
                    (byte)(unified.A * 255 / 127)
                ); break;
                case BlendModes.Overlay: Color = new Color(
                    (byte)unified.R,
                    (byte)unified.G,
                    (byte)unified.B,
                    (byte)(unified.A * 255 / 127)
                ); break;
            }
        }
        public void SetBlendMode(BlendModes blendMode) {
            var unified = GetUnifiedBlendColor();
            BlendMode = blendMode;
            SetUnifiedBlendColor(unified);
        }
        public static readonly CanvasBlending Default = new CanvasBlending() { color = Color.White, BlendMode = BlendModes.Tint, };
    }

    public interface ICanvasElement {
        bool HasDirtyFlags { get => false; }
        void Initialize(Canvas canvas);
        void Dispose(Canvas canvas);
        void MarkLayoutDirty() { }
        void UpdateLayout(Canvas canvas, in CanvasLayout layout);
        void Append(ref CanvasCompositor.Context compositor);
    }
    public interface ICanvasTransient {
        void Initialize(Canvas canvas);
        void Dispose(Canvas canvas);
        void Reset(Canvas canvas);
    }

    public struct CanvasImage : ICanvasElement, ICanvasTransient {
        CanvasElement element = CanvasElement.Invalid;

        public enum DirtyFlags : byte { None = 0, UV = 1, Color = 2, Position = 4, Indices = 8, All = 0x0f, };
        public enum DrawFlags : byte { None = 0, Never = 1, NinePatch = 2, };

        private CanvasBlending blending;
        private RectF uvrect;
        private RectF border = new RectF(0f, 0f, 0f, 0f);
        private RectF margins = new RectF(0f, 0f, 0f, 0f);
        private float spriteScale = 1.0f;
        private DirtyFlags dirty = DirtyFlags.None;
        private DrawFlags drawFlags = DrawFlags.None;

        public CSBufferReference Texture { get; private set; }
        public RectF UVRect { get => uvrect; set { if (uvrect == value) return; uvrect = value; dirty |= DirtyFlags.UV; } }
        public RectF Border { get => border; set { if (border == value) return; border = value; dirty |= DirtyFlags.UV; } }
        public Color Color { get => blending.Color; set { if (blending.Color == value) return; blending.Color = value; dirty |= DirtyFlags.Color; } }
        public bool IsNinePatch => (drawFlags & DrawFlags.NinePatch) != 0;
        public bool IsInitialized => element.IsValid;
        public bool HasDirtyFlags => dirty != DirtyFlags.None;
        public bool EnableDraw { get => (drawFlags & DrawFlags.Never) == 0; set { if (value) drawFlags &= ~DrawFlags.Never; else drawFlags |= DrawFlags.Never; } }
        public CanvasBlending.BlendModes BlendMode { get => blending.BlendMode; }
        public CanvasImage() : this(default, new RectF(0f, 0f, 1f, 1f)) { }
        public CanvasImage(Sprite? sprite) : this(default, new RectF(0f, 0f, 1f, 1f)) {
            if (sprite != null) SetSprite(sprite);
        }
        public CanvasImage(CSBufferReference texture, RectF uvrect) {
            Texture = texture;
            UVRect = uvrect;
            blending = CanvasBlending.Default;
        }
        unsafe public void Initialize(Canvas canvas) {
            Debug.Assert(!element.IsValid);
            dirty = DirtyFlags.Indices;
            SetTexture(Texture);
        }
        public void Dispose(Canvas canvas) {
            element.Dispose(canvas);
            dirty |= DirtyFlags.Indices;
        }
        public void Reset(Canvas canvas) {
            Texture = default;
            blending = CanvasBlending.Default;
            border = default;
            margins = default;
            uvrect = new(0f, 0f, 1f, 1f);
        }
        public void SetTexture(CSBufferReference texture) {
            Texture = texture;
            if (Texture.IsValid || element.Material != null) {
                RequireMaterial().SetValue("Texture", Texture);
                MarkLayoutDirty();
            }
        }
        public void SetSprite(Sprite? sprite) {
            SetTexture(sprite?.Texture ?? default);
            var wasDirty = dirty & DirtyFlags.UV;
            var posHash = margins.GetHashCode() + spriteScale.GetHashCode();
            var uvHash = UVRect.GetHashCode() + Border.GetHashCode();
            UVRect = sprite?.UVRect ?? new RectF(0f, 0f, 1f, 1f);
            Border = sprite?.Borders ?? new RectF(0f, 0f, 0f, 0f);
            margins = sprite?.Margins ?? default;
            spriteScale = sprite?.Scale ?? 1.0f;
            bool wasNine = IsNinePatch;
            if (Border != new RectF(0f, 0f, 1f, 1f)) drawFlags |= DrawFlags.NinePatch;
            else drawFlags &= ~DrawFlags.NinePatch;
            if (wasNine != IsNinePatch) dirty |= DirtyFlags.Indices;
            if (uvHash == UVRect.GetHashCode() + Border.GetHashCode()) {
                dirty = (dirty & ~DirtyFlags.UV) | wasDirty;
            }
            if (posHash != margins.GetHashCode() + spriteScale.GetHashCode()) {
                dirty |= DirtyFlags.Position;
            }
        }
        public void SetBlendMode(CanvasBlending.BlendModes mode) {
            blending.SetBlendMode(mode);
        }

        private void UpdateIndices(Canvas canvas) {
            var buffers = canvas.Builder.MapVertices(element.ElementId);
            Span<uint> indices = IsNinePatch
                ? stackalloc uint[] {
                    0, 1, 4, 1, 5, 4, 1, 2, 5, 2, 6, 5, 2, 3, 6, 3, 7, 6,
                    4, 5, 8, 5, 9, 8, 5, 6, 9, 6, 10, 9, 6, 7, 10, 7, 11, 10,
                    8, 9, 12, 9, 13, 12, 9, 10, 13, 10, 14, 13, 10, 11, 14, 11, 15, 14,
                }
                : stackalloc uint[] { 0, 1, 2, 1, 3, 2, };
            var outIndices = buffers.GetIndices();
            for (int i = 0; i < indices.Length; ++i) {
                outIndices[i] = (uint)(buffers.VertexOffset + indices[i]);
            }
            buffers.MarkIndicesChanged();
        }
        public void MarkLayoutDirty() {
            dirty |= DirtyFlags.Position;
        }
        public void UpdateLayout(Canvas canvas, in CanvasLayout layout) {
            if (dirty == 0) return;
            var elementId = element.ElementId;
            if (UnmarkDirty(DirtyFlags.Indices)) {
                if (IsNinePatch
                    ? canvas.Builder.Require(ref elementId, 16, 9 * 6)
                    : canvas.Builder.Require(ref elementId, 4, 6)) {
                    element.SetElementId(elementId);
                    dirty |= DirtyFlags.All & ~DirtyFlags.Indices;
                }
                Debug.Assert(elementId >= 0);
                Debug.Assert(element.ElementId >= 0);
                UpdateIndices(canvas);
            }
            Debug.Assert(elementId >= 0);

            var buffers = canvas.Builder.MapVertices(elementId);
            if (UnmarkDirty(DirtyFlags.Color)) {
                buffers.GetColors().Set(blending.GetUnifiedBlendColor());
                buffers.MarkVerticesChanged();
            }
            if (UnmarkDirty(DirtyFlags.UV)) {
                var texcoords = buffers.GetTexCoords();
                Span<Vector2> corners = IsNinePatch
                    ? stackalloc Vector2[] { UVRect.Min, UVRect.Lerp(Border.Min), UVRect.Lerp(Border.Max), UVRect.Max, }
                    : stackalloc Vector2[] { UVRect.Min, UVRect.Max, };
                for (int y = 0; y < corners.Length; ++y) {
                    int yI = y * corners.Length;
                    for (int x = 0; x < corners.Length; ++x) texcoords[yI + x] = new Vector2(corners[x].X, corners[y].Y);
                }
                buffers.MarkVerticesChanged();
            }
            if (UnmarkDirty(DirtyFlags.Position)) {
                Span<Vector2> p = IsNinePatch
                    ? stackalloc Vector2[] { new Vector2(0, 0), Vector2.Zero, Vector2.Zero, layout.GetSize(), }
                    : stackalloc Vector2[] { new Vector2(0, 0), layout.GetSize(), };
                if (IsNinePatch && Texture.IsValid) {
                    var spriteSize = (Vector2)Texture.GetTextureResolution() * UVRect.Size * spriteScale;
                    p[0] += margins.Min * spriteSize;
                    p[3] += margins.Max * spriteSize;
                    p[1] = p[0] + Border.Min * spriteSize;
                    p[2] = p[3] - (Vector2.One - Border.Max) * spriteSize;
                } else {
                    p[0] += margins.Min * p[1];
                    p[1] += margins.Max * p[1];
                }
                var positions = buffers.GetPositions();
                for (int y = 0; y < p.Length; ++y) {
                    int yI = y * p.Length;
                    for (int x = 0; x < p.Length; ++x) {
                        positions[yI + x] = layout.TransformPosition2D(new Vector2(p[x].X, p[y].Y));
                    }
                }
                buffers.MarkVerticesChanged();
            }
	    }
        private bool UnmarkDirty(DirtyFlags flag) {
            if ((dirty & flag) == 0) return false;
            dirty &= ~flag;
            return true;
        }

        public void Append(ref CanvasCompositor.Context compositor) {
            if ((drawFlags & DrawFlags.Never) != 0) return;
            compositor.Append(element);
        }

        public void PreserveAspect(ref CanvasLayout layout, Vector2 imageAnchor) {
            if (!Texture.IsValid) return;
            var size = layout.GetSize();
            var imgSize = (Vector2)Texture.GetTextureResolution() * UVRect.Size;
            var ratio = new Vector2(size.X * imgSize.Y, size.Y * imgSize.X);
            if (ratio.X != ratio.Y) {
                var osize = size;
                if (ratio.X > ratio.Y) {
                    size.X = ratio.Y / imgSize.Y;
                    layout.Position += layout.AxisX.toxyz() * ((osize.X - size.X) * imageAnchor.X);
                } else {
                    size.Y = ratio.X / imgSize.X;
                    layout.Position += layout.AxisY.toxyz() * ((osize.Y - size.Y) * imageAnchor.Y);
                }
                layout.SetSize(size);
            }
        }
        public Material RequireMaterial() {
            if (element.Material == null) element.SetMaterial(new Material());
            return element.Material!;
        }
    }
    public enum TextAlignment { Left, Centre, Right, };
    public class TextDisplayParameters {
        public Vector4 FaceColor;
        public float FaceDilate;
        public Vector4 OutlineColor;
        public float OutlineWidth;
        public float OutlineSoftness;
        public Vector4 UnderlayColor;
        public Vector2 UnderlayOffset;
        public float UnderlayDilate;
        public float UnderlaySoftness;
        public Vector4 GetDilation() {
            var dilateFactor = 256.0f / 20.0f;
            var fixedDilate = FaceDilate / 2.0f * dilateFactor;
            var oldilate = (OutlineWidth / 2.0f + OutlineSoftness / 2.0f) * dilateFactor;
            var uldilate = (UnderlayDilate + UnderlaySoftness / 2.0f) * dilateFactor;
            return new Vector4(
                fixedDilate + Math.Max(oldilate, uldilate - UnderlayOffset.X),
                fixedDilate + Math.Max(oldilate, uldilate - UnderlayOffset.Y),
                fixedDilate + Math.Max(oldilate, uldilate + UnderlayOffset.X),
                fixedDilate + Math.Max(oldilate, uldilate + UnderlayOffset.Y)
            );
        }
        public static TextDisplayParameters Flat = new() { FaceColor = Vector4.One, };
        public static TextDisplayParameters Header = new() { FaceColor = Vector4.One, OutlineColor = new Vector4(1f, 1f, 1f, 1f), OutlineWidth = 0.15f, OutlineSoftness = 0.2f, FaceDilate = 0.15f, };
        public static TextDisplayParameters Default = new() { FaceColor = Vector4.One, OutlineColor = new Vector4(0f, 0f, 0f, 0.3f), OutlineWidth = 0.1f, FaceDilate = 0.1f, };
        public static TextDisplayParameters Shadowed = new() {
            FaceColor = Vector4.One,
            FaceDilate = 0.2f,
            OutlineColor = new Vector4(0f, 0f, 0f, 0.5f),
            OutlineWidth = 0.2f,
            UnderlayColor = new Vector4(0f, 0f, 0f, 0.8f),
            UnderlayOffset = new Vector2(2.0f, 1.0f),
            UnderlaySoftness = 0.3f,
        };
    }
    public struct CanvasText : ICanvasElement, ICanvasTransient {
        CanvasElement element = CanvasElement.Invalid;

        public enum DirtyFlags : byte { None = 0, Layout = 1, Composite = 2, All = 0x0f, };

        public struct GlyphStyle {
            public float mFontSize;
            public Color mColor;
            public GlyphStyle(float size, Color color) { mFontSize = size; mColor = color; }
            public static readonly GlyphStyle Default = new GlyphStyle(16, Color.White);
        };
		public struct GlyphPlacement {
            public ushort mGlyphId;
            public short mStyleId;
            public float mAdvance;
        }
        public struct GlyphLayout {
            public int mVertexOffset;
            public Vector2 mLocalPosition;
        };

        string text = "";

        Font font;
        DirtyFlags dirty;
        GlyphStyle defaultStyle = GlyphStyle.Default;
        Vector2 anchor = new Vector2(0.5f, 0.5f);
        PooledList<GlyphStyle> styles;
        PooledList<GlyphPlacement> glyphPlacements;
        PooledList<GlyphLayout> glyphLayout;
        TextDisplayParameters displayParameters;

        public int ComputedGlyphCount => glyphLayout.Count;

        public string Text { get => text; set { if (text != value) SetText(value); } }
        public Color Color { get => defaultStyle.mColor; set { if (defaultStyle.mColor == value) return; defaultStyle.mColor = value; dirty |= DirtyFlags.Composite; ; } }
        public float FontSize { get => defaultStyle.mFontSize; set { if (defaultStyle.mFontSize == value) return; defaultStyle.mFontSize = value; dirty |= DirtyFlags.All; } }
        public Font Font { get => font; set { if (font != value) SetFont(value); } }
        public Vector2 Anchor { get => anchor; set { if (anchor == value) return; anchor = value; dirty |= DirtyFlags.Composite; } }
        public TextDisplayParameters DisplayParameters { get => displayParameters; set { displayParameters = value; dirty |= DirtyFlags.Composite; if (element.Material != null) UpdateMaterialProperties(); } }
        public bool HasDirtyFlags => dirty != DirtyFlags.None;

        public TextAlignment Alignment {
            get => anchor.X < 0.25f ? TextAlignment.Left : anchor.X > 0.75f ? TextAlignment.Right : TextAlignment.Centre;
            set => anchor.X = value switch { TextAlignment.Left => 0f, TextAlignment.Centre => 0.5f, TextAlignment.Right => 1f, _ => throw new NotImplementedException() };
        }

        public CanvasText() : this("") { }
        public CanvasText(string txt) {
            text = txt;
            styles = new();
            glyphPlacements = new();
            glyphLayout = new();
        }
        public void Initialize(Canvas canvas) {
            if (font == null) Font = canvas.DefaultFont;
        }
        public void Dispose(Canvas canvas) {
            styles.Dispose();
            glyphPlacements.Dispose();
            glyphLayout.Dispose();
            if (element.IsValid) element.Dispose(canvas);
            MarkLayoutDirty();
		}
        public void Reset(Canvas canvas) {
        }

        public void SetText(string value) {
            text = value;
            dirty |= DirtyFlags.All;
            glyphPlacements.Clear();
            glyphLayout.Clear();
        }
		public void SetFont(Font _font) {
            font = _font;
            dirty |= DirtyFlags.All;
            if (element.Material != null) UpdateMaterialProperties();
        }
        public void SetFont(Font _font, float fontSize) {
            SetFont(_font); FontSize = fontSize;
        }

        bool CompareConsume(string str, ref int c, string key) {
			if (string.Compare(str, c, key, 0, key.Length) != 0) return false;
			c += (int) key.Length;
			return true;
		}
		void UpdateGlyphPlacement() {
            if (font == null) return;
            float lineHeight = (float)font.LineHeight;
            glyphPlacements.Clear();
            styles.Clear();
            using PooledList<Color> colorStack = new();
            using PooledList<float> sizeStack = new();
            int activeStyle = -1;
            for (int c = 0; c < text.Length; ++c) {
                var chr = text[c];
                if (chr == '<') {
                    if (CompareConsume(text, ref c, "<color=")) {
                        for (; c < text.Length && char.IsWhiteSpace(text[c]); ++c) ;
                        CompareConsume(text, ref c, "0x");
                        CompareConsume(text, ref c, "#");
                        uint color = 0;
                        int count = 0;
                        for (; c < text.Length && char.IsDigit(text[c]); ++c, ++count) {
                            color = (color << 4) | (uint)(
                                char.IsDigit(text[c]) ? text[c] - '0' :
                                (char.ToUpperInvariant(text[c]) - 'A') + 10
                            );
                        }
                        // Upscale to 32 bit
                        if (count <= 4) color = ((color & 0xf000) * 0x11000) | ((color & 0x0f00) * 0x1100) | ((color & 0x00f0) * 0x110) | ((color & 0x000f) * 0x11);
                        // If no alpha specified, force full alpha
                        if (count == 3 || count == 6) color |= 0xff000000;
                        colorStack.Add(new Color(color));
                        activeStyle = -2;
                        continue;
                    }
                    if (CompareConsume(text, ref c, "</color")) {
                        colorStack.RemoveAt(colorStack.Count - 1);
                        activeStyle = -2;
                        continue;
                    }
                    if (CompareConsume(text, ref c, "<size=")) {
                        int s = c;
                        for (; c < text.Length && char.IsNumber(text[c]); ++c) ;
                        if (text[c] == '.') {
                            for (++c; c < text.Length && char.IsNumber(text[c]); ++c) ;
                        }
                        sizeStack.Add(float.Parse(text.AsSpan().Slice(s, c - s)));
                        activeStyle = -2;
                        continue;
                    }
                    if (CompareConsume(text, ref c, "</size")) {
                        sizeStack.RemoveAt(sizeStack.Count - 1);
                        activeStyle = -2;
                        continue;
                    }
                }
                if (chr == '\n') {
                    glyphPlacements.Add(new GlyphPlacement {
                        mGlyphId = (ushort)font.GetGlyphId(' '),
                        mStyleId = (short)activeStyle,
                        mAdvance = float.MaxValue,
                    });
                    continue;
                }
                var glyphId = font.GetGlyphId(chr);
                var glyph = font.GetGlyph(glyphId);
                if (glyph.mGlyph != chr) continue;

                if (activeStyle == -2) {
                    var tstyle = defaultStyle;
                    if (colorStack.Count == 0) tstyle.mColor = colorStack[^1];
                    if (sizeStack.Count == 0) tstyle.mFontSize = sizeStack[^1];
                    activeStyle = (int)styles.IndexOf(tstyle);
                    if (activeStyle == -1) { activeStyle = styles.Count; styles.Add(tstyle); }
                }

                var style = GetStyle(activeStyle);
                var scale = style.mFontSize / lineHeight;
                float advance = glyph.mAdvance;
                if (c > 0) advance += font.GetKerning(text[c - 1], (char)glyph.mGlyph);
                advance *= scale;

                glyphPlacements.Add(new GlyphPlacement {
                    mGlyphId = (ushort)glyphId,
                    mStyleId = (short)activeStyle,
                    mAdvance = advance,
                });
            }
        }
        void UpdateGlyphLayout(in CanvasLayout layout) {
			float lineHeight = (float)font.LineHeight;
			glyphLayout.Clear();
            var pos = Vector2.Zero;
            var min = new Vector2(10000.0f);
			var max = Vector2.Zero;
			for (int c = 0; c < glyphPlacements.Count; ++c) {
				var placement = glyphPlacements[c];
                var glyph = font.GetGlyph(placement.mGlyphId);
				var style = GetStyle(placement.mStyleId);
				var scale = style.mFontSize / lineHeight;
				var glyphSize2 = new Vector2(glyph.mSize.X, lineHeight) * scale;

                if (pos.X + glyphSize2.X > layout.AxisX.W + 0.1f) {
					pos.X = 0;
					pos.Y += lineHeight * scale;
					if (pos.Y + glyphSize2.Y > layout.AxisY.W) break;
					if (pos.X + glyphSize2.X > layout.AxisX.W) break;
				}
				glyphLayout.Add(new GlyphLayout{
					mVertexOffset = -1,
					mLocalPosition = pos + glyphSize2 / 2.0f,
				});
                min = Vector2.Min(min, new Vector2(pos.X, pos.Y + (float)(glyph.mOffset.Y) * scale));
                max = Vector2.Max(max, new Vector2(pos.X + (glyphSize2.X), pos.Y + (float)(glyph.mOffset.Y + glyph.mSize.Y) * scale));
                pos.X += placement.mAdvance;
			}
            var sizeDelta = layout.GetSize() - (max - min);
            var offset = sizeDelta * anchor - min;
            for (int l = 0; l < glyphLayout.Count; ++l) {
                var tlayout = glyphLayout[l];
                tlayout.mLocalPosition += offset;
                glyphLayout[l] = tlayout;
            }
        }
        public float GetPreferredWidth() {
            if (glyphPlacements.Count == 0) UpdateGlyphPlacement();
            var posX = 0.0f;
            int c = 0;
            for (; c < glyphPlacements.Count - 1; ++c) {
				var placement = glyphPlacements[c];
                posX += placement.mAdvance;
            }
            float lineHeight = (float)font.LineHeight;
            for (; c < glyphPlacements.Count; ++c) {
                var placement = glyphPlacements[c];
                var glyph = font.GetGlyph(placement.mGlyphId);
                var style = GetStyle(placement.mStyleId);
                var scale = style.mFontSize / lineHeight;
                posX += Math.Max(placement.mAdvance, glyph.mSize.X * scale);
            }
            return posX;
        }
        public float GetPreferredHeight(float width = float.MaxValue) {
			return defaultStyle.mFontSize;
        }
        public RectF GetComputedGlyphRect(int index) {
            if (glyphLayout.Count == 0) return default;
            if (index < glyphLayout.Count) {
                var lineHeight = (float)font.LineHeight;
                var placement = glyphPlacements[index];
                var layout = glyphLayout[index];
                var glyph = font.GetGlyph(placement.mGlyphId);
                var style = GetStyle(placement.mStyleId);
                var scale = style.mFontSize / lineHeight;
                var rect = new RectF(layout.mLocalPosition, (Vector2)glyph.mSize * scale);
                rect.X -= rect.Width / 2.0f;
                rect.Y -= rect.Height / 2.0f;
                return rect;
            }
            return default;
        }
        private GlyphStyle GetStyle(int styleId) => styleId < 0 ? defaultStyle : styles[styleId];
        public void MarkLayoutDirty() {
            dirty = DirtyFlags.All;
        }
        public void FillVertexBuffers(in CanvasLayout layout, TypedBufferView<Vector3> positions, TypedBufferView<Vector2> uvs, TypedBufferView<SColor> colors) {
            var atlasTexelSize = 1.0f / font.Texture.GetSize().X;
            var lineHeight = (float)font.LineHeight;
            int vindex = 0;
            var dilate = (displayParameters ?? TextDisplayParameters.Default).GetDilation();
            for (int c = 0; c < glyphLayout.Count; ++c) {
                var gplacement = glyphPlacements[c];
                var glayout = glyphLayout[c];
                var glyph = font.GetGlyph(gplacement.mGlyphId);
                if (glyph.mGlyph == ' ') continue;
                var style = GetStyle(gplacement.mStyleId);
                var scale = style.mFontSize / lineHeight;
                glayout.mVertexOffset = vindex;
                var uv_1 = ((Vector2)glyph.mAtlasOffset - dilate.toxy()) * atlasTexelSize;
                var uv_2 = ((Vector2)(glyph.mAtlasOffset + glyph.mSize) + dilate.tozw()) * atlasTexelSize;
                var size2 = ((Vector2)glyph.mSize + (dilate.toxy() + dilate.tozw()));
                var glyphOffMin = (Vector2)glyph.mOffset - new Vector2(glyph.mSize.X, lineHeight) / 2.0f - dilate.toxy();
                var glyphPos0 = layout.TransformPosition2D(glayout.mLocalPosition + glyphOffMin * scale);
                var glyphDeltaX = layout.AxisX.toxyz() * (size2.X * scale);
                var glyphDeltaY = layout.AxisY.toxyz() * (size2.Y * scale);
                colors[vindex] = style.mColor;
                uvs[vindex] = new Vector2(uv_1.X, uv_1.Y);
                positions[vindex++] = glyphPos0;
                colors[vindex] = style.mColor;
                uvs[vindex] = new Vector2(uv_2.X, uv_1.Y);
                positions[vindex++] = glyphPos0 + glyphDeltaX;
                colors[vindex] = style.mColor;
                uvs[vindex] = new Vector2(uv_1.X, uv_2.Y);
                positions[vindex++] = glyphPos0 + glyphDeltaY;
                colors[vindex] = style.mColor;
                uvs[vindex] = new Vector2(uv_2.X, uv_2.Y);
                positions[vindex++] = glyphPos0 + glyphDeltaY + glyphDeltaX;
            }
            for (; vindex < positions.mCount; ++vindex) positions[vindex] = default;
        }
        public void FillIndexBuffers(TypedBufferView<uint> indices, int vertOffset) {
            for (int v = 0, i = 0; i < indices.mCount; i += 6, v += 4) {
                indices[i + 0] = (uint)(vertOffset + v + 0);
                indices[i + 1] = (uint)(vertOffset + v + 1);
                indices[i + 2] = (uint)(vertOffset + v + 2);
                indices[i + 3] = (uint)(vertOffset + v + 1);
                indices[i + 4] = (uint)(vertOffset + v + 3);
                indices[i + 5] = (uint)(vertOffset + v + 2);
            }
        }
        public RangeInt WriteToMesh(Mesh mesh, in CanvasLayout layout) {
            if (dirty != 0) {
                UpdateGlyphPlacement();
                UpdateGlyphLayout(layout);
            }
            int vcount = (int)glyphLayout.Count * 4;
            int icount = (int)glyphLayout.Count * 6;
            int vstart = mesh.VertexCount;
            var istart = mesh.IndexCount;
            mesh.SetVertexCount(vstart + vcount);
            mesh.SetIndexCount(istart + icount);
            FillVertexBuffers(layout,
                mesh.GetPositionsV().Slice(vstart),
                mesh.GetTexCoordsV().Slice(vstart),
                mesh.GetColorsV().Slice(vstart).Reinterpret<SColor>()
            );
            FillIndexBuffers(mesh.GetIndicesV().Slice(istart).Reinterpret<uint>(), vstart);
            return RangeInt.FromBeginEnd(istart, mesh.IndexCount);
        }
        public void UpdateLayout(Canvas canvas, in CanvasLayout layout) {
            if (dirty == 0) return;
            UpdateGlyphPlacement();
			UpdateGlyphLayout(layout);

            if (element.Material == null) {
                if (element.Material == null) element.SetMaterial(new Material("./Assets/text.hlsl"));
                UpdateMaterialProperties();
            }
            var elementId = element.ElementId;

			int vcount = (int)glyphLayout.Count * 4;
            int icount = (int)glyphLayout.Count * 6;
            var mBuilder = canvas.Builder;
            if (mBuilder.Require(ref elementId, vcount, icount)) {
                element.SetElementId(elementId);
                var tbuffers = mBuilder.MapVertices(elementId);
				var indices = tbuffers.GetIndices();
                FillIndexBuffers(indices, tbuffers.VertexOffset);
                tbuffers.MarkIndicesChanged();
            }
			var buffers = mBuilder.MapVertices(elementId);
            var positions = buffers.GetPositions();
            var uvs = buffers.GetTexCoords();
            var colors = buffers.GetColors();
            FillVertexBuffers(layout, positions, uvs, colors);
			buffers.MarkVerticesChanged();
            dirty = DirtyFlags.None;
		}

        private void UpdateMaterialProperties() {
            element.Material!.SetTexture("Texture", font.Texture);
            var textParams = displayParameters ?? TextDisplayParameters.Default;
            element.Material.SetValue("_FaceColor", textParams.FaceColor);
            element.Material.SetValue("_FaceDilate", textParams.FaceDilate);
            var enableOutline = textParams.OutlineColor.W > 0.0f;
            var enableUnderlay = textParams.UnderlayColor.W > 0.0f;
            if (enableOutline) {
                element.Material.SetValue("_OutlineColor", textParams.OutlineColor);
                element.Material.SetValue("_OutlineWidth", textParams.OutlineWidth);
                element.Material.SetMacro("OUTLINE_ON", "1");
            }
            if (enableUnderlay) {
                element.Material.SetValue("_UnderlayColor", textParams.UnderlayColor);
                element.Material.SetValue("_UnderlayOffset", textParams.UnderlayOffset / 256.0f);
                element.Material.SetValue("_UnderlaySoftness", textParams.UnderlaySoftness);
                element.Material.SetMacro("UNDERLAY_ON", "1");
            }
        }

        public void Append(ref CanvasCompositor.Context compositor) {
			compositor.Append(element);
        }
        public override string ToString() {
            return $"text<{Text}>";
        }
    }

    public struct CanvasSelection : ICanvasElement, ICanvasTransient {
        DateTime beginTime;
        CanvasImage frame = new CanvasImage();
        public bool IsDirty { get; private set; }
        public CanvasSelection() { }
        public void Initialize(Canvas canvas) {
            beginTime = DateTime.UtcNow;
            frame.Initialize(canvas);
            frame.SetSprite(Resources.TryLoadSprite("ButtonFrame"));
            IsDirty = true;
        }
        public void Dispose(Canvas canvas) {
            frame.Dispose(canvas);
            IsDirty = true;
        }
        public void Reset(Canvas canvas) {
        }
        public void MarkLayoutDirty() { frame.MarkLayoutDirty(); IsDirty = true; }
        public void UpdateLayout(Canvas canvas, in CanvasLayout layout) {
            if (!IsDirty) return;
            var tlayout = layout;
            var tsince = DateTime.UtcNow - beginTime;
            var scale = Easing.BubbleIn(0.3f).WithFromTo(0.5f, 1.0f).Evaluate((float)tsince.TotalSeconds);
            tlayout = tlayout.Scale(scale);
            frame.UpdateLayout(canvas, tlayout);
            if (tsince < TimeSpan.FromSeconds(0.3f)) MarkLayoutDirty();
            if (tsince > TimeSpan.FromSeconds(0.3f)) IsDirty = false;
        }
        public void Append(ref CanvasCompositor.Context compositor) {
            frame.Append(ref compositor);
        }
    }

    public struct CanvasCurve : ICanvasElement, ICanvasTransient {
        CanvasElement element = CanvasElement.Invalid;
        public bool IsInitialized => element.IsValid;
        public CanvasCurve() {
        }
        public void Initialize(Canvas canvas) {
            Debug.Assert(!element.IsValid);
        }
        public void Dispose(Canvas canvas) {
            if (element.IsValid) element.Dispose(canvas);
        }
        public void Reset(Canvas canvas) {
        }
        public void Update(Canvas canvas, Span<Vector3> points) {
            if (points.Length == 0) return;
            var elementId = element.ElementId;
            var vertices = element.IsValid ? canvas.Builder.MapVertices(elementId) : default;
            if (!element.IsValid || vertices.GetVertexCount() != points.Length * 2) {
                element.Dispose(canvas);
                canvas.Builder.Require(ref elementId, points.Length * 2, (points.Length - 1) * 6);
                element.SetElementId(elementId);
                vertices = canvas.Builder.MapVertices(elementId);
                var indices = vertices.GetIndices();
                for (int p = 0; p < points.Length - 1; p++) {
                    var i = p * 6;
                    indices[i + 0] = (ushort)(vertices.VertexOffset + p * 2 + 0);
                    indices[i + 1] = (ushort)(vertices.VertexOffset + p * 2 + 1);
                    indices[i + 2] = (ushort)(vertices.VertexOffset + p * 2 + 2);
                    indices[i + 3] = (ushort)(vertices.VertexOffset + p * 2 + 1);
                    indices[i + 4] = (ushort)(vertices.VertexOffset + p * 2 + 3);
                    indices[i + 5] = (ushort)(vertices.VertexOffset + p * 2 + 2);
                }
                vertices.MarkIndicesChanged();
                vertices.GetColors().Set(Color.White);
            }
            var vertPos = vertices.GetPositions();
            for (int i = 0; i < points.Length; i++) {
                vertPos[i * 2 + 0] = points[i] + new Vector3(0f, -1f, 0f);
                vertPos[i * 2 + 1] = points[i] + new Vector3(0f, +1f, 0f);
            }
            vertices.MarkVerticesChanged();
        }
        public void MarkLayoutDirty() { }
        public void UpdateLayout(Canvas canvas, in CanvasLayout layout) { }
        public void Append(ref CanvasCompositor.Context compositor) {
            compositor.Append(element);
        }
    }

    public class CanvasCompositor : IDisposable {
        public class TransientElementCache {
            public interface IElementList { void Reset(Canvas canvas); }
            public struct ItemContainer<T> where T : ICanvasTransient, new() {
                public T Item;
                public ulong Context;
                public override string ToString() => $"{Context}: {Item}";
            }
            public class ElementList<T> : ArrayList<ItemContainer<T>>, IElementList where T : ICanvasTransient, new() {
                public int Iterator;
                public int InitializedTo;
                public void Reset(Canvas canvas) {
                    for (int i = Iterator; i < Count; ++i) this[i].Item.Dispose(canvas);
                    InitializedTo = Iterator;
                    //this.RemoveRange(Iterator, Count - Iterator);
                    Iterator = 0;
                }
                public ref T RequireItem(LinkedListNode<Node> owner, Canvas canvas, Func<T>? creator) {
                    var context = (ulong)owner.GetHashCode();
                    if (creator != null) context += (ulong)creator.Method.GetHashCode();
                    if (Iterator >= Count) {
                        var item = creator != null ? creator() : new T();
                        item.Initialize(canvas);
                        Add(new ItemContainer<T>() { Item = item, Context = context, });
                    } else {
                        int i = Iterator;
                        int end = Math.Min(InitializedTo, Iterator + 5);
                        for (; i < end; ++i) {
                            if (this[i].Context == context) break;
                        }
                        if (i != end) {
                            if (i != Iterator) {
                                (this[Iterator], this[i]) = (this[i], this[Iterator]);
                            }
                        } else {
                            ref var item = ref this[Iterator];
                            item.Context = context;
                            item.Item.Dispose(canvas);
                            if (creator != null) item.Item = creator();
                            else item.Item.Reset(canvas);
                            item.Item.Initialize(canvas);
                        }
                    }
                    return ref this[Iterator++].Item;
                }
            }
            Dictionary<Type, IElementList> typedElements = new();
            public void Reset(Canvas canvas) {
                foreach (var item in typedElements.Values) item.Reset(canvas);
            }
            public ElementList<T> Require<T>() where T : ICanvasTransient, new() {
                if (!typedElements.TryGetValue(typeof(T), out var list)) {
                    list = new ElementList<T>();
                    typedElements.Add(typeof(T), list);
                }
                return (ElementList<T>)list;
            }
        }
        public struct Node {
            public object? mContext;
            public LinkedListNode<Node>? mParent;
		};
        public struct Item {
            public LinkedListNode<Node> mNode;
            public int mElementId;
            public int mBatchId;
		};
        public struct BatchElement {
            public RangeInt IndexRange;
            public RectF MeshBounds;
        }
        public struct Batch {
			public Material[] Materials;
            public int MaterialHash;
            public int mIndexCount;
            public RangeInt mIndexAlloc;
            public RangeInt ElementRange;
            public RectF MeshBounds;
            public int ZIndex;
            public RangeInt IndexRange => new RangeInt(mIndexAlloc.Start, mIndexCount);
        };
        public struct ClippingRect {
            public CanvasLayout Layout;
        }
        public struct ClippingRef : IDisposable {
            private readonly CanvasCompositor compositor;
            public ClippingRef(CanvasCompositor _compositor) { compositor = _compositor; }
            public void Dispose() { compositor.clippingRects.RemoveAt(compositor.clippingRects.Count - 1); }
        }
        public struct TransformerRef : IDisposable {
            private readonly CanvasCompositor compositor;
            public TransformerRef(CanvasCompositor _compositor) { compositor = _compositor; }
            public void Dispose() { compositor.transformers.RemoveAt(compositor.transformers.Count - 1); }
        }
        public struct MaterialRef : IDisposable {
            private readonly CanvasCompositor compositor;
            public MaterialRef(CanvasCompositor _compositor) { compositor = _compositor; }
            public void Dispose() { compositor.materialStack.RemoveAt(compositor.materialStack.Count - 1); compositor.RecomputeMaterialStackHash(); }
        }
        public struct ZIndexRef : IDisposable {
            private readonly CanvasCompositor compositor;
            private readonly int previousZIndex;
            public ZIndexRef(CanvasCompositor _compositor) { compositor = _compositor; previousZIndex = _compositor.zindex; }
            public void Dispose() { compositor.zindex = previousZIndex; }
        }
        LinkedList<Node> mNodes = new();
        LinkedList<Item> mItems = new();
        SparseArray<BatchElement> batchElements = new();
        ArrayList<Batch> mBatches = new();
        ArrayList<Matrix3x2> transformers = new();
        ArrayList<ClippingRect> clippingRects = new();
        ArrayList<Material> materialStack = new();
        int matStackHash = 0;
        int zindex = 0;
        BufferLayoutPersistent mIndices;
        SparseIndices mUnusedIndices = new();
        TransientElementCache transientCache = new();
        CanvasMeshBuffer mBuilder;
        public CanvasCompositor(CanvasMeshBuffer builder) {
			mBuilder = builder;
            mIndices = new BufferLayoutPersistent(BufferLayoutPersistent.Usages.Index);
            mIndices.AppendElement(new CSBufferElement("INDEX", BufferFormat.FORMAT_R32_UINT));
        }
        public void Dispose() {
            mIndices.Dispose();
        }
        public void Clear() {
            batchElements.Clear();
            mBatches.Clear();
            mIndices.Clear();
            mUnusedIndices.Clear();
        }
        public TransformerRef PushTransformer(Matrix3x2 transform) {
            transformers.Add(transform);
            return new TransformerRef(this);
        }
        public ClippingRef PushClippingRect(CanvasLayout layout) {
            clippingRects.Add(new ClippingRect() { Layout = layout, });
            return new ClippingRef(this);
        }
        public MaterialRef PushMaterial(Material material) {
            materialStack.Add(material);
            RecomputeMaterialStackHash();
            return new MaterialRef(this);
        }
        private ZIndexRef PushZIndex(int _zindex) {
            var zref = new ZIndexRef(this);
            zindex += _zindex;
            return zref;
        }
        private void RecomputeMaterialStackHash() {
            matStackHash = 0;
            foreach (var mat in materialStack) matStackHash = HashCode.Combine(mat.GetHashCode(), matStackHash);
        }

        public BufferLayoutPersistent GetIndices() { return mIndices; }
        private RectF ComputeElementBounds(int elementId) {
            var verts = mBuilder.MapVertices(elementId);
            var positions = verts.GetPositions2D();
            positions.AssertRequireReader();
            Vector2 boundsMin = positions[0];
            Vector2 boundsMax = boundsMin;
            for (int i = 1; i < positions.mCount; ++i) {
                var pos = positions[i];
                boundsMin = Vector2.Min(boundsMin, pos);
                boundsMax = Vector2.Max(boundsMax, pos);
            }
            verts.VertexBounds = new RectF(boundsMin, boundsMax - boundsMin);
            return verts.VertexBounds;
        }
        private bool GetOverlaps(Batch batch, int elementId, RectF elBounds) {
            var elements = batchElements.Slice(batch.ElementRange);
            for (int e = elements.Count - 1; e >= 0; --e) {
                var el = elements[e];
                if (el.IndexRange.Start == -1) {
                    if (!el.MeshBounds.Overlaps(elBounds))
                        e -= el.IndexRange.Length;
                    continue;
                }
                if (!el.MeshBounds.Overlaps(elBounds)) continue;
                var vertices = mBuilder.GetPositions<Vector2>();
                var bIndices = new TypedBufferView<uint>(mIndices.Elements[0], batch.IndexRange);
                var elIndices = mBuilder.MapIndices(elementId);
                for (int i = 0; i < el.IndexRange.Length; i += 3) {
                    int i1 = el.IndexRange.Start + i;
                    var t0v0 = vertices[bIndices[i1 + 0]];
                    var t0v1 = vertices[bIndices[i1 + 1]];
                    var t0v2 = vertices[bIndices[i1 + 2]];
                    for (int i2 = 0; i2 < elIndices.mCount; i2 += 3) {
                        var t1v0 = vertices[elIndices[i2 + 0]];
                        var t1v1 = vertices[elIndices[i2 + 1]];
                        var t1v2 = vertices[elIndices[i2 + 2]];
                        if (Triangulation.GetTrianglesOverlap(t0v0, t0v1, t0v2, t1v0, t1v1, t1v2)) return true;
                    }
                }
            }
            return false;
        }
        private int RequireBatchForElement(int elementId, Material material, RectF bounds, int icount) {
            const int MaxBatchSize = 15 * 1024 * 3;
            const int MaxOverlapSize = 5 * 1024 * 3;
            var matHash = (material?.GetHashCode() ?? 0);
            matHash += matStackHash;
            int lastZId = mBatches.Count - 1;
            for (; lastZId >= 0; --lastZId) if (mBatches[lastZId].ZIndex <= zindex) break;
            for (var batchId = lastZId; batchId >= 0; --batchId) {
                var batch = mBatches[batchId];
                if (batch.ZIndex != zindex) break;
                // If materials match, use this batch
                if (batch.MaterialHash == matHash) {
                    // Avoid huge batches (expensive to reallocate)
                    if (batch.mIndexCount <= Math.Max(MaxBatchSize, batch.mIndexAlloc.Length - icount)) return batchId;
                }
                // Cant merge with anything under batch 0
                if (batchId == 0) break;
                if (!batch.MeshBounds.Overlaps(bounds)) continue;
                // Dont do expensive overlaps test with large buffers
                if (batch.mIndexCount > MaxOverlapSize) break;
                if (GetOverlaps(batch, elementId, bounds)) break;
            }
            var materials = new Material[(material != null ? 1 : 0) + materialStack.Count];
            materialStack.CopyTo(materials);
            if (material != null) materials[^1] = material;
            return mBatches.Insert(lastZId + 1, new Batch {
                Materials = materials,
                MaterialHash = matHash,
                ZIndex = zindex,
                mIndexAlloc = default,
                MeshBounds = bounds,
            });
        }
        public void AppendElementData(int elementId, Material material) {
			var verts = mBuilder.MapVertices(elementId);
            var inds = verts.GetIndices();
            //Debug.Assert(inds.mCount > 0);

            // Determine valid batch to insert into
            var bounds = verts.VertexBounds;
            if (bounds.Width == -1f) bounds = ComputeElementBounds(elementId);
            int batchId = RequireBatchForElement(elementId, material, bounds, inds.mCount);
            if (clippingRects.Count > 0) {
                var top = clippingRects[^1];
                var localBounds = bounds;
                localBounds.X -= top.Layout.Position.X;
                localBounds.Y -= top.Layout.Position.Y;
                if (localBounds.Max.X <= 0 || localBounds.Max.Y <= 0 ||
                    localBounds.Min.X >= top.Layout.GetWidth() || localBounds.Min.Y >= top.Layout.GetHeight()) return;
            }

            ref var batch = ref mBatches[batchId];
            var newICount = batch.mIndexCount + inds.mCount;
            if (newICount > batch.mIndexAlloc.Length) {
                var oldRange = batch.mIndexAlloc;
                if (batch.mIndexAlloc.Length > 0 && batch.mIndexAlloc.End == mIndices.Count) {
                    // We can directly consume at the end of the array (mIndices will be resized below)
                    batch.mIndexAlloc.Length += inds.mCount;
                } else {
                    int reqCount = oldRange.Length + inds.mCount;
                    reqCount += Math.Max(reqCount / 2, 128);
                    // Try to use an existing block
                    batch.mIndexAlloc = new(mUnusedIndices.Take(reqCount), reqCount);
                    // Otherwise allocate a new block at the end of mIndices
                    if (batch.mIndexAlloc.Start == -1) {
                        batch.mIndexAlloc = new RangeInt(mIndices.Count, reqCount);
                    }
                }
                // Resize index buffer to contain index range
                if (batch.mIndexAlloc.End > mIndices.BufferLayout.mCount) {
                    mIndices.BufferLayout.mCount = batch.mIndexAlloc.End;
                    if (mIndices.BufferCapacityCount < mIndices.BufferLayout.mCount) {
                        int resize = Math.Max(mIndices.BufferCapacityCount + 2048, mIndices.BufferLayout.mCount);
                        mIndices.AllocResize(resize);
                    }
                }
                // If batch was moved, copy data
                if (batch.mIndexAlloc.Start != oldRange.Start && batch.mIndexCount > 0) {
                    mIndices.CopyRange(oldRange.Start, batch.mIndexAlloc.Start, batch.mIndexCount);
                    //mIndices.InvalidateRange(oldRange.Start, oldRange.Length);
                    mUnusedIndices.Add(ref oldRange);
                    mIndices.BufferLayout.mCount -= mUnusedIndices.Compact(mIndices.BufferLayout.mCount);
                }
            }
            // Write the index values
            var subRegionIndRange = new RangeInt(batch.mIndexCount, inds.mCount);
            var elIndexRange = new RangeInt(batch.mIndexAlloc.Start + batch.mIndexCount, inds.mCount);
            var elIndices = new TypedBufferView<uint>(mIndices.Elements[0], elIndexRange);
            inds.CopyTo(elIndices);
            batch.MeshBounds = batch.MeshBounds.ExpandToInclude(bounds);

            batch.mIndexCount = newICount;

            // Track sub-regions for efficient overlap queries
            // Special case to append to an existing region
            bool combine = false;
            if (batch.ElementRange.Length > 0) {
                ref var lastEl = ref batchElements[batch.ElementRange.End - 1];
                Debug.Assert(lastEl.IndexRange.End == subRegionIndRange.Start);
                var combinedBounds = lastEl.MeshBounds.ExpandToInclude(bounds);
                var origArea = lastEl.MeshBounds.Area;
                var combinedArea = combinedBounds.Area;
                if (combinedArea < MathF.Max(origArea * 1.1f, 50f * 50f) && lastEl.IndexRange.Length < 512) {
                    lastEl.MeshBounds = combinedBounds;
                    lastEl.IndexRange.Length += subRegionIndRange.Length;
                    combine = true;
                }
            }
            if (!combine) {
                while (true) {
                    var level = GetQuadLevelPrecise(batch.ElementRange.Length + 1);
                    if (level == 0) break;
                    var count = GetLevelQuad(level - 1) + 1;
                    RectF quadBounds = batchElements[batch.ElementRange.End - 1].MeshBounds;
                    int iBeg = batch.ElementRange.Length - 1;
                    int iEnd = iBeg - count * 4;
                    for (int i = iBeg; i > iEnd; i -= count) {
                        quadBounds = quadBounds.ExpandToInclude(batchElements[batch.ElementRange.Start + i].MeshBounds);
                    }
                    batchElements.Append(ref batch.ElementRange, new BatchElement() {
                        MeshBounds = quadBounds,
                        IndexRange = new RangeInt(-1, count * 4),
                    });
                }
                Debug.Assert(subRegionIndRange.End <= batch.mIndexCount);
                batchElements.Append(ref batch.ElementRange, new BatchElement() {
                    MeshBounds = bounds,
                    IndexRange = subRegionIndRange,
                });
            }
        }
        private static int GetQuadLevelPrecise(int quad) {
            int level = GetQuadLevel(quad);
            for (; level > 0; --level) {
                int quadStride = GetLevelQuad(level) + 1;
                Debug.Assert(quad >= 0);
                while (quad >= quadStride) quad -= quadStride;
                if (quad == 0) return level;
            }
            return 0;
        }
        // Get the level for the specified quad index
        // 0,0,0,0, 1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,1, 2,2,..
        private static int GetQuadLevel(int quad) {
            return (31 - BitOperations.LeadingZeroCount((uint)(quad + 1) * 3 + 1)) / 2 - 1;
        }
        // Get the quad index at the start of each level
        // 0, 4, 20, 84,..
        private static int GetLevelQuad(int level) {
            return (int)((1u << (2 * (level + 1))) - 1) / 3 - 1;
        }
        private static int GetLevelQuadCount(int level) {
            return (int)(1u << (2 * (level + 1)));
        }
        // Gets the id of the highest index at the start of each level:
        // 0, 3, 15, 63,..
        private static int GetLevelIndex(int level) {
            return (int)(1u << (2 * level)) - 1;
        }
        public struct Builder {
			public CanvasCompositor mCompositor;
            public LinkedListNode<Node>? mChildBefore;
            public LinkedListNode<Item>? mItem;
            public int ZIndex;
            public Builder(CanvasCompositor compositor, LinkedListNode<Node>? childBefore, LinkedListNode<Item>? item) {
				mCompositor = compositor;
                mChildBefore = childBefore;
                mItem = item;
            }
			// A where of null = at the end of the list
            private LinkedListNode<T> InsertBefore<T>(LinkedList<T> list, LinkedListNode<T>? where, T item) {
				if (where == null) return list.AddLast(item);
				else return list.AddBefore(where, item);
            }
            public void AppendItem(LinkedListNode<Node> node, CanvasElement element) {
                if (mItem != null && mItem.ValueRef.mNode == node) {
                    mItem.ValueRef.mElementId = element.ElementId;
                    mItem = mItem.Next;
				}
				else {
					InsertBefore(mCompositor.mItems, mItem, new Item{
						mNode = node,
						mElementId = element.ElementId,
                        //mBatchId = batchId,
                    });
				}
		        mCompositor.AppendElementData(element.ElementId, element.Material);
			}
            public LinkedListNode<Node> InsertChild(LinkedListNode<Node> parent, CanvasRenderable context) {
				var next = mChildBefore; next = next!.Next;
				if (next != null && next.ValueRef.mContext == context) {
					next.ValueRef.mParent = parent;
					mChildBefore = next;
				}
				else {
					mChildBefore = mCompositor.mNodes.AddAfter(mChildBefore!, new Node{
						mContext = context,
						mParent = parent,
					});
				}
				return mChildBefore;
			}
            // Removes anything from this point onward with the specified node as a parent
            public bool ClearChildrenRecursive(LinkedListNode<Node> node) {
                for (; ; ) {
					// Remove direct child items
					while (mItem != null && mItem.ValueRef.mNode == node) {
                        var item = mItem;
                        mItem = item.Next;
                        mCompositor.mItems.Remove(item);
                    }
                    var child = mChildBefore!.Next;
                    if (child == null || child.ValueRef.mParent != node) break;
                    // Recursively remove child nodes
                    if (ClearChildrenRecursive(child)) {
                        var next = mChildBefore; next = next!.Next;
                        Debug.Assert(next == child);
                        mCompositor.mNodes.Remove(child);
                    }
                }
                return true;
            }
		}
        public ref struct Context {
			ref Builder mBuilder;
            public LinkedListNode<Node> mNode;
            public Context(ref Builder builder, LinkedListNode<Node> node) {
                mBuilder = ref builder;
				mNode = node;
            }
            public CanvasCompositor GetCompositor() { return mBuilder.mCompositor; }
            public void Append(CanvasElement element) {
				mBuilder.AppendItem(mNode, element);
            }
            public Context InsertChild(CanvasRenderable element) {
				return new Context(ref mBuilder, mBuilder.InsertChild(mNode, element));
			}
            public void ClearRemainder() {
				if (mBuilder.mItem != null)
					mBuilder.ClearChildrenRecursive(mNode);
			}
            public ref T CreateTransient<T>(Canvas canvas, Func<T>? creator = null) where T : ICanvasTransient, new() {
                return ref mBuilder.mCompositor.RequireTransient<T>(mNode, canvas, creator);
            }

            public CanvasCompositor.TransformerRef PushTransformer(Matrix3x2 transform) {
                return mBuilder.mCompositor.PushTransformer(transform);
            }
            public CanvasCompositor.ClippingRef PushClippingArea(CanvasLayout rect) {
                return mBuilder.mCompositor.PushClippingRect(rect);
            }
            public CanvasCompositor.MaterialRef PushMaterial(Material material) {
                return mBuilder.mCompositor.PushMaterial(material);
            }
            public CanvasCompositor.ZIndexRef PushZIndex(int zindex) {
                return mBuilder.mCompositor.PushZIndex(zindex);
            }
        }

        private ref T RequireTransient<T>(LinkedListNode<Node> context, Canvas canvas, Func<T>? creator) where T : ICanvasTransient, new() {
            var list = transientCache.Require<T>();
            return ref list.RequireItem(context, canvas, creator);
        }

        public Builder CreateBuilder(Canvas canvas) {
            transientCache.Reset(canvas);
            Clear();
            if (mNodes.Count == 0) {
				mNodes.AddFirst(new Node{ mContext = null, mParent = null, });
			}
			return new Builder(this, mNodes.First, mItems.First);
		}
        public Context CreateRoot(ref Builder builder) {
			return new Context(ref builder, mNodes.First!);
		}
        public void EndBuild(Builder builder) {
            CompactIndexBuffer();
            mIndices.BufferLayout.revision++;

            /*string v = "";
            for (int i = 0; i < 100; i++) {
                var lvl = GetQuadLevelPrecise(i);
                v += lvl + ",";
            }
            Console.WriteLine(v);*/
        }

        public void CompactIndexBuffer() {
            int bindex = 0;
            int cindex = 0;
            foreach (ref var batch in mBatches) {
                if (batch.mIndexAlloc.Length > batch.mIndexCount) {
                    mUnusedIndices.Add(batch.mIndexAlloc.Start + batch.mIndexCount, batch.mIndexAlloc.Length - batch.mIndexCount);
                    batch.mIndexAlloc.Length = batch.mIndexCount;
                }
            }
            for (int i = 0; i <= mUnusedIndices.Ranges.Count; ++i) {
                var range = i < mUnusedIndices.Ranges.Count ? mUnusedIndices.Ranges[i] : new RangeInt(mIndices.Count, 0);
                foreach (ref var batch in mBatches) {
                    if (batch.mIndexAlloc.Start >= bindex && batch.mIndexAlloc.Start < range.Start) batch.mIndexAlloc.Start += cindex - bindex;
                }
                mIndices.CopyRange(bindex, cindex, range.Start - bindex);
                cindex += range.Start - bindex;
                bindex = range.End;
            }
            mUnusedIndices.Clear();
            mIndices.BufferLayout.mCount = cindex;
        }

        public unsafe void Render(CSGraphics graphics, Material material) {
            if (mIndices.Count == 0) return;
			var vertices = mBuilder.GetVertices();
            var bindingsPtr = stackalloc CSBufferLayout[2];
            var bindings = new MemoryBlock<CSBufferLayout>(bindingsPtr, 2);
            bindings[0] = mIndices.BufferLayout;
            bindings[1] = vertices.BufferLayout;
            bindings[0].size = mIndices.BufferLayout.mCount * mIndices.BufferStride;
            using var materials = new PooledList<Material>(2);
            foreach (var batch in mBatches) {
                materials.Clear();
                foreach (var mat in batch.Materials)
                    materials.Add(mat);
                materials.Add(material);
                var pso = MaterialEvaluator.ResolvePipeline(
                    graphics,
					bindings,
                    materials
				);
                var resources = MaterialEvaluator.ResolveResources(
					graphics,
					pso,
					materials
				);
                var drawConfig = new CSDrawConfig(batch.mIndexAlloc.Start, batch.mIndexCount);
                graphics.CommitResources(pso, resources);
                graphics.Draw(pso, bindings.AsCSSpan(), resources.AsCSSpan(), drawConfig);
            }
	    }
    }

    public static class ContextHelper {
        public ref struct Scope {
            public CanvasCompositor.Context Composer;
            public CanvasRenderable Renderable;

            private struct TransientValue : ICanvasTransient {
                public int ValueHash;
                public void Dispose(Canvas canvas) { }
                public void Initialize(Canvas canvas) { ValueHash = -1; }
                public void Reset(Canvas canvas) { }
            }

            public Scope(ref CanvasCompositor.Context composer, CanvasRenderable renderable) {
                Composer = composer;
                Renderable = renderable;
            }
            public void Dispose() { }
            public ref T CreateTransient<T>(Func<T>? creator = null) where T : ICanvasElement, ICanvasTransient, new() {
                ref var element = ref Composer.CreateTransient<T>(Renderable.Canvas, creator);
                if (Renderable.HasDirtyFlag(CanvasRenderable.DirtyFlags.Layout)) element.MarkLayoutDirty();
                return ref element;
            }
            public bool WithValueChanged<T>(T value) where T : struct {
                ref var transient = ref Composer.CreateTransient<TransientValue>(Renderable.Canvas);
                var valueHash = value.GetHashCode();
                if (transient.ValueHash != -1 && transient.ValueHash == valueHash) return false;
                transient.ValueHash = valueHash;
                return true;
            }
            public void Append<T>(ref T element) where T : ICanvasElement {
                element.Append(ref Composer);
            }
            public void Append<T>(ref T element, in CanvasLayout layout) where T : ICanvasElement {
                if (element.HasDirtyFlags) element.UpdateLayout(Renderable.Canvas, layout);
                element.Append(ref Composer);
            }
            public void Append<T>(ref T element, in CanvasLayout layout, in CanvasTransform transform) where T : ICanvasElement {
                if (element.HasDirtyFlags) {
                    transform.Apply(layout, out var localLayout);
                    element.UpdateLayout(Renderable.Canvas, localLayout);
                }
                element.Append(ref Composer);
            }
        }
        public static Scope WithScope(this ref CanvasCompositor.Context composer, CanvasRenderable renderable) {
            return new(ref composer, renderable);
        }
    }

    public static class GUIUtility {
        public static void Box(this Canvas canvas, ref CanvasCompositor.Context context, CanvasLayout layout)
            => Box(canvas, ref context, layout, Color.White);
        public static void Box(this Canvas canvas, ref CanvasCompositor.Context context, CanvasLayout layout, Color color) {
            ref var img = ref context.CreateTransient<CanvasImage>(canvas, static () => new() { });
            img.Color = color;
            img.MarkLayoutDirty();
            img.UpdateLayout(canvas, layout);
            img.Append(ref context);
        }
        public static void Label(this Canvas canvas, ref CanvasCompositor.Context context, CanvasLayout layout, string text, TextAlignment alignment = TextAlignment.Left)
            => Label(canvas, ref context, layout, text, Color.White, alignment);
        public static void Label(this Canvas canvas, ref CanvasCompositor.Context context, CanvasLayout layout, string text, Color color, TextAlignment alignment = TextAlignment.Left) {
            ref var txt = ref context.CreateTransient<CanvasText>(canvas, static () => new() { FontSize = 12, });
            txt.Alignment = alignment;
            txt.Text = text;
            txt.Color = color;
            txt.MarkLayoutDirty();
            txt.UpdateLayout(canvas, layout);
            txt.Append(ref context);
        }

    }
}
