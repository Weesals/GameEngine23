#include "SelectionRenderer.h"

#include "EntityComponents.h"
#include "EntitySystems.h"

SelectionRenderer::SelectionRenderer(std::shared_ptr<SelectionManager>& manager, std::shared_ptr<Material> rootMaterial)
	: mManager(manager)
{
	//mManager->CreateEntityListener(std::bind(&OnEntityRegistered, this, std::placeholders::_1), true);
	mMaterial = std::make_shared<Material>(L"assets/selection.hlsl");
	mMaterial->InheritProperties(rootMaterial);
	mMaterial->SetBlendMode(BlendMode::AlphaBlend());
	auto& loader = ResourceLoader::GetSingleton();
	mFlagMesh = loader.LoadModel(L"assets/SM_Flag.fbx");
	mFlagMaterial = std::make_shared<Material>(L"assets/flags.hlsl");
	mFlagMaterial->InheritProperties(rootMaterial);
}

void SelectionRenderer::OnEntityRegistered(flecs::entity entity)
{
}

void SelectionRenderer::Render(CommandBuffer& cmdBuffer)
{
	// Generate a XZ quad of 2x2 size centred at 0,0 (with uvs and normals)
	if (mMesh == nullptr)
	{
		mMesh = std::make_shared<Mesh>("Selection");
		mMesh->SetVertexCount(4);
		auto p = { Vector3(-1, 0, -1), Vector3(+1, 0, -1), Vector3(-1, 0, +1), Vector3(+1, 0, +1), };
		std::copy(p.begin(), p.end(), mMesh->GetPositions().begin());
		auto n = { Vector3(0, 1, 0), Vector3(0, 1, 0), Vector3(0, 1, 0), Vector3(0, 1, 0), };
		std::copy(n.begin(), n.end(), mMesh->GetNormals(true).begin());
		auto uv = { Vector2(0, 0), Vector2(1, 0), Vector2(0, 1), Vector2(1, 1), };
		std::copy(uv.begin(), uv.end(), mMesh->GetUVs(true).begin());
		mMesh->SetIndices({ 0, 3, 1, 0, 2, 3, });
		mMesh->MarkChanged();
	}

	std::vector<Vector4> instanceData;
	std::vector<Vector4> instanceData2;

	// Render the generated instances
	auto flush = [&]()
	{
		mMaterial->SetInstanceCount((int)instanceData.size());
		mMaterial->SetInstancedUniform("InstanceData", instanceData);
		mMaterial->SetInstancedUniform("InstanceData2", instanceData2);
		cmdBuffer.DrawMesh(mMesh.get(), mMaterial.get());
		instanceData.clear();
		instanceData2.clear();
	};
	for (auto entity : mManager->GetSelection())
	{
		if (!entity.is_alive()) continue;
		const auto& tform = entity.get<Components::Transform>();
		const auto& footprint = entity.get<Components::Footprint>();
		instanceData.push_back(Vector4(tform->mPosition, footprint != nullptr ? footprint->mSize.x : 1.0f));
		Vector4 i = Vector4::Zero;
		auto owner = entity.target<Components::Owner>();
		if (owner.is_alive()) i.w = (float)owner.get<MetaComponents::PlayerData>()->mPlayerId;
		instanceData2.push_back(i);
		if (instanceData.size() >= 256) flush();
	}
	if (!instanceData.empty()) flush();


	// Render flags for movement markers
	auto flushFlags = [&]()
	{
		mFlagMaterial->SetInstanceCount((int)instanceData.size());
		mFlagMaterial->SetInstancedUniform("InstanceData", instanceData);
		mFlagMaterial->SetInstancedUniform("InstanceData2", instanceData2);
		mFlagMesh->Render(cmdBuffer, mFlagMaterial);
		instanceData.clear();
		instanceData2.clear();
	};
	for (auto entity : mManager->GetSelection())
	{
		if (!entity.is_alive()) continue;
		const auto& moveTarget = entity.get<Components::Runtime::ActionMove>();
		if (moveTarget == nullptr) continue;
		Vector4 i = Vector4::Zero;
		i.xyz() = moveTarget->mLocation;
		instanceData.push_back(i);
		i = Vector4::Zero;
		auto owner = entity.target<Components::Owner>();
		if (owner.is_alive()) i.w = (float)owner.get<MetaComponents::PlayerData>()->mPlayerId;
		instanceData2.push_back(i);
		if (instanceData.size() >= 256) flushFlags();
	}
	if (!instanceData.empty()) flushFlags();
}
