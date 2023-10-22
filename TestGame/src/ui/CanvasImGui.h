#pragma once

#include "Canvas.h"

class CanvasImGui : public Canvas {
protected:
	std::shared_ptr<Texture> mFontTexture;

public:
	CanvasImGui();
	~CanvasImGui();

	void SetSize(Int2 size) override;
	virtual bool GetIsPointerOverUI(Vector2 v) const override;

	virtual void Update(const std::shared_ptr<Input>& input) override;
	virtual void Render(CommandBuffer& cmdBuffer) override;

};
