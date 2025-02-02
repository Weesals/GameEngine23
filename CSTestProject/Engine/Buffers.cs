using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Weesals.Utility;

namespace Weesals.Engine {
    public enum BufferFormat : byte {
        FORMAT_UNKNOWN = 0,
        FORMAT_R32G32B32A32_TYPELESS = 1,
        FORMAT_R32G32B32A32_FLOAT = 2,
        FORMAT_R32G32B32A32_UINT = 3,
        FORMAT_R32G32B32A32_SINT = 4,
        FORMAT_R32G32B32_TYPELESS = 5,
        FORMAT_R32G32B32_FLOAT = 6,
        FORMAT_R32G32B32_UINT = 7,
        FORMAT_R32G32B32_SINT = 8,
        FORMAT_R16G16B16A16_TYPELESS = 9,
        FORMAT_R16G16B16A16_FLOAT = 10,
        FORMAT_R16G16B16A16_UNORM = 11,
        FORMAT_R16G16B16A16_UINT = 12,
        FORMAT_R16G16B16A16_SNORM = 13,
        FORMAT_R16G16B16A16_SINT = 14,
        FORMAT_R32G32_TYPELESS = 15,
        FORMAT_R32G32_FLOAT = 16,
        FORMAT_R32G32_UINT = 17,
        FORMAT_R32G32_SINT = 18,
        FORMAT_R32G8X24_TYPELESS = 19,
        FORMAT_D32_FLOAT_S8X24_UINT = 20,
        FORMAT_R32_FLOAT_X8X24_TYPELESS = 21,
        FORMAT_X32_TYPELESS_G8X24_UINT = 22,
        FORMAT_R10G10B10A2_TYPELESS = 23,
        FORMAT_R10G10B10A2_UNORM = 24,
        FORMAT_R10G10B10A2_UINT = 25,
        FORMAT_R11G11B10_FLOAT = 26,
        FORMAT_R8G8B8A8_TYPELESS = 27,
        FORMAT_R8G8B8A8_UNORM = 28,
        FORMAT_R8G8B8A8_UNORM_SRGB = 29,
        FORMAT_R8G8B8A8_UINT = 30,
        FORMAT_R8G8B8A8_SNORM = 31,
        FORMAT_R8G8B8A8_SINT = 32,
        FORMAT_R16G16_TYPELESS = 33,
        FORMAT_R16G16_FLOAT = 34,
        FORMAT_R16G16_UNORM = 35,
        FORMAT_R16G16_UINT = 36,
        FORMAT_R16G16_SNORM = 37,
        FORMAT_R16G16_SINT = 38,
        FORMAT_R32_TYPELESS = 39,
        FORMAT_D32_FLOAT = 40,
        FORMAT_R32_FLOAT = 41,
        FORMAT_R32_UINT = 42,
        FORMAT_R32_SINT = 43,
        FORMAT_R24G8_TYPELESS = 44,
        FORMAT_D24_UNORM_S8_UINT = 45,
        FORMAT_R24_UNORM_X8_TYPELESS = 46,
        FORMAT_X24_TYPELESS_G8_UINT = 47,
        FORMAT_R8G8_TYPELESS = 48,
        FORMAT_R8G8_UNORM = 49,
        FORMAT_R8G8_UINT = 50,
        FORMAT_R8G8_SNORM = 51,
        FORMAT_R8G8_SINT = 52,
        FORMAT_R16_TYPELESS = 53,
        FORMAT_R16_FLOAT = 54,
        FORMAT_D16_UNORM = 55,
        FORMAT_R16_UNORM = 56,
        FORMAT_R16_UINT = 57,
        FORMAT_R16_SNORM = 58,
        FORMAT_R16_SINT = 59,
        FORMAT_R8_TYPELESS = 60,
        FORMAT_R8_UNORM = 61,
        FORMAT_R8_UINT = 62,
        FORMAT_R8_SNORM = 63,
        FORMAT_R8_SINT = 64,
        FORMAT_A8_UNORM = 65,
        FORMAT_R1_UNORM = 66,
        FORMAT_R9G9B9E5_SHAREDEXP = 67,
        FORMAT_R8G8_B8G8_UNORM = 68,
        FORMAT_G8R8_G8B8_UNORM = 69,
        FORMAT_BC1_TYPELESS = 70,
        FORMAT_BC1_UNORM = 71,
        FORMAT_BC1_UNORM_SRGB = 72,
        FORMAT_BC2_TYPELESS = 73,
        FORMAT_BC2_UNORM = 74,
        FORMAT_BC2_UNORM_SRGB = 75,
        FORMAT_BC3_TYPELESS = 76,
        FORMAT_BC3_UNORM = 77,
        FORMAT_BC3_UNORM_SRGB = 78,
        FORMAT_BC4_TYPELESS = 79,
        FORMAT_BC4_UNORM = 80,
        FORMAT_BC4_SNORM = 81,
        FORMAT_BC5_TYPELESS = 82,
        FORMAT_BC5_UNORM = 83,
        FORMAT_BC5_SNORM = 84,
        FORMAT_B5G6R5_UNORM = 85,
        FORMAT_B5G5R5A1_UNORM = 86,
        FORMAT_B8G8R8A8_UNORM = 87,
        FORMAT_B8G8R8X8_UNORM = 88,
    }
    unsafe struct BufferFormatType {
        public enum Types : byte {
            SNrm = 0b000, SInt = 0b001,
            UNrm = 0b010, UInt = 0b011,
            Float = 0b101, TLss = 0b111,
        }
        public enum Sizes : byte {
            Size32, Size16, Size8,
            Size5651, Size1010102, Size444,
            Size9995, Other
        }
        byte mPacked;
        public byte GetPacked() { return mPacked; }
        public bool IsInt() { return (mPacked & 0b101) == 0b001; }
        public bool IsIntOrNrm() { return (mPacked & 0b100) == 0b000; }
        public bool IsFloat() { return (Types)(mPacked & 0b111) == Types.Float; }
        public bool IsNormalized() { return (mPacked & 0b001) == 0b000; }
        public bool IsSigned() { return (mPacked & 0b010) == 0b000; }
        public int GetComponentCount() { return (mPacked >> 6) + 1; }
        public Sizes GetSize() { return (Sizes)((mPacked >> 3) & 0x03); }
        public int GetByteSize() {
            switch (GetSize()) {
                case Sizes.Size32: return GetComponentCount() * 4;
                case Sizes.Size16: return GetComponentCount() * 2;
                case Sizes.Size8: return GetComponentCount() * 1;
                default: break;
            }
            return -1;
        }
        BufferFormatType(Types type, Sizes size, byte cmp) {
            mPacked = (byte)(((int)type) | ((int)size << 3) | ((cmp - 1) << 6));
        }
        public static BufferFormatType GetMeta(BufferFormat fmt) {
            return Metadata[(int)fmt];
        }
        public static bool operator ==(BufferFormatType o1, BufferFormatType o2) { return o1.mPacked == o2.mPacked; }
        public static bool operator !=(BufferFormatType o1, BufferFormatType o2) { return o1.mPacked != o2.mPacked; }
        public override bool Equals(object? obj) { return obj is BufferFormatType type && type == this; }
        public override int GetHashCode() { return mPacked; }
        public static bool GetIsDepthBuffer(BufferFormat fmt) {
            return (depthMask[(int)fmt >> 7] & (1ul << ((int)fmt & 63))) != 0;
        }
        static readonly ulong[] depthMask = new ulong[] {
            /*   */0b0000000010000000001000010000000000000000000100000000000000000000,
            /*   */0b0000000000000000000000000000000000000000000000000000000000000000,
			//  	^64		^56		^48		^40		^32		^24		^16		^8		^0
		};
        static readonly BufferFormatType[] Metadata = new[] {
            new BufferFormatType(Types.TLss, Sizes.Other, 0),//FORMAT_UNKNOWN = 0,
			new BufferFormatType(Types.TLss, Sizes.Size32, 4),//FORMAT_R32G32B32A32_TYPELESS = 1,
			new BufferFormatType(Types.Float, Sizes.Size32, 4),//FORMAT_R32G32B32A32_FLOAT = 2,
			new BufferFormatType(Types.UInt, Sizes.Size32, 4),//FORMAT_R32G32B32A32_UINT = 3,
			new BufferFormatType(Types.SInt, Sizes.Size32, 4),//FORMAT_R32G32B32A32_SINT = 4,
			new BufferFormatType(Types.TLss, Sizes.Size32, 3),//FORMAT_R32G32B32_TYPELESS = 5,
			new BufferFormatType(Types.Float, Sizes.Size32, 3),//FORMAT_R32G32B32_FLOAT = 6,
			new BufferFormatType(Types.UInt, Sizes.Size32, 3),//FORMAT_R32G32B32_UINT = 7,
			new BufferFormatType(Types.SInt, Sizes.Size32, 3),//FORMAT_R32G32B32_SINT = 8,
			new BufferFormatType(Types.TLss, Sizes.Size16, 4),//FORMAT_R16G16B16A16_TYPELESS = 9,
			new BufferFormatType(Types.Float, Sizes.Size16, 4),//FORMAT_R16G16B16A16_FLOAT = 10,
			new BufferFormatType(Types.UNrm, Sizes.Size16, 4),//FORMAT_R16G16B16A16_UNORM = 11,
			new BufferFormatType(Types.UInt, Sizes.Size16, 4),//FORMAT_R16G16B16A16_UINT = 12,
			new BufferFormatType(Types.SNrm, Sizes.Size16, 4),//FORMAT_R16G16B16A16_SNORM = 13,
			new BufferFormatType(Types.SInt, Sizes.Size16, 4),//FORMAT_R16G16B16A16_SINT = 14,
			new BufferFormatType(Types.TLss, Sizes.Size32, 2),//FORMAT_R32G32_TYPELESS = 15,
			new BufferFormatType(Types.Float, Sizes.Size32, 2),//FORMAT_R32G32_FLOAT = 16,
			new BufferFormatType(Types.UInt, Sizes.Size32, 2),//FORMAT_R32G32_UINT = 17,
			new BufferFormatType(Types.SInt, Sizes.Size32, 2),//FORMAT_R32G32_SINT = 18,
			new BufferFormatType(Types.TLss, Sizes.Size32, 2),//FORMAT_R32G8X24_TYPELESS = 19,
			new BufferFormatType(Types.UInt, Sizes.Size32, 1),//FORMAT_D32_FLOAT_S8X24_UINT = 20,
			new BufferFormatType(Types.TLss, Sizes.Size32, 1),//FORMAT_R32_FLOAT_X8X24_TYPELESS = 21,
			new BufferFormatType(Types.UInt, Sizes.Size32, 1),//FORMAT_X32_TYPELESS_G8X24_UINT = 22,
			new BufferFormatType(Types.TLss, Sizes.Size1010102, 4),//FORMAT_R10G10B10A2_TYPELESS = 23,
			new BufferFormatType(Types.UNrm, Sizes.Size1010102, 4),//FORMAT_R10G10B10A2_UNORM = 24,
			new BufferFormatType(Types.UInt, Sizes.Size1010102, 4),//FORMAT_R10G10B10A2_UINT = 25,
			new BufferFormatType(Types.Float, Sizes.Size1010102, 3),//FORMAT_R11G11B10_FLOAT = 26,
			new BufferFormatType(Types.TLss, Sizes.Size8, 4),//FORMAT_R8G8B8A8_TYPELESS = 27,
			new BufferFormatType(Types.UNrm, Sizes.Size8, 4),//FORMAT_R8G8B8A8_UNORM = 28,
			new BufferFormatType(Types.UNrm, Sizes.Size8, 4),//FORMAT_R8G8B8A8_UNORM_SRGB = 29,
			new BufferFormatType(Types.UInt, Sizes.Size8, 4),//FORMAT_R8G8B8A8_UINT = 30,
			new BufferFormatType(Types.SNrm, Sizes.Size8, 4),//FORMAT_R8G8B8A8_SNORM = 31,
			new BufferFormatType(Types.SInt, Sizes.Size8, 4),//FORMAT_R8G8B8A8_SINT = 32,
			new BufferFormatType(Types.TLss, Sizes.Size16, 2),//FORMAT_R16G16_TYPELESS = 33,
			new BufferFormatType(Types.Float, Sizes.Size16, 2),//FORMAT_R16G16_FLOAT = 34,
			new BufferFormatType(Types.UNrm, Sizes.Size16, 2),//FORMAT_R16G16_UNORM = 35,
			new BufferFormatType(Types.UInt, Sizes.Size16, 2),//FORMAT_R16G16_UINT = 36,
			new BufferFormatType(Types.SNrm, Sizes.Size16, 2),//FORMAT_R16G16_SNORM = 37,
			new BufferFormatType(Types.SInt, Sizes.Size16, 2),//FORMAT_R16G16_SINT = 38,
			new BufferFormatType(Types.TLss, Sizes.Size32, 1),//FORMAT_R32_TYPELESS = 39,
			new BufferFormatType(Types.Float, Sizes.Size32, 1),//FORMAT_D32_FLOAT = 40,
			new BufferFormatType(Types.Float, Sizes.Size32, 1),//FORMAT_R32_FLOAT = 41,
			new BufferFormatType(Types.UInt, Sizes.Size32, 1),//FORMAT_R32_UINT = 42,
			new BufferFormatType(Types.SInt, Sizes.Size32, 1),//FORMAT_R32_SINT = 43,
			new BufferFormatType(Types.TLss, Sizes.Size32, 1),//FORMAT_R24G8_TYPELESS = 44,
			new BufferFormatType(Types.UInt, Sizes.Size32, 1),//FORMAT_D24_UNORM_S8_UINT = 45,
			new BufferFormatType(Types.TLss, Sizes.Size32, 1),//FORMAT_R24_UNORM_X8_TYPELESS = 46,
			new BufferFormatType(Types.UInt, Sizes.Size32, 1),//FORMAT_X24_TYPELESS_G8_UINT = 47,
			new BufferFormatType(Types.TLss, Sizes.Size8, 2),//FORMAT_R8G8_TYPELESS = 48,
			new BufferFormatType(Types.UNrm, Sizes.Size8, 2),//FORMAT_R8G8_UNORM = 49,
			new BufferFormatType(Types.UInt, Sizes.Size8, 2),//FORMAT_R8G8_UINT = 50,
			new BufferFormatType(Types.SNrm, Sizes.Size8, 2),//FORMAT_R8G8_SNORM = 51,
			new BufferFormatType(Types.SInt, Sizes.Size8, 2),//FORMAT_R8G8_SINT = 52,
			new BufferFormatType(Types.TLss, Sizes.Size16, 1),//FORMAT_R16_TYPELESS = 53,
			new BufferFormatType(Types.Float, Sizes.Size16, 1),//FORMAT_R16_FLOAT = 54,
			new BufferFormatType(Types.UNrm, Sizes.Size16, 1),//FORMAT_D16_UNORM = 55,
			new BufferFormatType(Types.UNrm, Sizes.Size16, 1),//FORMAT_R16_UNORM = 56,
			new BufferFormatType(Types.UInt, Sizes.Size16, 1),//FORMAT_R16_UINT = 57,
			new BufferFormatType(Types.SNrm, Sizes.Size16, 1),//FORMAT_R16_SNORM = 58,
			new BufferFormatType(Types.SInt, Sizes.Size16, 1),//FORMAT_R16_SINT = 59,
			new BufferFormatType(Types.TLss, Sizes.Size8, 1),//FORMAT_R8_TYPELESS = 60,
			new BufferFormatType(Types.UNrm, Sizes.Size8, 1),//FORMAT_R8_UNORM = 61,
			new BufferFormatType(Types.UInt, Sizes.Size8, 1),//FORMAT_R8_UINT = 62,
			new BufferFormatType(Types.SNrm, Sizes.Size8, 1),//FORMAT_R8_SNORM = 63,
			new BufferFormatType(Types.SInt, Sizes.Size8, 1),//FORMAT_R8_SINT = 64,
			new BufferFormatType(Types.UNrm, Sizes.Size8, 1),//FORMAT_A8_UNORM = 65,
			new BufferFormatType(Types.UNrm, Sizes.Size8, 1),//FORMAT_R1_UNORM = 66,

            new BufferFormatType(Types.TLss, Sizes.Size32, 1),//FORMAT_R9G9B9E5_SHAREDEXP = 67,
            new BufferFormatType(Types.UNrm, Sizes.Size8, 2),//FORMAT_R8G8_B8G8_UNORM = 68,
            new BufferFormatType(Types.UNrm, Sizes.Size8, 2),//FORMAT_G8R8_G8B8_UNORM = 69,
            new BufferFormatType(Types.TLss, Sizes.Other, 4),//FORMAT_BC1_TYPELESS = 70,
            new BufferFormatType(Types.TLss, Sizes.Other, 4),//FORMAT_BC1_UNORM = 71,
            new BufferFormatType(Types.TLss, Sizes.Other, 4),//FORMAT_BC1_UNORM_SRGB = 72,
            new BufferFormatType(Types.TLss, Sizes.Other, 4),//FORMAT_BC2_TYPELESS = 73,
            new BufferFormatType(Types.TLss, Sizes.Other, 4),//FORMAT_BC2_UNORM = 74,
            new BufferFormatType(Types.TLss, Sizes.Other, 4),//FORMAT_BC2_UNORM_SRGB = 75,
            new BufferFormatType(Types.TLss, Sizes.Other, 4),//FORMAT_BC3_TYPELESS = 76,
            new BufferFormatType(Types.TLss, Sizes.Other, 4),//FORMAT_BC3_UNORM = 77,
            new BufferFormatType(Types.TLss, Sizes.Other, 4),//FORMAT_BC3_UNORM_SRGB = 78,
            new BufferFormatType(Types.TLss, Sizes.Other, 1),//FORMAT_BC4_TYPELESS = 79,
            new BufferFormatType(Types.TLss, Sizes.Other, 1),//FORMAT_BC4_UNORM = 80,
            new BufferFormatType(Types.TLss, Sizes.Other, 1),//FORMAT_BC4_SNORM = 81,
            new BufferFormatType(Types.TLss, Sizes.Other, 2),//FORMAT_BC5_TYPELESS = 82,
            new BufferFormatType(Types.TLss, Sizes.Other, 2),//FORMAT_BC5_UNORM = 83,
            new BufferFormatType(Types.TLss, Sizes.Other, 2),//FORMAT_BC5_SNORM = 84,
            new BufferFormatType(Types.UNrm, Sizes.Size5651, 3),//FORMAT_B5G6R5_UNORM = 85,
            new BufferFormatType(Types.UNrm, Sizes.Size5651, 4),//FORMAT_B5G5R5A1_UNORM = 86,
            new BufferFormatType(Types.UNrm, Sizes.Size8, 4),//FORMAT_B8G8R8A8_UNORM = 87,
            new BufferFormatType(Types.UNrm, Sizes.Size8, 4),//FORMAT_B8G8R8X8_UNORM = 88,
        };
    }

