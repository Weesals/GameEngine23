#pragma once

#include "MathTypes.h"
#include "Resources.h"
#include <span>
#include <algorithm>
#include <cassert>

enum BufferFormat : uint8_t {
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
	FORMAT_R10G10B10_XR_BIAS_A2_UNORM = 89,
	FORMAT_B8G8R8A8_TYPELESS = 90,
	FORMAT_B8G8R8A8_UNORM_SRGB = 91,
	FORMAT_B8G8R8X8_TYPELESS = 92,
	FORMAT_B8G8R8X8_UNORM_SRGB = 93,
	FORMAT_BC6H_TYPELESS = 94,
	FORMAT_BC6H_UF16 = 95,
	FORMAT_BC6H_SF16 = 96,
	FORMAT_BC7_TYPELESS = 97,
	FORMAT_BC7_UNORM = 98,
	FORMAT_BC7_UNORM_SRGB = 99,
};
struct BufferFormatType {
	enum Types : uint8_t {
		SNrm = 0b000, SInt = 0b001,
		UNrm = 0b010, UInt = 0b011,
		Float = 0b101, TLss = 0b111,
	};
	enum Sizes : uint8_t {
		Size32, Size16, Size8,
		Size5651, Size1010102, Size444,
		Size9995, Other
	};
	union {
		struct {
			Types type : 3;
			Sizes size : 3;
			uint8_t cmp : 2;
		};
		uint8_t mPacked;
	};
	bool IsInt() const { return (type & 0b101) == 0b001; }
	bool IsIntOrNrm() const { return (type & 0b100) == 0b000; }
	bool IsFloat() const { return type == Float; }
	bool IsNormalized() const { return (type & 0b001) == 0b000; }
	bool IsSigned() const { return (type & 0b010) == 0b000; }
	int GetComponentCount() const { return cmp + 1; }
	Sizes GetSize() const { return size; }
	int GetByteSize() const {
		switch (size) {
		case Size32: return GetComponentCount() * 4;
		case Size16: return GetComponentCount() * 2;
		case Size8: return GetComponentCount() * 1;
		default: break;
		}
		return -1;
	}
	static int GetBitSize(BufferFormat fmt) {
		auto type = BufferFormatType::GetType(fmt);
		int byteSize = type.GetByteSize();
		if (byteSize > 0) return byteSize * 8;
		switch (fmt) {
			case FORMAT_BC1_TYPELESS:
			case FORMAT_BC1_UNORM:
			case FORMAT_BC1_UNORM_SRGB:
			case FORMAT_BC4_TYPELESS:
			case FORMAT_BC4_UNORM:
			case FORMAT_BC4_SNORM: return 4;
			case FORMAT_BC2_TYPELESS:
			case FORMAT_BC2_UNORM:
			case FORMAT_BC2_UNORM_SRGB:
			case FORMAT_BC3_TYPELESS:
			case FORMAT_BC3_UNORM:
			case FORMAT_BC3_UNORM_SRGB:
			case FORMAT_BC5_TYPELESS:
			case FORMAT_BC5_UNORM:
			case FORMAT_BC5_SNORM:
			case FORMAT_BC6H_TYPELESS:
			case FORMAT_BC6H_UF16:
			case FORMAT_BC6H_SF16:
			case FORMAT_BC7_TYPELESS:
			case FORMAT_BC7_UNORM:
			case FORMAT_BC7_UNORM_SRGB: return 8;
		}
		return -1;
	}
	static int GetCompressedBlockSize(BufferFormat fmt) {
		return (fmt >= FORMAT_BC1_TYPELESS && fmt <= FORMAT_BC5_SNORM) ? 4 :
			(fmt >= FORMAT_BC6H_TYPELESS && fmt <= FORMAT_BC7_UNORM_SRGB) ? 4 :
			-1;
	}
	constexpr BufferFormatType(Types type, Sizes size, uint8_t cmp) : type(type), size(size), cmp(cmp - 1) { }
	static BufferFormatType GetType(BufferFormat fmt) {
		static_assert(sizeof(BufferFormatType) == 1);
		static const BufferFormatType Types[]{
			BufferFormatType(Types::TLss, Sizes::Other, 0),//FORMAT_UNKNOWN = 0,
			BufferFormatType(Types::TLss, Sizes::Size32, 4),//FORMAT_R32G32B32A32_TYPELESS = 1,
			BufferFormatType(Types::Float, Sizes::Size32, 4),//FORMAT_R32G32B32A32_FLOAT = 2,
			BufferFormatType(Types::UInt, Sizes::Size32, 4),//FORMAT_R32G32B32A32_UINT = 3,
			BufferFormatType(Types::SInt, Sizes::Size32, 4),//FORMAT_R32G32B32A32_SINT = 4,
			BufferFormatType(Types::TLss, Sizes::Size32, 3),//FORMAT_R32G32B32_TYPELESS = 5,
			BufferFormatType(Types::Float, Sizes::Size32, 3),//FORMAT_R32G32B32_FLOAT = 6,
			BufferFormatType(Types::UInt, Sizes::Size32, 3),//FORMAT_R32G32B32_UINT = 7,
			BufferFormatType(Types::SInt, Sizes::Size32, 3),//FORMAT_R32G32B32_SINT = 8,
			BufferFormatType(Types::TLss, Sizes::Size16, 4),//FORMAT_R16G16B16A16_TYPELESS = 9,
			BufferFormatType(Types::Float, Sizes::Size16, 4),//FORMAT_R16G16B16A16_FLOAT = 10,
			BufferFormatType(Types::UNrm, Sizes::Size16, 4),//FORMAT_R16G16B16A16_UNORM = 11,
			BufferFormatType(Types::UInt, Sizes::Size16, 4),//FORMAT_R16G16B16A16_UINT = 12,
			BufferFormatType(Types::SNrm, Sizes::Size16, 4),//FORMAT_R16G16B16A16_SNORM = 13,
			BufferFormatType(Types::SInt, Sizes::Size16, 4),//FORMAT_R16G16B16A16_SINT = 14,
			BufferFormatType(Types::TLss, Sizes::Size32, 2),//FORMAT_R32G32_TYPELESS = 15,
			BufferFormatType(Types::Float, Sizes::Size32, 2),//FORMAT_R32G32_FLOAT = 16,
			BufferFormatType(Types::UInt, Sizes::Size32, 2),//FORMAT_R32G32_UINT = 17,
			BufferFormatType(Types::SInt, Sizes::Size32, 2),//FORMAT_R32G32_SINT = 18,
			BufferFormatType(Types::TLss, Sizes::Size32, 2),//FORMAT_R32G8X24_TYPELESS = 19,
			BufferFormatType(Types::UInt, Sizes::Size32, 1),//FORMAT_D32_FLOAT_S8X24_UINT = 20,
			BufferFormatType(Types::TLss, Sizes::Size32, 1),//FORMAT_R32_FLOAT_X8X24_TYPELESS = 21,
			BufferFormatType(Types::UInt, Sizes::Size32, 1),//FORMAT_X32_TYPELESS_G8X24_UINT = 22,
			BufferFormatType(Types::TLss, Sizes::Size1010102, 4),//FORMAT_R10G10B10A2_TYPELESS = 23,
			BufferFormatType(Types::UNrm, Sizes::Size1010102, 4),//FORMAT_R10G10B10A2_UNORM = 24,
			BufferFormatType(Types::UInt, Sizes::Size1010102, 4),//FORMAT_R10G10B10A2_UINT = 25,
			BufferFormatType(Types::Float, Sizes::Size1010102, 3),//FORMAT_R11G11B10_FLOAT = 26,
			BufferFormatType(Types::TLss, Sizes::Size8, 4),//FORMAT_R8G8B8A8_TYPELESS = 27,
			BufferFormatType(Types::UNrm, Sizes::Size8, 4),//FORMAT_R8G8B8A8_UNORM = 28,
			BufferFormatType(Types::UNrm, Sizes::Size8, 4),//FORMAT_R8G8B8A8_UNORM_SRGB = 29,
			BufferFormatType(Types::UInt, Sizes::Size8, 4),//FORMAT_R8G8B8A8_UINT = 30,
			BufferFormatType(Types::SNrm, Sizes::Size8, 4),//FORMAT_R8G8B8A8_SNORM = 31,
			BufferFormatType(Types::SInt, Sizes::Size8, 4),//FORMAT_R8G8B8A8_SINT = 32,
			BufferFormatType(Types::TLss, Sizes::Size16, 2),//FORMAT_R16G16_TYPELESS = 33,
			BufferFormatType(Types::Float, Sizes::Size16, 2),//FORMAT_R16G16_FLOAT = 34,
			BufferFormatType(Types::UNrm, Sizes::Size16, 2),//FORMAT_R16G16_UNORM = 35,
			BufferFormatType(Types::UInt, Sizes::Size16, 2),//FORMAT_R16G16_UINT = 36,
			BufferFormatType(Types::SNrm, Sizes::Size16, 2),//FORMAT_R16G16_SNORM = 37,
			BufferFormatType(Types::SInt, Sizes::Size16, 2),//FORMAT_R16G16_SINT = 38,
			BufferFormatType(Types::TLss, Sizes::Size32, 1),//FORMAT_R32_TYPELESS = 39,
			BufferFormatType(Types::Float, Sizes::Size32, 1),//FORMAT_D32_FLOAT = 40,
			BufferFormatType(Types::Float, Sizes::Size32, 1),//FORMAT_R32_FLOAT = 41,
			BufferFormatType(Types::UInt, Sizes::Size32, 1),//FORMAT_R32_UINT = 42,
			BufferFormatType(Types::SInt, Sizes::Size32, 1),//FORMAT_R32_SINT = 43,
			BufferFormatType(Types::TLss, Sizes::Size32, 1),//FORMAT_R24G8_TYPELESS = 44,
			BufferFormatType(Types::UInt, Sizes::Size32, 1),//FORMAT_D24_UNORM_S8_UINT = 45,
			BufferFormatType(Types::TLss, Sizes::Size32, 1),//FORMAT_R24_UNORM_X8_TYPELESS = 46,
			BufferFormatType(Types::UInt, Sizes::Size32, 1),//FORMAT_X24_TYPELESS_G8_UINT = 47,
			BufferFormatType(Types::TLss, Sizes::Size8, 2),//FORMAT_R8G8_TYPELESS = 48,
			BufferFormatType(Types::UNrm, Sizes::Size8, 2),//FORMAT_R8G8_UNORM = 49,
			BufferFormatType(Types::UInt, Sizes::Size8, 2),//FORMAT_R8G8_UINT = 50,
			BufferFormatType(Types::SNrm, Sizes::Size8, 2),//FORMAT_R8G8_SNORM = 51,
			BufferFormatType(Types::SInt, Sizes::Size8, 2),//FORMAT_R8G8_SINT = 52,
			BufferFormatType(Types::TLss, Sizes::Size16, 1),//FORMAT_R16_TYPELESS = 53,
			BufferFormatType(Types::Float, Sizes::Size16, 1),//FORMAT_R16_FLOAT = 54,
			BufferFormatType(Types::UNrm, Sizes::Size16, 1),//FORMAT_D16_UNORM = 55,
			BufferFormatType(Types::UNrm, Sizes::Size16, 1),//FORMAT_R16_UNORM = 56,
			BufferFormatType(Types::UInt, Sizes::Size16, 1),//FORMAT_R16_UINT = 57,
			BufferFormatType(Types::SNrm, Sizes::Size16, 1),//FORMAT_R16_SNORM = 58,
			BufferFormatType(Types::SInt, Sizes::Size16, 1),//FORMAT_R16_SINT = 59,
			BufferFormatType(Types::TLss, Sizes::Size8, 1),//FORMAT_R8_TYPELESS = 60,
			BufferFormatType(Types::UNrm, Sizes::Size8, 1),//FORMAT_R8_UNORM = 61,
			BufferFormatType(Types::UInt, Sizes::Size8, 1),//FORMAT_R8_UINT = 62,
			BufferFormatType(Types::SNrm, Sizes::Size8, 1),//FORMAT_R8_SNORM = 63,
			BufferFormatType(Types::SInt, Sizes::Size8, 1),//FORMAT_R8_SINT = 64,
			BufferFormatType(Types::UNrm, Sizes::Size8, 1),//FORMAT_A8_UNORM = 65,
			BufferFormatType(Types::UNrm, Sizes::Size8, 1),//FORMAT_R1_UNORM = 66,

			BufferFormatType(Types::TLss, Sizes::Size32, 1),//FORMAT_R9G9B9E5_SHAREDEXP = 67,
			BufferFormatType(Types::UNrm, Sizes::Size8, 2),//FORMAT_R8G8_B8G8_UNORM = 68,
			BufferFormatType(Types::UNrm, Sizes::Size8, 2),//FORMAT_G8R8_G8B8_UNORM = 69,
			BufferFormatType(Types::TLss, Sizes::Other, 4),//FORMAT_BC1_TYPELESS = 70,
			BufferFormatType(Types::TLss, Sizes::Other, 4),//FORMAT_BC1_UNORM = 71,
			BufferFormatType(Types::TLss, Sizes::Other, 4),//FORMAT_BC1_UNORM_SRGB = 72,
			BufferFormatType(Types::TLss, Sizes::Other, 4),//FORMAT_BC2_TYPELESS = 73,
			BufferFormatType(Types::TLss, Sizes::Other, 4),//FORMAT_BC2_UNORM = 74,
			BufferFormatType(Types::TLss, Sizes::Other, 4),//FORMAT_BC2_UNORM_SRGB = 75,
			BufferFormatType(Types::TLss, Sizes::Other, 4),//FORMAT_BC3_TYPELESS = 76,
			BufferFormatType(Types::TLss, Sizes::Other, 4),//FORMAT_BC3_UNORM = 77,
			BufferFormatType(Types::TLss, Sizes::Other, 4),//FORMAT_BC3_UNORM_SRGB = 78,
			BufferFormatType(Types::TLss, Sizes::Other, 1),//FORMAT_BC4_TYPELESS = 79,
			BufferFormatType(Types::TLss, Sizes::Other, 1),//FORMAT_BC4_UNORM = 80,
			BufferFormatType(Types::TLss, Sizes::Other, 1),//FORMAT_BC4_SNORM = 81,
			BufferFormatType(Types::TLss, Sizes::Other, 2),//FORMAT_BC5_TYPELESS = 82,
			BufferFormatType(Types::TLss, Sizes::Other, 2),//FORMAT_BC5_UNORM = 83,
			BufferFormatType(Types::TLss, Sizes::Other, 2),//FORMAT_BC5_SNORM = 84,
			BufferFormatType(Types::UNrm, Sizes::Size5651, 3),//FORMAT_B5G6R5_UNORM = 85,
			BufferFormatType(Types::UNrm, Sizes::Size5651, 4),//FORMAT_B5G5R5A1_UNORM = 86,
			BufferFormatType(Types::UNrm, Sizes::Size8, 4),//FORMAT_B8G8R8A8_UNORM = 87,
			BufferFormatType(Types::UNrm, Sizes::Size8, 4),//FORMAT_B8G8R8X8_UNORM = 88,
			BufferFormatType(Types::UNrm, Sizes::Size1010102, 4),//FORMAT_R10G10B10_XR_BIAS_A2_UNORM = 89,
			BufferFormatType(Types::UNrm, Sizes::Size8, 4),//FORMAT_B8G8R8A8_TYPELESS = 90,
			BufferFormatType(Types::UNrm, Sizes::Size8, 4),//FORMAT_B8G8R8A8_UNORM_SRGB = 91,
			BufferFormatType(Types::UNrm, Sizes::Size8, 4),//FORMAT_B8G8R8X8_TYPELESS = 92,
			BufferFormatType(Types::UNrm, Sizes::Size8, 4),//FORMAT_B8G8R8X8_UNORM_SRGB = 93,
			BufferFormatType(Types::UNrm, Sizes::Other, 4),//FORMAT_BC6H_TYPELESS = 94,
			BufferFormatType(Types::UNrm, Sizes::Other, 4),//FORMAT_BC6H_UF16 = 95,
			BufferFormatType(Types::UNrm, Sizes::Other, 4),//FORMAT_BC6H_SF16 = 96,
			BufferFormatType(Types::UNrm, Sizes::Other, 4),//FORMAT_BC7_TYPELESS = 97,
			BufferFormatType(Types::UNrm, Sizes::Other, 4),//FORMAT_BC7_UNORM = 98,
			BufferFormatType(Types::UNrm, Sizes::Other, 4),//FORMAT_BC7_UNORM_SRGB = 99,
		};
		return Types[fmt];
	}
	bool operator ==(BufferFormatType o) const { return mPacked == o.mPacked; }
	bool operator !=(BufferFormatType o) const { return mPacked != o.mPacked; }
	/*enum TypeMasks {
		// Float, UInt, UNrm, SInt, SNrm, Typeless
		// -> (Float | Int) + (U | S) + (Sca | Nrm)
		// Float: 101, Uint: 011, UNrm: 010, SInt: 001, SNrm: 000, Typeless: 111
		// Size: 32, 16, 8, (565,5551), 1010102, 4444, 9995
		// Components: 4, 3, 2, 1
		Float = 0b00110000010000000100000000010000010001000100,
		SInt =	0b10000100000100000000000001000100000100010000,
		//UInt =	0b01000001000001000000000001000100000100010000,
	};*/
	static bool GetIsDepthBuffer(BufferFormat fmt) {
		static uint64_t depthMask[] = {
			   0b0000000010000000001000010000000000000000000100000000000000000000,
			   0b0000000000000000000000000000000000000000000000000000000000000000,
			   //	^64		^56		^48		^40		^32		^24		^16		^8		^0
		};
		return (depthMask[fmt >> 7] & (1ull << (fmt & 63))) != 0;
	}
};

