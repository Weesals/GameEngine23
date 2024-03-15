using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Weesals.Engine {
    public static class MipGenerator {
        public static int GetSliceSize(Int3 res, int mips, BufferFormat fmt) {
            var imgSize = GetRawImageSize(res, fmt);
            var sliceStride = imgSize;
            for (int m = 1; m < mips; ++m) {
                var mipSize = GetMipResolution(res, fmt, m);
                sliceStride += GetRawImageSize(mipSize, fmt);
            }
            return sliceStride;
        }
        public static Int3 GetMipResolution(Int3 res, BufferFormat fmt, int mip) {
            return new Int3(
                Math.Max(1, res.X >> mip),
                Math.Max(1, res.Y >> mip),
                Math.Max(1, res.Z >> mip)
            );
        }
        public static int GetRawImageSize(Int3 res, BufferFormat fmt) {
            // TODO: Support compressed formats
            return res.X * res.Y * res.Z * BufferFormatType.GetMeta(fmt).GetByteSize();
        }
        public static unsafe int CalculateMipCount(CSTexture tex) {
            var size = tex.GetSize3D();
            return 32 - BitOperations.LeadingZeroCount(
                Math.Max(Math.Max((uint)size.X, (uint)size.Y), (uint)size.Z));
        }
        public static void GenerateMips(this CSTexture tex) {
            var size = tex.GetSize3D();
            int mips = CalculateMipCount(tex);
            tex.SetMipCount(mips);
            for (int s = 0; s < tex.GetArrayCount(); ++s) {
                var srcRes = GetMipResolution(size, tex.GetFormat(), 0);
                var srcData = tex.GetTextureData(0, s).Reinterpret<Color>();
                for (int m = 1; m < tex.GetMipCount(); ++m) {
                    var dstRes = GetMipResolution(size, tex.GetFormat(), m);
                    var dstData = tex.GetTextureData(m, s).Reinterpret<Color>();
                    for (int z = 0; z < dstRes.Z; ++z) {
                        int sZ = (z + 0) * srcRes.Z / dstRes.Z;
                        int eZ = (z + 1) * srcRes.Z / dstRes.Z;
                        for (int y = 0; y < dstRes.Y; ++y) {
                            int sY = (y + 0) * srcRes.Y / dstRes.Y;
                            int eY = (y + 1) * srcRes.Y / dstRes.Y;
                            for (int x = 0; x < dstRes.X; ++x) {
                                int sX = (x + 0) * srcRes.X / dstRes.X;
                                int eX = (x + 1) * srcRes.X / dstRes.X;
                                int r = 0, g = 0, b = 0, a = 0;
                                for (int iz = sZ; iz < eZ; iz++) {
                                    for (int iy = sY; iy < eY; iy++) {
                                        for (int ix = sX; ix < eX; ix++) {
                                            Color srcCol = srcData[(iz * srcRes.Y + iy) * srcRes.X + ix];
                                            r += srcCol.R;
                                            g += srcCol.G;
                                            b += srcCol.B;
                                            a += srcCol.A;
                                        }
                                    }
                                }
                                int count = (eX - sX) * (eY - sY) * (eZ - sZ);
                                dstData[(z * dstRes.Y + y) * dstRes.X + x]
                                    = new Color((byte)(r / count), (byte)(g / count), (byte)(b / count), (byte)(a / count));
                            }
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
