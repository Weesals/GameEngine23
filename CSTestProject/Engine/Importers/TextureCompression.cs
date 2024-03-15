using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Buffers;
using System.Diagnostics;

namespace Weesals.Engine {
    unsafe static public class TextureCompression {
        public struct RGBASurface {
            public byte* ptr;
            public int width, height;
            public int stride; // in bytes
        }

        public struct bc7_enc_settings {
            [InlineArray(4)]
            public struct mode_selectionArray { public bool value; }

            [InlineArray(8)]
            public struct refineIterationsArray { public int value; }

            public mode_selectionArray mode_selection;
            public refineIterationsArray refineIterations;

            public bool skip_mode2;
            public int fastSkipTreshold_mode1;
            public int fastSkipTreshold_mode3;
            public int fastSkipTreshold_mode7;

            public int mode45_channel0;
            public int refineIterations_channel;

            public int channels;
        };

        public struct bc6h_enc_settings {
            public bool slow_mode;
            public bool fast_mode;
            public int refineIterations_1p;
            public int refineIterations_2p;
            public int fastSkipTreshold;
        };

        public struct etc_enc_settings {
            public int fastSkipTreshold;
        };

        public struct astc_enc_settings {
            public int block_width;
            public int block_height;
            public int channels;
            
            public int fastSkipTreshold;
            public int refineIterations;
        };

        [DllImport("ispc_texcomp.dll")]
        public static extern void CompressBlocksBC1(RGBASurface* surface, byte* output);

        [DllImport("ispc_texcomp.dll")]
        public static extern void CompressBlocksBC3(RGBASurface* surface, byte* output);

        [DllImport("ispc_texcomp.dll")]
        public static extern void CompressBlocksBC4(RGBASurface* surface, byte* output);

        [DllImport("ispc_texcomp.dll")]
        public static extern void CompressBlocksBC5(RGBASurface* surface, byte* output);

        [DllImport("ispc_texcomp.dll")]
        public static extern void CompressBlocksBC6H(RGBASurface* surface, byte* output, bc6h_enc_settings* settings);

        [DllImport("ispc_texcomp.dll")]
        public static extern void CompressBlocksBC7(RGBASurface* surface, byte* output, bc7_enc_settings* settings);

        [DllImport("ispc_texcomp.dll")]
        public static extern void CompressBlocksETC1(RGBASurface* surface, byte* output, etc_enc_settings* settings);

        [DllImport("ispc_texcomp.dll")]
        public static extern void CompressBlocksASTC(RGBASurface* surface, byte* output, astc_enc_settings* settings);

        unsafe public struct InputData {
            public Int3 size;
            public byte* data;
        };

        [DllImport("CSBindings")]
        public static extern void NVTTCompressTextureBC1(InputData* img, void* outData);
        [DllImport("CSBindings")]
        public static extern void NVTTCompressTextureBC2(InputData* img, void* outData);
        [DllImport("CSBindings")]
        public static extern void NVTTCompressTextureBC3(InputData* img, void* outData);
        [DllImport("CSBindings")]
        public static extern void NVTTCompressTextureBC4(InputData* img, void* outData);
        [DllImport("CSBindings")]
        public static extern void NVTTCompressTextureBC5(InputData* img, void* outData);