struct BufferLayout {
	enum Usage : uint8_t { Vertex, Index, Instance, Uniform, };
	struct Element {
		Identifier mBindName;
		uint16_t mBufferStride = 0;	// Separation between items in this buffer (>= mItemSize)
		BufferFormat mFormat = FORMAT_UNKNOWN;
		void* mData = nullptr;
		Element() { }
		Element(Identifier name, BufferFormat format)
			: Element(name, format, 0, nullptr) {
			mBufferStride = BufferFormatType::GetType(format).GetByteSize();
		}
		Element(Identifier name, BufferFormat format, int stride, void* data)
			: mBindName(name), mFormat(format), mBufferStride(stride), mData(data)
		{ }
		int GetItemByteSize() const {
			int bsize = BufferFormatType::GetType(mFormat).GetByteSize();
			assert(bsize <= mBufferStride);
			return bsize;
		}
	};
	size_t mIdentifier;
	int mRevision = 0;
	int mSize = 0;		// Size in bytes to allocate for the entire buffer
	Element* mElements = nullptr;
	uint8_t mElementCount = 0;
	Usage mUsage = Usage::Vertex;
	int mOffset = 0;	// Offset in count when binding a view to this buffer
	int mCount = 0;		// How many elements to make current
	BufferLayout() : mIdentifier(0), mSize(0), mUsage(Usage::Vertex) { }
	BufferLayout(size_t identifier, int size, Usage usage, int count)
		: mIdentifier(identifier), mSize(size), mUsage(usage), mCount(count) { }
	std::span<Element> GetElements() { return std::span<Element>(mElements, mElementCount); }
	std::span<const Element> GetElements() const { return std::span<const Element>((const Element*)mElements, mElementCount); }
	bool IsValid() { return mElementCount != 0; }
	int CalculateBufferStride() const {
		int size = 0;
		for (auto& el : GetElements()) size += el.GetItemByteSize();
		return size;
	}
	void CalculateImplicitSize(int minSize = 0, bool roundTo256 = false) {
		mSize = CalculateBufferStride();
		mSize *= mCount;
		mSize = std::max(mSize, minSize);
		if (roundTo256) mSize = (mSize + 255) & (~255);
	}
};
struct BufferLayoutPersistent : public BufferLayout {
protected:
	std::vector<Element> mElementsStore;
public:
	int mAllocCount = 0;
	BufferLayoutPersistent() : BufferLayout() { }
	BufferLayoutPersistent(size_t identifier, int size, Usage usage, int count, int reserve = 4)
		: BufferLayout(identifier, size, usage, count)
	{
		mElementsStore.reserve(reserve);
	}
	BufferLayoutPersistent(const BufferLayoutPersistent& other) { *this = other; }
	BufferLayoutPersistent(BufferLayoutPersistent&& other) noexcept { *this = std::move(other); }
	BufferLayoutPersistent& operator =(const BufferLayoutPersistent& other) {
		*(BufferLayout*)this = *(BufferLayout*)&other;
		mElementsStore = other.mElementsStore;
		mElements = mElementsStore.data();
		return *this;
	}
	BufferLayoutPersistent& operator =(BufferLayoutPersistent&& other) {
		*(BufferLayout*)this = *(BufferLayout*)&other;
		mElementsStore = std::move(other.mElementsStore);
		mElements = mElementsStore.data();
		return *this;
	}
	int AppendElement(Element element) {
		mElementsStore.push_back(element);
		mElements = mElementsStore.data();
		mElementCount = (int)mElementsStore.size();
		return mElementCount - 1;
	}

