#pragma once

#include "Mesh.h"
#include "Model.h"
#include <string>

class FBXImport
{
public:
	static std::shared_ptr<Model> ImportAsModel(const std::wstring& filename);

};

