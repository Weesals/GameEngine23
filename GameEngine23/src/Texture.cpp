#include "Texture.h"

Texture::Texture()
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

void Texture::SetPixels32Bit(std::span<uint32_t> colors)
{
	std::transform(colors.begin(), colors.end(), (uint32_t*)mData.data(), [&](auto pixel)
		{
			return pixel;
		});
}
std::vector<uint8_t>& Texture::GetRawData()
{
	return mData;
}
const std::vector<uint8_t>& Texture::GetData() const
{
	return mData;
}
