using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine.Profiling;
using Weesals.Utility;

namespace Weesals.Engine {
    public static class MipGenerator {
        private static ProfilerMarker ProfileMarker_GenerateMips = new("Generate Mips");
        private static ProfilerMarker ProfileMarker_GenerateMipsSlow = new("Generate Mips Slow");
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
            using var marker = ProfileMarker_GenerateMips.Auto();
            if (tex.GetIsCompressed()) {
                Debug.WriteLine("Cannot mip a compressed texture. Generate mips before compression");
                return;
            }

            var size = tex.GetSize3D();
            int mips = CalculateMipCount(tex);
            tex.SetMipCount(mips);
            for (int s = 0; s < tex.GetArrayCount(); ++s) {
                var srcRes = GetMipResolution(size, tex.GetFormat(), 0);
                var srcData = tex.GetTextureData(0, s).Reinterpret<Color>();
                for (int m = 1; m < tex.GetMipCount(); ++m) {
                    var dstRes = GetMipResolution(size, tex.GetFormat(), m);
                    var dstData = tex.GetTextureData(m, s).Reinterpret<Color>();
                    DownsampleBlock(dstData, dstRes, srcData, srcRes);
                    srcRes = dstRes;
                    srcData = dstData;
                }
            }
            tex.MarkChanged();
        }

        unsafe private static void DownsampleBlock(MemoryBlock<Color> dstData, Int3 dstRes, MemoryBlock<Color> srcData, Int3 srcRes) {
            var SrcL = Vector<byte>.Count;
            var DstL = Vector<ushort>.Count;
            if (((int)srcData.Data & (SrcL - 1)) == 0   // Aligned
                && srcRes.X == dstRes.X * 2 && srcRes.Y == dstRes.Y * 2 && (srcRes.Z == dstRes.Z || srcRes.Z == dstRes.Z * 2)   // Mag2
                && ((srcRes.X * 4) & (SrcL - 1)) == 0      // Src is big enough
            ) {
                DownsampleBlockSIMD(dstData, dstRes, srcData, srcRes);
                return;
            }
            for (int z = 0; z < dstRes.Z; ++z) {
                int sZ = (z + 0) * srcRes.Z / dstRes.Z, eZ = (z + 1) * srcRes.Z / dstRes.Z;
                for (int y = 0; y < dstRes.Y; ++y) {
                    int sY = (y + 0) * srcRes.Y / dstRes.Y, eY = (y + 1) * srcRes.Y / dstRes.Y;
                    for (int x = 0; x < dstRes.X; ++x) {
                        int sX = (x + 0) * srcRes.X / dstRes.X, eX = (x + 1) * srcRes.X / dstRes.X;
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
                        int bias = count / 2;
                        r = (r + bias) / count; g = (g + bias) / count; b = (b + bias) / count; a = (a + bias) / count;
                        dstData[(z * dstRes.Y + y) * dstRes.X + x]
                            = new Color((byte)(r), (byte)(g), (byte)(b), (byte)(a));
                    }
                }
            }
        }
        unsafe private static void DownsampleBlockSIMD(MemoryBlock<Color> dstData, Int3 dstRes, MemoryBlock<Color> srcData, Int3 srcRes) {
            var SrcL = Vector<byte>.Count;
            var DstL = Vector<ushort>.Count;

            var yStride = srcRes.X * 4;
            var zStride = srcRes.Y * yStride;
            int zMag = srcRes.Z / dstRes.Z, yMag = srcRes.Y / dstRes.Y;
            var shift = (zMag - 1) + (yMag - 1) + 1;
            var bias = new Vector<ushort>((ushort)(1 << (shift - 1)));
            var ySize = yMag * yStride;
            var zSize = zMag * zStride;
            for (int z = 0; z < dstRes.Z; ++z) {
                for (int y = 0; y < dstRes.Y; ++y) {
                    byte* dstDataX = (byte*)dstData.Data + ((z * dstRes.Y + y) * dstRes.X) * 4;
                    var orow = Vector<ushort>.Zero;
                    bool store = false;
                    byte* dataX0 = (byte*)srcData.Data + z * zSize + y * ySize, dataX1 = dataX0 + yStride;
                    for (var dataX = dataX0; dataX < dataX1; dataX += SrcL) {
                        var row = Vector<ushort>.Zero;
                        byte* dataZ0 = dataX, dataZ1 = dataZ0 + zSize;
                        for (var dataZ = dataZ0; ; ) {
                            [MethodImpl(MethodImplOptions.AggressiveInlining)]
                            static Vector<ushort> ProcessBlock(Vector<ulong> vec) {
                                var colors = Vector.Narrow(vec, Vector.ShiftRightLogical(vec, 32)).As<uint, byte>();
                                Vector.Widen(colors, out var cl, out var cr);
                                return Vector.Add(cl, cr);
                            }
                            var rD0 = Vector.LoadAligned((ulong*)dataZ);
                            var rD1 = Vector.LoadAligned((ulong*)(dataZ + yStride));
                            row = Vector.Add(row, ProcessBlock(rD0));
                            row = Vector.Add(row, ProcessBlock(rD1));
                            if ((dataZ += zStride) >= dataZ1) break;
                        }
                        row = Vector.ShiftRightLogical(Vector.Add(row, bias), shift);
                        if (store) {
                            Vector.Narrow(orow, row).CopyTo(new Span<byte>(dstDataX, DstL * 2));
                            dstDataX += DstL * 2;
                        }
                        orow = row;
                        store = !store;
                    }
                    if (store) {
                        for (int i = 0; i < DstL; i++) {
                            dstDataX[i] = (byte)orow[i];
                        }
                    }
                }
            }
        }

