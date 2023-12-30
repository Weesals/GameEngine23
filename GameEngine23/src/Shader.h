#pragma once

#include <string>
#include "Resources.h"

// Reference to a shader file on the HD
class Shader
{
private:
	Identifier mEntryPoint;
	Identifier mPathId;
public:
	Shader(std::wstring_view path, std::string entrypoint) : mPathId(path), mEntryPoint(entrypoint) { }

	const std::wstring& GetPath() const { return Identifier::GetWName(mPathId); }
	Identifier GetIdentifier() const { return mPathId; }
	Identifier GetEntryPoint() const { return mEntryPoint; }

	size_t GetHash() const { return (mPathId.mId) + (mEntryPoint.mId << 16); }

};
