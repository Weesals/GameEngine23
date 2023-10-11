#pragma once

#include "RenderTarget2D.h"
#include "Material.h"
#include <memory>

class LightBase
{
};

class DirectionalLight : public LightBase
{
	Vector3 mDirection;
	std::shared_ptr<RenderTarget2D> mRenderTarget;
	std::shared_ptr<Material> mOverrideMaterial;
public:
	DirectionalLight();
	const std::shared_ptr<RenderTarget2D>& GetShadowBuffer() const { return mRenderTarget; }
	const std::shared_ptr<Material>& GetRenderPassMaterialOverride() const { return mOverrideMaterial; }
};
