#include "LandscapeRenderer.h"

#include <algorithm>
#include <numeric>

void LandscapeRenderer::Initialise(std::shared_ptr<Landscape>& landscape, std::shared_ptr<Material>& rootMaterial)
{
	mLandscape = landscape;
	if (mLandMaterial == nullptr) {
		mLandMaterial = std::make_shared<Material>(std::make_shared<Shader>(L"assets/landscape.hlsl"), std::make_shared<Shader>(L"assets/landscape.hlsl"));
		mLandMaterial->InheritProperties(rootMaterial);
	}
	mChangeListener = mLandscape->RegisterOnLandscapeChanged([this](auto& landscape, auto& changed)
		{
			mDirtyRegion.CombineWith(changed);
		});
}

std::shared_ptr<Mesh>& LandscapeRenderer::RequireTileMesh()
{
	if (mTileMesh == nullptr) {
		mTileMesh = std::make_shared<Mesh>();
		mTileMesh->SetVertexCount((TileResolution + 1) * (TileResolution + 1));
		mTileMesh->SetIndexCount(TileResolution * TileResolution * 6);
		for (int y = 0; y < TileResolution + 1; ++y)
		{
			for (int x = 0; x < TileResolution + 1; ++x)
			{
				int v = x + y * (TileResolution + 1);
				mTileMesh->GetPositions()[v] = Vector3((float)x, 0, (float)y);
				mTileMesh->GetNormals(true)[v] = Vector3(0.0f, 1.0f, 0.0f);
			}
		}
		for (int y = 0; y < TileResolution; ++y)
		{
			for (int x = 0; x < TileResolution; ++x)
			{
				int i = (x + y * TileResolution) * 6;
				int v0 = x + (y + 0) * (TileResolution + 1);
				int v1 = x + (y + 1) * (TileResolution + 1);
				mTileMesh->GetIndices()[i + 0] = v0;
				mTileMesh->GetIndices()[i + 1] = v1 + 1;
				mTileMesh->GetIndices()[i + 2] = v0 + 1;
				mTileMesh->GetIndices()[i + 3] = v0;
				mTileMesh->GetIndices()[i + 4] = v1;
				mTileMesh->GetIndices()[i + 5] = v1 + 1;
			}
		}
	}
	return mTileMesh;
}

void LandscapeRenderer::Render(CommandBuffer& cmdBuffer)
{
	// Pack heightmap data into a texture
	if (mHeightMap == nullptr)
	{
		mHeightMap = std::make_shared<Texture>();
		mHeightMap->SetSize(mLandscape->GetSize());
		mRevision = -1;
		mDirtyRegion = Landscape::LandscapeChangeEvent::All(mLandscape->GetSize());
	}
	// The terrain has changed, need to update the texture
	if (mDirtyRegion.GetHasChanges())
	{
		// Get the heightmap data
		const auto& heightmap = mLandscape->GetRawHeightMap();
		auto sizing = mLandscape->GetSizing();
		// Get the min/max range of heights (to normalize into the texture)
		int heightMin = std::accumulate(heightmap.begin(), heightmap.end(), std::numeric_limits<int>::max(),
			[](int v, auto item) { return std::min(v, (int)item.Height); });
		int heightMax = std::accumulate(heightmap.begin(), heightmap.end(), std::numeric_limits<int>::min(),
			[](int v, auto item) { return std::max(v, (int)item.Height); });
		// Get the inner texture data
		auto& pxHeightData = mHeightMap->GetRawData();
		auto range = mDirtyRegion.Range;
		for (int y = range.GetMin().y; y < range.GetMax().y; ++y)
		{
			for (int x = range.GetMin().x; x < range.GetMax().x; ++x)
			{
				auto i = sizing.ToIndex(Int2(x, y));
				auto h11 = heightmap[i];
				uint32_t c = 0;
				// Pack height into first byte
				((uint8_t*)&c)[0] = 255 * (heightmap[i].Height - heightMin) / std::max(1, heightMax - heightMin);
				{
					auto h01 = heightmap[sizing.ToIndex(Int2::Clamp(Int2(x - 1, y), 0, sizing.Size - 1))];
					auto h21 = heightmap[sizing.ToIndex(Int2::Clamp(Int2(x + 1, y), 0, sizing.Size - 1))];
					auto h10 = heightmap[sizing.ToIndex(Int2::Clamp(Int2(x, y - 1), 0, sizing.Size - 1))];
					auto h12 = heightmap[sizing.ToIndex(Int2::Clamp(Int2(x, y + 1), 0, sizing.Size - 1))];
					auto nrm = Vector3((float)(h01.Height - h21.Height), (float)sizing.Scale1024, (float)(h10.Height - h12.Height));
					nrm = nrm.Normalize();
					// Pack normal into 2nd and 3rd
					((uint8_t*)&c)[1] = (uint8_t)(127 + (nrm.x * 127));
					((uint8_t*)&c)[2] = (uint8_t)(127 + (nrm.z * 127));
				}

				// Write the pixel
				((uint32_t*)pxHeightData.data())[i] = c;
			}
		}
		// Mark the texture as having been changed
		mHeightMap->MarkChanged();
		// Allow the shader to reconstruct the height range
		mMetadata.MinHeight = (float)heightMin / Landscape::HeightScale;
		mMetadata.MaxHeight = (float)heightMax / Landscape::HeightScale;
		// Mark the data as current
		mDirtyRegion = Landscape::LandscapeChangeEvent::None();
		mRevision = mLandscape->GetRevision();
	}

	// Calculate material parameters
	auto scale = mLandscape->GetScale();
	auto xform = Matrix::CreateScale(scale, 1.0f, scale) *
		Matrix::CreateTranslation(mLandscape->GetSizing().Location);
	mLandMaterial->SetUniform("Model", xform.Transpose());
	mLandMaterial->SetUniform("HeightMap", mHeightMap);
	mLandMaterial->SetUniform("HeightRange", Vector4(mMetadata.MinHeight, mMetadata.MaxHeight, 0.0f, 0.0f));

	// The mesh for each chunk
	auto tileMesh = RequireTileMesh();
	// How many tile instances we need to render
	Int2 tileCount = (mLandscape->GetSize() + TileResolution - 1) / TileResolution;
	// A buffer to store tile offsets
	std::vector<Vector4> offsets;
	offsets.reserve(256);

	// Render the generated instances
	auto flush = [&]() {
		mLandMaterial->SetInstanceCount((int)offsets.size());
		mLandMaterial->SetInstancedUniform("Offsets", offsets);
		cmdBuffer.DrawMesh(tileMesh, mLandMaterial);
		offsets.clear();
	};
	for (int y = 0; y < tileCount.y; ++y)
	{
		for (int x = 0; x < tileCount.x; ++x)
		{
			offsets.push_back(Vector4((float)(x * TileResolution), (float)(y * TileResolution), 0.0f, 0.0f));

			// Can only draw in batches of 256 instances
			// (as thats what the shader defines)
			if (offsets.size() >= 256) flush();
		}
	}
	if (!offsets.empty()) flush();
}