	// Note! Unsfe! Must manually free later!
	bool AllocResize(int newCount) {
		int newSizeBytes = 0;
		for (auto& el : GetElements()) {
			auto newData = realloc(el.mData, el.mBufferStride * newCount);
			if (newData != nullptr) el.mData = newData;
			else return false;
			newSizeBytes += el.GetItemByteSize();
		}
		mSize = newSizeBytes * newCount;
		mAllocCount = newCount;
		return true;
	}
};
template<class T>
inline std::span<const T*> operator+(const std::vector<T*>& value) {
	return std::span<const T*>((const T**)value.data(), value.size());
}

struct BufferView {
	/*
	* byteN ->intN  : multiply
	* byteN ->int   : identity
	* byte  ->intN  : identity
	* byte  ->int   : identity
	* float ->int   : identity
	* int   ->float : identity
	* intN  ->float : multiply
	* float ->intN  : multiply
	* byteN ->float : multiply
	* float ->byteN : multiply
	*
	* Both types must be: Either float or any normalized type
	*
	*/
	const BufferLayout::Element* mElement;
	BufferFormatType mType;
	uint16_t mItemSize;
	static bool IsFloat(BufferFormat fmt) { return 0b01011 & (int)fmt; }

	template<bool B> struct Bool { constexpr static bool Get() { return B; } };
	template<bool N, typename T> struct Normalizer2 {
		typedef Bool<N> Normalized;
		constexpr static T GetFactor() { return (T)1; }
	};
	template<typename T> struct Normalizer2<true, T> {
		typedef Bool<true> Normalized;
		constexpr static T GetFactor() { return std::numeric_limits<T>::max(); }
	};
	template<> struct Normalizer2<true, float> {
		typedef Bool<true> Normalized;
		constexpr static float GetFactor() { return 1.0f; }
	};
	template<bool ToN, class To, bool FromN, class From> struct Normalizer3 {
		constexpr static To Convert(From v) {
			if constexpr (Normalizer2<ToN, To>::Normalized::Get() && Normalizer2<FromN, From>::Normalized::Get()) {
				return (To)(v * (float)Normalizer2<ToN, To>::GetFactor() / Normalizer2<FromN, From>::GetFactor());
			}
			else {
				return (To)v;
			}
		}
	};

