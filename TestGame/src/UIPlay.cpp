#include "UIPlay.h"

#include "Play.h"
#include <imgui.h>

void UIPlay::Render(CommandBuffer& cmdBuffer)
{
	//ImGui::ShowDemoWindow(&show_demo_window);
	auto world = mPlay->GetWorld();

	auto size = mCanvas->GetSize();

	// Display the players resources
	auto player = world->GetPlayer(1);
	auto pdata = player.get<MetaComponents::PlayerData>();
	if (ImGui::Begin("Player", 0, ImGuiWindowFlags_AlwaysAutoResize | ImGuiWindowFlags_NoMove))
	{
		auto wsize = ImGui::GetWindowSize();
		ImGui::SetWindowSize(ImVec2(200.0f, 50.0f));
		ImGui::SetWindowPos(ImVec2(size.x / 2 - wsize.x / 2, 10.0f), ImGuiCond_Always);
		ImGui::BeginTable("Resources", 10);
		for (auto res : pdata->mResources)
		{
			ImGui::TableNextColumn();
			ImGui::Text("%d = %d", res.mResourceId, res.mAmount);
		}
		ImGui::EndTable();
		ImGui::End();
	}

	// Display details of the selected unit
	const auto& selection = mPlay->GetSelection();
	auto hero = selection->GetHeroEntity();
	if (hero.is_alive())
	{
		auto mutProtos = world->GetMutatedProtos();
		auto bundleId = MutatedPrototypes::GetBundleIdFromEntity(hero);

		// Render the hero entity panel
		auto type = hero.target(flecs::IsA);
		while (type.is_alive() && type.name() == nullptr) type = type.target(flecs::IsA);
		auto name = type.is_alive() ? type.name() : nullptr;
		if (name == nullptr || std::strlen(name) == 0) name = "-";
		ImGui::Begin(name, 0, ImGuiWindowFlags_AlwaysAutoResize | ImGuiWindowFlags_NoMove);
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
		// Helper function to determine if an item is available
		// (does not have valid restrictions)
		auto GetIsAvailable = [](flecs::entity e)
		{
			auto* reqAge = e.get<Tags::RequireAge>();
			if (reqAge != nullptr && reqAge->mAge >= 0) return false;
			return true;
		};
		// Statistics
		const auto& los = hero.get<Components::LineOfSight>();
		if (los != nullptr) ImGui::Text("LOS = %.0f", los->mRange);
		const auto& gathers = hero.get<Components::Gathers>();
		if (gathers != nullptr) ImGui::Text("Holding: %d = %d", gathers->mHolding.mResourceId, gathers->mHolding.mAmount);
		const auto& stockpile = hero.get<Components::Stockpile>();
		if (stockpile != nullptr)
		{
			for (auto res : stockpile->mResources)
			{
				ImGui::Text("%d = %d", res.mResourceId, res.mAmount);
			}
		}
		ImGui::End();
		ImVec2 pos(240.0f, size.y - wsize.y - 10.0f);
		// Training panel
		const auto* trains = hero.get<Components::Trains>();
		if (trains != nullptr && ImGui::Begin("Trains", 0, ImGuiWindowFlags_AlwaysAutoResize | ImGuiWindowFlags_NoMove))
		{
			ImGui::SetWindowPos(pos, ImGuiCond_Always);
			ImGui::BeginTable("Trains", 5);
			for (auto item : trains->mTrains)
			{
				auto protoId = world->GetPrototypes()->GetPrototypeId(item);
				auto mutProto = mutProtos->RequireMutatedPrefab(bundleId, protoId);
				if (!GetIsAvailable(mutProto)) continue;

				ImGui::TableNextColumn();
				if (ImGui::Button(item.c_str(), ImVec2(60, 20)))
				{
					mPlay->SendActionRequest(Actions::ActionRequest{
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
			pos.x += ImGui::GetWindowSize().x;
			ImGui::End();
		}
		// Build panel
		const auto* builds = hero.get<Components::Builds>();
		if (builds != nullptr && ImGui::Begin("Builds", 0, ImGuiWindowFlags_AlwaysAutoResize | ImGuiWindowFlags_NoMove))
		{
			ImGui::SetWindowPos(pos, ImGuiCond_Always);
			ImGui::BeginTable("Builds", 10);
			auto placeId = mPlay->GetPlacementProtoId();
			for (auto item : builds->mBuilds)
			{
				auto protoId = mPlay->GetWorld()->GetPrototypes()->GetPrototypeId(item);
				auto mutProto = mutProtos->RequireMutatedPrefab(bundleId, protoId);
				if (!GetIsAvailable(mutProto)) continue;

				ImGui::TableNextColumn();
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
			pos.x += ImGui::GetWindowSize().x;
			ImGui::End();
		}
		// Tech panel
		const auto* techs = hero.get<Components::Techs>();
		if (techs != nullptr && ImGui::Begin("Techs", 0, ImGuiWindowFlags_AlwaysAutoResize | ImGuiWindowFlags_NoMove))
		{
			ImGui::SetWindowPos(pos, ImGuiCond_Always);
			ImGui::BeginTable("Techs", 10);
			auto placeId = mPlay->GetPlacementProtoId();
			for (auto item : techs->mTechs)
			{
				auto mutId = mutProtos->FindMutationId(item);
				if (mutProtos->GetHasMutation(bundleId, mutId)) continue;
				ImGui::TableNextColumn();
				auto protoId = mPlay->GetWorld()->GetPrototypes()->GetPrototypeId(item);
				if (protoId != -1 && protoId == placeId)
				{
					ImGui::PushStyleColor(ImGuiCol_Button, ImVec4(1.0f, 1.0f, 0.3f, 1.0f));
					ImGui::PushStyleColor(ImGuiCol_ButtonHovered, ImVec4(1.0f, 1.0f, 0.5f, 1.0f));
				}
				if (ImGui::Button(item.c_str()))
				{
					mutProtos->ApplyMutation(bundleId, mutId);
				}
				if (protoId != -1 && protoId == placeId) ImGui::PopStyleColor(2);
			}
			ImGui::EndTable();
			pos.x += ImGui::GetWindowSize().x;
			ImGui::End();
		}
	}

}
