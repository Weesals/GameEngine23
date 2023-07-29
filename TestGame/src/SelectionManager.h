#pragma once

#include <flecs.h>
#include <set>
#include <span>
#include <functional>
#include <memory>

#include <Delegate.h>

class SelectionManager
{
	typedef Delegate<flecs::entity, bool> EntityRegisterListener;
	typedef Delegate<flecs::entity, flecs::entity> EntityHeroListener;
	std::set<flecs::entity> mSelection;

	EntityRegisterListener mEntityListeners;
	EntityHeroListener mHeroListeners;

	struct MutationTracker
	{
		std::set<flecs::entity> mAdded;
		std::set<flecs::entity> mRemoved;
		flecs::entity mHeroEntity;
		MutationTracker(SelectionManager* manager);
		// Notify that an entity was added
		void Append(flecs::entity entity);
		// Notify that an entity was removed
		void Remove(flecs::entity entity);
	};
	MutationTracker* mTracker;

public:
	struct TrackerScope
	{
		SelectionManager* mManager;
		MutationTracker* mPrevTracker;
		MutationTracker mMutations;
		TrackerScope(SelectionManager* manager)
			: mManager(manager)
			, mMutations(manager)
		{
			mPrevTracker = mManager->mTracker;
			mManager->mTracker = &mMutations;
		}
		~TrackerScope()
		{
			// Push new changes to previous tracker
			if (mPrevTracker != nullptr)
			{
				for (auto item : mMutations.mAdded) mPrevTracker->Append(item);
				for (auto item : mMutations.mRemoved) mPrevTracker->Remove(item);
			}
			mManager->mTracker = mPrevTracker;
		}
	};
	struct RootTrackerScope : public TrackerScope
	{
		~RootTrackerScope()
		{
			auto newHero = mManager->GetHeroEntity();
			// Notify that the hero has changed
			if (mMutations.mHeroEntity != newHero)
				mManager->mHeroListeners.Invoke(mMutations.mHeroEntity, newHero);
			// Notify of entities that were removed
			for (auto entity : mMutations.mRemoved)
			{
				mManager->mEntityListeners.Invoke(entity, false);
			}
			// Notify of entities that were added
			for (auto entity : mMutations.mAdded)
			{
				mManager->mEntityListeners.Invoke(entity, true);
			}
		}
	};

	// Deselect all entities
	void Clear()
	{
		mSelection.clear();
	}
	// Add an entity to the selection
	bool Append(flecs::entity entity)
	{
		RootTrackerScope tracker(this);
		if (!mSelection.insert(entity).second) return false;
		if (mTracker != nullptr) mTracker->Append(entity);
		return true;
	}
	// Remove an entity from the selection
	bool Remove(flecs::entity entity)
	{
		RootTrackerScope tracker(this);
		if (mSelection.erase(entity) == 0) return false;
		if (mTracker != nullptr) mTracker->Remove(entity);
		return true;
	}

	// Get the currently selected entities
	const std::set<flecs::entity>& GetSelection() const
	{
		return mSelection;
	}

	// TODO: Do not implicitly assume hero entity is first; manage explicitly
	flecs::entity GetHeroEntity()
	{
		return mSelection.empty() ? flecs::entity::null() : *mSelection.begin();
	}

	// Register to be notified of entity select events
	void RegisterEntityListener(const EntityRegisterListener::Function& listener)
	{
		mEntityListeners.Add(listener);
	}
	void RegisterHeroListener(const EntityHeroListener::Function& listener)
	{
		mHeroListeners.Add(listener);
	}

};