    unsafe public struct BufferLayoutPersistent : IDisposable {
        public enum Usages : byte { Vertex, Index, Instance, Uniform, };
        public CSBufferLayout BufferLayout;
        public readonly Usages Usage => (Usages)BufferLayout.mUsage;
        public readonly int Offset => BufferLayout.mOffset;    // Offset in count when binding a view to this buffer
        public readonly int Count => BufferLayout.mCount;     // How many elements to make current
        public readonly CSBufferElement* ElementsBuffer => BufferLayout.mElements;
        public readonly int ElementCount => BufferLayout.mElementCount;
        public readonly int Revision => BufferLayout.revision;
        private int mBufferAllocCount = 0;
        private int mBufferStride = 0;
        private int mElementAllocCount = 0;
        public readonly int BufferCapacityCount => mBufferAllocCount;
        public readonly int BufferStride => mBufferStride;
        public readonly Span<CSBufferElement> Elements => new(BufferLayout.mElements, BufferLayout.mElementCount);
        public readonly bool IsValid => mElementAllocCount > 0;
        public bool UnorderedAccess { get => BufferLayout.GetAllowUnorderedAccess(); set => BufferLayout.SetAllowUnorderedAccess(value); }
        public BufferLayoutPersistent(Usages usage) {
            BufferLayout.identifier = MakeId();
            BufferLayout.mUsage = (byte)usage;
        }
        public BufferLayoutPersistent(Usages usage, int count) : this(usage) {
            AllocResize(count);
            SetCount(count);
        }
        public void InitializeAppendBuffer(int capacity) {
            Debug.Assert(ElementCount > 0,
                "Elements must be added before initializing Append");
            // Must be big enough to store "count"
            AllocResize(1);
            UnorderedAccess = true;
            BufferLayout.mCount = -1;
            BufferLayout.size = BufferStride * capacity;
            BufferLayout.revision = -1;
        }
        public void MarkInvalid() {
            BufferLayout.mCount = -1;
            mBufferAllocCount = -1;
        }
        public void NotifyChanged() {
            BufferLayout.revision++;
        }
        public unsafe void RevisionFromDataHash() {
            BufferLayout.revision = 0;
            ulong binaryHash = 0;
            foreach (var element in Elements) {
                var bufferSize = element.mBufferStride * Count;
                var bufferData = new Span<byte>(element.mData, bufferSize);
                int i = 0;
                for (; i < bufferSize - 8; i += 8) {
                    binaryHash = binaryHash * 1000003 + *(ulong*)((byte*)element.mData + i);
                }
                for (; i < bufferSize; ++i) {
                    binaryHash = binaryHash * 1000003 + *((byte*)element.mData + i);
                }
            }
            BufferLayout.revision = (int)binaryHash ^ (int)(binaryHash >> 32);
        }
        public void Dispose() {
            if (BufferLayout.mElements != null)
                Marshal.FreeHGlobal((nint)BufferLayout.mElements);
            BufferLayout.mElements = null;
        }
        public void SetElementCount(int count) {
            if (count >= mElementAllocCount) {
                mElementAllocCount += 4;
                BufferLayout.mElements = (CSBufferElement*)Marshal.ReAllocHGlobal((nint)BufferLayout.mElements, sizeof(CSBufferElement) * mElementAllocCount);
            }
            BufferLayout.mElementCount = (byte)count;
        }
        public int AppendElement(CSBufferElement element, bool allocateData = true) {
            var elementId = BufferLayout.mElementCount;
            SetElementCount(ElementCount + 1);
            if (allocateData && mBufferAllocCount > 0) {
                element.mData = Marshal.ReAllocHGlobal((nint)element.mData, element.mBufferStride * mBufferAllocCount).ToPointer();
            }
            BufferLayout.mElements[elementId] = element;
            mBufferStride += element.mBufferStride;// BufferFormatType.GetMeta(element.mFormat).GetByteSize();
            CalculateImplicitSize();
            return elementId;
        }
        public int FindElement(CSIdentifier bindName) {
            for (int e = 0; e < Elements.Length; ++e) {
                ref var el = ref Elements[e];
                if (el.mBindName == bindName) return e;
            }
            return -1;
        }
        public void SetElementFormat(int elementId, BufferFormat fmt) {
            ref var el = ref Elements[elementId];
            if (el.mFormat == fmt) return;
            var oldStride = el.mBufferStride;
            el.mFormat = fmt;
            el.mBufferStride = (ushort)BufferFormatType.GetMeta(fmt).GetByteSize();
            mBufferStride += el.mBufferStride - oldStride;
            if (el.mBufferStride != oldStride && mBufferAllocCount > 0) {
                el.mData = Marshal.ReAllocHGlobal((nint)el.mData, el.mBufferStride * mBufferAllocCount).ToPointer();
            }
            CalculateImplicitSize();
        }
        unsafe public bool AllocResize(int newCount) {
            for (int e = 0; e < Elements.Length; ++e) {
                ref var el = ref Elements[e];
                el.mData = Marshal.ReAllocHGlobal((nint)el.mData, el.mBufferStride * newCount).ToPointer();
            }
            mBufferAllocCount = newCount;
            CalculateImplicitSize();
            return true;
        }
        public void SetCount(int count) {
            Debug.Assert(count <= mBufferAllocCount, "Buffer must be resized first!");
            BufferLayout.mCount = count;
        }
        public void RequireCount(int count) {
            if (count >= mBufferAllocCount) {
                AllocResize((int)BitOperations.RoundUpToPowerOf2((uint)count + 2));
            }
            BufferLayout.mCount = count;
        }
        public void Clear() {
            BufferLayout.mCount = 0;
        }
        public void CalculateImplicitSize() {
            BufferLayout.size = mBufferAllocCount * mBufferStride;
        }

