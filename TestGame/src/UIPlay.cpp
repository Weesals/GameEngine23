#include "UIPlay.h"

#include "Play.h"
#include <imgui.h>

void UIPlay::Render(CommandBuffer& cmdBuffer)
{
	//ImGui::ShowDemoWindow(&show_demo_window);
	auto world = mPlay->GetWorld();

	auto size = mCanvas->GetSize();

	const auto& selection = mPlay->GetSelection();
	auto hero = selection->GetHeroEntity();
	if (hero.is_alive())
	{
		// Render the hero entity panel
		auto type = hero.target(flecs::IsA);
		ImGui::Begin(type.name(), 0, ImGuiWindowFlags_AlwaysAutoResize | ImGuiWindowFlags_NoMove);
		auto wsize = ImGui::GetWindowSize();
		ImGui::SetWindowPos(ImVec2(10.0f, size.y - wsize.y - 10.0f), ImGuiCond_Always);
		// Hit points
		const auto& durability = hero.get<Components::Durability>();
		if (durability != nullptr)
		{
			float v = (float)durability->mBaseHitPoints / 100.0f;
			ImGui::Text("Health");
			ImGui::ProgressBar(v, ImVec2(200, 4), "");
		}
		// Statistics
		const auto& los = hero.get<Components::LineOfSight>();
		if (los != nullptr) ImGui::Text("LOS = %.0f", los->mRange);
		ImGui::End();
		// Training panel
		const auto& trains = hero.get<Components::Trains>();
		if (trains != nullptr && ImGui::Begin("Trains", 0, ImGuiWindowFlags_AlwaysAutoResize | ImGuiWindowFlags_NoMove))
		{
			ImGui::SetWindowPos(ImVec2(240.0f, size.y - wsize.y - 10.0f), ImGuiCond_Always);
			ImGui::BeginTable("Trains", 5);
			for (auto item : trains->mTrains)
			{
				ImGui::TableNextColumn();
				auto protoId = world->GetPrototypes()->GetPrototypeId(item);
				if (ImGui::Button(item.c_str(), ImVec2(60, 20)))
				{
					mPlay->SendActionRequest(Components::ActionRequest{
						.mActionTypeId = Systems::TrainingSystem::ActionId,
							.mActionData = protoId,
					});
				}
				auto training = hero.get<Components::Runtime::ActionTrain>();
				if (training != nullptr && training->mProtoId == protoId)
				{
					ImGui::ProgressBar((float)training->mTrainPoints / 5000.0f, ImVec2(60, 5), "");
				}
			}
			ImGui::EndTable();
			ImGui::End();
		}
		// Build panel
		const auto& builds = hero.get<Components::Builds>();
		if (builds != nullptr && ImGui::Begin("Builds", 0, ImGuiWindowFlags_AlwaysAutoResize | ImGuiWindowFlags_NoMove))
		{
			ImGui::SetWindowPos(ImVec2(240.0f, size.y - wsize.y - 10.0f), ImGuiCond_Always);
			ImGui::BeginTable("Builds", 10);
			auto placeId = mPlay->GetPlacementProtoId();
			for (auto item : builds->mBuilds)
			{
				ImGui::TableNextColumn();
				auto protoId = mPlay->GetWorld()->GetPrototypes()->GetPrototypeId(item);
				if (protoId != -1 && protoId == placeId)
				{
					ImGui::PushStyleColor(ImGuiCol_Button, ImVec4(1.0f, 1.0f, 0.3f, 1.0f));
					ImGui::PushStyleColor(ImGuiCol_ButtonHovered, ImVec4(1.0f, 1.0f, 0.5f, 1.0f));
				}
				if (ImGui::Button(item.c_str()))
				{
					mPlay->BeginPlacement(protoId);
				}
				if (protoId != -1 && protoId == placeId) ImGui::PopStyleColor(2);
			}
			ImGui::EndTable();
			ImGui::End();
		}
	}

}
