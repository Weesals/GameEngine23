#pragma once

#include "Texture.h"

class RenderTarget2D : public TextureBase
{
	Int2 mResolution;
public:
	RenderTarget2D(Int2 resolution);
	Int2 GetResolution() const { return mResolution; }
};

