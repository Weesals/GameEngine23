#pragma once

#include <vector>
#include <memory>

#include "Mesh.h"
#include "GraphicsDeviceBase.h"

// A collection of meshes
// TODO: Should store animation data
// TODO: Should store mesh hierarchy
class Model
{
	std::vector<std::shared_ptr<Mesh>> mMeshes;

public:
	void AppendMesh(std::shared_ptr<Mesh> mesh) {
		mMeshes.push_back(mesh);
	}

	std::span<std::shared_ptr<Mesh>> GetMeshes() {
		return mMeshes;
	}

	void Render(CommandBuffer& cmdBuffer, const std::shared_ptr<Material>& material)
	{
		for (auto& mesh : GetMeshes())
		{
			auto& meshMat = mesh->GetMaterial();
			if (meshMat != nullptr)
			{
				meshMat->InheritProperties(material);
				cmdBuffer.DrawMesh(mesh.get(), meshMat.get());
				meshMat->RemoveInheritance(material);
			}
			else
			{
				cmdBuffer.DrawMesh(mesh.get(), material.get());
			}
		}
	}

};