	template<bool N, typename T> struct Normalizer {
		constexpr static float Normalize(T v) { return (float)v; }
		constexpr static T Denormalize(float v) { return (T)v; }
		constexpr static float GetFactor() { return 1.0f; }
	};
	template<typename T> struct Normalizer<true, T> {
		constexpr static float Normalize(T v) { return (float)v / (float)GetFactor(); }
		constexpr static T Denormalize(float v) { return (T)v * GetFactor(); }
		constexpr static T GetFactor() { return std::numeric_limits<T>::max(); }
	};
	template<bool N> struct ConvertTF {
		template<class T> static void TToF32(float* out, const T* in, int count) { std::transform(in, in + count, out, [](auto i) { return Normalizer<N, T>::Normalize(i); }); }
		template<class T> static void F32ToT(T* out, const float* in, int count) { std::transform(in, in + count, out, [](auto i) { return Normalizer<N, T>::Denormalize(i); }); }
	};
	struct ConvertGeneric {
		template<bool ToN, class To, bool FromN, class From> static void Convert(To* dest, const From* src, int count) {
			std::transform(src, src + count, dest, [](auto i) {
				return Normalizer3<ToN, To, FromN, From>::Convert(i);
				//return (To)(i * (Normalizer<To, ToN>::GetFactor() / Normalizer<From, FromN>::GetFactor()));
				});
		}
	};
	template<bool S, typename T> struct GetSigned { typedef T Type; };
	template<> struct GetSigned<false, int32_t> { typedef uint32_t Type; };
	template<> struct GetSigned<false, int16_t> { typedef uint16_t Type; };
	template<> struct GetSigned<false, int8_t> { typedef uint8_t Type; };
	struct Getter {
		template<bool ToN, class To, bool FromN, bool FromS, class From>
		static void ConvertTypedGet(To* dest, const void* src, int srcByteSize) {
			typedef typename GetSigned<FromS, From>::Type SFrom;
			ConvertGeneric::Convert<ToN, To, FromN, SFrom>(dest, (SFrom*)src, srcByteSize / sizeof(SFrom));
		}
		template<bool ToN, bool ToS, class To, bool FromN, class From>
		static void ConvertTypedSet(void* dest, const From* src, int srcByteSize) {
			typedef typename GetSigned<ToS, To>::Type STo;
			ConvertGeneric::Convert<ToN, STo, FromN, From>((STo*)dest, src, srcByteSize / sizeof(STo));
		}
		template<bool FromN, bool FromS, bool ToN = false, class To = float, bool ToS = true>
		static void GetO4(To* outDat, BufferFormatType::Sizes size, const void* data, int itemSize) {
			typedef typename GetSigned<ToS, To>::Type STo;
			switch (size) {
			case BufferFormatType::Size32: ConvertTypedGet<ToN, STo, FromN, FromS, int32_t>((STo*)outDat, data, itemSize); break;
			case BufferFormatType::Size16: ConvertTypedGet<ToN, STo, FromN, FromS, int16_t>((STo*)outDat, data, itemSize); break;
			case BufferFormatType::Size8: ConvertTypedGet<ToN, STo, FromN, FromS, int8_t>((STo*)outDat, data, itemSize); break;
			default: break;
			}
		}
		template<bool ToN, bool ToS, bool FromN = true, bool FromS = false, class From = float>
		static void SetO4(void* outDat, BufferFormatType::Sizes size, const From* data, int itemSize) {
			typedef typename GetSigned<FromS, From>::Type SFrom;
			switch (size) {
			case BufferFormatType::Size32: ConvertTypedSet<ToN, ToS, int32_t, FromN, SFrom>(outDat, (SFrom*)data, itemSize); break;
			case BufferFormatType::Size16: ConvertTypedSet<ToN, ToS, int16_t, FromN, SFrom>(outDat, (SFrom*)data, itemSize); break;
			case BufferFormatType::Size8: ConvertTypedSet<ToN, ToS, int8_t, FromN, SFrom>(outDat, (SFrom*)data, itemSize); break;
			default: break;
			}
		}
		template<bool S, bool N>
		static Vector4 GetItoF4(BufferFormatType::Sizes size, const void* data, int itemSize) {
			Vector4 value = { };
			switch (size) {
			case BufferFormatType::Size32: ConvertTF<N>::TToF32(&value.x, (typename GetSigned<S, int32_t>::Type*)data, itemSize / sizeof(int32_t)); break;
			case BufferFormatType::Size16: ConvertTF<N>::TToF32(&value.x, (typename GetSigned<S, int16_t>::Type*)data, itemSize / sizeof(int16_t)); break;
			case BufferFormatType::Size8: ConvertTF<N>::TToF32(&value.x, (typename GetSigned<S, int8_t>::Type*)data, itemSize / sizeof(int8_t)); break;
			default: break;
			}
			return value;
		}
	};
	BufferView(const BufferLayout::Element* element)
		: mElement(element)
		, mType(BufferFormatType::GetType(element->mFormat))
		, mItemSize(mType.GetByteSize()) { }
	Vector4 GetVec4(int index) const {
		auto* data = (uint8_t*)mElement->mData + index * mElement->mBufferStride;
		auto type = BufferFormatType::GetType(mElement->mFormat);
		if (type.size == BufferFormatType::Size32 && type.type == BufferFormatType::Float) {
			Vector4 value = { }; std::memcpy(&value, data, mItemSize); return value;
		}
		if (type.IsIntOrNrm()) {
			if (type.IsNormalized()) {
				if (type.IsSigned()) return Getter::GetItoF4<true, true>(type.size, data, mItemSize);
				else return Getter::GetItoF4<true, false>(type.size, data, mItemSize);
			}
			else {
				if (type.IsSigned()) return Getter::GetItoF4<false, true>(type.size, data, mItemSize);
				else return Getter::GetItoF4<false, false>(type.size, data, mItemSize);
			}
		}
		throw "Not implemented";
	}
	Vector3 GetVec3(int index) const { return GetVec4(index).xyz(); }
	Vector2 GetVec2(int index) const { return GetVec4(index).xy(); }
	float GetFloat(int index) const { return GetVec4(index).x; }
	ColorB4 GetColorB4(int index) const {
		auto* data = (uint8_t*)mElement->mData + index * mElement->mBufferStride;
		auto type = BufferFormatType::GetType(mElement->mFormat);
		ColorB4 value(ColorB4::Black);
		if (type.size == BufferFormatType::Size8 && type.IsIntOrNrm()) { std::memcpy(&value, data, mItemSize); return value; }
		if (type.IsIntOrNrm()) {
			if (type.IsNormalized()) {
				if (type.IsSigned()) Getter::GetO4<true, true, true>(&value.r, type.size, data, mItemSize);
				else				Getter::GetO4<true, false, true>(&value.r, type.size, data, mItemSize);
			}
			else {
				if (type.IsSigned()) Getter::GetO4<false, true, true>(&value.r, type.size, data, mItemSize);
				else				Getter::GetO4<false, false, true>(&value.r, type.size, data, mItemSize);
			}
			return value;
		}
		if (type.IsFloat()) {
			Getter::ConvertTypedGet<true, uint8_t, true, true, float>(&value.r, data, mItemSize);
			return value;
		}
		throw "Not implemented";
	}
	Int4 GetInt4(int index) const {
		auto* data = (uint8_t*)mElement->mData + index * mElement->mBufferStride;
		auto type = BufferFormatType::GetType(mElement->mFormat);
		Int4 value = { };
		if (type.size == BufferFormatType::Size32 && type.IsIntOrNrm()) {
			std::memcpy(&value, data, mItemSize); return value;
		}
		if (type.IsIntOrNrm()) {
			if (type.IsNormalized()) {
				if (type.IsSigned()) Getter::GetO4<true, true, false>(&value.x, type.size, data, mItemSize);
				else				Getter::GetO4<true, false, false>(&value.x, type.size, data, mItemSize);
			}
			else {
				if (type.IsSigned()) Getter::GetO4<false, true, false>(&value.x, type.size, data, mItemSize);
				else				Getter::GetO4<false, false, false>(&value.x, type.size, data, mItemSize);
			}
			return value;
		}
		if (type.IsFloat()) {
			Getter::ConvertTypedGet<false, int, true, true, float>(&value.x, data, mItemSize);
			return value;
		}
		throw "Not implemented";
	}
	int GetInt(int index) const { return GetInt4(index).x; }
	template<class T> T Get(int index) const;
	template<> Vector4 Get<Vector4>(int index) const { return GetVec4(index); }
	template<> Vector3 Get<Vector3>(int index) const { return GetVec3(index); }
	template<> Vector2 Get<Vector2>(int index) const { return GetVec2(index); }
	template<> float Get<float>(int index) const { return GetFloat(index); }
	template<> Int4 Get<Int4>(int index) const { return GetInt4(index); }
	template<> ColorB4 Get<ColorB4>(int index) const { return GetColorB4(index); }
	template<> int32_t Get<int32_t>(int index) const { return GetInt(index); }
	template<> uint32_t Get<uint32_t>(int index) const { return (uint32_t)GetInt(index); }