        unsafe static void ConvertC4toC3(byte* dst, byte* src, int count) {
            var end = src + count * 4;
            for (; src < end; src += 1) { *(dst++) = *(src++); *(dst++) = *(src++); *(dst++) = *(src++); }
        }
        unsafe static void ConvertC4toC2(byte* dst, byte* src, int count) {
            var end = src + count * 4;
            for (; src < end; src += 2) { *(dst++) = *(src++); *(dst++) = *(src++); }
        }
        unsafe static void ConvertC4toC1(byte* dst, byte* src, int count) {
            var end = src + count * 4;
            for (; src < end; src += 3) { *(dst++) = *(src++); }
        }
        unsafe static void ConvertC3toC2(byte* dst, byte* src, int count) {
            var end = src + count * 3;
            for (; src < end; src += 1) { *(dst++) = *(src++); *(dst++) = *(src++); }
        }
        unsafe static void ConvertC3toC1(byte* dst, byte* src, int count) {
            var end = src + count * 3;
            for (; src < end; src += 2) { *(dst++) = *(src++); }
        }
        unsafe static void ConvertC2toC1(byte* dst, byte* src, int count) {
            var end = src + count * 2;
            for (; src < end; src += 1) { *(dst++) = *(src++); }
        }
        unsafe public static void CompressTexture(this CSTexture other, BufferFormat compressedFormat = BufferFormat.FORMAT_BC1_UNORM) {
            var compressed = CreateCompressed(other, compressedFormat);
            compressed.Swap(other);
            compressed.Dispose();
        }
        unsafe public static CSTexture CreateCompressed(this CSTexture other, BufferFormat compressedFormat = BufferFormat.FORMAT_BC1_UNORM) {
            var otherSize = other.GetSize3D();
            var otherFormat = other.Format;
            var otherFormatMeta = BufferFormatType.GetMeta(otherFormat);
            var otherPixelSize = otherFormatMeta.GetByteSize();
            var newTex = CSTexture.Create("Compressed");
            newTex.SetFormat(compressedFormat);
            newTex.SetSize3D(other.GetSize3D());
            newTex.SetMipCount(other.MipCount);
            newTex.SetArrayCount(other.ArrayCount);
            delegate*<byte*, byte*, int, void> converter = null;
            Debug.Assert(otherFormatMeta.GetSize() == BufferFormatType.Sizes.Size8);
            int srcChanCount = otherFormatMeta.GetComponentCount();
            int dstChanCount = BufferFormatType.GetMeta(compressedFormat).GetComponentCount();
            if (srcChanCount != dstChanCount) {
                if (dstChanCount == 4) {
                    switch (srcChanCount) {
                        default: break;
                    }
                } else if (dstChanCount == 3) {
                    switch (srcChanCount) {
                        case 4: converter = &ConvertC4toC3; break;
                    }
                } else if (dstChanCount == 2) {
                    switch (srcChanCount) {
                        case 4: converter = &ConvertC4toC2; break;
                        case 3: converter = &ConvertC3toC2; break;
                    }
                } else if (dstChanCount == 1) {
                    switch (srcChanCount) {
                        case 4: converter = &ConvertC4toC1; break;
                        case 3: converter = &ConvertC3toC1; break;
                        case 2: converter = &ConvertC2toC1; break;
                    }
                }
                if (converter == null) {
                    throw new NotImplementedException();
                }
            }
            byte[] tempArray = Array.Empty<byte>();
            if (converter != null) {
                tempArray = ArrayPool<byte>.Shared.Rent(otherSize.X * otherSize.Y * dstChanCount);
            }
            fixed (byte* arrData = tempArray) {
                for (int s = 0; s < other.ArrayCount; s++) {
                    for (int m = 0; m < other.MipCount; m++) {
                        int useMip = Math.Min(m, other.MipCount - 3);
                        var mipRes = MipGenerator.GetMipResolution(otherSize, otherFormat, useMip);
                        var srcData = other.GetTextureData(useMip, s);
                        srcData.Length /= otherSize.Z;
                        var outData = newTex.GetTextureData(m, s);
                        outData.Length /= otherSize.Z;
                        for (int d = 0; d < otherSize.Z; d++) {
                            if (converter != null) converter(arrData, srcData.Data, mipRes.X * mipRes.Y);
#if false
                        var nvttData = new TextureCompression.InputData() {
                            size = mipRes,
                            data = tempArray.Length > 0 ? arrData : srcData,
                        };
                        if (compressedFormat >= BufferFormat.FORMAT_BC1_TYPELESS && compressedFormat <= BufferFormat.FORMAT_BC1_UNORM_SRGB) {
                            TextureCompression.NVTTCompressTextureBC1(&nvttData, outData.Data);
                        } else if (compressedFormat >= BufferFormat.FORMAT_BC2_TYPELESS && compressedFormat <= BufferFormat.FORMAT_BC2_UNORM_SRGB) {
                            TextureCompression.NVTTCompressTextureBC2(&nvttData, outData.Data);
                        } else if (compressedFormat >= BufferFormat.FORMAT_BC3_TYPELESS && compressedFormat <= BufferFormat.FORMAT_BC3_UNORM_SRGB) {
                            TextureCompression.NVTTCompressTextureBC3(&nvttData, outData.Data);
                        } else if (compressedFormat >= BufferFormat.FORMAT_BC4_TYPELESS && compressedFormat <= BufferFormat.FORMAT_BC4_SNORM) {
                            TextureCompression.NVTTCompressTextureBC4(&nvttData, outData.Data);
                        } else if (compressedFormat >= BufferFormat.FORMAT_BC5_TYPELESS && compressedFormat <= BufferFormat.FORMAT_BC5_SNORM) {
                            TextureCompression.NVTTCompressTextureBC5(&nvttData, outData.Data);
                        }
#else
                            var surface = new TextureCompression.RGBASurface() {
                                ptr = tempArray.Length > 0 ? arrData : srcData.Data,
                                width = mipRes.X,
                                height = mipRes.Y,
                                stride = mipRes.X * dstChanCount,
                            };
                            if (compressedFormat >= BufferFormat.FORMAT_BC1_TYPELESS && compressedFormat <= BufferFormat.FORMAT_BC1_UNORM_SRGB) {
                                TextureCompression.CompressBlocksBC1(&surface, outData.Data);
                            } else if (compressedFormat >= BufferFormat.FORMAT_BC2_TYPELESS && compressedFormat <= BufferFormat.FORMAT_BC2_UNORM_SRGB) {
                                //TextureCompression.CompressBlocksBC2(&surface, outData.Data);
                                throw new NotImplementedException();
                            } else if (compressedFormat >= BufferFormat.FORMAT_BC3_TYPELESS && compressedFormat <= BufferFormat.FORMAT_BC3_UNORM_SRGB) {
                                TextureCompression.CompressBlocksBC3(&surface, outData.Data);
                            } else if (compressedFormat >= BufferFormat.FORMAT_BC4_TYPELESS && compressedFormat <= BufferFormat.FORMAT_BC4_SNORM) {
                                TextureCompression.CompressBlocksBC4(&surface, outData.Data);
                            } else if (compressedFormat >= BufferFormat.FORMAT_BC5_TYPELESS && compressedFormat <= BufferFormat.FORMAT_BC5_SNORM) {
                                TextureCompression.CompressBlocksBC5(&surface, outData.Data);
                            }
#endif
                            srcData.Data += srcData.Length;
                            outData.Data += outData.Length;
                        }
                    }
                }
            }
            if (tempArray.Length > 0) ArrayPool<byte>.Shared.Return(tempArray);
            newTex.MarkChanged();
            return newTex;
        }
    }
}
