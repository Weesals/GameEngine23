#include "Texture.h"

#include <algorithm>

TextureBase::TextureBase()
	: mRevision(0) { }

void Texture::SetSize(Int2 size)
{
	mSize = size;
	mData.resize(size.x * size.y * 4);
}
Int2 Texture::GetSize() const
{
	return mSize;
}

void Texture::SetPixels32Bit(std::span<const uint32_t> colors)
{
	std::transform(colors.begin(), colors.end(), (uint32_t*)mData.data(), [&](auto pixel)
		{
			return pixel;
		});
	MarkChanged();
}
std::span<uint8_t> Texture::GetRawData()
{
	return mData;
}
std::span<const uint8_t> Texture::GetData() const
{
	return mData;
}
