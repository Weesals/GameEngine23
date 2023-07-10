// Needs to be first because has conflicts with windows.h
#include <ofbx.h>

#include "FBXImport.h"

#include <iostream>
#include <fstream>
#include <algorithm>

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

	int meshCount = fbxScene->getMeshCount();
	for (int i = 0; i < meshCount; ++i) {
		auto fbxMesh = fbxScene->getMesh(i);
		auto fbxMeshGeo = fbxMesh->getGeometry();

		auto mesh = std::make_shared<Mesh>();
		auto vertCount = fbxMeshGeo->getVertexCount();
		auto indCount = fbxMeshGeo->getIndexCount();

		// Grab the mesh transform (TODO: Do not bake into meshes)
		auto fbxXForm = fbxMesh->getGlobalTransform();
		Matrix xform;
		std::transform(fbxXForm.m, fbxXForm.m + 16, (float*)&xform, [fbxXForm](const auto item) { return (float)item; });
		auto flip = xform.Determinant() > 0;
		mesh->SetVertexCount(vertCount);

		// Copy vertices
		auto vertices = fbxMeshGeo->getVertices();
		std::transform(vertices, vertices + vertCount, mesh->GetPositions().begin(), [xform](const auto item) {
			return Vector3::Transform((Vector3((float)item.x, (float)item.y, (float)item.z) / 100), xform);
		});

		// Copy normals
		auto normals = fbxMeshGeo->getNormals();
		if (normals != nullptr)
		{
			std::transform(normals, normals + vertCount, mesh->GetNormals(true).begin(), [xform](const auto item) {
				auto normal = Vector3::TransformNormal(Vector3((float)item.x, (float)item.y, (float)item.z), xform);
				normal = normal.Normalize();
				return normal;
			});
		}

		// Copy UVs
		auto uvs = fbxMeshGeo->getUVs();
		if (uvs != nullptr)
		{
			std::transform(uvs, uvs + vertCount, mesh->GetUVs(true).begin(), [](const auto item) {
				return Vector2((float)item.x, (float)item.y);
			});
		}

		// Copy vertex colours
		auto colors = fbxMeshGeo->getColors();
		if (colors != nullptr)
		{
			std::transform(colors, colors + vertCount, mesh->GetColors(true).begin(), [](const auto item) {
				return Color((float)item.x, (float)item.y, (float)item.z, (float)item.w);
			});
		}

		// Copy indices
		mesh->SetIndexCount(indCount);
		auto indices = fbxMeshGeo->getFaceIndices();
		std::transform(indices, indices + indCount, mesh->GetIndices().begin(), [](const auto item) {
			int idx = (item < 0) ? -item - 1 : item;
			return idx;
		});

		// If the mesh transform flipped face orientation,
		// flip them back (via index swizzling)
		if (flip) {
			auto meshInds = mesh->GetIndices();
			for (int i = 2; i < meshInds.size(); i += 3) std::swap(meshInds[i - 1], meshInds[i]);
		}

		// Notify that this mesh data has changed
		mesh->MarkChanged();

		// Add to the model to be returned
		outModel->AppendMesh(mesh);
	}

	return outModel;
}
