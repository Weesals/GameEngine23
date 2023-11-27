using GameEngine23.Interop;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
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
            0b0000000010000000001000010000000000000000000100000000000000000000,
            0b0000000000000000000000000000000000000000000000000000000000000000,
			//	^64		^56		^48		^40		^32		^24		^16		^8		^0
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
		};
    }

    /*public unsafe struct BufferElement {
        public ushort mItemSize = 0;     // Size of each item
        public ushort mBufferStride = 0; // Separation between items in this buffer (>= mItemSize)
        public BufferFormat mFormat = BufferFormat.FORMAT_UNKNOWN;
        public void* mData = null;
        public string mBindName;
        public BufferElement(string name, BufferFormat format)
            : this(name, format, 0, 0, null) {
            mItemSize = mBufferStride = (ushort)BufferFormatType.GetMeta(format).GetByteSize();
        }
        public BufferElement(string name, BufferFormat format, int stride, int size, void* data) {
            mBindName = name;
            mFormat = format;
            mBufferStride = (ushort)stride;
            mItemSize = (ushort)size;
            mData = data;
        }
    }*/
    unsafe public struct BufferLayoutPersistent : IDisposable {
        public enum Usages : byte { Vertex, Index, Instance, Uniform, };
        public CSBufferLayout BufferLayout;
        public Usages Usage => (Usages)BufferLayout.mUsage;
        public int Offset => BufferLayout.mOffset;    // Offset in count when binding a view to this buffer
        public int Count => BufferLayout.mCount;     // How many elements to make current
        public CSBufferElement* ElementsBuffer => BufferLayout.mElements;
        public int ElementCount => BufferLayout.mElementCount;
        private int mBufferAllocCount = 0;
        private int mBufferStride = 0;
        private int mElementAllocCount = 0;
        public int BufferCapacityCount => mBufferAllocCount;
        public int BufferStride => mBufferStride;
        public Span<CSBufferElement> Elements => new(BufferLayout.mElements, BufferLayout.mElementCount);
        public BufferLayoutPersistent(int size, Usages usage, int count) {
            BufferLayout.identifier = MakeId();
            BufferLayout.size = size;
            BufferLayout.mUsage = (byte)usage;
            BufferLayout.mCount = count;
        }
        public void Dispose() {
            if (BufferLayout.mElements != null)
                Marshal.FreeHGlobal((nint)BufferLayout.mElements);
            BufferLayout.mElements = null;
        }
        public int AppendElement(CSBufferElement element, bool allocateData = true) {
            if (ElementCount + 1 >= mElementAllocCount) {
                mElementAllocCount += 4;
                BufferLayout.mElements = (CSBufferElement*)Marshal.ReAllocHGlobal((nint)BufferLayout.mElements, sizeof(CSBufferElement) * mElementAllocCount);
            }
            if (allocateData) {
                element.mData = Marshal.ReAllocHGlobal((nint)element.mData, element.mBufferStride * mBufferAllocCount).ToPointer();
            }
            BufferLayout.mElements[ElementCount] = element;
            ++BufferLayout.mElementCount;
            mBufferStride += BufferFormatType.GetMeta(element.mFormat).GetByteSize();
            CalculateImplicitSize();
            return ElementCount - 1;
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
            if (el.mBufferStride != oldStride) {
                mBufferStride += el.mBufferStride - oldStride;
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
        public void Clear() {
            BufferLayout.mCount = 0;
        }
        public void CalculateImplicitSize() {
            BufferLayout.size = mBufferAllocCount * mBufferStride;
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

        public static implicit operator CSBufferLayout(BufferLayoutPersistent buffer) { return buffer.BufferLayout; }

        private static ulong gId;
        public static ulong MakeId() { return gId++; }
    }
    public unsafe interface IConverterFn {
        public static delegate*<void*, void*, void> mConvert { get; }
    }
    public unsafe struct ConvertFn<From, To> : IConverterFn where From : unmanaged where To : unmanaged {
        public static delegate*<void*, void*, void> mConvert { get; set; }
    }
    struct Normalized<T> where T : unmanaged {
        public T Value;
        public static implicit operator Normalized<T>(T v) { return new Normalized<T>() { Value = v }; }
        public static implicit operator T(Normalized<T> v) { return v.Value; }
    }
    public unsafe struct ConvertFiller {
        static ConvertFiller() {
            InitializeType<int>();
            InitializeAlias<int, uint>();
            InitializeType<uint>();
            InitializeType<short>();
            InitializeType<ushort>();
            InitializeType<float>();
            InitializeType<Vector2>();
            InitializeType<Vector3>();
            InitializeType<Vector4>();
            InitializeType<Short2>();
            InitializeType<UShort2>();
            InitializeType<Color>();
            InitializeAlias<Short2, UShort2>();
            ConvertFn<int, float>.mConvert = &ConvertIToF;
            ConvertFn<float, int>.mConvert = &ConvertFToI;
            ConvertFn<uint, float>.mConvert = &ConvertUIToF;
            ConvertFn<float, uint>.mConvert = &ConvertFToUI;
            ConvertFn<Vector4, Color>.mConvert = &ConvertV4ToC4;
            ConvertFn<Vector3, Color>.mConvert = &ConvertV3ToC4;
            ConvertFn<Color, Vector4>.mConvert = &ConvertC4ToV4;
            ConvertFn<Color, Vector3>.mConvert = &ConvertC4ToV3;
            ConvertFn<Vector2, Short2>.mConvert = &ConvertV2ToS2;
            ConvertFn<Short2, Vector2>.mConvert = &ConvertS2ToV2;
            ConvertFn<Vector2, UShort2>.mConvert = &ConvertV2ToUS2;
            ConvertFn<UShort2, Vector2>.mConvert = &ConvertUS2ToV2;
            ConvertFn<Vector2, Normalized<Short2>>.mConvert = &ConvertV2ToS2N;
            ConvertFn<Normalized<Short2>, Vector2>.mConvert = &ConvertS2NToV2;
            ConvertFn<Vector2, Normalized<UShort2>>.mConvert = &ConvertV2ToUS2N;
            ConvertFn<Normalized<UShort2>, Vector2>.mConvert = &ConvertUS2NToV2;
        }
        public ConvertFiller(bool create) { }
        unsafe private static void InitializeType<T>() where T : unmanaged {
            ConvertFn<T, T>.mConvert = &Convert<T>;
        }
        unsafe private static void InitializeAlias<T1, T2>() where T1 : unmanaged where T2 : unmanaged {
            Debug.Assert(sizeof(T1) == sizeof(T2));
            ConvertFn<T1, T2>.mConvert = &Convert<T1>;
            ConvertFn<T2, T1>.mConvert = &Convert<T1>;
        }
        private static void ConvertIToF(void* dest, void* src) { *(float*)dest = *(int*)src; }
        private static void ConvertFToI(void* dest, void* src) { *(int*)dest = (int)*(float*)src; }
        private static void ConvertUIToF(void* dest, void* src) { *(float*)dest = *(uint*)src; }
        private static void ConvertFToUI(void* dest, void* src) { *(uint*)dest = (uint)*(float*)src; }
        private static void Convert<T>(void* dest, void* src) where T : unmanaged { *(T*)dest = *(T*)src; }
        private static void ConvertV4ToC4(void* dest, void* src) { *(Color*)dest = new Color(*(Vector4*)src); }
        private static void ConvertV3ToC4(void* dest, void* src) { *(Color*)dest = new Color(*(Vector3*)src); }
        private static void ConvertC4ToV4(void* dest, void* src) { *(Vector4*)dest = *(Color*)src; }
        private static void ConvertC4ToV3(void* dest, void* src) { *(Vector3*)dest = *(Color*)src; }
        private static void ConvertV2ToS2(void* dest, void* src) { *(Short2*)dest = new Short2(*(Vector2*)src); }
        private static void ConvertV2ToUS2(void* dest, void* src) { *(UShort2*)dest = new UShort2(*(Vector2*)src); }
        private static void ConvertS2ToV2(void* dest, void* src) { *(Vector2*)dest = (*(Short2*)src).ToVector2(); }
        private static void ConvertUS2ToV2(void* dest, void* src) { *(Vector2*)dest = (*(UShort2*)src).ToVector2(); }
        private static void ConvertV2ToS2N(void* dest, void* src) { *(Short2*)dest = new Short2(*(Vector2*)src * short.MaxValue); }
        private static void ConvertV2ToUS2N(void* dest, void* src) { *(UShort2*)dest = new UShort2(*(Vector2*)src * ushort.MaxValue); }
        private static void ConvertS2NToV2(void* dest, void* src) { *(Vector2*)dest = (*(Short2*)src).ToVector2() / short.MaxValue; }
        private static void ConvertUS2NToV2(void* dest, void* src) { *(Vector2*)dest = (*(UShort2*)src).ToVector2() / ushort.MaxValue; }
    }
    public unsafe struct TypedBufferView<T> where T : unmanaged {
        delegate*<void*, void*, void> mWriter;
        delegate*<void*, void*, void> mReader;
        public void* mData = null;
        public ushort mCount = 0;
        public ushort mStride = 0;
        public TypedBufferView(void* data, int stride, int count, BufferFormat fmt) {
            var filler = new ConvertFiller(true);
            mData = data;
            mStride = (ushort)stride;
            mCount = (ushort)count;
            var type = BufferFormatType.GetMeta(fmt);
            if (type.IsFloat()) {
                if (type.GetSize() == BufferFormatType.Sizes.Size32) {
                    switch (type.GetComponentCount()) {
                        case 1: Initialize<float>(); break;
                        case 2: Initialize<Vector2>(); break;
                        case 3: Initialize<Vector3>(); break;
                        case 4: Initialize<Vector4>(); break;
                    }
                }
            } else {
                switch (type.GetSize()) {
                    case BufferFormatType.Sizes.Size32: {
                        switch (type.GetComponentCount()) {
                            case 1: Initialize<int>(); break;
                            case 2: Initialize<Int2>(); break;
                            case 4: Initialize<Int4>(); break;
                        }
                    } break;
                    case BufferFormatType.Sizes.Size16: {
                        bool nrm = type.IsNormalized();
                        switch (type.GetComponentCount()) {
                            case 1: if (type.IsSigned()) Initialize<short>(nrm); else Initialize<ushort>(nrm); break;
                            case 2: if (type.IsSigned()) Initialize<Short2>(nrm); else Initialize<UShort2>(nrm); break;
                        }
                    } break;
                    case BufferFormatType.Sizes.Size8: {
                        switch (type.GetComponentCount()) {
                            case 1: if (type.IsSigned()) Initialize<sbyte>(); else Initialize<byte>(); break;
                            case 4: Initialize<Color>(); break;
                        }
                    } break;
                }
            }
            Debug.Assert(mWriter != null,
                "Cound not find converter for " + typeof(T).Name + " and " + fmt);
        }

        public TypedBufferView(CSBufferElement element, int count)
            : this((byte*)element.mData, element.mBufferStride, count, element.mFormat) { }
        public TypedBufferView(CSBufferElement element, RangeInt range)
            : this((byte*)element.mData + element.mBufferStride * range.Start, element.mBufferStride, range.Length, element.mFormat)
            { }

        private void Initialize<Raw>() where Raw : unmanaged {
            mWriter = ConvertFn<T, Raw>.mConvert;
            mReader = ConvertFn<Raw, T>.mConvert;
        }
        private void Initialize<Raw>(bool normalized) where Raw : unmanaged {
            if (normalized) {
                mWriter = ConvertFn<T, Normalized<Raw>>.mConvert;
                mReader = ConvertFn<Normalized<Raw>, T>.mConvert;
                if (mWriter != null) return;
            }
            Initialize<Raw>();
        }

        public T this[int index] {
            get { T value; mReader(&value, (byte*)mData + mStride * index); return value; }
            set { mWriter((byte*)mData + mStride * index, &value); }
        }
        public void Set(Span<T> values) {
            for (int i = 0; i < values.Length; ++i) this[i] = values[i];
        }

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
