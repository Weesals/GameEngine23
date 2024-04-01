using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Weesals.Editor.Assets;

namespace Weesals.Engine {
    public class Font {

        public CSTexture Texture;
        public int LineHeight;
        public CSGlyph[] Glyphs;
        public Dictionary<ValueTuple<char, char>, int> Kernings = new();

        public int GetKerning(char c1, char c2) {
            if (Kernings.TryGetValue((c1, c2), out var k)) return k;
            return 0;
        }

        public int GetGlyphId(char chr) {
            int min = 0, max = Glyphs.Length - 1;
            while (min < max) {
                int mid = (min + max) / 2;
                if (chr > Glyphs[mid].mGlyph)
                    min = mid + 1;
                else
                    max = mid;
            }
            return min;
        }
        public CSGlyph GetGlyph(int id) { return Glyphs[id]; }

        public void Serialize(BinaryWriter writer) {
            TextureImporter.Serialize(writer, Texture);
            writer.Write(LineHeight);
            writer.Write(Glyphs.Length);
            for (int i = 0; i < Glyphs.Length; i++) {
                var glyph = Glyphs[i];
                writer.Write(glyph.mGlyph);
                writer.Write(glyph.mAtlasOffset.X);
                writer.Write(glyph.mAtlasOffset.Y);
                writer.Write(glyph.mSize.X);
                writer.Write(glyph.mSize.Y);
                writer.Write(glyph.mOffset.X);
                writer.Write(glyph.mOffset.Y);
                writer.Write(glyph.mAdvance);
            }
            writer.Write(Kernings.Count);
            foreach (var kerning in Kernings) {
                writer.Write(kerning.Key.Item1);
                writer.Write(kerning.Key.Item2);
                writer.Write(kerning.Value);
            }
        }
        public void Serialize(BinaryReader reader) {
            Texture = CSTexture.Create("Font");
            TextureImporter.Serialize(reader, Texture);
            LineHeight = reader.ReadInt32();
            Glyphs = new CSGlyph[reader.ReadInt32()];
            for (int i = 0; i < Glyphs.Length; i++) {
                ref var glyph = ref Glyphs[i];
                glyph.mGlyph = (char)reader.ReadInt16();
                glyph.mAtlasOffset.X = reader.ReadInt32();
                glyph.mAtlasOffset.Y = reader.ReadInt32();
                glyph.mSize.X = reader.ReadInt32();
                glyph.mSize.Y = reader.ReadInt32();
                glyph.mOffset.X = reader.ReadInt32();
                glyph.mOffset.Y = reader.ReadInt32();
                glyph.mAdvance = reader.ReadInt32();
            }
            int kcount = reader.ReadInt32();
            Kernings.Clear();
            for (int i = 0; i < kcount; i++) {
                var c1 = (char)reader.ReadInt16();
                var c2 = (char)reader.ReadInt16();
                var k = reader.ReadInt32();
                Kernings.Add((c1, c2), k);
            }
        }
    }
}
