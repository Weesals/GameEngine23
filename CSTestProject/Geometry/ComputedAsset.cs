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
    public class NoiseTexture2D : ComputedAsset {
        public CSTexture Require() {
            const string CacheName = "$tex_noise2d";
            var noiseTex = Resources.TryLoadTexture(CacheName);
            if (!noiseTex.IsValid) {
                noiseTex = CSTexture.Create("Noise2D");
                noiseTex.SetSize(128);
                Generate(noiseTex, 8);
                Resources.TryPutTexture(CacheName, noiseTex);
            }
            return noiseTex;
        }
        public void Generate(CSTexture noiseTex, int frequency = 8) {
            var data = noiseTex.GetTextureData().Reinterpret<Color>();
            var size = noiseTex.GetSize();
            var noiseWrap = frequency;
            var noiseScale = noiseWrap * 1024 / size;
            var SampleNoise = (Int2 p) => {
                float b = PerlinIntNoise.GetAt(p * 1, 1 * noiseWrap) * 1.0f
                    + PerlinIntNoise.GetAt(p * 2, 2 * noiseWrap) * (1.0f / 2.0f)
                    + PerlinIntNoise.GetAt(p * 4, 4 * noiseWrap) * (1.0f / 4.0f)
                    + PerlinIntNoise.GetAt(p * 8, 8 * noiseWrap) * (1.0f / 8.0f)
                    + PerlinIntNoise.GetAt(p * 16, 16 * noiseWrap) * (1.0f / 16.0f)
                    + PerlinIntNoise.GetAt(p * 32, 32 * noiseWrap) * (1.0f / 32.0f)
                    + PerlinIntNoise.GetAt(p * 64, 64 * noiseWrap) * (1.0f / 64.0f)
                    + PerlinIntNoise.GetAt(p * 128, 128 * noiseWrap) * (1.0f / 128.0f)
                    ;
                b = Easing.Clamp01(b / 1024f + 0.5f);
                return b;
            };
            for (int y = 0; y < size.Y; y++) {
                for (int x = 0; x < size.X; x++) {
                    var p = new Int2(x, y);
                    var r = SampleNoise(p * noiseScale + new Int2(000, 000));
                    var g = SampleNoise(p * noiseScale + new Int2(116, 613));
                    var b = SampleNoise(p * noiseScale + new Int2(917, 122));
                    data[y * size.X + x] = new Color(new Vector4(r, g, b, 1.0f));
                }
            }
            noiseTex.MarkChanged();
            noiseTex.GenerateMips();
            noiseTex.CompressTexture();
        }
    }
    public class NoiseTexture3D : ComputedAsset {
        public CSTexture Require() {
            const string CacheName = "$tex_noise3d";
            var volumeTex = Resources.TryLoadTexture(CacheName);
            if (!volumeTex.IsValid) {
                volumeTex = CSTexture.Create("Noise3D");
                volumeTex.SetSize3D(128);
                Generate(volumeTex, 2);
                Resources.TryPutTexture(CacheName, volumeTex);
            }
            return volumeTex;
        }
        public CSTexture Generate(CSTexture volumeTex, int frequency = 2) {
            var data = volumeTex.GetTextureData().Reinterpret<Color>();
            var size = volumeTex.GetSize3D();
            var noiseWrap = frequency;
            var noiseScale = noiseWrap * 1024 / size;
            var SampleNoise = (Int3 p) => {
                float b = PerlinIntNoise.GetAt(p * 1, 1 * noiseWrap) * 1.0f
                    + PerlinIntNoise.GetAt(p * 2, 2 * noiseWrap) * (1.0f / 2.0f)
                    + PerlinIntNoise.GetAt(p * 4, 4 * noiseWrap) * (1.0f / 4.0f)
                    + PerlinIntNoise.GetAt(p * 8, 8 * noiseWrap) * (1.0f / 8.0f)
                    + PerlinIntNoise.GetAt(p * 16, 16 * noiseWrap) * (1.0f / 16.0f)
                    + PerlinIntNoise.GetAt(p * 32, 32 * noiseWrap) * (1.0f / 32.0f)
                    + PerlinIntNoise.GetAt(p * 64, 64 * noiseWrap) * (1.0f / 64.0f)
                    + PerlinIntNoise.GetAt(p * 128, 128 * noiseWrap) * (1.0f / 128.0f)
                    ;
                b = Easing.Clamp01(b / 1024f + 0.5f);
                return b;
            };
            var GetRandom3 = (Int3 p, int seed) => {
                const int Scale = 64;
                return new Int3(
                    PerlinIntNoise.GetAt(p * Scale + new Int3(000, 000, 000) + seed, Scale * size / 1024),
                    PerlinIntNoise.GetAt(p * Scale + new Int3(917, 477, 133) + seed, Scale * size / 1024),
                    PerlinIntNoise.GetAt(p * Scale + new Int3(267, 122, 961) + seed, Scale * size / 1024)
                ) * noiseScale / 32;
            };
            for (int z = 0; z < size.Z; z++) {
                for (int y = 0; y < size.Y; y++) {
                    for (int x = 0; x < size.X; x++) {
                        var p = new Int3(x, y, z);
                        var r = SampleNoise(p * noiseScale + new Int3(000, 000, 000) + GetRandom3(p, 0000) + GetRandom3(p * 2, 0000) / 2);
                        var g = SampleNoise(p * noiseScale + new Int3(116, 613, 135) + GetRandom3(p, 1573) + GetRandom3(p * 2, 1573) / 2);
                        var b = SampleNoise(p * noiseScale + new Int3(917, 122, 735) + GetRandom3(p, 0793) + GetRandom3(p * 2, 0793) / 2);
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
