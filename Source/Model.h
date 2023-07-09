#pragma once

#include <vector>
#include <memory>

#include "Mesh.h"

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

};

