#pragma once

#include "Canvas.h"
#include <imgui.h>

class UIGraphicsDebug : public CanvasRenderable
{
	const std::shared_ptr<GraphicsDeviceBase>& mGraphics;
	time_point mTimePoint;
public:
	UIGraphicsDebug(const std::shared_ptr<GraphicsDeviceBase>& graphics)
		: mGraphics(graphics) { }
	void Render(CommandBuffer& cmdBuffer) {
		auto now = steady_clock::now();
		auto fps = 1000.0f / (float)std::chrono::duration_cast<std::chrono::milliseconds>(now - mTimePoint).count();
		mTimePoint = now;

		if (ImGui::Begin("GDbg", 0, ImGuiWindowFlags_AlwaysAutoResize | ImGuiWindowFlags_NoMove))
		{
			int uiCount = mCanvas->GetDrawCount();
			auto& stats = mGraphics->mStatistics;
			int frameData = cmdBuffer.GetFrameDataConsumed();
			ImGui::Text("FPS = %f", fps);
			ImGui::Text("BufferCreate = %d", stats.mBufferCreates);
			ImGui::Text("BufferWrites = %d", stats.mBufferWrites);
			ImGui::Text("Bandwidth = %d kb", stats.mBufferBandwidth / 1024);
			ImGui::Text("FrameArena = %d kb", frameData / 1024);
			ImGui::Text("DrawCalls = %d (%d = UI)", stats.mDrawCount, uiCount);
			ImGui::Text("Instances = %d", stats.mInstanceCount);
			stats = { };
		}
		ImGui::End();
	}
};