	void Set(int index, Vector4 value) {
		auto* data = (uint8_t*)mElement->mData + index * mElement->mBufferStride;
		auto type = BufferFormatType::GetType(mElement->mFormat);
		if (type.size == BufferFormatType::Size32 && type.IsFloat()) { std::memcpy(data, &value, mItemSize); return; }
		if (type.IsIntOrNrm()) {
			if (type.IsNormalized()) {
				if (type.IsSigned()) Getter::SetO4<true, true, true, true>(data, type.size, &value.x, mItemSize);
				else				Getter::SetO4<true, false, true, true>(data, type.size, &value.x, mItemSize);
			}
			else {
				if (type.IsSigned()) Getter::SetO4<false, true, true, true>(data, type.size, &value.x, mItemSize);
				else				Getter::SetO4<false, false, true, true>(data, type.size, &value.x, mItemSize);
			}
			return;
		}
		throw "Not supported";
	}
	void Set(int index, Vector3 value) { Set(index, Vector4(value, 0.0f)); }
	void Set(int index, Vector2 value) { Set(index, Vector4(value.x, value.y, 0.0f, 0.0f)); }
	void Set(int index, float value) { Set(index, Vector4(value)); }
	void Set(int index, ColorB4 value) {
		auto* data = (uint8_t*)mElement->mData + index * mElement->mBufferStride;
		if (mElement->mFormat == FORMAT_R8G8B8A8_UNORM || mElement->mFormat == FORMAT_R8G8B8A8_UINT) { std::memcpy(data, &value, mItemSize); return; }
		Set(index, (Vector4)value);
	}
	void Set(int index, Int4 value) {
		auto* data = (uint8_t*)mElement->mData + index * mElement->mBufferStride;
		auto type = BufferFormatType::GetType(mElement->mFormat);
		if (type.size == BufferFormatType::Size32 && type.IsIntOrNrm()) { std::memcpy(data, &value, mItemSize); return; }
		if (type.IsIntOrNrm()) {
			if (type.IsNormalized()) {
				if (type.IsSigned()) Getter::SetO4<true, false, false, true>(data, type.size, &value.x, mItemSize);
				else				Getter::SetO4<true, false, false, true>(data, type.size, &value.x, mItemSize);
			}
			else {
				if (type.IsSigned()) Getter::SetO4<false, true, false, true>(data, type.size, &value.x, mItemSize);
				else				Getter::SetO4<false, false, false, true>(data, type.size, &value.x, mItemSize);
			}
			return;
		}
		if (type.IsFloat()) {
			Getter::ConvertTypedSet<false, false, float, false>((float*)data, &value.x, mItemSize);
			return;
		}
		throw "Not supported";
	}
	void Set(int index, Int2 value) {
		auto* data = (uint8_t*)mElement->mData + index * mElement->mBufferStride;
		auto type = BufferFormatType::GetType(mElement->mFormat);
		if (type.size == BufferFormatType::Size32) {
			if (type.IsIntOrNrm()) { std::memcpy(data, &value, mItemSize); return; }
			if (type.IsFloat()) { std::transform(&value.x, &value.x + mType.GetComponentCount(), (float*)data, [=](auto v) { return (float)v; }); return; }
		}
		if (type.size == BufferFormatType::Size16) {
			if (type.IsIntOrNrm()) { std::transform(&value.x, &value.x + mType.GetComponentCount(), (int16_t*)data, [=](auto v) { return (int16_t)v; }); return; }
		}
		if (type.size == BufferFormatType::Size8) {
			if (type.IsIntOrNrm()) { std::transform(&value.x, &value.x + mType.GetComponentCount(), (int8_t*)data, [=](auto v) { return (int8_t)v; }); return; }
		}
		throw "Not supported";
	}
	void Set(int index, int32_t value) { Set(index, Int2(value, value)); }
	void Set(int index, uint32_t value) { Set(index, Int2((int)value, (int)value)); }

