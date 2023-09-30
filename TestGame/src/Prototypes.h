#pragma once

#include <flecs.h>
#include <vector>
#include <map>
#include <string>

#include <Model.h>
#include <ResourceLoader.h>
#include "EntityComponents.h"

// Store named entity "prototypes" which an entity can be an instance of
class Prototypes
{
	std::vector<flecs::entity> mPrototypes;
	std::map<std::string, int, Identifier::comp> mProtoByName;

	std::vector<std::shared_ptr<Model>> mEntityModels;
	std::map<std::wstring, int, Identifier::comp> mModelsByName;

	void AppendEntity(flecs::entity entity);
public:

	void Load(flecs::world& world);

	int GetPrototypeId(const std::string_view& name) const;
	flecs::entity GetPrototypePrefab(int id) const;
	flecs::entity GetPrototypePrefab(const std::string_view& name) const;

	int RequireModelId(const std::wstring_view& path);
	const std::shared_ptr<Model>& GetModel(int id);

};

class MutatedPrototypes
{
	struct Bundle
	{
		std::string mName;
		std::vector<int> mMutations;
		std::map<int, flecs::entity> mProtoCaches;
	};
	struct Mutation
	{
		std::string mName;
		std::function<bool(flecs::entity)> mIsRelevant;
		std::function<void(flecs::entity)> mApply;
	};
	flecs::world* mECS;
	std::shared_ptr<Prototypes> mPrototypes;
	std::vector<Bundle> mBundles;
	std::vector<Mutation> mMutations;

public:
	struct UsesBundle { int mBundleId; };

	void Load(flecs::world* ecs, const std::shared_ptr<Prototypes>& prototypes);
	int CrateStateBundle(const std::string_view& name);
	int GetStateBundleId(const std::string_view& name);
	int FindMutationId(const std::string_view& name) const;
	bool ApplyMutation(int bundleId, int mutationId);
	bool GetHasMutation(int bundleId, int mutationId) const;
	flecs::entity RequireMutatedPrefab(int stateBundle, int protoId);

	static int GetBundleIdFromEntity(flecs::entity);
};
