#include "Prototypes.h"

#include "FBXImport.h"
#include<algorithm>

void Prototypes::AppendEntity(flecs::entity entity)
{
	int id = (int)mPrototypes.size();
	mPrototypes.push_back(entity);
	mProtoByName[std::string(entity.name())] = id;
}

void Prototypes::Load(flecs::world& world)
{
	auto entityBase = world.prefab("Entity Base")
		.add<Components::Transform>();
	auto buildingBase = world.prefab("Building Base")
		.is_a(entityBase)
		.set(Components::Footprint {.mSize = { 4.0f, 4.0f }, .mHeight = 1.0f, })
		.set(Components::LineOfSight {.mRange = 1000 });
	auto unitBase = world.prefab("Unit Base")
		.is_a(entityBase)
		.set(Components::LineOfSight {.mRange = 1000 })
		.set(Components::Mobility{.mSpeed = 2, .mTurnSpeed = 400, });
	AppendEntity(world.prefab("Town Centre")
		.is_a(buildingBase)
		.set(Tags::RequireAge{.mAge = 2})
		.set(Tags::Flags {.mSingular = true, })
		.set(Components::Footprint {.mSize = { 6.0f, 6.0f }, .mHeight = 1.0f, })
		.set(Components::Durability {.mBaseHitPoints = 500, })
		.set(Components::Dropsite::All())
		.set(Components::Trains {.mTrains { "Villager", "Hero", } })
		.set(Components::Techs {.mTechs { "Age 2" } })
		.set(Components::Renderable{.mModelId = RequireModelId(L"assets/SM_TownCentre.fbx")})
	);
	AppendEntity(world.prefab("Storehouse")
		.is_a(buildingBase)
		.set(Components::Durability {.mBaseHitPoints = 200, })
		.set(Components::Dropsite::All())
		.set(Components::Renderable{.mModelId = RequireModelId(L"assets/SM_Storehouse.fbx")})
	);
	AppendEntity(world.prefab("Farm")
		.is_a(buildingBase)
		.set(Components::Durability {.mBaseHitPoints = 200, })
		.set(Components::Footprint {.mSize = { 6.0f, 6.0f }, .mHeight = 1.0f, })
		.set(Components::Stockpile {.mResources = { ResourceSet(0, 100), }, })
		.set(Components::Renderable{.mModelId = RequireModelId(L"assets/SM_Farm.fbx")})
	);
	AppendEntity(world.prefab("House")
		.is_a(buildingBase)
		.set(Components::Durability {.mBaseHitPoints = 200, })
		.set(Components::Renderable{.mModelId = RequireModelId(L"assets/SM_House.fbx")})
	);
	AppendEntity(world.prefab("Barracks")
		.is_a(buildingBase)
		.set(Tags::RequireAge{.mAge = 2})
		.set(Components::Durability {.mBaseHitPoints = 200, })
		.set(Components::Footprint {.mSize = { 6.0f, 6.0f }, .mHeight = 1.0f, })
		.set(Components::Trains {.mTrains { "Militia", "Swordsman", } })
		.set(Components::Renderable{.mModelId = RequireModelId(L"assets/SM_Barracks.fbx")})
	);
	AppendEntity(world.prefab("Archery Range")
		.is_a(buildingBase)
		.set(Tags::RequireAge{.mAge = 2})
		.set(Components::Durability {.mBaseHitPoints = 200, })
		.set(Components::Footprint {.mSize = { 6.0f, 6.0f }, .mHeight = 1.0f, })
		.set(Components::Trains {.mTrains { "Archer", "Crossbow", "Longbow", } })
		.set(Components::Renderable{.mModelId = RequireModelId(L"assets/SM_ArcheryRange.fbx")})
	);
	AppendEntity(world.prefab("Construction")
		.is_a(buildingBase)
		.set(Components::Footprint {.mSize = { 6.0f, 6.0f }, .mHeight = 1.0f, })
		.set(Components::Construction {.mProtoId = -1, })
		.set(Components::Renderable{.mModelId = RequireModelId(L"assets/SM_Construction3x3.fbx")})
	);
	AppendEntity(world.prefab("Villager")
		.is_a(unitBase)
		.set(Components::Builds {.mBuilds { "House", "Farm", "Storehouse", "Barracks", "Archery Range", "Town Centre", } })  
		.set(Components::Gathers {.mGathers { ResourceSet(0, 100), }, })
		.set(Components::Renderable{.mModelId = RequireModelId(L"assets/SM_Character_Worker.fbx")})
	);
	AppendEntity(world.prefab("Hero")
		.is_a(unitBase)
		.set(Tags::RequireAge{.mAge = 2})
		.set(Components::Renderable{.mModelId = RequireModelId(L"assets/SM_Character_Worker.fbx")})
	);
	AppendEntity(world.prefab("Deer")
		.is_a(unitBase)
		.add<Components::Wanders>()
		.set(Components::Renderable{.mModelId = RequireModelId(L"assets/SM_Deer.fbx")})
	);
	AppendEntity(world.prefab("Tree")
		.is_a(entityBase)
		.set(Tags::Flags {.mDefaultGaia = true, })
		.set(Components::Footprint {.mSize = { 2.0f, 2.0f }, .mHeight = 2.0f, })
		.set(Components::Stockpile {.mResources = { ResourceSet(1, 100), }, })
		.set(Components::Renderable{.mModelId = RequireModelId(L"assets/SM_Tree.fbx")})
	);
}

