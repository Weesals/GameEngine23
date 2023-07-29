#pragma once

#include "SelectionManager.h"
#include "GraphicsDeviceBase.h"
#include <FBXImport.h>

#include <memory>

class SelectionRenderer
{
	std::shared_ptr<SelectionManager> mManager;
	std::shared_ptr<Mesh> mMesh;
	std::shared_ptr<Material> mMaterial;
	std::shared_ptr<Model> mFlagMesh;
	std::shared_ptr<Material> mFlagMaterial;

public:
	SelectionRenderer(std::shared_ptr<SelectionManager>& manager, std::shared_ptr<Material> rootMaterial);

	void OnEntityRegistered(flecs::entity entity);

	void Render(CommandBuffer& cmdBuffer);

};

