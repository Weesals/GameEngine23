#pragma once

#include <vector>
#include <span>
#include <memory>

#include "MathTypes.h"
#include "Buffer.h"

class TextureBase : public std::enable_shared_from_this<TextureBase>
{
	int mRevision;
public:
	TextureBase();
	virtual ~TextureBase() { }
	void MarkChanged() { mRevision++; }
	int GetRevision() const { return mRevision; }
};

class Texture : public TextureBase
{
	struct Sizing {
		Int2 mSize;
		int mMipCount = 1;
		int mArrayCount = 1;
	};
	Sizing mSize;
	BufferFormat mFormat = BufferFormat::FORMAT_R8G8B8A8_UNORM;
	std::vector<uint8_t> mData;

	void ResizeData(Sizing oldSize);

public:
	using TextureBase::TextureBase;
	void SetSize(Int2 size);
	Int2 GetSize() const;

	void SetMipCount(int count);
	int GetMipCount() const;

	void SetArrayCount(int count);
	int GetArrayCount() const;

	void SetBufferFormat(BufferFormat fmt);
	BufferFormat GetBufferFormat() const;

	// Set texture data in 0xAABBGGRR format
	void RequireData();
	void SetPixels32Bit(std::span<const uint32_t> colors);
	std::span<uint8_t> GetRawData(int mip = 0, int slice = 0);
	std::span<const uint8_t> GetData(int mip = 0, int slice = 0) const;

	static int GetSliceSize(Int2 res, int mips, BufferFormat fmt);
	static Int2 GetMipResolution(Int2 res, BufferFormat fmt, int mip);
	static uint32_t GetRawImageSize(Int2 res, BufferFormat fmt);

};
