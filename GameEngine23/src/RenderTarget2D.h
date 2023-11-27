#pragma once

#include "Texture.h"
#include "Buffer.h"

class RenderTarget2D : public TextureBase
{
	Int2 mResolution;
	BufferFormat mFormat = BufferFormat::FORMAT_R8G8B8A8_UNORM;
public:
	RenderTarget2D(Int2 resolution);
	Int2 GetResolution() const { return mResolution; }
	void SetResolution(Int2 res) { mResolution = res; MarkChanged(); }
	BufferFormat GetFormat() const { return mFormat; }
	void SetFormat(BufferFormat format) { mFormat = format; MarkChanged(); }

	std::shared_ptr<RenderTarget2D>&& GetSharedPtr() { return (std::shared_ptr<RenderTarget2D>&&)shared_from_this(); }
};

