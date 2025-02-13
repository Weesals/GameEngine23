﻿using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.Utility;

namespace Weesals.UI {
    public struct SpriteVertex {
        public Vector2 Position;
        public Vector2 UV;
        public SpriteVertex(Vector2 pos, Vector2 uv) {
            Position = pos;
            UV = uv;
        }
    }
    public struct SpritePolygon {
        public SpriteVertex[] Vertices;
        public ushort[] Indices;
    }
    public class Sprite {
        public CSTexture Texture;
        public RectF Margins = new RectF(0f, 0f, 0f, 0f);
        public RectF Borders = RectF.Unit01;
        public RectF UVRect = RectF.Unit01;
        public float Scale = 1.0f;
        public Vector2 Size;
        public SpritePolygon Polygon;
        public override string ToString() {
            return $"Size<{Size}> UV<{UVRect}>";
        }
    }

    public class SpriteAtlas {
        public readonly Sprite[] Sprites;
        public SpriteAtlas(Sprite[] sprites) {
            Sprites = sprites;
        }
    }

    public class SpriteRenderer {
        public int Padding = 1;
        private class OrderByHeight : IComparer<CSTexture> {
            public int Compare(CSTexture x, CSTexture y) {
                var height1 = x.IsValid ? x.GetSize().Y : 0;
                var height2 = y.IsValid ? y.GetSize().Y : 0;
                return height2 - height1;
            }
            public static OrderByHeight Default = new();
        }
        public unsafe SpriteAtlas Generate(IList<CSTexture> insprites) {
            var sprites = new Sprite[insprites.Count];
            var atlas = CSTexture.Create("Sprite Atlas");
            atlas.SetSize(1024);
            var atlasSize = atlas.GetSize();
            using var sorted = new PooledArray<CSTexture>(insprites.Count);
            using var ids = new PooledArray<int>(insprites.Count);
            for (int i = 0; i < ids.Count; ++i) ids[i] = i;
            insprites.CopyTo(sorted.Data, 0);
            Array.Sort(sorted.Data, ids.Data, 0, sorted.Count, OrderByHeight.Default);
            Int2 pos = Int2.Zero;
            int maxHeight = 0;
            for (int s = 0; s < insprites.Count; ++s) {
                var sprite = sorted[s];
                if (!sprite.IsValid) continue;
                var size = sprite.GetSize();
                if (pos.X + size.X > atlasSize.X) {
                    pos.X = 0;
                    pos.Y += maxHeight + Padding;
                    maxHeight = 0;
                }
                if (pos.Y + size.Y > atlasSize.Y) break;
                maxHeight = Math.Max(maxHeight, size.Y);
                Blit(atlas, pos, sprite, Int2.Zero, size);
                var uvrect = new RectF(pos.X, pos.Y, size.X, size.Y) / (Vector2)atlasSize;
                sprites[ids[s]] = new Sprite() {
                    Polygon = new SpritePolygon() {
                        Vertices = new SpriteVertex[] {
                            new SpriteVertex((Vector2)size * new Vector2(-0.5f, -0.5f), uvrect.TopLeft),
                            new SpriteVertex((Vector2)size * new Vector2(+0.5f, -0.5f), uvrect.TopRight),
                            new SpriteVertex((Vector2)size * new Vector2(+0.5f, +0.5f), uvrect.BottomRight),
                            new SpriteVertex((Vector2)size * new Vector2(-0.5f, +0.5f), uvrect.BottomLeft),
                        },
                        Indices = new ushort[] { 0, 1, 2, 0, 2, 3, },
                    },
                    Size = size,
                    Texture = atlas,
                    UVRect = uvrect,
                };
                pos.X += size.X + Padding;
            }
            atlas.MarkChanged();
            return new SpriteAtlas(sprites);
        }

        private unsafe void Blit(CSTexture dst, Int2 dstOff, CSTexture src, Int2 srcOff, Int2 size) {
            var dstSize = dst.GetSize();
            var srcSize = src.GetSize();
            var dstData = dst.GetTextureData();
            var srcData = src.GetTextureData();
            var srcEnd = srcOff + size;
            var srcToDstY = dstOff.Y - srcOff.Y;
            for (int y = srcOff.Y; y < srcEnd.Y; ++y) {
                var srcOffset = ((y) * srcSize.X + srcOff.X);
                var dstOffset = ((y + srcToDstY) * dstSize.X + dstOff.X);
                srcData.AsSpan().Slice(srcOffset * 4, size.X * 4)
                    .CopyTo(dstData.AsSpan().Slice(dstOffset * 4, size.X * 4));
            }
        }
    }

}
