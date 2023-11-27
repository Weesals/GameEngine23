using GameEngine23.Interop;
using System;
using System.Collections.Generic;
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
		public void Initialize() {
			ElementId = -1;
		}
        public bool IsValid() { return ElementId != -1; }
        public void SetMaterial(Material mat) { Material = mat; }
		public void SetElementId(int id) {
			Debug.Assert(ElementId == -1, "Element already has an ElementId");
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

    public struct CanvasImage {
        CanvasElement element = CanvasElement.Invalid;

        private Color color;
        private RectF uvrect;
        private bool invalid;

        public CSTexture Texture { get; private set; }
		public RectF UVRect { get => uvrect; set { uvrect = value; invalid = true; } }
        public Color Color { get => color; set { color = value; invalid = true; } }
        public CanvasImage() : this(default, new RectF(0f, 0f, 1f, 1f)) { }
        public CanvasImage(CSTexture texture, RectF uvrect) {
            Texture = texture;
            UVRect = uvrect;
            color = Color.White;
        }
        public void Dispose(Canvas canvas) { element.Dispose(canvas); }
        unsafe public void Initialize(Canvas canvas) {
            Debug.Assert(!element.IsValid());
            element.Initialize();
            if (Texture.IsValid()) {
                if (element.Material == null) element.SetMaterial(new Material());
                element.Material.SetTexture("Texture", Texture);
            }
            var elementId = canvas.Builder.Allocate(4, 6);
            element.SetElementId(elementId);
            UpdateIndices(canvas);
            UpdateVertices(canvas);
        }
        private void UpdateIndices(Canvas canvas) {
            var rectVerts = canvas.Builder.MapVertices(element.ElementId);
            Span<uint> inds = stackalloc uint[] { 0, 1, 2, 1, 3, 2, };
            rectVerts.GetIndices().Set(inds);
        }
        private void UpdateVertices(Canvas canvas) {
            var rectVerts = canvas.Builder.MapVertices(element.ElementId);
            Span<Vector2> uv = stackalloc Vector2[] {
                UVRect.BottomLeft,
                UVRect.BottomRight,
                UVRect.TopLeft,
                UVRect.TopRight,
            };
            rectVerts.GetTexCoords().Set(uv);
            Span<Color> colors = stackalloc Color[] { Color, Color, Color, Color, };
            rectVerts.GetColors().Set(colors);
            invalid = false;
        }
	    public void UpdateLayout(Canvas canvas, CanvasLayout layout) {
            if (invalid) UpdateVertices(canvas);
            var rectVerts = canvas.Builder.MapVertices(element.ElementId);
		    Span<Vector3> p = stackalloc Vector3[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0), };
            foreach (ref var v in p) v = layout.TransformPositionN(v);
		    rectVerts.GetPositions().Set(p);
		    rectVerts.MarkChanged();
	    }
        public void Append(ref CanvasCompositor.Context compositor) {
            compositor.Append(element);
        }
    }
    public struct CanvasText {
        CanvasElement element = CanvasElement.Invalid;

        public struct GlyphStyle {
            public float mFontSize;
            public Color mColor;
            public GlyphStyle(float size, Color color) { mFontSize = size; mColor = color; }
            public static readonly GlyphStyle Default = new GlyphStyle(24, Color.Black);
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

        public string text = "";
        public string Text { get => text; set { SetText(value); } }

        public CSFont font;
        public bool isInvalid;
        public GlyphStyle defaultStyle = GlyphStyle.Default;
        List<GlyphStyle> styles;
        List<GlyphPlacement> glyphPlacements;
        List<GlyphLayout> glyphLayout;

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
			element.Dispose(canvas);
		}

        private void SetText(string value) {
            text = value;
            isInvalid = true;
        }
		public void SetFont(CSFont _font) {
            font = _font;
			isInvalid = true;
            if (element.Material == null) element.SetMaterial(new Material("./assets/text.hlsl"));
            element.Material.SetTexture("Texture", font.GetTexture());
        }
        public void SetFontSize(float size) {
            defaultStyle.mFontSize = size;
            isInvalid = true;
        }
        public void SetFont(CSFont _font, float fontSize) {
            SetFont(_font); SetFontSize(fontSize);
        }
        public void SetColor(Color color) {
            defaultStyle.mColor = color;
            isInvalid = true;
        }

        bool CompareConsume(string str, ref int c, string key) {
			if (string.Compare(str, c, key, 0, key.Length) != 0) return false;
			c += (int) key.Length;
			return true;
		}
		void UpdateGlyphPlacement() {
            float lineHeight = (float)font.GetLineHeight();
            glyphPlacements.Clear();
            styles.Clear();
            styles.Add(defaultStyle);
            List<Color> colorStack = new();
            List<float> sizeStack = new();
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
                    if (colorStack.Count == 0) tstyle.mColor = colorStack.Last();
                    if (sizeStack.Count == 0) tstyle.mFontSize = sizeStack.Last();
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
			var size = Vector2.Zero;
			for (int c = 0; c < glyphPlacements.Count; ++c) {
				var placement = glyphPlacements[c];
                var glyph = font.GetGlyph(placement.mGlyphId);
				var style = styles[placement.mStyleId];
				var scale = style.mFontSize / lineHeight;
				var glyphSize2 = new Vector2((float)glyph.mAdvance, lineHeight) * scale;

				if (pos.X + glyphSize2.X >= layout.AxisX.W) {
					pos.X = 0;
					pos.Y += lineHeight * scale;
					if (pos.Y + glyphSize2.Y > layout.AxisY.W) break;
					if (pos.X + glyphSize2.X >= layout.AxisX.W) break;
				}
				glyphLayout.Add(new GlyphLayout{
					mVertexOffset = -1,
					mLocalPosition = pos + glyphSize2 / 2.0f,
				});
				size = Vector2.Max(size, new Vector2(pos.X + glyphSize2.X, pos.Y + (float)(glyph.mOffset.Y + glyph.mSize.Y) * scale));
                pos.X += placement.mAdvance;
			}
			var offset = (layout.GetSize() - size) / 2.0f;
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
        public float GetPreferredHeight() {
			return defaultStyle.mFontSize;
        }
        public void UpdateLayout(Canvas canvas, CanvasLayout layout) {
            UpdateGlyphPlacement();
			UpdateGlyphLayout(layout);

			var elementId = element.ElementId;

			int vcount = (int)glyphLayout.Count * 4;
			var mBuilder = canvas.Builder;
            if (isInvalid || elementId == -1 || mBuilder.MapVertices(elementId).GetVertexCount() < vcount) {
				isInvalid = false;
                if (elementId != -1 && mBuilder.MapVertices(elementId).GetVertexCount() != vcount) {
                    element.Dispose(canvas);
                    elementId = -1;
				}
				if (elementId == -1) {
                    elementId = mBuilder.Allocate(vcount, vcount * 6 / 4);
                    var rectVerts = mBuilder.MapVertices(elementId);
					var inds = rectVerts.GetIndices();
					for (int v = 0, i = 0; i < inds.mCount; i += 6, v += 4) {
						inds[i + 0] = (uint)(v + 0);
						inds[i + 1] = (uint)(v + 1);
						inds[i + 2] = (uint)(v + 2);
						inds[i + 3] = (uint)(v + 1);
						inds[i + 4] = (uint)(v + 3);
						inds[i + 5] = (uint)(v + 2);
					}
				}
				element.SetElementId(elementId);
            }
			var textVerts = mBuilder.MapVertices(elementId);
            var positions = textVerts.GetPositions();
            var uvs = textVerts.GetTexCoords();
            var colors = textVerts.GetColors();
            var atlasTexelSize = 1.0f / font.GetTexture().GetSize().X;
            var lineHeight = (float)font.GetLineHeight();
			int vindex = 0;
			for (int c = 0; c < glyphLayout.Count; ++c) {
                var gplacement = glyphPlacements[c];
                var glayout = glyphLayout[c];
                var glyph = font.GetGlyph(gplacement.mGlyphId);
				var style = styles[gplacement.mStyleId];
				var scale = style.mFontSize / lineHeight;
                glayout.mVertexOffset = vindex;
                var uv_1 = (Vector2)(glyph.mAtlasOffset) * atlasTexelSize;
                var uv_2 = (Vector2)(glyph.mAtlasOffset + glyph.mSize) * atlasTexelSize;
                var size2 = (Vector2)glyph.mSize * scale;
                var glyphOffMin = (Vector2)glyph.mOffset - new Vector2((float)glyph.mAdvance, lineHeight) / 2.0f;
                var glyphPos0 = layout.TransformPosition2D(glayout.mLocalPosition + glyphOffMin * scale);
                var glyphDeltaX = layout.AxisX.toxyz() * size2.X;
                var glyphDeltaY = layout.AxisY.toxyz() * size2.Y;
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
			textVerts.MarkChanged();
		}
		public void Append(ref CanvasCompositor.Context compositor) {
			compositor.Append(element);
        }
    }

    public class CanvasCompositor : IDisposable {
        public struct Node {
            public object? mContext;
            public LinkedListNode<Node>? mParent;
		};
        public struct Item {
            public LinkedListNode<Node> mNode;
            public int mVertexRange;
		};
        public struct Batch {
			public Material mMaterial;
            public RangeInt mIndexRange;
		};
        LinkedList<Node> mNodes = new();
        LinkedList<Item> mItems = new();
		List<Batch> mBatches = new();
		BufferLayoutPersistent mIndices;
        CanvasMeshBuffer mBuilder;
        public CanvasCompositor(CanvasMeshBuffer builder) {
			mBuilder = builder;
            mIndices = new BufferLayoutPersistent(0, BufferLayoutPersistent.Usages.Index, 0);
            mIndices.AppendElement(new CSBufferElement("INDEX", BufferFormat.FORMAT_R32_UINT));
        }
        public void Dispose() {
            mIndices.Dispose();
        }
        public BufferLayoutPersistent GetIndices() { return mIndices; }
		public void AppendElementData(int elementId, Material material) {
			var verts = mBuilder.MapVertices(elementId);
            var inds = verts.GetIndices();
			if (mIndices.BufferCapacityCount < mIndices.Count + inds.mCount) {
				mIndices.AllocResize(
					Math.Max(
						mIndices.BufferCapacityCount + 2048,
						mIndices.Count + inds.mCount
					)
				);
			}
			int istart = mIndices.Count;
			int icount = inds.mCount;
			if (mBatches.Count == 0 || mBatches[^1].mMaterial != material) {
				mBatches.Add(new Batch {
					mMaterial = material,
					mIndexRange = new RangeInt(istart, 0)
				});
			}
			var outInds = new TypedBufferView<uint>(mIndices.Elements[0], new RangeInt(istart, icount));
			for (int i = 0; i < inds.mCount; ++i) {
				outInds[i] = (uint)(inds[i] + verts.VertexOffset);
			}
			mIndices.BufferLayout.mCount += icount;
			var batch = mBatches[^1];
            batch.mIndexRange.Length += icount;
			mBatches[^1] = batch;
        }
        public struct Builder {
			public CanvasCompositor mCompositor;
            public LinkedListNode<Node>? mChildBefore;
            public LinkedListNode<Item>? mItem;
            public int mIndex;
			public int mBatch;
            public Builder(CanvasCompositor compositor, LinkedListNode<Node>? childBefore, LinkedListNode<Item>? item) {
				mCompositor = compositor;
                mChildBefore = childBefore;
                mItem = item;
				mIndex = 0;
				mBatch = 0;
            }
			// A where of null = at the end of the list
            private LinkedListNode<T> InsertBefore<T>(LinkedList<T> list, LinkedListNode<T>? where, T item) {
				if (where == null) return list.AddLast(item);
				else return list.AddBefore(where, item);
            }
            public void AppendItem(LinkedListNode<Node> node, CanvasElement element) {
				if (mItem != null && mItem.ValueRef.mNode == node) {
                    mItem.ValueRef.mVertexRange = element.ElementId;
					mItem = mItem.Next;
				}
				else {
					InsertBefore(mCompositor.mItems, mItem, new Item{
						mNode = node,
						mVertexRange = element.ElementId,
					});
					// Clear all future indices
					mCompositor.mIndices.BufferLayout.mCount = mIndex;
				}
				if (mIndex >= mCompositor.mIndices.Count)
					mCompositor.AppendElementData(element.ElementId, element.Material);
				var verts = mCompositor.mBuilder.MapVertices(element.ElementId);
				mIndex += verts.GetIndexCount();
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
                    var item = mItem;
                    if (item == null) break;
					// Remove direct child items
					if (item.ValueRef.mNode == node) {
						mItem = item.Next;
                        mCompositor.mItems.Remove(item);
						continue;
                    }
                    var child = mChildBefore; child = child!.Next;
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
            public LinkedList<Node> GetNodes() { return mBuilder.mCompositor.mNodes; }
            public LinkedList<Item> GetItems() { return mBuilder.mCompositor.mItems; }
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
		}
		public Builder CreateBuilder() {
			if (mNodes.Count == 0) {
				mNodes.AddFirst(new Node{ mContext = null, mParent = null, });
			}
			return new Builder(this, mNodes.First, mItems.First);
		}
        public Context CreateRoot(ref Builder builder) {
			return new Context(ref builder, mNodes.First!);
		}

        public unsafe void Render(CSGraphics graphics, Material material) {
            if (mIndices.Count == 0) return;
			var vertices = mBuilder.GetVertices();
            var bindingsPtr = stackalloc CSBufferLayout[2];
            var bindings = new MemoryBlock<CSBufferLayout>(bindingsPtr, 2);
			bindings[0] = mIndices.BufferLayout;
            bindings[1] = vertices.BufferLayout;
            using var materials = new PooledList<Material>(2);
            foreach (var batch in mBatches) {
                materials.Clear();
                if (batch.mMaterial != null) materials.Add(batch.mMaterial);
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
                var drawConfig = new CSDrawConfig(batch.mIndexRange.Start, batch.mIndexRange.Length);
                graphics.Draw(pso, bindings, resources, drawConfig);
            }
	    }
    };

}