        public static void GenerateMips(this CSTexture tex, bool normalizeAlpha) {
            if (!normalizeAlpha) { GenerateMips(tex); return; }
            using var marker = ProfileMarker_GenerateMipsSlow.Auto();
            if (tex.GetIsCompressed()) {
                Debug.WriteLine("Cannot mip a compressed texture. Generate mips before compression");
                return;
            }

            var size = tex.GetSize3D();
            int mips = CalculateMipCount(tex);
            tex.SetMipCount(mips);
            for (int s = 0; s < tex.GetArrayCount(); ++s) {
                var srcRes = GetMipResolution(size, tex.GetFormat(), 0);
                var srcData = tex.GetTextureData(0, s).Reinterpret<Color>();
                for (int m = 1; m < tex.GetMipCount(); ++m) {
                    var dstRes = GetMipResolution(size, tex.GetFormat(), m);
                    var dstData = tex.GetTextureData(m, s).Reinterpret<Color>();
                    var srcTAlpha = 0;
                    var dstTAlpha = 0;
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
                                            srcTAlpha += a > 127 ? 1 : 0;
                                        }
                                    }
                                }
                                int count = (eX - sX) * (eY - sY) * (eZ - sZ);
                                int bias = count / 2;
                                r = (r + bias) / count; g = (g + bias) / count; b = (b + bias) / count; a = (a + bias) / count;
                                dstTAlpha += a > 127 ? 1 : 0;
                                dstData[(z * dstRes.Y + y) * dstRes.X + x]
                                    = new Color((byte)(r), (byte)(g), (byte)(b), (byte)(a));
                            }
                        }
                    }
                    if (normalizeAlpha) {
                        int alphaDiff = 256 * srcTAlpha / (srcRes.X * srcRes.Y) - 256 * dstTAlpha / (dstRes.X * dstRes.Y);
                        alphaDiff /= 10;
                        if (Math.Abs(alphaDiff) >= 1) {
                            for (int i = 0; i < dstData.Length; i++) {
                                ref var c = ref dstData[i];
                                var delta = 127 - Math.Abs(c.A - 127);
                                c.A = (byte)(c.A + delta * alphaDiff / 256);
                            }
                        }
                    }
                    //srcData.Slice(0, dstData.Length).CopyTo(dstData);
                    srcRes = dstRes;
                    srcData = dstData;
                }
            }
            tex.MarkChanged();
        }
        private static Random rnd = new();
    }
}