        unsafe public void CopyRange(int from, int to, int count) {
            foreach (var element in Elements) {
                var fromPtr = (byte*)element.mData + from * element.mBufferStride;
                var toPtr = (byte*)element.mData + to * element.mBufferStride;
                var byteSize = count * element.mBufferStride;
                new Span<byte>(fromPtr, byteSize).CopyTo(new Span<byte>(toPtr, byteSize));
            }
        }
        public void InvalidateRange(int from, int count) {
            foreach (var element in Elements) {
                var fromPtr = (byte*)element.mData + from * element.mBufferStride;
                var byteSize = count * element.mBufferStride;
                new Span<byte>(fromPtr, byteSize).Fill(255);
            }
        }
        public void CopyFrom(in CSBufferLayout other) {
            AllocResize(other.mCount);
            BufferLayout.mCount = other.mCount;
            for (int e = 0; e < other.mElementCount; ++e) {
                ref var srcEl = ref other.mElements[e];
                var elId = FindElement(srcEl.mBindName);
                if (elId == -1) elId = AppendElement(new CSBufferElement(srcEl.mBindName, srcEl.mFormat));
                else SetElementFormat(elId, srcEl.mFormat);
                ref var el = ref Elements[elId];
                new Span<byte>(srcEl.mData, srcEl.mBufferStride * other.mCount)
                    .CopyTo(new Span<byte>(el.mData, el.mBufferStride * Count));
            }
        }

