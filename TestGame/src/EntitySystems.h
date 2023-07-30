#pragma once

#include "EntityComponents.h"
#include <flecs.h>
#include <map>

namespace Systems
{
	class ActionSystemBase;
}

class World;
#include "World.h"

// Components specific to actions; are transitory and only exist
// while actions are being processed
// TODO: Perhaps might be more efficient to not mutate entity
namespace Components::Runtime
{
	struct ActionTrain
	{
		RequestId mRequestId;
		int mProtoId;
		int mTrainPoints;
	};
	struct ActionMove
	{
		RequestId mRequestId;
		Vector3 mLocation;
	};
	struct ActionAttack
	{
		RequestId mRequestId;
		flecs::entity mTarget;
	};
	struct ActionBuild
	{
		RequestId mRequestId;
		flecs::entity mTarget;
	};
}

namespace Systems
{
	// All systems should extend this class
	class SystemBase
	{
	protected:
		World* mWorld;
	public:
		SystemBase(World* world)
			: mWorld(world) { }

		virtual void Initialise() { }
		virtual void Uninitialise() { }

	};

	// A system to redirect action requests to specific action systems
	// and enable action systems to invoke other action systems
	class ActionDispatchSystem : public SystemBase
	{
		std::vector<std::shared_ptr<ActionSystemBase>> mActionSystems;
		std::multimap<flecs::entity, Components::RequestId> mActiveRequests;
	public:
		using SystemBase::SystemBase;
		void Initialise() override;
		int GetActionForRequest(flecs::entity e, const Components::ActionRequest& request);

		void BeginAction(flecs::entity e, const Components::ActionQueue::RequestItem& request);
		void EndAction(flecs::entity e, Components::RequestId request);

		void CancelAction(flecs::entity e, Components::RequestId request);

		template<class T>
		void RegisterAction()
		{
			auto id = T::ActionId;
			if (id >= mActionSystems.size())mActionSystems.resize(id + 1);
			mActionSystems[id] = std::make_shared<T>(mWorld);
			mActionSystems[id]->Initialise();
			mActionSystems[id]->Bind(this);
		}
	};

	// An action system (move/attack/build) should extend this
	class ActionSystemBase : public SystemBase
	{
	protected:
		ActionDispatchSystem* mDispatchSystem;
	public:
		using SystemBase::SystemBase;

		void Bind(ActionDispatchSystem* dispatchSystem);
		virtual float ScoreRequest(flecs::entity entity, const Components::ActionRequest& action) { return -1.0f; }
		virtual void BeginInvoke(flecs::entity entity, const Components::ActionQueue::RequestItem& request) { }
		virtual void EndInvoke(flecs::entity entity, Components::RequestId request) { }
		void EndAction(flecs::entity e, Components::RequestId requestId);
	};

	// Train a new unit
	class TrainingSystem : public ActionSystemBase
	{
	public:
		static const int ActionId = 1;

		using ActionSystemBase::ActionSystemBase;
		
		void Initialise() override;
		void BeginInvoke(flecs::entity entity, const Components::ActionQueue::RequestItem& request) override;
		void EndInvoke(flecs::entity entity, Components::RequestId request) override;
	};

	// Walk a unit across the landscape to a target location
	class MovementSystem : public ActionSystemBase
	{
	public:
		static const int ActionId = 2;

		using ActionSystemBase::ActionSystemBase;

		void Initialise() override;
		float ScoreRequest(flecs::entity entity, const Components::ActionRequest& action) override;
		void BeginInvoke(flecs::entity entity, const Components::ActionQueue::RequestItem& request) override;
		void EndInvoke(flecs::entity entity, Components::RequestId request) override;
	};

	// Attack the target entity using melee
	class AttackSystem : public ActionSystemBase
	{
	public:
		static const int ActionId = 3;

		using ActionSystemBase::ActionSystemBase;

		void Initialise() override;
		float ScoreRequest(flecs::entity entity, const Components::ActionRequest& action) override;
		void BeginInvoke(flecs::entity entity, const Components::ActionQueue::RequestItem& request) override;
		void EndInvoke(flecs::entity entity, Components::RequestId request) override;
	};

	// Attack the target entity using melee
	class BuildSystem : public ActionSystemBase
	{
	public:
		static const int ActionId = 4;

		using ActionSystemBase::ActionSystemBase;

		void Initialise() override;
		float ScoreRequest(flecs::entity entity, const Components::ActionRequest& action) override;
		void BeginInvoke(flecs::entity entity, const Components::ActionQueue::RequestItem& request) override;
		void EndInvoke(flecs::entity entity, Components::RequestId request) override;
	};

}
