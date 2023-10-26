#pragma once

#include "ui/Canvas.h"
#include "ui/CanvasElements.h"

class Play;

class UIResources : public CanvasRenderable {
	Play* mPlay;
	int mPlayerId;
	CanvasImage mBackground;
public:
	UIResources();
	void Setup(Play* play, int playerId);
	void Initialise(CanvasBinding binding) override;
	void UpdateLayout(const CanvasLayout& parent) override;
	void Compose(CanvasCompositor::Context& composer) override;
	void Render(CommandBuffer& cmdBuffer) override;
};

class UIPlay : public CanvasRenderable {
	Play* mPlay;
	Canvas::OnInput::Reference mInputIntercept;

	CanvasImage mBackground;
	CanvasText mText;
	std::shared_ptr<UIResources> mResources;

public:
	UIPlay(Play* play);
	void Initialise(CanvasBinding binding) override;
	void Uninitialise(CanvasBinding binding) override;
	void UpdateLayout(const CanvasLayout& parent) override;
	void Compose(CanvasCompositor::Context& composer) override;
	void Render(CommandBuffer& cmdBuffer) override;
};
