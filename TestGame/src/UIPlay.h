#pragma once

#include "Canvas.h"

class Play;

class UIPlay : public CanvasRenderable
{
	Play* mPlay;
public:
	UIPlay(Play* play) : mPlay(play) { }
	void Render(CommandBuffer& cmdBuffer);
};
