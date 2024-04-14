#include "Texture.h"

#include <algorithm>

TextureBase::TextureBase(const std::wstring_view& name)
	: mName(name), mRevision(0) { }

void TextureBase::SetAllowUnorderedAccess(bool value) {
	(int&)mFlags &= ~Flags::AllowUnorderedAccess;
	if (value) (int&)mFlags |= Flags::AllowUnorderedAccess;
}
bool TextureBase::GetAllowUnorderedAccess() const {
	return (mFlags & Flags::AllowUnorderedAccess) != 0;
}


void Texture::ResizeData(Sizing oldSize) {
	if (mData.empty()) return;
	size_t oldDataSize = (int)mData.size();
	size_t newDataSize = GetSliceSize(mSize.mSize, mSize.mMipCount, mFormat) * mSize.mArrayCount;
	mData.resize(std::max(newDataSize, oldDataSize));
	int oldSliceSize = GetSliceSize(oldSize.mSize, oldSize.mMipCount, mFormat);
	int newSliceSize = GetSliceSize(mSize.mSize, mSize.mMipCount, mFormat);
	int slicesToCopy = std::min(mSize.mArrayCount, oldSize.mArrayCount);
	int sStart = newDataSize > oldDataSize ? slicesToCopy - 1 : 0;
	int sEnd = newDataSize > oldDataSize ? -1 : slicesToCopy;
	int sDir = newDataSize > oldDataSize ? -1 : 1;
	for (int s = sStart; s != sEnd; s += sDir) {
		int oldOffset = oldSliceSize * s;
		int newOffset = newSliceSize * s;
		if (oldOffset != newOffset) {
			std::memcpy(mData.data() + newOffset, mData.data() + oldOffset, std::min(newSliceSize, oldSliceSize));
		}
	}
	mData.resize(newDataSize);
}

void Texture::SetSize(Int2 size) {
	SetSize3D(Int3(size, 1));
}
void Texture::SetSize3D(Int3 size) {
	if (mSize.mSize == size) return;
	auto oldSize = mSize;
	mSize.mSize = size;
	ResizeData(oldSize);
}
Int3 Texture::GetSize() const {
	return mSize.mSize;
}

void Texture::SetMipCount(int count) {
	if (mSize.mMipCount == count) return;
	auto oldSize = mSize;
	mSize.mMipCount = count;
	ResizeData(oldSize);
}
int Texture::GetMipCount() const { return mSize.mMipCount; }

void Texture::SetArrayCount(int count) {
	if (mSize.mArrayCount == count) return;
	auto oldSize = mSize;
	mSize.mArrayCount = count;
	ResizeData(oldSize);
}
int Texture::GetArrayCount() const { return mSize.mArrayCount; }

void Texture::SetBufferFormat(BufferFormat fmt) {
	mFormat = fmt;
	mData.clear();
}
BufferFormat Texture::GetBufferFormat() const { return mFormat; }

void Texture::SetPixels32Bit(std::span<const uint32_t> colors) {
	std::transform(colors.begin(), colors.end(), (uint32_t*)GetRawData(0, 0).data(), [&](auto pixel) { return pixel; });
	MarkChanged();
}
void Texture::RequireData() {
	if (!mData.empty()) return;
	int dataSize = GetSliceSize(mSize.mSize, mSize.mMipCount, mFormat) * mSize.mArrayCount;
	mData.resize(dataSize);
}
std::span<uint8_t> Texture::GetRawData(int mip, int slice) {
	RequireData();
	if (mip < 0 || slice < 0) return std::span<uint8_t>(mData.begin(), mData.end());
	uint32_t imgOffset = 0;
	uint32_t imgSize = 0;
	if (slice > 0) {
		imgOffset += GetSliceSize(mSize.mSize, mSize.mMipCount, mFormat) * slice;
	}
	for (int m = 0; m < mip; ++m) {
		auto mipSize = GetMipResolution(mSize.mSize, mFormat, m);
		imgOffset += GetRawImageSize(mipSize, mFormat);
	}
	{
		auto mipSize = GetMipResolution(mSize.mSize, mFormat, mip);
		imgSize = GetRawImageSize(mipSize, mFormat);
	}
	return std::span<uint8_t>(mData.begin() + imgOffset, imgSize);
}
std::span<const uint8_t> Texture::GetData(int mip, int slice) const {
	return const_cast<Texture*>(this)->GetRawData(mip, slice);
}

int Texture::GetSliceSize(Int3 res, int mips, BufferFormat fmt) {
	uint32_t sliceSize = GetRawImageSize(res, fmt);
	for (int m = 0; m < mips; ++m) {
		auto mipSize = GetMipResolution(res, fmt, m);
		sliceSize += GetRawImageSize(mipSize, fmt);
	}
	return sliceSize;
}
Int3 Texture::GetMipResolution(Int3 res, BufferFormat fmt, int mip) {
	return Int3(
		std::max(1, res.x >> mip),
		std::max(1, res.y >> mip),
		std::max(1, res.z >> mip)
	);
}
uint32_t Texture::GetRawImageSize(Int3 res, BufferFormat fmt) {
	auto meta = BufferFormatType::GetType(fmt);
	if (meta.size == BufferFormatType::Sizes::Other) {
		int bitSize = BufferFormatType::GetBitSize(fmt);
		int blockSize = BufferFormatType::GetCompressedBlockSize(fmt);
		int blocksX = (res.x + blockSize - 1) / blockSize;
		int blocksY = (res.y + blockSize - 1) / blockSize;
		return blocksX * blocksY * bitSize * (blockSize * blockSize) / 8 * res.z;
	}
	return res.x * res.y * res.z * meta.GetByteSize();
}
