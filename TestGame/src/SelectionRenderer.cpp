#include "SelectionRenderer.h"

#include "EntityComponents.h"
#include "EntitySystems.h"

SelectionRenderer::SelectionRenderer(std::shared_ptr<SelectionManager>& manager, std::shared_ptr<Material> rootMaterial)
	: mManager(manager)
{
	mMaterial = std::make_shared<Material>(L"assets/selection.hlsl");
	mMaterial->InheritProperties(rootMaterial);
	mMaterial->SetBlendMode(BlendMode::AlphaBlend());
	// Generate a XZ quad of 2x2 size centred at 0,0 (with uvs and normals)
	{
		mMesh = std::make_shared<Mesh>("Selection");
		mMesh->SetVertexCount(4);

		mMesh->RequireVertexNormals(BufferFormat::FORMAT_R8G8B8A8_SNORM);
		mMesh->RequireVertexTexCoords(0, BufferFormat::FORMAT_R8G8_UNORM);

		auto p = { Vector3(-1, 0, -1), Vector3(+1, 0, -1), Vector3(-1, 0, +1), Vector3(+1, 0, +1), };
		mMesh->GetPositionsV().Set(p);

		auto n = { Vector3(0, 1, 0), Vector3(0, 1, 0), Vector3(0, 1, 0), Vector3(0, 1, 0), };
		mMesh->GetNormalsV().Set(n);

		auto uv = { Vector2(0, 0), Vector2(1, 0), Vector2(0, 1), Vector2(1, 1), };
		mMesh->GetTexCoordsV(0).Set(uv);

		mMesh->SetIndices(std::span<const int>({ 0, 3, 1, 0, 2, 3, }));
		mMesh->MarkChanged();
	}

	auto& loader = ResourceLoader::GetSingleton();
	mFlagMesh = loader.LoadModel(L"assets/SM_Flag.fbx");
	mFlagMaterial = std::make_shared<Material>(L"assets/flags.hlsl");
	mFlagMaterial->InheritProperties(rootMaterial);

	// Create instanced renderers
	mSelectionRenderer = MeshDrawInstanced(mMesh.get(), mMaterial.get());
	mSelectionRenderer.InvalidateMesh();
	mSelectionRenderer.AddInstanceElement("INST_POSSIZE", BufferFormat::FORMAT_R32G32B32A32_FLOAT, sizeof(Vector4));
	mSelectionRenderer.AddInstanceElement("INST_PLAYERID", BufferFormat::FORMAT_R32G32B32A32_FLOAT, sizeof(Vector4));
	mFlagRenderer = MeshDrawInstanced(mFlagMesh->GetMeshes()[0].get(), mFlagMaterial.get());
	mFlagRenderer.AddInstanceElement("INST_POSSIZE", BufferFormat::FORMAT_R32G32B32A32_FLOAT, sizeof(Vector4));
	mFlagRenderer.AddInstanceElement("INST_PLAYERID", BufferFormat::FORMAT_R32G32B32A32_FLOAT, sizeof(Vector4));
	mFlagRenderer.InvalidateMesh();
}

void SelectionRenderer::OnEntityRegistered(flecs::entity entity)
{
}

void SelectionRenderer::Render(CommandBuffer& cmdBuffer, RenderPassList& passList)
{
	int selCount = (int)mManager->GetSelection().size();
	if (selCount == 0) return;
	{
		auto instanceData = cmdBuffer.RequireFrameData<Vector4>(selCount);
		auto instanceData2 = cmdBuffer.RequireFrameData<Vector4>(selCount);

		// Generate instances for selection reticles
		int i = 0;
		for (auto entity : mManager->GetSelection())
		{
			if (!entity.is_alive()) continue;
			const auto& tform = entity.get<Components::Transform>();
			const auto& footprint = entity.get<Components::Footprint>();
			instanceData[i] = (Vector4(tform->mPosition, footprint != nullptr ? footprint->mSize.x : 1.0f));
			Vector4 data2 = Vector4::Zero;
			auto owner = entity.target<Components::Owner>();
			if (owner.is_alive()) data2.w = (float)owner.get<MetaComponents::PlayerData>()->mPlayerId;
			instanceData2[i] = data2;
			++i;
		}
		auto hash = VariadicHash(
			GenericHash(instanceData.data(), i * sizeof(instanceData.data()[0])),
			GenericHash(instanceData.data(), i * sizeof(instanceData.data()[0]))
		);

		// Render the generated instances
		mSelectionRenderer.SetInstanceData(instanceData.data(), i, 0, mSelectionRendererHash != hash);
		mSelectionRenderer.SetInstanceData(instanceData2.data(), i, 1, mSelectionRendererHash != hash);
		for (auto& pass : passList)
			mSelectionRenderer.Draw(cmdBuffer, pass.mRenderQueue, DrawConfig::MakeDefault());
		mSelectionRendererHash = hash;
	}
	
	{
		auto instanceData = cmdBuffer.RequireFrameData<Vector4>(selCount);
		auto instanceData2 = cmdBuffer.RequireFrameData<Vector4>(selCount);
		// Generate instances for rendering target flags
		int i = 0;
		for (auto entity : mManager->GetSelection())
		{
			if (!entity.is_alive()) continue;
			const auto& moveTarget = entity.get<Components::Runtime::ActionMove>();
			if (moveTarget == nullptr) continue;
			instanceData[i] = Vector4(moveTarget->mLocation, 0.0f);
			Vector4 data2 = Vector4::Zero;
			auto owner = entity.target<Components::Owner>();
			if (owner.is_alive()) data2.w = (float)owner.get<MetaComponents::PlayerData>()->mPlayerId;
			instanceData2[i] = data2;
			++i;
		}
		auto hash = VariadicHash(
			GenericHash(instanceData.data(), i * sizeof(instanceData.data()[0])),
			GenericHash(instanceData.data(), i * sizeof(instanceData.data()[0]))
		);

		// Render flags for movement markers
		mFlagRenderer.SetInstanceData(instanceData.data(), i, 0, mFlagRendererHash != hash);
		mFlagRenderer.SetInstanceData(instanceData2.data(), i, 1, mFlagRendererHash != hash);
		for (auto& pass : passList)
			mFlagRenderer.Draw(cmdBuffer, pass.mRenderQueue, DrawConfig::MakeDefault());
		mFlagRendererHash = hash;
	}
}
