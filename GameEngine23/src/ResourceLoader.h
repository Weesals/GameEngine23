#pragma once

#include <map>
#include "Resources.h"
#include "Texture.h"
#include "Material.h"
#include "Model.h"
#include "./ui/font/FontRenderer.h"

class ResourceLoader
{
	std::map<std::wstring, std::shared_ptr<Model>, Identifier::comp> mLoadedMeshes;
	std::map<std::wstring, std::shared_ptr<Texture>, Identifier::comp> mLoadedTextures;
	std::map<std::wstring, std::shared_ptr<FontInstance>, Identifier::comp> mLoadedFonts;

	std::shared_ptr<FontRenderer> mFontRenderer;

	static ResourceLoader gInstance;
public:
	const std::shared_ptr<Model>& LoadModel(const std::wstring_view& path);
	const std::shared_ptr<Texture>& LoadTexture(const std::wstring_view& path);
	const std::shared_ptr<FontInstance>& LoadFont(const std::wstring_view& path);
	void Unload();

	static ResourceLoader& GetSingleton() { return gInstance; }
};