	void Set(std::span<const Vector4> values, int offset = 0) {
		if (Float32FastPath(offset, values.data(), (int)values.size(), 4)) return;
		for (int i = 0; i < values.size(); ++i) Set(offset + i, values[i]);
	}
	void Set(std::span<const Vector3> values, int offset = 0) {
		if (Float32FastPath(offset, values.data(), (int)values.size(), 3)) return;
		for (int i = 0; i < values.size(); ++i) Set(offset + i, values[i]);
	}
	void Set(std::span<const Vector2> values, int offset = 0) {
		if (Float32FastPath(offset, values.data(), (int)values.size(), 2)) return;
		for (int i = 0; i < values.size(); ++i) Set(offset + i, values[i]);
	}
	void Set(std::span<const float> values, int offset = 0) {
		if (Float32FastPath(offset, values.data(), (int)values.size(), 1)) return;
		for (int i = 0; i < values.size(); ++i) Set(offset + i, values[i]);
	}
	void Set(std::span<const Int4> values, int offset = 0) {
		if (Int32FastPath(offset, values.data(), (int)values.size(), 4)) return;
		for (int i = 0; i < values.size(); ++i) Set(offset + i, values[i]);
	}
	void Set(std::span<const Int2> values, int offset = 0) {
		if (Int32FastPath(offset, values.data(), (int)values.size(), 2)) return;
		for (int i = 0; i < values.size(); ++i) Set(offset + i, values[i]);
	}
	void Set(std::span<const int32_t> values, int offset = 0) {
		if (Int32FastPath(offset, values.data(), (int)values.size(), 1)) return;
		for (int i = 0; i < values.size(); ++i) Set(offset + i, values[i]);
	}
	void Set(std::span<const uint32_t> values, int offset = 0) {
		if (Int32FastPath(offset, values.data(), (int)values.size(), 1)) return;
		for (int i = 0; i < values.size(); ++i) Set(offset + i, values[i]);
	}
	void Set(std::span<const ColorB4> values, int offset = 0) {
		if (Int8FastPath(offset, values.data(), (int)values.size(), 4)) return;
		for (int i = 0; i < values.size(); ++i) Set(offset + i, values[i]);
	}

