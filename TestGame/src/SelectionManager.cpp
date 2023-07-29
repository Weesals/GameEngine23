#include "SelectionManager.h"


SelectionManager::MutationTracker::MutationTracker(SelectionManager* manager)
	: mHeroEntity(manager->GetHeroEntity()) { }
void SelectionManager::MutationTracker::Append(flecs::entity entity)
{
	// Either remove from Removed list, or add to Added list
	if (mRemoved.erase(entity) == 0)
		mAdded.insert(entity);
}
void SelectionManager::MutationTracker::Remove(flecs::entity entity)
{
	// Either remove from Added list, or add to Removed list
	if (mAdded.erase(entity) == 0)
		mRemoved.insert(entity);
}
