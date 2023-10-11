#include "Lighting.h"

DirectionalLight::DirectionalLight()
	: mDirection(-0.4f, -0.8f, 0.0f)
{
	mRenderTarget = std::make_shared<RenderTarget2D>(Int2(1024, 1024));
	mOverrideMaterial = std::make_shared<Material>();
	mOverrideMaterial->SetRenderPassOverride("ShadowCast");
	mOverrideMaterial->SetUniform("View", Matrix::Identity);
	mOverrideMaterial->SetUniform("Projection", Matrix::Identity);
}
