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
using Weesals.Utility;

namespace Weesals.UI {
    public struct CanvasElement {
        public int ElementId { get; private set; }
        public Material Material { get; private set; }
        public bool IsValid() { return ElementId != -1; }
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
                    (sbyte)Math.Min(0, Color.A * 127 / 255)
                    ); break;
                case BlendModes.Overlay: unified = new SColor(
                    (sbyte)(Color.R),
                    (sbyte)(Color.G),
                    (sbyte)(Color.B),
                    (sbyte)Math.Min(0, Color.A * 127 / 255)
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
        void Initialize(Canvas canvas);
        void Dispose(Canvas canvas);
        void UpdateLayout(Canvas canvas, in CanvasLayout layout);
        void Append(ref CanvasCompositor.Context compositor);
    }
    public interface ICanvasTransient {
        void Initialize(Canvas canvas);
        void Dispose(Canvas canvas);
    }

    public struct CanvasImage : ICanvasElement, ICanvasTransient {
        CanvasElement element = CanvasElement.Invalid;

        public enum DirtyFlags : byte { None = 0, UV = 1, Color = 2, Position = 4, Indices = 8, All = 0x0f, };

        private CanvasBlending blending;
        private RectF uvrect;
        private RectF border;
        private DirtyFlags dirty;

