#pragma once

#include <vector>
#include <span>
#include <algorithm>

#include "MathTypes.h"

class Texture
{

	Int2 mSize;
	std::vector<uint8_t> mData;
	int mRevision;

public:
	Texture();
	void SetSize(Int2 size);
	Int2 GetSize() const;

	// Set texture data in 0xAABBGGRR format
	void SetPixels32Bit(std::span<const uint32_t> colors);
	std::vector<uint8_t>& GetRawData();
	const std::vector<uint8_t>& GetData() const;

	void MarkChanged() { mRevision++; }
	int GetRevision() const { return mRevision; }

};
