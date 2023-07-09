#pragma once

#include <string>

// Reference to a shader file on the HD
class Shader
{
private:
	std::wstring mPath;
public:
	Shader(std::wstring path) : mPath(path) { }

	const std::wstring& GetPath() const { return mPath; }

};
