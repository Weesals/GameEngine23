#pragma once

#include "Canvas.h"
#include <imgui.h>

class UIGraphicsDebug : public CanvasRenderable
{
	const std::shared_ptr<GraphicsDeviceBase>& mGraphics;
public:
	UIGraphicsDebug(const std::shared_ptr<GraphicsDeviceBase>& graphics)
		: mGraphics(graphics) { }
	void Render(CommandBuffer& cmdBuffer) {
		if (ImGui::Begin("GDbg", 0, ImGuiWindowFlags_AlwaysAutoResize | ImGuiWindowFlags_NoMove))
		{
			int uiCount = mCanvas->GetDrawCount();
			auto& stats = mGraphics->mStatistics;
			// Hit points
			int bwbase = stats.mBufferBandwidth > 1024 ? 1024 : 1;
			ImGui::Text("BufferCreate = %d", stats.mBufferCreates);
			ImGui::Text("BufferWrites = %d", stats.mBufferWrites);
			ImGui::Text("Bandwidth = %d %s", stats.mBufferBandwidth / bwbase, bwbase == 1024 ? "kb" : "b");
			ImGui::Text("DrawCalls = %d (%d = UI)", stats.mDrawCount, uiCount);
			ImGui::Text("Instances = %d", stats.mInstanceCount);
			stats = { };
		}
		ImGui::End();
	}
};