int Prototypes::GetPrototypeId(const std::string_view& name) const
{
	auto item = mProtoByName.find(name);
	if (item == mProtoByName.end())
	{
		return -1;
	}
	return item->second;
}
flecs::entity Prototypes::GetPrototypePrefab(int id) const
{
	return mPrototypes[id];
}
flecs::entity Prototypes::GetPrototypePrefab(const std::string_view& name) const
{
	return mPrototypes[GetPrototypeId(name)];
}

int Prototypes::RequireModelId(const std::wstring_view& path)
{
	auto item = mModelsByName.find(path);
	if (item != mModelsByName.end()) return item->second;
	int id = (int)mEntityModels.size();
	mModelsByName.insert({ std::wstring(path), id, });
	auto& loader = ResourceLoader::GetSingleton();
	mEntityModels.push_back(loader.LoadModel(path));
	return id;

}
const std::shared_ptr<Model>& Prototypes::GetModel(int id)
{
	return mEntityModels[id];
}


void MutatedPrototypes::Load(flecs::world* ecs, const std::shared_ptr<Prototypes>& prototypes)
{
	mECS = ecs;
	mPrototypes = prototypes;
	mMutations.push_back(
		Mutation{
			"Wheelbarrow",
			[](flecs::entity e) { return e.has<Tags::Villager>(); },
			[](flecs::entity e)
			{
				auto gatherer = e.get_mut<Components::Gathers>();
				if (gatherer == nullptr) return;
				for (auto& item : gatherer->mGathers)
					item.mAmount *= 2;
			},
		}
	);
	mMutations.push_back(
		Mutation{
			"Age 2",
			[](flecs::entity e) { auto a = e.get<Tags::RequireAge>(); return a != nullptr && a->mAge == 2; },
			[](flecs::entity e) { e.set(Tags::RequireAge::MakeNone()); },
		}
	);
	mMutations.push_back(
		Mutation{
			"Age 3",
			[](flecs::entity e) { auto a = e.get<Tags::RequireAge>(); return a != nullptr && a->mAge == 3; },
			[](flecs::entity e) { e.set(Tags::RequireAge::MakeNone()); },
		}
	);
	mMutations.push_back(
		Mutation{
			"Age 4",
			[](flecs::entity e) { auto a = e.get<Tags::RequireAge>(); return a != nullptr && a->mAge == 4; },
			[](flecs::entity e) { e.set(Tags::RequireAge::MakeNone()); },
		}
	);
}
int MutatedPrototypes::CrateStateBundle(const std::string_view& name)
{
	Bundle b; b.mName = name;
	mBundles.push_back(b);
	return (int)mBundles.size() - 1;
}
int MutatedPrototypes::GetStateBundleId(const std::string_view& name)
{
	for (size_t i = 0; i < mBundles.size(); ++i) if (mBundles[i].mName == name) return (int)i;
	return -1;
}
int MutatedPrototypes::FindMutationId(const std::string_view& name) const
{
	for (size_t m = 0; m < mMutations.size(); ++m)
	{
		if (mMutations[m].mName == name) return (int)m;
	}
	return -1;
}
bool MutatedPrototypes::ApplyMutation(int bundleId, int mutationId)
{
	auto& bundle = mBundles[bundleId];
	if (std::find(bundle.mMutations.begin(), bundle.mMutations.end(), mutationId) != bundle.mMutations.end()) return false;
	bundle.mMutations.push_back(mutationId);
	auto& mutation = mMutations[mutationId];
	for (auto& item : bundle.mProtoCaches)
	{
		if (!mutation.mIsRelevant(item.second)) continue;
		mutation.mApply(item.second);
	}
	return true;
}
bool MutatedPrototypes::GetHasMutation(int bundleId, int mutationId) const
{
	auto& bundle = mBundles[bundleId];
	return std::find(bundle.mMutations.begin(), bundle.mMutations.end(), mutationId) != bundle.mMutations.end();
}
flecs::entity MutatedPrototypes::RequireMutatedPrefab(int bundleId, int protoId)
{
	if (bundleId == -1) return mPrototypes->GetPrototypePrefab(protoId);
	auto& bundle = mBundles[bundleId];
	auto i = bundle.mProtoCaches.find(protoId);
	if (i == bundle.mProtoCaches.end())
	{
		auto prefab = mPrototypes->GetPrototypePrefab(protoId);
		auto proto = mECS->prefab()
			.is_a(prefab);
		proto.set<UsesBundle>({ bundleId });
		for (auto mutId : bundle.mMutations)
		{
			if (!mMutations[mutId].mIsRelevant(proto)) continue;
			mMutations[mutId].mApply(proto);
		}
		i = bundle.mProtoCaches.insert({ protoId, proto }).first;
	}
	return i->second;
}
int MutatedPrototypes::GetBundleIdFromEntity(flecs::entity entity)
{
	auto uses = entity.get<UsesBundle>();
	return uses != nullptr ? uses->mBundleId : -1;
}
