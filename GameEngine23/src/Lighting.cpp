#include "Lighting.h"

DirectionalLight::DirectionalLight()
	: mDirection(-0.4f, -0.8f, 0.0f)
{
	mRenderTarget = std::make_shared<RenderTarget2D>(Int2(1024));
	//mRenderTarget->SetFormat(BufferFormat::FORMAT_R8G8B8A8_UNORM);
	mRenderTarget->SetFormat(BufferFormat::FORMAT_D24_UNORM_S8_UINT);
	mOverrideMaterial = std::make_shared<Material>();
	mOverrideMaterial->SetRenderPassOverride("ShadowCast");
	mOverrideMaterial->SetUniform("View", Matrix::Identity);
	mOverrideMaterial->SetUniform("Projection", Matrix::Identity);
}