	template<class T>
	bool DataFastPath(BufferFormatType type, int index, const void* data, int count, int chCount) {
		if (type.GetComponentCount() == chCount) { memcpy((T*)mElement->mData + index * chCount, data, count * chCount * sizeof(T)); return true; }
		int dstCnt = mType.GetComponentCount();
		int cpyCnt = std::min(chCount, dstCnt);
		T* dstData = (T*)mElement->mData;
		T* srcData = (T*)data;
		for (int i = 0; i < count; ++i) {
			int c = 0;
			for (; c < cpyCnt; ++c) *(dstData++) = *(srcData++);
			for (; c < dstCnt; ++c) *(dstData++) = T();
			srcData += chCount - cpyCnt;
		}
		return false;
	}
	bool Float32FastPath(int index, const void* data, int count, int chCount) {
		auto type = BufferFormatType::GetType(mElement->mFormat);
		if (type.IsFloat() && type.size == BufferFormatType::Size32) return DataFastPath<float>(type, index, data, count, chCount);
		return false;
	}
	bool Int32FastPath(int index, const void* data, int count, int chCount) {
		auto type = BufferFormatType::GetType(mElement->mFormat);
		if (type.IsIntOrNrm() && type.size == BufferFormatType::Size32) return DataFastPath<uint32_t>(type, index, data, count, chCount);
		return false;
	}
	bool Int8FastPath(int index, const void* data, int count, int chCount) {
		auto type = BufferFormatType::GetType(mElement->mFormat);
		if (type.IsIntOrNrm() && type.size == BufferFormatType::Size8) return DataFastPath<uint8_t>(type, index, data, count, chCount);
		return false;
	}
};

