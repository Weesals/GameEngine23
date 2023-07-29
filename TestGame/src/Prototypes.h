#pragma once

#include <flecs.h>
#include <vector>
#include <map>
#include <string>

#include <Model.h>
#include <ResourceLoader.h>
#include "EntityComponents.h"

class Prototypes
{
	std::vector<flecs::entity> mPrototypes;
	std::map<std::string, int, Resources::comp> mProtoByName;

	std::vector<std::shared_ptr<Model>> mEntityModels;
	std::map<std::wstring, int, Resources::comp> mModelsByName;

	void AppendEntity(flecs::entity entity);
public:

	void Load(flecs::world& world);

	int GetPrototypeId(const std::string_view& name) const;
	flecs::entity GetPrototypePrefab(int id) const;
	flecs::entity GetPrototypePrefab(const std::string_view& name) const;

	int RequireModelId(const std::wstring_view& path);
	const std::shared_ptr<Model>& GetModel(int id);

};

