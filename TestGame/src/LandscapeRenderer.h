#pragma once

#include <Mesh.h>
#include <Texture.h>
#include <Material.h>
#include <GraphicsDeviceBase.h>
#include <RenderQueue.h>
#include "Landscape.h"

class LandscapeRenderer
{
	typedef std::tuple<uint16_t, uint16_t> OffsetIV2;
	static const int TileResolution = 8;

	// The mesh that is instanced across the surface
	std::shared_ptr<Mesh> mTileMesh;
	std::shared_ptr<Texture> mHeightMap;
	std::shared_ptr<Texture> mControlMap;
	std::shared_ptr<Material> mLandMaterial;

	MeshDrawInstanced mLandscapeDraw;
	size_t mLandscapeDrawHash;

	std::shared_ptr<Landscape> mLandscape;

	int mRevision;

	struct Metadata
	{
		float MinHeight;
		float MaxHeight;
	};
	Metadata mMetadata;

	Landscape::ChangeDelegate::Reference mChangeListener;
	Landscape::LandscapeChangeEvent mDirtyRegion;

public:
	LandscapeRenderer();

	void Initialise(const std::shared_ptr<Landscape>& landscape, const std::shared_ptr<Material>& rootMaterial);

	std::shared_ptr<Mesh>& RequireTileMesh();

	void Render(CommandBuffer& cmdBuffer, RenderPass& pass);

};

