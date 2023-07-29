#include "Prototypes.h"

#include "FBXImport.h"

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
		.set(Components::Footprint {.mSize = { 6.0f, 6.0f }, .mHeight = 1.0f, })
		.set(Components::Durability {.mBaseHitPoints = 500, })
		.set(Components::Dropsite::All())
		.set(Components::Trains {.mTrains { "Villager", "Hero", } })
		.set(Components::Flags {.mSingular = true, })
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
		.set(Components::Footprint {.mSize = { 6.0f, 6.0f }, .mHeight = 1.0f, })
		.set(Components::Durability {.mBaseHitPoints = 200, })
		.set(Components::Dropsite::All())
		.set(Components::Renderable{.mModelId = RequireModelId(L"assets/SM_Farm.fbx")})
	);
	AppendEntity(world.prefab("House")
		.is_a(buildingBase)
		.set(Components::Durability {.mBaseHitPoints = 200, })
		.set(Components::Dropsite::All())
		.set(Components::Renderable{.mModelId = RequireModelId(L"assets/SM_House.fbx")})
	);
	AppendEntity(world.prefab("Villager")
		.is_a(unitBase)
		.set(Components::Builds {.mBuilds { "House", "Farm", "Storehouse", "Town Centre", } })  
		.set(Components::Renderable{.mModelId = RequireModelId(L"assets/SM_Character_Worker.fbx")})
	);
	AppendEntity(world.prefab("Deer")
		.is_a(unitBase)
		.add<Components::Wanderer>()
		.set(Components::Renderable{.mModelId = RequireModelId(L"assets/SM_Deer.fbx")})
	);
	AppendEntity(world.prefab("Tree")
		.is_a(entityBase)
		.set(Components::Footprint {.mSize = { 2.0f, 2.0f }, .mHeight = 2.0f, })
		.set(Components::Flags {.mDefaultGaia = true, })
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
