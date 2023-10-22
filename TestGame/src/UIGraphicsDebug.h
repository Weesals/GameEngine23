#pragma once

#include "ui/Canvas.h"
#include <imgui.h>

class UIGraphicsDebug : public CanvasRenderable
{
	const std::shared_ptr<GraphicsDeviceBase>& mGraphics;
	time_point mTimePoint;

	std::chrono::nanoseconds mStepTimer;
	std::chrono::nanoseconds mRenderTimer;
public:
	UIGraphicsDebug(const std::shared_ptr<GraphicsDeviceBase>& graphics)
		: mGraphics(graphics) { }
	void Render(CommandBuffer& cmdBuffer) {
		auto now = steady_clock::now();
		auto ms = (int)std::chrono::duration_cast<std::chrono::milliseconds>(now - mTimePoint).count();
		auto fps = 1000.0f / (float)ms;

		auto update_ms = (int)std::chrono::duration_cast<std::chrono::milliseconds>(mStepTimer).count();
		auto render_ms = (int)std::chrono::duration_cast<std::chrono::milliseconds>(mRenderTimer).count();

		if (ImGui::Begin("GDbg", 0, ImGuiWindowFlags_AlwaysAutoResize | ImGuiWindowFlags_NoMove))
		{
			int uiCount = GetCanvas()->GetDrawCount();
			auto& stats = mGraphics->mStatistics;
			int frameData = cmdBuffer.GetFrameDataConsumed();
			ImGui::Text("FPS = %.0f", fps, ms);
			ImGui::Text("Update %d ms  Render %d ms", update_ms, render_ms);
			ImGui::Text("BufferCreate = %d", stats.mBufferCreates);
			ImGui::Text("BufferWrites = %d", stats.mBufferWrites);
			ImGui::Text("Bandwidth = %d kb", stats.mBufferBandwidth / 1024);
			ImGui::Text("FrameArena = %d kb", frameData / 1024);
			ImGui::Text("DrawCalls = %d (%d = UI)", stats.mDrawCount, uiCount);
			ImGui::Text("Instances = %d", stats.mInstanceCount);
			stats = { };
		}
		ImGui::End();

		mTimePoint = now;
		mStepTimer = { };
		mRenderTimer = { };
	}
	void AppendStepTimer(std::chrono::nanoseconds timer) {
		mStepTimer += timer;
	}
	void AppendRenderTimer(std::chrono::nanoseconds timer) {
		mRenderTimer += timer;
	}
};
