#pragma once

#include <map>
#include "Resources.h"
#include "Texture.h"
#include "Material.h"
#include "Model.h"

class ResourceLoader
{
	std::map<std::wstring, std::shared_ptr<Model>, Resources::comp> mLoadedMeshes;
	std::map<std::wstring, std::shared_ptr<Texture>, Resources::comp> mLoadedTextures;

	static ResourceLoader gInstance;
public:
	const std::shared_ptr<Model>& LoadModel(const std::wstring_view& path);
	const std::shared_ptr<Texture>& LoadTexture(const std::wstring_view& path);

	static ResourceLoader& GetSingleton() { return gInstance; }
};