        public Span<T> GetElementAs<T>(int elementId) {
            return new Span<T>(Elements[elementId].mData, Count);
        }

        public override string ToString() {
            return $"H{BufferLayout.identifier:x} E{ElementCount} R{Revision}";
        }

        public static implicit operator CSBufferLayout(BufferLayoutPersistent buffer) { return buffer.BufferLayout; }

        private static ulong gId = 1;
        public static ulong MakeId() { return Interlocked.Increment(ref gId); }
    }
    struct Normalized<T> where T : unmanaged {
        public T Value;
        public static implicit operator Normalized<T>(T v) { return new Normalized<T>() { Value = v }; }
        public static implicit operator T(Normalized<T> v) { return v.Value; }
    }
    public struct SColor {
        public sbyte R, G, B, A;
        public SColor(Vector4 v) : this(
            (sbyte)Math.Clamp(127.0f * v.X, 0.0f, 127.0f),
            (sbyte)Math.Clamp(127.0f * v.Y, 0.0f, 127.0f),
            (sbyte)Math.Clamp(127.0f * v.Z, 0.0f, 127.0f),
            (sbyte)Math.Clamp(127.0f * v.W, 0.0f, 127.0f)) { }
        public SColor(Vector3 v, sbyte a = sbyte.MaxValue) : this(
            (sbyte)Math.Clamp(127.0f * v.X, 0.0f, 127.0f),
            (sbyte)Math.Clamp(127.0f * v.Y, 0.0f, 127.0f),
            (sbyte)Math.Clamp(127.0f * v.Z, 0.0f, 127.0f),
            a) { }
        public SColor(sbyte r, sbyte g, sbyte b, sbyte a) { R = r; G = g; B = b; A = a; }
        public static implicit operator Color(SColor c) {
            return new Color((byte)Math.Max(0, c.R * 255 / 127), (byte)Math.Max(0, c.G * 255 / 127), (byte)Math.Max(0, c.B * 255 / 127), (byte)Math.Max(0, c.A * 255 / 127));
        }
        public static implicit operator SColor(Color c) {
            return new SColor((sbyte)(c.R * 127 / 255), (sbyte)(c.G * 127 / 255), (sbyte)(c.B * 127 / 255), (sbyte)(c.A * 127 / 255));
        }
        public static implicit operator Vector4(SColor c) {
            return new Vector4(c.R, c.G, c.B, c.A) * (1.0f / 127.0f);
        }
        public static implicit operator Vector3(SColor c) {
            return new Vector3(c.R, c.G, c.B) * (1.0f / 127.0f);
        }
    }
    public unsafe interface IConverterFn {
        public static delegate*<void*, void*, void> mConvert { get; }
    }
    public unsafe struct ConvertFn<From, To> : IConverterFn where From : unmanaged where To : unmanaged {
        public static delegate*<void*, void*, void> mConvert { get; set; }
    }
    public unsafe struct ConvertFiller {
        static ConvertFiller() {
            InitializeType<int>();
            InitializeAlias<int, uint>();
            InitializeType<short>();
            InitializeType<ushort>();
            InitializeType<float>();
            InitializeType<Vector2>();
            InitializeType<Vector3>();
            InitializeType<Vector4>();
            InitializeType<Short2>();
            InitializeType<Color>();
            InitializeType<SColor>();
            InitializeType<Byte4>();
            InitializeAlias<Short2, UShort2>();
            InitializeAlias<ushort, short>();
            InitializeAlias<Byte4, Color>();
            ConvertFn<int, float>.mConvert = &ConvertIToF;
            ConvertFn<float, int>.mConvert = &ConvertFToI;
            ConvertFn<uint, float>.mConvert = &ConvertUIToF;
            ConvertFn<float, uint>.mConvert = &ConvertFToUI;
            ConvertFn<uint, ushort>.mConvert = &ConvertUIToUS;
            ConvertFn<ushort, uint>.mConvert = &ConvertUSToUI;
            ConvertFn<int, ushort>.mConvert = &ConvertUIToUS;
            ConvertFn<ushort, int>.mConvert = &ConvertUSToUI;
            ConvertFn<Int4, Color>.mConvert = &ConvertI4ToB4;
            ConvertFn<Color, Int4>.mConvert = &ConvertB4ToI4;
            ConvertFn<Vector2, Vector3>.mConvert = &ConvertV2ToV3;
            ConvertFn<Vector4, Color>.mConvert = &ConvertV4ToC4;
            ConvertFn<Vector3, Color>.mConvert = &ConvertV3ToC4;
            ConvertFn<Color, SColor>.mConvert = &ConvertC4ToSC4;
            ConvertFn<SColor, Color>.mConvert = &ConvertSC4ToC4;
            ConvertFn<Color, Vector4>.mConvert = &ConvertC4ToV4;
            ConvertFn<Color, Vector3>.mConvert = &ConvertC4ToV3;
            ConvertFn<Vector4, SColor>.mConvert = &ConvertV4ToSC4;
            ConvertFn<Vector3, SColor>.mConvert = &ConvertV3ToSC4;
            ConvertFn<SColor, Vector4>.mConvert = &ConvertSC4ToV4;
            ConvertFn<SColor, Vector3>.mConvert = &ConvertSC4ToV3;
            ConvertFn<Vector2, Short2>.mConvert = &ConvertV2ToS2;
            ConvertFn<Short2, Vector2>.mConvert = &ConvertS2ToV2;
            ConvertFn<Short2, Vector3>.mConvert = &ConvertS2ToV3;
            ConvertFn<Vector2, UShort2>.mConvert = &ConvertV2ToUS2;
            ConvertFn<UShort2, Vector2>.mConvert = &ConvertUS2ToV2;
            ConvertFn<Vector2, Normalized<Short2>>.mConvert = &ConvertV2ToS2N;
            ConvertFn<Normalized<Short2>, Vector2>.mConvert = &ConvertS2NToV2;
            ConvertFn<Vector2, Normalized<UShort2>>.mConvert = &ConvertV2ToUS2N;
            ConvertFn<Normalized<UShort2>, Vector2>.mConvert = &ConvertUS2NToV2;
            ConvertFn<Half, float>.mConvert = &ConvertHToF;
            ConvertFn<float, Half>.mConvert = &ConvertFToH;
            ConvertFn<Half2, Vector2>.mConvert = &ConvertH2ToV2;
            ConvertFn<Half2, Vector3>.mConvert = &ConvertH2ToV3;
            ConvertFn<Vector2, Half2>.mConvert = &ConvertV2ToH2;
            ConvertFn<Half3, Vector3>.mConvert = &ConvertH3ToV3;
            ConvertFn<Vector3, Half3>.mConvert = &ConvertV3ToH3;
            ConvertFn<Half4, Vector4>.mConvert = &ConvertH4ToV4;
            ConvertFn<Vector4, Half4>.mConvert = &ConvertV4ToH4;
        }
        public ConvertFiller(bool create) { }
        unsafe private static void InitializeType<T>() where T : unmanaged {
            ConvertFn<T, T>.mConvert = &Convert<T>;
        }
        unsafe private static void InitializeAlias<T1, T2>() where T1 : unmanaged where T2 : unmanaged {
            Debug.Assert(sizeof(T1) == sizeof(T2));
            ConvertFn<T1, T2>.mConvert = &Convert<T1>;
            ConvertFn<T2, T1>.mConvert = &Convert<T1>;
            ConvertFn<T2, T2>.mConvert = &Convert<T1>;
        }
        public static void ConvertIToF(void* dest, void* src) { *(float*)dest = *(int*)src; }
        public static void ConvertFToI(void* dest, void* src) { *(int*)dest = (int)*(float*)src; }
        public static void ConvertUIToF(void* dest, void* src) { *(float*)dest = *(uint*)src; }
        public static void ConvertFToUI(void* dest, void* src) { *(uint*)dest = (uint)*(float*)src; }
        public static void ConvertUSToUI(void* dest, void* src) { *(uint*)dest = *(ushort*)src; }
        public static void ConvertUIToUS(void* dest, void* src) { *(ushort*)dest = (ushort)*(int*)src; }
        public static void ConvertI4ToB4(void* dest, void* src) { var s = *(Int4*)src; * (Color*)dest = new Color((byte)s.X, (byte)s.Y, (byte)s.Z, (byte)s.W); }
        public static void ConvertB4ToI4(void* dest, void* src) { var s = *(Color*)src; * (Int4*)dest = new Int4(s.R, s.G, s.B, s.A); }
        public static void Convert<T>(void* dest, void* src) where T : unmanaged { *(T*)dest = *(T*)src; }
        public static void ConvertV2ToV3(void* dest, void* src) { *(Vector3*)dest = new Vector3(*(Vector2*)src, 0.0f); }
        public static void ConvertV4ToC4(void* dest, void* src) { *(Color*)dest = new Color(*(Vector4*)src); }
        public static void ConvertV3ToC4(void* dest, void* src) { *(Color*)dest = new Color(*(Vector3*)src); }
        public static void ConvertC4ToSC4(void* dest, void* src) { *(SColor*)dest = *(Color*)src; }
        public static void ConvertSC4ToC4(void* dest, void* src) { *(Color*)dest = *(SColor*)src; }
        public static void ConvertC4ToV4(void* dest, void* src) { *(Vector4*)dest = *(Color*)src; }
        public static void ConvertC4ToV3(void* dest, void* src) { *(Vector3*)dest = *(Color*)src; }
        public static void ConvertV4ToSC4(void* dest, void* src) { *(SColor*)dest = new Color(*(Vector4*)src); }
        public static void ConvertV3ToSC4(void* dest, void* src) { *(SColor*)dest = new Color(*(Vector3*)src); }
        public static void ConvertSC4ToV4(void* dest, void* src) { *(Vector4*)dest = *(SColor*)src; }
        public static void ConvertSC4ToV3(void* dest, void* src) { *(Vector3*)dest = *(SColor*)src; }
        public static void ConvertV2ToS2(void* dest, void* src) { *(Short2*)dest = new Short2(*(Vector2*)src); }
        public static void ConvertV2ToUS2(void* dest, void* src) { *(UShort2*)dest = new UShort2(*(Vector2*)src); }
        public static void ConvertS2ToV2(void* dest, void* src) { *(Vector2*)dest = (*(Short2*)src).ToVector2(); }
        public static void ConvertS2ToV3(void* dest, void* src) { *(Vector3*)dest = new Vector3((*(Short2*)src).ToVector2(), 0.0f); }
        public static void ConvertUS2ToV2(void* dest, void* src) { *(Vector2*)dest = (*(UShort2*)src).ToVector2(); }
        public static void ConvertV2ToS2N(void* dest, void* src) { *(Short2*)dest = new Short2(*(Vector2*)src * short.MaxValue); }
        public static void ConvertV2ToUS2N(void* dest, void* src) { *(UShort2*)dest = new UShort2(*(Vector2*)src * ushort.MaxValue); }
        public static void ConvertS2NToV2(void* dest, void* src) { *(Vector2*)dest = (*(Short2*)src).ToVector2() / short.MaxValue; }
        public static void ConvertUS2NToV2(void* dest, void* src) { *(Vector2*)dest = (*(UShort2*)src).ToVector2() / ushort.MaxValue; }
        public static void ConvertHToF(void* dest, void* src) { *(float*)dest = *(Half*)src; }
        public static void ConvertFToH(void* dest, void* src) { *(Half*)dest = *(float*)src; }
        public static void ConvertH2ToV2(void* dest, void* src) { *(Vector2*)dest = *(Half2*)src; }
        public static void ConvertH2ToV3(void* dest, void* src) { *(Vector3*)dest = new Vector3(*(Half2*)src, 0.0f); }
        public static void ConvertV2ToH2(void* dest, void* src) { *(Half2*)dest = *(Vector2*)src; }
        public static void ConvertH3ToV3(void* dest, void* src) { *(Vector3*)dest = *(Half3*)src; }
        public static void ConvertV3ToH3(void* dest, void* src) { *(Half3*)dest = *(Vector3*)src; }
        public static void ConvertH4ToV4(void* dest, void* src) { *(Vector4*)dest = *(Half4*)src; }
        public static void ConvertV4ToH4(void* dest, void* src) { *(Half4*)dest = *(Vector4*)src; }
    }
    public unsafe struct TypedBufferView<T> where T : unmanaged {
        static ConvertFiller filler = new ConvertFiller(true);
        public unsafe struct ReadWriterPair {
            public delegate*<void*, void*, void> mWriter;
            public delegate*<void*, void*, void> mReader;
#pragma warning disable CS8909 // Do not compare function pointer values
            public readonly bool IsPassthroughWriter => mWriter == ConvertFn<T, T>.mConvert;
            public readonly bool IsPassthroughReader => mReader == ConvertFn<T, T>.mConvert;
#pragma warning restore CS8909 // Do not compare function pointer values
        }
        private static ReadWriterPair[] cachedReadWriters = new ReadWriterPair[256];
        public ReadWriterPair ReadWriter;
        public void* mData = null;
        public ushort mCount = 0;
        public byte mStride = 0;
        public BufferFormat mFormat;
        public TypedBufferView(void* data, int stride, int count, BufferFormat fmt) {
            mData = data;
            mStride = (byte)stride;
            mCount = (ushort)count;
            mFormat = fmt;
            ReadWriter = cachedReadWriters[(byte)fmt];
            if (ReadWriter.mWriter == null) {
                ReadWriter = cachedReadWriters[(byte)fmt] = FindConverterFor<T>(fmt);
                Debug.Assert(ReadWriter.mWriter != null,
                    "Cound not find converter for " + typeof(T).Name + " and " + fmt);
            }
        }
        [Conditional("DEBUG")]
        public void AssertRequireReader() {
            if (ReadWriter.mReader == null) Debug.Fail("Require a reader for " + typeof(T).Name);
        }
        public static ReadWriterPair FindConverterFor<View>(BufferFormat fmt) where View : unmanaged {
            if (fmt == BufferFormat.FORMAT_UNKNOWN) return Initialize<View, View>();
            var type = BufferFormatType.GetMeta(fmt);
            if (type.IsFloat()) {
                if (type.GetSize() == BufferFormatType.Sizes.Size32) {
                    switch (type.GetComponentCount()) {
                        case 1: return Initialize<View, float>();
                        case 2: return Initialize<View, Vector2>();
                        case 3: return Initialize<View, Vector3>();
                        case 4: return Initialize<View, Vector4>();
                    }
                }
                if (type.GetSize() == BufferFormatType.Sizes.Size16) {
                    switch (type.GetComponentCount()) {
                        case 1: return Initialize<View, Half>();
                        case 2: return Initialize<View, Half2>();
                        case 3: return Initialize<View, Half3>();
                        case 4: return Initialize<View, Half4>();
                    }
                }
            } else {
                switch (type.GetSize()) {
                    case BufferFormatType.Sizes.Size32: {
                        switch (type.GetComponentCount()) {
                            case 1: if (type.IsSigned()) return Initialize<View, int>(); else return Initialize<View, uint>();
                            case 2: return Initialize<View, Int2>();
                            case 4: return Initialize<View, Int4>();
                        }
                    }
                    break;
                    case BufferFormatType.Sizes.Size16: {
                        bool nrm = type.IsNormalized();
                        switch (type.GetComponentCount()) {
                            case 1: if (type.IsSigned()) return Initialize<View, short>(nrm); else return Initialize<View, ushort>(nrm);
                            case 2: if (type.IsSigned()) return Initialize<View, Short2>(nrm); else return Initialize<View, UShort2>(nrm);
                        }
                    }
                    break;
                    case BufferFormatType.Sizes.Size8: {
                        switch (type.GetComponentCount()) {
                            case 1: if (type.IsSigned()) return Initialize<View, sbyte>(); else return Initialize<View, byte>();
                            case 4: if (type.IsSigned()) return Initialize<View, SColor>(); else return Initialize<View, Color>();
                        }
                    }
                    break;
                }
            }
            return default;
        }

