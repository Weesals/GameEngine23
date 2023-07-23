#pragma once

#include <Mesh.h>
#include <Texture.h>
#include <Material.h>
#include <GraphicsDeviceBase.h>
#include "Landscape.h"

class LandscapeRenderer
{
	static const int TileResolution = 8;

	// The mesh that is instanced across the surface
	std::shared_ptr<Mesh> mTileMesh;
	std::shared_ptr<Texture> mHeightMap;
	std::shared_ptr<Texture> mControlMap;
	std::shared_ptr<Material> mLandMaterial;

	std::shared_ptr<Landscape> mLandscape;

	int mRevision;

	struct Metadata
	{
		float MinHeight;
		float MaxHeight;
	};
	Metadata mMetadata;

public:
	void Initialise(std::shared_ptr<Landscape>& landscape, std::shared_ptr<Material>& rootMaterial);

	std::shared_ptr<Mesh>& RequireTileMesh();

	void Render(CommandBuffer& cmdBuffer);

};