template<typename T> struct TypedIterator;

template<typename T>
struct TypedAccessor {
	BufferView mView;
	int mIndex;
	TypedAccessor(BufferView view, int index)
		: mView(view), mIndex(index) { }
	//operator T () const { return mView.Get<T>(mIndex); }
	operator auto() const { return mView.Get<T>(mIndex); }
	void operator= (T value) { return mView.Set(mIndex, value); }
	//void operator= (const TypedAccessor<T> value) { return mView.Set(mIndex, (T)value); }
	bool operator== (const TypedAccessor<T>& o) const { return mIndex == o.mIndex; }
	//TypedIterator<T> operator&() const { return TypedIterator<T>(mView, mIndex); }
};
template<typename T>
struct TypedIterator : public TypedAccessor<T> {
	TypedIterator(BufferView view, int index)
		: TypedAccessor<T>(view, index) { }
	TypedIterator<T>& operator++() { this->mIndex++; return *this; }
	TypedAccessor<T>& operator* () { return *this; }
	const TypedAccessor<T>& operator* () const { return *this; }
};

template<typename T>
struct TypedBufferView {
	RangeInt mRange;
	BufferView mView;

	TypedBufferView() : mView(nullptr) {  }
	TypedBufferView(const BufferLayout::Element* element, int count) : mView(element), mRange(0, count) {  }
	TypedBufferView(const BufferLayout::Element* element, RangeInt range) : mView(element), mRange(range) {  }
	TypedIterator<T> begin() const { return TypedIterator<T>(mView, mRange.start); }
	TypedIterator<T> end() const { return TypedIterator<T>(mView, mRange.end()); }
	TypedAccessor<T> operator[](int i) const { i += mRange.start; assert(mRange.Contains(i)); return TypedAccessor<T>(mView, i); }
	size_t size() const { return mRange.length; }
	template<typename O>
	TypedBufferView<O> Reinterpret() { return TypedBufferView<O>(mView.mElement, mRange); }
	void Set(std::span<const Vector4> values, int offset = 0) { mView.Set(values, offset + mRange.start); }
	void Set(std::span<const Vector3> values, int offset = 0) { mView.Set(values, offset + mRange.start); }
	void Set(std::span<const Vector2> values, int offset = 0) { mView.Set(values, offset + mRange.start); }
	void Set(std::span<const float> values, int offset = 0) { mView.Set(values, offset + mRange.start); }
	void Set(std::span<const Int4> values, int offset = 0) { mView.Set(values, offset + mRange.start); }
	void Set(std::span<const Int2> values, int offset = 0) { mView.Set(values, offset + mRange.start); }
	void Set(std::span<const int> values, int offset = 0) { mView.Set(values, offset + mRange.start); }
	void Set(std::span<const ColorB4> values, int offset = 0) { mView.Set(values, offset + mRange.start); }
	template<typename V>
	void Set(int offset, const V value) { mView.Set(offset + mRange.start, value); }
};
