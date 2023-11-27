#pragma once

#include <string>
#include "Resources.h"

// Reference to a shader file on the HD
class Shader
{
private:
	std::wstring mPath;
	std::string mEntryPoint;
	Identifier mPathId;
public:
	Shader(std::wstring_view path, std::string entrypoint) : mPath(path), mPathId(path), mEntryPoint(entrypoint) { }

	const std::wstring& GetPath() const { return mPath; }
	Identifier GetIdentifier() const { return mPathId; }
	const std::string& GetEntryPoint() const { return mEntryPoint; }

};
