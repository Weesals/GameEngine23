using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Weesals.Engine {
    public static class MipGenerator {
        public static int GetSliceSize(Int2 res, int mips, BufferFormat fmt) {
            var imgSize = GetRawImageSize(res, fmt);
            var sliceStride = imgSize;
            for (int m = 1; m < mips; ++m) {
                var mipSize = GetMipResolution(res, fmt, m);
                sliceStride += GetRawImageSize(mipSize, fmt);
            }
            return sliceStride;
        }
        public static Int2 GetMipResolution(Int2 res, BufferFormat fmt, int mip) {
            return new Int2(
                Math.Max(1, res.X >> mip),
                Math.Max(1, res.Y >> mip)
            );
        }
        public static int GetRawImageSize(Int2 res, BufferFormat fmt) {
            // TODO: Support compressed formats
            return res.X * res.Y * BufferFormatType.GetMeta(fmt).GetByteSize();
        }

        public static void GenerateMips(this CSTexture tex) {
            var size = tex.GetSize();
            int mips = 31 - BitOperations.LeadingZeroCount(Math.Max((uint)size.X, (uint)size.Y));
            tex.SetMipCount(mips);
            for (int s = 0; s < tex.GetArrayCount(); ++s) {
                var srcRes = GetMipResolution(tex.GetSize(), tex.GetFormat(), 0);
                var srcData = tex.GetTextureData(0, s).Reinterpret<Color>();
                for (int m = 1; m < tex.GetMipCount(); ++m) {
                    var dstRes = GetMipResolution(tex.GetSize(), tex.GetFormat(), m);
                    var dstData = tex.GetTextureData(m, s).Reinterpret<Color>();
                    for (int y = 0; y < dstRes.Y; ++y) {
                        for (int x = 0; x < dstRes.Y; ++x) {
                            int sX = x * 2, sY = y * 2;
                            int r = 0, g = 0, b = 0, a = 0;
                            for (int iy = 0; iy < 2; iy++) {
                                for (int ix = 0; ix < 2; ix++) {
                                    Color srcCol = srcData[(sX + ix) + (sY + iy) * srcRes.X];
                                    r += srcCol.R;
                                    g += srcCol.G;
                                    b += srcCol.B;
                                    a += srcCol.A;
                                }
                            }
                            dstData[x + y * dstRes.X] = new Color((byte)(r / 4), (byte)(g / 4), (byte)(b / 4), (byte)(a / 4));
                        }
                    }
                    //srcData.Slice(0, dstData.Length).CopyTo(dstData);
                    srcRes = dstRes;
                    srcData = dstData;
                }
            }
        }
    }
}
