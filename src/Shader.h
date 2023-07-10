#pragma once

#include <string>
#include "Resources.h"

// Reference to a shader file on the HD
class Shader
{
private:
	std::wstring mPath;
	Identifier mPathId;
public:
	Shader(std::wstring path) : mPath(path), mPathId(path) { }

	const std::wstring& GetPath() const { return mPath; }
	Identifier GetIdentifier() const { return mPathId; }

};