        public TypedBufferView(CSBufferElement element, int count)
            : this((byte*)element.mData, element.mBufferStride, count, element.mFormat) { }
        public TypedBufferView(CSBufferElement element, RangeInt range)
            : this((byte*)element.mData + element.mBufferStride * range.Start, element.mBufferStride, range.Length, element.mFormat)
            { }

        private static ReadWriterPair Initialize<View, Raw>() where View : unmanaged where Raw : unmanaged {
            return new ReadWriterPair() {
                mWriter = FindConverterWithFallback<View, Raw>(),
                mReader = FindConverterWithFallback<Raw, View>(),
            };
        }
        private static delegate*<void*, void*, void> FindConverterWithFallback<From, To>() where From : unmanaged where To : unmanaged {
            var converter = ConvertFn<From, To>.mConvert;
            if (converter != null) return converter;
            if (typeof(From) == typeof(Vector4)) return FindConverterWithFallback<Vector3, To>();
            if (typeof(From) == typeof(Vector3)) return FindConverterWithFallback<Vector2, To>();
            if (typeof(From) == typeof(Vector2)) return FindConverterWithFallback<float, To>();
            if (typeof(From) == typeof(Half4)) return FindConverterWithFallback<Half3, To>();
            if (typeof(From) == typeof(Half3)) return FindConverterWithFallback<Half2, To>();
            if (typeof(From) == typeof(Half2)) return FindConverterWithFallback<Half, To>();
            if (typeof(From) == typeof(Int4)) return FindConverterWithFallback<Int2, To>();
            if (typeof(From) == typeof(Int2)) return FindConverterWithFallback<int, To>();
            if (typeof(From) == typeof(Short2)) return FindConverterWithFallback<short, To>();
            if (typeof(From) == typeof(UShort2)) return FindConverterWithFallback<ushort, To>();
            return default;
        }