        public CSTexture Texture { get; private set; }
        public RectF UVRect { get => uvrect; set { uvrect = value; dirty |= DirtyFlags.UV; } }
        public RectF Border { get => border; set { border = value; dirty |= DirtyFlags.UV; } }
        public Color Color { get => blending.Color; set { blending.Color = value; dirty |= DirtyFlags.Color; } }
        public bool IsNinePatch { get; private set; }
        public bool IsInitialized => element.IsValid();
        public bool HasDirtyFlags => dirty != DirtyFlags.None;
        public CanvasBlending.BlendModes BlendMode { get => blending.BlendMode; }
        public CanvasImage() : this(default, new RectF(0f, 0f, 1f, 1f)) { }
        public CanvasImage(CSTexture texture, RectF uvrect) {
            Texture = texture;
            UVRect = uvrect;
            Border = new RectF(0f, 0f, 0f, 0f);
            IsNinePatch = false;
            blending = CanvasBlending.Default;
        }
        unsafe public void Initialize(Canvas canvas) {
            Debug.Assert(!element.IsValid());
            dirty = DirtyFlags.Indices;
            SetTexture(Texture);
        }
        public void Dispose(Canvas canvas) { element.Dispose(canvas); dirty |= DirtyFlags.Indices; }
        public void SetTexture(CSTexture texture) {
            Texture = texture;
            if (Texture.IsValid()) {
                if (element.Material == null) element.SetMaterial(new Material());
                element.Material!.SetTexture("Texture", Texture);
            }
        }
        public void SetSprite(Sprite? sprite) {
            SetTexture(sprite?.Texture ?? default);
            var wasDirty = dirty & DirtyFlags.UV;
            var hash = UVRect.GetHashCode() + Border.GetHashCode();
            UVRect = sprite?.UVRect ?? new RectF(0f, 0f, 1f, 1f);
            Border = sprite?.Borders ?? new RectF(0f, 0f, 0f, 0f);
            bool wasNine = IsNinePatch;
            IsNinePatch = Border != new RectF(0f, 0f, 0f, 0f);
            if (wasNine != IsNinePatch) dirty |= DirtyFlags.Indices;
            if (UVRect.GetHashCode() + Border.GetHashCode() == hash) {
                dirty = (dirty & ~DirtyFlags.UV) | wasDirty;
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
            var elementId = element.ElementId;
            if (UnmarkDirty(DirtyFlags.Indices)) {
                if (IsNinePatch
                    ? canvas.Builder.Require(ref elementId, 16, 9 * 6)
                    : canvas.Builder.Require(ref elementId, 4, 6)) {
                    element.SetElementId(elementId);
                    dirty |= DirtyFlags.All & ~DirtyFlags.Indices;
                }
                UpdateIndices(canvas);
            }

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
                    for (int x = 0; x < corners.Length; ++x) texcoords[yI + x] = new Vector2(corners[x].X, corners[corners.Length - y - 1].Y);
                }
                buffers.MarkVerticesChanged();
            }
            if (UnmarkDirty(DirtyFlags.Position)) {
                Span<Vector2> p = IsNinePatch
                    ? stackalloc Vector2[] { new Vector2(0, 0), Vector2.Zero, Vector2.Zero, layout.GetSize(), }
                    : stackalloc Vector2[] { new Vector2(0, 0), layout.GetSize(), };
                if (IsNinePatch && Texture.IsValid()) {
                    var spriteSize = (Vector2)Texture.GetSize() * UVRect.Size;
                    p[1] = Border.Min * spriteSize;
                    p[2] = p[3] - (Vector2.One - Border.Max) * spriteSize;
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
            compositor.Append(element);
        }

        public void PreserveAspect(ref CanvasLayout layout, Vector2 imageAnchor) {
            if (!Texture.IsValid()) return;
            var size = layout.GetSize();
            var imgSize = (Vector2)Texture.GetSize() * UVRect.Size;
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
        public static TextDisplayParameters Default = new() {
            FaceColor = Vector4.One,
            FaceDilate = 0.2f,
            OutlineColor = new Vector4(0f, 0f, 0f, 0.5f),
            OutlineWidth = 0.2f,
            UnderlayColor = new Vector4(0f, 0f, 0f, 0.8f),
            UnderlayOffset = new Vector2(2.0f, 1.0f),
            UnderlaySoftness = 0.3f,
        };
    }
    public struct CanvasText : ICanvasElement {
        CanvasElement element = CanvasElement.Invalid;

        public struct GlyphStyle {
            public float mFontSize;
            public Color mColor;
            public GlyphStyle(float size, Color color) { mFontSize = size; mColor = color; }
            public static readonly GlyphStyle Default = new GlyphStyle(24, Color.White);
        };
		public struct GlyphPlacement {
            public ushort mGlyphId;
            public ushort mStyleId;
            public float mAdvance;
        }
        public struct GlyphLayout {
            public int mVertexOffset;
            public Vector2 mLocalPosition;
        };

        string text = "";
        public string Text { get => text; set { SetText(value); } }

        CSFont font;
        bool dirty;
        GlyphStyle defaultStyle = GlyphStyle.Default;
        TextAlignment alignment = TextAlignment.Centre;
        PooledList<GlyphStyle> styles;
        PooledList<GlyphPlacement> glyphPlacements;
        PooledList<GlyphLayout> glyphLayout;
        TextDisplayParameters displayParameters;

        public Color Color { get => defaultStyle.mColor; set { defaultStyle.mColor = value; dirty = true; } }
        public float FontSize { get => defaultStyle.mFontSize; set { defaultStyle.mFontSize = value; dirty = true; } }
        public CSFont Font { get => font; set { SetFont(value); dirty = true; } }
        public TextAlignment Alignment { get => alignment; set { alignment = value; dirty = true; } }
        public TextDisplayParameters DisplayParameters { get => displayParameters; set { displayParameters = value; dirty = true; if (element.Material != null) UpdateMaterialProperties(); } }

        public CanvasText() : this("") { }
        public CanvasText(string txt) {
            text = txt;
            styles = new();
            glyphPlacements = new();
            glyphLayout = new();
        }
        public void Initialize(Canvas canvas) {
        }
        public void Dispose(Canvas canvas) {
            styles.Dispose();
            glyphPlacements.Dispose();
            glyphLayout.Dispose();
            if (element.IsValid()) element.Dispose(canvas);
		}

        private void SetText(string value) {
            text = value;
            dirty = true;
        }
		public void SetFont(CSFont _font) {
            font = _font;
			dirty = true;
            if (element.Material != null) UpdateMaterialProperties();
        }
        public void SetFont(CSFont _font, float fontSize) {
            SetFont(_font); FontSize = fontSize;
        }

        bool CompareConsume(string str, ref int c, string key) {
			if (string.Compare(str, c, key, 0, key.Length) != 0) return false;
			c += (int) key.Length;
			return true;
		}
		void UpdateGlyphPlacement() {
            if (!font.IsValid()) return;
            float lineHeight = (float)font.GetLineHeight();
            glyphPlacements.Clear();
            styles.Clear();
            styles.Add(defaultStyle);
            using PooledList<Color> colorStack = new();
            using PooledList<float> sizeStack = new();
            int activeStyle = 0;
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
                        activeStyle = -1;
                        continue;
                    }
                    if (CompareConsume(text, ref c, "</color")) {
                        colorStack.RemoveAt(colorStack.Count - 1);
                        activeStyle = -1;
                        continue;
                    }
                    if (CompareConsume(text, ref c, "<size=")) {
                        int s = c;
                        for (; c < text.Length && char.IsNumber(text[c]); ++c) ;
                        if (text[c] == '.') {
                            for (++c; c < text.Length && char.IsNumber(text[c]); ++c) ;
                        }
                        sizeStack.Add(float.Parse(text.AsSpan().Slice(s, c - s)));
                        activeStyle = -1;
                        continue;
                    }
                    if (CompareConsume(text, ref c, "</size")) {
                        sizeStack.RemoveAt(sizeStack.Count - 1);
                        activeStyle = -1;
                        continue;
                    }
                }
                var glyphId = font.GetGlyphId(chr);
                var glyph = font.GetGlyph(glyphId);
                if (glyph.mGlyph != chr) continue;

                if (activeStyle == -1) {
                    var tstyle = defaultStyle;
                    if (colorStack.Count == 0) tstyle.mColor = colorStack[^1];
                    if (sizeStack.Count == 0) tstyle.mFontSize = sizeStack[^1];
                    activeStyle = (int)styles.IndexOf(tstyle);
                    if (activeStyle == -1) { activeStyle = styles.Count; styles.Add(tstyle); }
                }

                var style = styles[activeStyle];
                var scale = style.mFontSize / lineHeight;
                float advance = glyph.mAdvance;
                if (c > 0) advance += font.GetKerning(text[c - 1], (char)glyph.mGlyph);
                advance *= scale;

                glyphPlacements.Add(new GlyphPlacement {
                    mGlyphId = (ushort)glyphId,
                    mStyleId = (ushort)activeStyle,
                    mAdvance = advance,
                });
            }
        }
        void UpdateGlyphLayout(in CanvasLayout layout) {
			float lineHeight = (float)font.GetLineHeight();
			glyphLayout.Clear();
            var pos = Vector2.Zero;
            var min = new Vector2(10000.0f);
			var max = Vector2.Zero;
			for (int c = 0; c < glyphPlacements.Count; ++c) {
				var placement = glyphPlacements[c];
                var glyph = font.GetGlyph(placement.mGlyphId);
				var style = styles[placement.mStyleId];
				var scale = style.mFontSize / lineHeight;
				var glyphSize2 = new Vector2(glyph.mSize.X, lineHeight) * scale;

				if (pos.X + glyphSize2.X > layout.AxisX.W) {
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
                max = Vector2.Max(max, new Vector2(pos.X + glyphSize2.X, pos.Y + (float)(glyph.mOffset.Y + glyph.mSize.Y) * scale));
                pos.X += placement.mAdvance;
			}
            var sizeDelta = layout.GetSize() - (max - min);
            var offset = new Vector2(0f, sizeDelta.Y / 2.0f) - min;
            switch (alignment) {
                case TextAlignment.Centre: offset.X = sizeDelta.X * 0.5f; break;
                case TextAlignment.Right: offset.X = sizeDelta.X * 1.0f; break;
            }
            for (int l = 0; l < glyphLayout.Count; ++l) {
                var tlayout = glyphLayout[l];
                tlayout.mLocalPosition += offset;
                glyphLayout[l] = tlayout;
            }
        }
        public float GetPreferredWidth() {
            if (glyphPlacements.Count == 0) UpdateGlyphPlacement();
            var posX = 0.0f;
            for (int c = 0; c < glyphPlacements.Count; ++c) {
				var placement = glyphPlacements[c];
                posX += placement.mAdvance;
            }
            return posX;
        }
        public float GetPreferredHeight(float width = float.MaxValue) {
			return defaultStyle.mFontSize;
        }
        public void MarkLayoutDirty() {
            dirty = true;
        }
        public void FillVertexBuffers(in CanvasLayout layout, TypedBufferView<Vector3> positions, TypedBufferView<Vector2> uvs, TypedBufferView<SColor> colors) {
            var atlasTexelSize = 1.0f / font.GetTexture().GetSize().X;
            var lineHeight = (float)font.GetLineHeight();
            int vindex = 0;
            var dilate = (displayParameters ?? TextDisplayParameters.Default).GetDilation();
            for (int c = 0; c < glyphLayout.Count; ++c) {
                var gplacement = glyphPlacements[c];
                var glayout = glyphLayout[c];
                var glyph = font.GetGlyph(gplacement.mGlyphId);
                var style = styles[gplacement.mStyleId];
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
            if (dirty) {
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
            if (!dirty) return;
            UpdateGlyphPlacement();
			UpdateGlyphLayout(layout);

            if (element.Material == null) {
                if (element.Material == null) element.SetMaterial(new Material("./assets/text.hlsl"));
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
            dirty = false;
		}

        private void UpdateMaterialProperties() {
            element.Material!.SetTexture("Texture", font.GetTexture());
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
        public void MarkLayoutDirty() { frame.MarkLayoutDirty(); IsDirty = true; }
        public void UpdateLayout(Canvas canvas, in CanvasLayout layout) {
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

    public class CanvasCompositor : IDisposable {
        public class TransientElementCache {
            public interface IElementList { void Reset(Canvas canvas); }
            public struct ItemContainer<T> where T : ICanvasTransient, new() {
                public T Item;
                public LinkedListNode<Node> Context;
            }
            public class ElementList<T> : ArrayList<ItemContainer<T>>, IElementList where T : ICanvasTransient, new() {
                public int Iterator;
                public void Reset(Canvas canvas) {
                    for (int i = Iterator; i < Count; ++i) this[i].Item.Dispose(canvas);
                    //this.RemoveRange(Iterator, Count - Iterator);
                    Iterator = 0;
                }
                public ref T RequireItem(LinkedListNode<Node> context, Canvas canvas) {
                    if (Iterator >= Count) {
                        var item = new T();
                        item.Initialize(canvas);
                        Add(new ItemContainer<T>() { Item = item, Context = context, });
                    } else {
                        int i = Iterator;
                        int end = Math.Min(Count, Iterator + 5);
                        for (; i < end; ++i) {
                            if (this[i].Context == context) break;
                        }
                        if (i != end) {
                            if (i != Iterator) {
                                var t = this[Iterator];
                                this[Iterator] = this[i];
                                this[i] = t;
                            }
                        } else {
                            ref var item = ref this[Iterator];
                            item.Context = context;
                            item.Item.Dispose(canvas);
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
        LinkedList<Node> mNodes = new();
        LinkedList<Item> mItems = new();
        SparseArray<BatchElement> batchElements = new();
        ArrayList<Batch> mBatches = new();
        ArrayList<Matrix3x2> transformers = new();
        ArrayList<ClippingRect> clippingRects = new();
        ArrayList<Material> materialStack = new();
        int matStackHash = 0;
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
        private void RecomputeMaterialStackHash() {
            matStackHash = 0;
            foreach (var mat in materialStack) matStackHash = HashCode.Combine(mat.GetHashCode(), matStackHash);
        }

        public BufferLayoutPersistent GetIndices() { return mIndices; }
        private RectF ComputeElementBounds(int elementId) {
            var verts = mBuilder.MapVertices(elementId);
            var positions = verts.GetPositions2D();
            positions.AssetRequireReader();
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
                        if (Geometry.GetTrianglesOverlap(t0v0, t0v1, t0v2, t1v0, t1v1, t1v2)) return true;
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
            for (int batchId = mBatches.Count - 1; batchId >= 0; --batchId) {
                var batch = mBatches[batchId];
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
            mBatches.Add(new Batch {
                Materials = materials,
                MaterialHash = matHash,
                mIndexAlloc = default,
                MeshBounds = bounds,
            });
            return mBatches.Count - 1;
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
                    batch.mIndexAlloc = mUnusedIndices.Take(reqCount);
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
            public LinkedListNode<Node> InsertChild(LinkedListNode<Node> parent, object context) {
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
            public Context InsertChild(object element) {
				return new Context(ref mBuilder, mBuilder.InsertChild(mNode, element));
			}
            public void ClearRemainder() {
				if (mBuilder.mItem != null)
					mBuilder.ClearChildrenRecursive(mNode);
			}
            public ref T CreateTransient<T>(Canvas canvas) where T : ICanvasTransient, new() {
                return ref mBuilder.mCompositor.RequireTransient<T>(mNode, canvas);
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
        }

        private ref T RequireTransient<T>(LinkedListNode<Node> context, Canvas canvas) where T : ICanvasTransient, new() {
            var list = transientCache.Require<T>();
            return ref list.RequireItem(context, canvas);
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
                graphics.Draw(pso, bindings, resources, drawConfig);
            }
	    }
    }
}
