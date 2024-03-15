using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.Utility;

namespace Weesals.Geometry {
    public abstract class ComputedAsset {
    }

    public class FontAsset : ComputedAsset {
        public CSFont Require() {
            //CSFont.
            return default;
        }
    }

    public class NoiseTexture3D : ComputedAsset {
        public CSTexture Require() {
            const string CacheName = "DataCache/$volumetex";
            if (File.Exists(CacheName)) {
                var data = File.ReadAllBytes(CacheName);
                var volumeTex = CSTexture.Create("FogDensity");
                volumeTex.SetSize3D(64);
                volumeTex.SetMipCount(MipGenerator.CalculateMipCount(volumeTex));
                volumeTex.SetFormat(BufferFormat.FORMAT_BC1_UNORM);
                data.AsSpan().CopyTo(
                    volumeTex.GetTextureData()
                );
                volumeTex.MarkChanged();
                return volumeTex;
            }
            var tex = Generate();
            File.WriteAllBytes(CacheName, tex.GetTextureData().AsSpan().ToArray());
            return tex;
        }
        public CSTexture Generate() {
            var volumeTex = CSTexture.Create("FogDensity");
            volumeTex.SetSize3D(64);
            var data = volumeTex.GetTextureData().Reinterpret<Color>();
            var size = volumeTex.GetSize3D();
            var noiseScale = 1;
            var noiseWrap = 32 * size / 1024 * noiseScale;
            var SampleNoise = (Int3 p) => {
                p *= noiseScale;
                float b = PerlinIntNoise.GetAt(p * 1, 1 * noiseWrap) * 1.0f
                    + PerlinIntNoise.GetAt(p * 2, 2 * noiseWrap) * 0.5f
                    + PerlinIntNoise.GetAt(p * 4, 4 * noiseWrap) * 0.25f
                    + PerlinIntNoise.GetAt(p * 8, 8 * noiseWrap) * 0.125f
                    + PerlinIntNoise.GetAt(p * 16, 16 * noiseWrap) * 0.0625f
                    + PerlinIntNoise.GetAt(p * 32, 32 * noiseWrap) * (0.0625f / 2.0f)
                    ;
                /*b = Easing.Clamp01(b / 1024f * 20.0f + 0.3f) * 0.8f
                    + Easing.Clamp01(b / 1024f * 0.5f + 0.5f) * 0.1f
                    + 0.1f;*/
                b = Easing.Clamp01(b / 1024f + 0.5f);
                return b;
            };
            var GetRandom3 = (Int3 p, int seed) => {
                const int Scale = 128;
                return new Int3(
                    PerlinIntNoise.GetAt(p * Scale + new Int3(000, 000, 000) + seed, Scale * size / 1024),
                    PerlinIntNoise.GetAt(p * Scale + new Int3(917, 477, 133) + seed, Scale * size / 1024),
                    PerlinIntNoise.GetAt(p * Scale + new Int3(267, 122, 961) + seed, Scale * size / 1024)
                ) / 4;
            };
            for (int z = 0; z < size.Z; z++) {
                for (int y = 0; y < size.Y; y++) {
                    for (int x = 0; x < size.X; x++) {
                        var p = new Int3(x, y, z);
                        var r = SampleNoise(p * 32 + GetRandom3(p, 000));
                        var g = SampleNoise(p * 32 + new Int3(116, 613, 135) + GetRandom3(p, 1573));
                        var b = SampleNoise(p * 32 + new Int3(917, 122, 735) + GetRandom3(p, 793));
                        data[(z * size.Y + y) * size.X + x] =
                            new Color(new Vector4(r, g, b, 1.0f));
                    }
                }
            }
            volumeTex.MarkChanged();
            volumeTex.GenerateMips();
            volumeTex.CompressTexture();
            return volumeTex;
        }
    }
}
