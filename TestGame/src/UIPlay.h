#pragma once

#include "Canvas.h"

class Play;

class UIPlay : public CanvasRenderable
{
	Play* mPlay;
	Canvas::OnInput::Reference mInputIntercept;
public:
	UIPlay(Play* play) : mPlay(play) { }
	void Initialise(Canvas* canvas) override;
	void Render(CommandBuffer& cmdBuffer) override;
};
