#pragma once

#include "ui/Canvas.h"
#include "ui/CanvasElements.h"

class Play;

class UIResources : public CanvasRenderable {
	Play* mPlay;
	int mPlayerId;
public:
	UIResources();
	void Initialise(Play* play, int playerId);
	void Render(CommandBuffer& cmdBuffer) override;
};

class UIPlay : public CanvasRenderable {
	Play* mPlay;
	Canvas::OnInput::Reference mInputIntercept;

	CanvasImage mBackground;
	std::shared_ptr<UIResources> mResources;

public:
	UIPlay(Play* play);
	void Initialise(CanvasBinding binding) override;
	void Uninitialise(CanvasBinding binding) override;
	void UpdateLayout(const CanvasLayout& parent) override;
	void Compose(CanvasCompositor::Context& composer);
	void Render(CommandBuffer& cmdBuffer) override;
};