        private static ReadWriterPair Initialize<View, Raw>(bool normalized) where View : unmanaged where Raw : unmanaged {
            if (normalized) {
                var pair = new ReadWriterPair() {
                    mWriter = ConvertFn<View, Normalized<Raw>>.mConvert,
                    mReader = ConvertFn<Normalized<Raw>, View>.mConvert,
                };
                if (pair.mWriter != null) return pair;
            }
            return Initialize<View, Raw>();
        }

        public T this[uint index] {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => this[(int)index];
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => this[(int)index] = value;
        }
        public T this[int index] {
            get { T value; ReadWriter.mReader(&value, (byte*)mData + mStride * index); return value; }
            set { ReadWriter.mWriter((byte*)mData + mStride * index, &value); }
        }
        public void Set(Span<T> values) {
            Debug.Assert(mCount >= values.Length);
            for (int i = 0; i < values.Length; ++i) this[i] = values[i];
        }
        public void Set(T value) {
            if (mCount == 0) return;
            //for (int i = 0; i < mCount; ++i) this[i] = value;
            // Convert into the first value
            this[0] = value;
            // Then copy into remaining buffer size
            var dataSize = mStride * mCount;
            for (int i = mStride; ; i *= 2) {
                var remain = dataSize - i;
                if (remain > i) {
                    new Span<byte>(mData, i).CopyTo(new Span<byte>((byte*)mData + i, i));
                } else {
                    new Span<byte>(mData, remain).CopyTo(new Span<byte>((byte*)mData + i, remain));
                    break;
                }
            }
        }
        public void CopyTo(TypedBufferView<T> destination) {
            if (destination.ReadWriter.IsPassthroughWriter) {
                if (mStride == destination.mStride && mStride == sizeof(T) && ReadWriter.IsPassthroughReader) {
                    // Data is packed tightly, can memcpy
                    int dataSize = mCount * mStride;
                    new Span<byte>(mData, dataSize).CopyTo(new Span<byte>(destination.mData, dataSize));
                } else {
                    // Identity writer; can directly read into dest buffer
                    byte* srcData = (byte*)mData;
                    byte* dstData = (byte*)destination.mData;
                    for (int i = 0; i < mCount; ++i) {
                        ReadWriter.mReader(dstData, srcData);
                        srcData += mStride;
                        dstData += destination.mStride;
                    }
                }
            } else {
                // Require type conversion
                for (int i = 0; i < mCount; ++i) destination[i] = this[i];
            }
        }
        public TypedBufferView<T> Slice(int offset) {
            return Slice(new RangeInt(offset, mCount - offset));
        }
        public TypedBufferView<T> Slice(RangeInt range) {
            return new TypedBufferView<T>() {
                ReadWriter = ReadWriter,
                mData = (byte*)mData + mStride * range.Start,
                mCount = (ushort)range.Length,
                mStride = mStride,
                mFormat = mFormat,
            };
        }
        public TypedBufferView<NewT> Reinterpret<NewT>() where NewT : unmanaged {
            Debug.Assert(sizeof(NewT) == sizeof(T));
            return new TypedBufferView<NewT>(mData, mStride, mCount, mFormat);
        }
        public struct Enumerator : IEnumerator<T> {
            public TypedBufferView<T> View;
            int index = 0;
            public T Current => View[index];
            object IEnumerator.Current => Current;
            public Enumerator(TypedBufferView<T> view) { View = view; index = -1; }
            public void Dispose() { }
            public bool MoveNext() { return ++index < View.mCount; }
            public void Reset() { index = -1; }
        }
        public Enumerator GetEnumerator() { return new Enumerator(this); }
        public override string ToString() { return "Count = " + mCount; }

