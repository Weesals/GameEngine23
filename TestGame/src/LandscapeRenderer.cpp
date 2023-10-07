#include "LandscapeRenderer.h"
#include <ResourceLoader.h>

#include <algorithm>
#include <numeric>

LandscapeRenderer::LandscapeRenderer()
{
	mInstanceOffsets.reserve(256);
}

void LandscapeRenderer::Initialise(const std::shared_ptr<Landscape>& landscape, const std::shared_ptr<Material>& rootMaterial)
{
	mLandscape = landscape;
	if (mLandMaterial == nullptr) {
		mLandMaterial = std::make_shared<Material>(L"assets/landscape.hlsl");
		mLandMaterial->InheritProperties(rootMaterial);
		auto tex = ResourceLoader::GetSingleton().LoadTexture(L"assets/T_Grass_BaseColor.png");
		mLandMaterial->SetUniform("GrassTexture", tex);
	}
	mChangeListener = mLandscape->RegisterOnLandscapeChanged([this](auto& landscape, auto& changed)
		{
			mDirtyRegion.CombineWith(changed);
		});
	mLandscapeDraw = MeshDrawInstanced(RequireTileMesh().get(), mLandMaterial.get());
	mLandscapeDraw.AddInstanceElement("INSTANCE", BufferFormat::FORMAT_R16G16_UINT, sizeof(OffsetIV2));
}

std::shared_ptr<Mesh>& LandscapeRenderer::RequireTileMesh()
{
	if (mTileMesh == nullptr) {
		mTileMesh = std::make_shared<Mesh>("LandscapeTile");
		mTileMesh->SetVertexCount((TileResolution + 1) * (TileResolution + 1));
		mTileMesh->SetIndexCount(TileResolution * TileResolution * 6);
		mTileMesh->RequireVertexNormals(BufferFormat::FORMAT_R8G8B8A8_UNORM);
		for (int y = 0; y < TileResolution + 1; ++y)
		{
			for (int x = 0; x < TileResolution + 1; ++x)
			{
				int v = x + y * (TileResolution + 1);
				mTileMesh->GetPositionsV()[v] = Vector3((float)x, 0, (float)y);
				mTileMesh->GetNormalsV(true)[v] = Vector3(0.0f, 1.0f, 0.0f);
			}
		}
		auto indices = mTileMesh->GetIndicesV();
		for (int y = 0; y < TileResolution; ++y)
		{
			for (int x = 0; x < TileResolution; ++x)
			{
				int i = (x + y * TileResolution) * 6;
				int v0 = x + (y + 0) * (TileResolution + 1);
				int v1 = x + (y + 1) * (TileResolution + 1);
				indices[i + 0] = v0;
				indices[i + 1] = v1 + 1;
				indices[i + 2] = v0 + 1;
				indices[i + 3] = v0;
				indices[i + 4] = v1;
				indices[i + 5] = v1 + 1;
			}
		}
	}
	return mTileMesh;
}

void LandscapeRenderer::Render(CommandBuffer& cmdBuffer, const Matrix& vp)
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
	mLandMaterial->SetUniform("Model", xform);
	mLandMaterial->SetUniform("HeightMap", mHeightMap);
	mLandMaterial->SetUniform("HeightRange", Vector4(mMetadata.MinHeight, mMetadata.MaxHeight, 0.0f, 0.0f));

	// How many tile instances we need to render
	Int2 tileCount = (mLandscape->GetSize() + TileResolution - 1) / TileResolution;

	Frustum frustum(vp);

	mInstanceOffsets.reserve(tileCount.y * tileCount.x);
	int i = -1;
	int minChanged = std::numeric_limits<int>::max();
	int maxChanged = std::numeric_limits<int>::min();
	// Render the generated instances
	for (int y = 0; y < tileCount.y; ++y)
	{
		for (int x = 0; x < tileCount.x; ++x)
		{
			auto value = OffsetIV2((uint16_t)(y * TileResolution), (uint16_t)(x * TileResolution));
			auto ctr = Vector3::Transform(Vector3((x + 0.5f) * TileResolution, 1.0f, (y + 0.5f) * TileResolution), xform);
			auto ext = Vector3(TileResolution / 2.0f, 2.0f, TileResolution / 2.0f);
			if (!frustum.GetIsVisible(ctr, ext)) continue;

			++i;
			if (i >= mInstanceOffsets.size()) mInstanceOffsets.push_back({ });
			else if (mInstanceOffsets[i] == value) continue;
			mInstanceOffsets[i] = value;
			minChanged = std::min(minChanged, i);
			maxChanged = std::max(maxChanged, i);
		}
	}
	++i;
	if ((int)mInstanceOffsets.size() > i) {
		mInstanceOffsets.erase(mInstanceOffsets.begin() + i, mInstanceOffsets.end());
		minChanged = std::min(minChanged, i);
		maxChanged = std::max(maxChanged, i);
	}
	mLandscapeDraw.SetInstanceData(mInstanceOffsets.data(), (int)mInstanceOffsets.size(), 0, minChanged <= maxChanged);
	mLandscapeDraw.Draw(cmdBuffer, DrawConfig::MakeDefault());

}
