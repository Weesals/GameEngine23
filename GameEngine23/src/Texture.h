#pragma once

#include <vector>
#include <span>

#include "MathTypes.h"

class TextureBase
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

	Int2 mSize;
	std::vector<uint8_t> mData;

public:
	using TextureBase::TextureBase;
	void SetSize(Int2 size);
	Int2 GetSize() const;

	// Set texture data in 0xAABBGGRR format
	void SetPixels32Bit(std::span<const uint32_t> colors);
	std::span<uint8_t> GetRawData();
	std::span<const uint8_t> GetData() const;

};