        public struct BufferSummary : IEnumerable {
            public struct BufferEnumerator : IEnumerator {
                TypedBufferView<T> buffer;
                int index;
                object IEnumerator.Current => *(T*)((byte*)buffer.mData + index * buffer.mStride);
                public BufferEnumerator(TypedBufferView<T> view) { buffer = view; index = -1; }
                public void Dispose() { }
                public void Reset() { index = -1; }
                public bool MoveNext() { return ++index < buffer.mCount; }
            }
            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            public BufferEnumerator Enumerator;
            public BufferSummary(TypedBufferView<T> view) { Enumerator = new(view); }
            public IEnumerator GetEnumerator() => Enumerator;
        }
        public BufferSummary View => new(this);
    }
    unsafe public class BufferLayoutCollection : IDisposable {
        CSBufferLayout** mBuffers = null;
        int mCount = 0;
        int mAllocCount = 0;

        public int Count => mCount;
        public CSBufferLayout** BufferPtr => mBuffers;

        public CSBufferLayout* this[int index] => mBuffers[index];

        public void Add(CSBufferLayout* buffer) {
            if (mCount + 1 > mAllocCount) {
                mAllocCount += 4;
                var newArr = (CSBufferLayout**)Marshal.AllocHGlobal(sizeof(CSBufferLayout**) * mAllocCount);
                if (mBuffers != null) {
                    for (var i = 0; i < mCount; ++i) newArr[i] = mBuffers[i];
                    Marshal.FreeHGlobal((nint)mBuffers);
                }
                mBuffers = newArr;
            }
            mBuffers[mCount] = buffer;
            ++mCount;
        }
        public void Clear() {
            mCount = 0;
        }
        public void Dispose() {
            if (mBuffers != null) { Marshal.FreeHGlobal((nint)mBuffers); mBuffers = null; }
        }
    }
}
