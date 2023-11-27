// Needs to be first because has conflicts with windows.h
#include <ofbx.h>

#include "FBXImport.h"

#include "ResourceLoader.h"
#include "Material.h"

#include <iostream>
#include <fstream>
#include <algorithm>

// Load FBX data and convert it to the internal engine representation of a Model
std::shared_ptr<Model> FBXImport::ImportAsModel(const std::wstring& filename)
{

	// Read file data
	std::ifstream file(filename, std::ios::binary);
	if (!file) throw "Failed to open file";
	file.seekg(0, std::ios::end);
	auto filesize = file.tellg();
	file.seekg(0, std::ios::beg);

	std::vector<ofbx::u8> contents(filesize, '\0');
	if (!file.read((char*)contents.data(), filesize)) throw "Failed to read file contents";

	// Load FBX contents
	ofbx::LoadFlags flags =
		ofbx::LoadFlags::TRIANGULATE |
		ofbx::LoadFlags::IGNORE_BLEND_SHAPES |
		ofbx::LoadFlags::IGNORE_CAMERAS |
		ofbx::LoadFlags::IGNORE_LIGHTS |
		ofbx::LoadFlags::IGNORE_SKIN |
		ofbx::LoadFlags::IGNORE_BONES |
		ofbx::LoadFlags::IGNORE_PIVOTS |
		ofbx::LoadFlags::IGNORE_POSES |
		ofbx::LoadFlags::IGNORE_VIDEOS |
		ofbx::LoadFlags::IGNORE_LIMBS |
		ofbx::LoadFlags::IGNORE_ANIMATIONS;

	auto fbxScene = ofbx::load(contents.data(), (int)contents.size(), (ofbx::u16)flags);

	// The model that will be returned
	auto outModel = std::make_shared<Model>();
	// FBX is in cm; this engine units are meters
	auto scaleFactor = fbxScene->getGlobalSettings()->UnitScaleFactor / 100.0f;

	int meshCount = fbxScene->getMeshCount();
	for (int i = 0; i < meshCount; ++i) {
		auto fbxMesh = fbxScene->getMesh(i);
		auto fbxMeshGeo = fbxMesh->getGeometry();

		auto mesh = std::make_shared<Mesh>(fbxMesh->name);
		auto vertCount = fbxMeshGeo->getVertexCount();
		auto indCount = fbxMeshGeo->getIndexCount();

		// Grab the mesh transform (TODO: Do not bake into meshes)
		auto fbxXForm = fbxMesh->getGlobalTransform();
		Matrix xform;
		std::transform(fbxXForm.m, fbxXForm.m + 16, (float*)&xform, [](const auto item) { return (float)item; });
		xform *= Matrix::CreateScale(scaleFactor);

		// Copy vertices
		mesh->SetVertexCount(vertCount);
		auto vertices = fbxMeshGeo->getVertices();
		std::transform(vertices, vertices + vertCount, mesh->GetPositionsV().begin(), [=](const auto item) {
			return Vector3::Transform((Vector3((float)item.x, (float)item.y, (float)item.z)), xform);
		});

		// Copy normals
		auto normals = fbxMeshGeo->getNormals();
		if (normals != nullptr)
		{
			mesh->RequireVertexNormals(BufferFormat::FORMAT_R8G8B8A8_SNORM);
			std::transform(normals, normals + vertCount, mesh->GetNormalsV(true).begin(), [=](const auto item) {
				auto normal = Vector3::TransformNormal(Vector3((float)item.x, (float)item.y, (float)item.z), xform);
				normal = normal.Normalize();
				return normal;
			});
		}

		// Copy UVs
		auto uvs = fbxMeshGeo->getUVs();
		if (uvs != nullptr)
		{
			mesh->RequireVertexTexCoords(0, BufferFormat::FORMAT_R8G8_UNORM);
			std::transform(uvs, uvs + vertCount, mesh->GetTexCoordsV(0, true).begin(), [=](const auto item) {
				return Vector2((float)item.x, (float)item.y);
			});
		}

		// Copy vertex colours
		auto colors = fbxMeshGeo->getColors();
		if (colors != nullptr)
		{
			std::transform(colors, colors + vertCount, mesh->GetColorsV(true).begin(), [](const auto item) {
				return ColorB4((float)item.x / 255.0f, (float)item.y / 255.0f, (float)item.z / 255.0f, (float)item.w / 255.0f);
			});
		}

		// Merge same vertices
		auto& vbuffer = mesh->GetVertexBuffer();
		std::vector<int> vertRemap;
		std::unordered_map<size_t, int> vertHashMap;
		vertRemap.reserve(vbuffer.mCount);
		vertHashMap.reserve(vbuffer.mCount / 2);
		for (int v = 0; v < vbuffer.mCount; ++v) {
			size_t hash = 0;
			for (auto& element : vbuffer.GetElements()) {
				hash = AppendHash((uint8_t*)element.mData + element.mBufferStride * v, element.mFormat, hash);
			}
			int index = (int)vertHashMap.size();
			auto match = vertHashMap.find(hash);
			if (match != vertHashMap.end()) {
				index = vertRemap[match->second];
			}
			else {
				vertHashMap.insert(std::make_pair(hash, v));
			}
			vertRemap.push_back(index);
			if (index != v) {
				// Copy vertex to compacted index
				for (auto& element : vbuffer.GetElements()) {
					std::memcpy(
						(uint8_t*)element.mData + element.mBufferStride * index,
						(uint8_t*)element.mData + element.mBufferStride * v,
						element.GetItemByteSize());
				}
			}
		}
		vbuffer.mCount = (int)vertHashMap.size();
		vbuffer.CalculateImplicitSize();

		// Copy indices
		mesh->SetIndexFormat(false);
		mesh->SetIndexCount(indCount);
		auto indices = fbxMeshGeo->getFaceIndices();
		std::transform(indices, indices + indCount, mesh->GetIndicesV().begin(), [&](const auto item) {
			// Negative indices represent the end of a face in this library
			// but we requested triangulation, so ignore it
			int idx = (item < 0) ? -item - 1 : item;
			return vertRemap[idx];
		});

		// If the mesh transform flipped face orientation,
		// flip them back (via index swizzling)
		auto flip = xform.Determinant() < 0;
		if (flip)
		{
			auto meshInds = mesh->GetIndicesV();
			for (int i = 2; i < meshInds.size(); i += 3)
			{
				int t = meshInds[i - 1];
				meshInds[i - 1] = (int)meshInds[i];
				meshInds[i] = t;
				//std::swap(meshInds[i - 1], meshInds[i]);
			}
		}

		auto TexLoader = [&](const ofbx::Texture* tex)->std::shared_ptr<Texture> {
			if (tex == nullptr) return nullptr;
			auto fbxFName = tex->getFileName();
			std::wstring texPath;
			std::transform(fbxFName.begin, fbxFName.end, std::back_inserter(texPath), [](auto c) { return (wchar_t)c; });
			auto& loader = ResourceLoader::GetSingleton();
			return loader.LoadTexture(texPath);
		};

		auto fbxMatCount = fbxMesh->getMaterialCount();
		auto fbxMat = fbxMesh->getMaterial(0);
		auto texDiffuse = TexLoader(fbxMat->getTexture(ofbx::Texture::TextureType::DIFFUSE));
		if (texDiffuse != nullptr)
		{
			auto material = mesh->GetMaterial(true);
			material->SetUniformTexture("Texture", texDiffuse);
		}

		// Notify that this mesh data has changed
		mesh->MarkChanged();
		mesh->CalculateBoundingBox();

		// Add to the model to be returned
		outModel->AppendMesh(mesh);
	}
	fbxScene->destroy();

	return outModel;
}
