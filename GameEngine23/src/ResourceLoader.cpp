#include "ResourceLoader.h"

#include "FBXImport.h"
#define STB_IMAGE_IMPLEMENTATION
#include <stb_image.h>
#include <string>
#include <algorithm>
#include <iterator>

ResourceLoader ResourceLoader::gInstance;

const std::shared_ptr<Model>& ResourceLoader::LoadModel(const std::wstring_view& path)
{
	auto i = mLoadedMeshes.find(path);
	if (i == mLoadedMeshes.end())
	{
		std::wstring pathStr(path);
		mLoadedMeshes.insert(std::make_pair(pathStr, FBXImport::ImportAsModel(pathStr))).second;
		i = mLoadedMeshes.find(path);
	}
	return i->second;
}

const std::shared_ptr<Texture>& ResourceLoader::LoadTexture(const std::wstring_view& path)
{
	auto i = mLoadedTextures.find(path);
	if (i == mLoadedTextures.end())
	{
		std::string pathStr;
		std::transform(path.begin(), path.end(), std::back_inserter(pathStr), [](auto c) { return (char)c; });
		Int2 size;
		//auto data = SOIL_load_image(pathStr.c_str(), &size.x, &size.y, 0, SOIL_LOAD_RGBA);
		//stbi_set_flip_vertically_on_load(true);
		auto data = stbi_load(pathStr.c_str(), &size.x, &size.y, 0, STBI_rgb_alpha);
		std::shared_ptr<Texture> tex;
		if (data != nullptr) {
			tex = std::make_shared<Texture>();
			tex->SetSize(size);
			std::transform((const uint32_t*)data, (const uint32_t*)data + size.x * size.y, (uint32_t*)tex->GetRawData().data(), [](auto p)
				{
					return p;
				}
			);
			tex->MarkChanged();
			//SOIL_free_image_data(data);
			stbi_image_free(data);
		}
		i = mLoadedTextures.insert(std::make_pair(std::wstring(path), tex)).first;
	}
	return i->second;
}
const std::shared_ptr<FontInstance>& ResourceLoader::LoadFont(const std::wstring_view& path)
{
	if (mFontRenderer == nullptr) mFontRenderer = FontRenderer::Create();
	auto i = mLoadedFonts.find(path);
	if (i == mLoadedFonts.end())
	{
		auto instance = mFontRenderer->CreateInstance();
		std::string pathStr;
		std::transform(path.begin(), path.end(), std::back_inserter(pathStr), [](auto c) { return (char)c; });
		instance->Load(pathStr, "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()-=_+[]{}\\|;:'\",.<>/?`~ ");
		i = mLoadedFonts.insert(std::make_pair(std::wstring(path), instance)).first;
	}
	return i->second;
}
void ResourceLoader::Unload()
{
	mLoadedMeshes.clear();
	mLoadedTextures.clear();
}
