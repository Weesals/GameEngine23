#include "EntitySystems.h"

#include <algorithm>
#include <numbers>

using namespace Systems;

void ActionSystemBase::Bind(ActionDispatchSystem* dispatchSystem)
{
	mDispatchSystem = dispatchSystem;
}
void ActionSystemBase::EndAction(flecs::entity e, Components::RequestId requestId)
{
	mDispatchSystem->EndAction(e, requestId);
}

void ActionDispatchSystem::Initialise()
{
	mActionSystems.push_back(std::make_shared<TrainingSystem>(mWorld));
	mWorld->GetECS().system<Components::ActionQueue>()
		.without<Components::Runtime::ActionTrain>()
		.each([=](flecs::entity e, Components::ActionQueue& aq)
			{
				if (aq.mRequests.empty()) return;
				auto request = aq.mRequests[0];
				auto actionId = GetActionForRequest(e, request);
				if (actionId == -1) return;
				request.mRequestId.mActionId = actionId;
				BeginAction(e, request);
				aq.mRequests.erase(aq.mRequests.begin() + 0);
			});
}
int ActionDispatchSystem::GetActionForRequest(flecs::entity e, const Components::ActionRequest& request) {
	if (request.mActionTypeId != -1) return request.mActionTypeId;
	auto bestScore = 0.0f;
	int bestSystem = -1;
	for (int i = 0; i < mActionSystems.size(); i++) {
		auto action = mActionSystems[i];
		if (action == nullptr) continue;
		auto score = action->ScoreRequest(e, request);
		if (score > bestScore) {
			bestScore = score;
			bestSystem = i;
		}
	}
	return bestSystem;
}
void ActionDispatchSystem::BeginAction(flecs::entity e, const Components::ActionQueue::RequestItem& request)
{
	mActiveRequests.insert(std::make_pair(e, request.mRequestId));
	mActionSystems[request.mRequestId.mActionId]->BeginInvoke(e, request);
}
void ActionDispatchSystem::EndAction(flecs::entity e, Components::RequestId request)
{
	auto begin = mActiveRequests.lower_bound(e);
	auto end = mActiveRequests.upper_bound(e);
	for (auto i = begin; i != end; ++i)
	{
		if (i->second != request) continue;
		mActionSystems[i->second.mActionId]->EndInvoke(e, i->second);
		mActiveRequests.erase(i);
		break;
	}
}
void ActionDispatchSystem::CancelAction(flecs::entity e, Components::RequestId request)
{
	auto begin = mActiveRequests.lower_bound(e);
	auto end = mActiveRequests.upper_bound(e);
	for (auto i = begin; i != end; )
	{
		if (i->second != request) { ++i; continue; }
		mActionSystems[i->second.mActionId]->EndInvoke(e, i->second);
		i = mActiveRequests.erase(i);
		end = mActiveRequests.upper_bound(e);
	}
}


void TrainingSystem::TrainingSystem::Initialise()
{
	const auto& time = *mWorld->GetECS().get<Singleton::Time>();
	mWorld->GetECS().system<Components::Runtime::ActionTrain>()
		.each([&](flecs::entity e, Components::Runtime::ActionTrain& ta)
			{
				ta.mTrainPoints += time.mSteps;
				if (ta.mTrainPoints >= 5000)
				{
					const auto* tform = e.get<Components::Transform>();
					auto owner = e.target<Components::Owner>();
					Vector3 pos = Vector3::Transform(Vector3(0.0f, 0.0f, 3.0f), tform->GetMatrix());
					mWorld->SpawnEntity(ta.mProtoId, owner, Components::Transform(pos, tform->mOrientation));
					EndAction(e, ta.mRequestId);
				}
			});
}
void TrainingSystem::BeginInvoke(flecs::entity entity, const Components::ActionQueue::RequestItem& request)
{
	entity.set(Components::Runtime::ActionTrain {
		.mRequestId = request.mRequestId,
		.mProtoId = request.mActionData,
		.mTrainPoints = 0,
	});
}
void TrainingSystem::EndInvoke(flecs::entity entity, Components::RequestId request)
{
	entity.remove<Components::Runtime::ActionTrain>();
}

void MovementSystem::Initialise()
{
	const auto& time = *mWorld->GetECS().get<Singleton::Time>();
	mWorld->GetECS().system<Components::Runtime::ActionMove, Components::Mobility, Components::Transform>()
		.each([&](flecs::entity e, const Components::Runtime::ActionMove& ma, const Components::Mobility& mo, Components::Transform& t)
			{
				auto delta = ma.mLocation - t.mPosition;
				auto dst = delta.xz().Length();
				auto move = mo.mSpeed * time.mDeltaTime;
				if (move >= dst) move = 1.0f; else move /= dst;
				t.mPosition += delta * move;
				t.mPosition.y = mWorld->GetLandscape()->GetHeightMap().GetHeightAtF(t.mPosition.xz());
				if (delta.LengthSquared() > 0.001f)
				{
					auto deltaOri = atan2(delta.x, delta.z) - t.mOrientation;
					auto twoPi = (float)(std::numbers::pi * 2.0f);
					deltaOri -= std::round(deltaOri / twoPi) * twoPi;
					auto turn = mo.mTurnSpeed / 180.0f * 3.14f * time.mDeltaTime;
					t.mOrientation += (deltaOri < 0 ? -1 : 1) * std::min(std::abs(deltaOri), turn);
				}
				if (move >= 1.0f) EndAction(e, ma.mRequestId);
			});
}
float MovementSystem::ScoreRequest(flecs::entity entity, const Components::ActionRequest& action)
{
	if ((action.mActionTypes & Components::ActionTypes::Move) != 0) return 1.0f;
	return ActionSystemBase::ScoreRequest(entity, action);
}
void MovementSystem::BeginInvoke(flecs::entity entity, const Components::ActionQueue::RequestItem& request)
{
	entity.set(Components::Runtime::ActionMove {
		.mRequestId = request.mRequestId,
		.mLocation = request.mLocation,
	});
}
void MovementSystem::EndInvoke(flecs::entity entity, Components::RequestId request)
{
	entity.remove<Components::Runtime::ActionMove>();
}

void AttackSystem::Initialise()
{
	const auto& time = *mWorld->GetECS().get<Singleton::Time>();
	mWorld->GetECS().system<Components::Runtime::ActionAttack, Components::Transform>()
		.without<Components::Runtime::ActionMove>()
		.each([&](flecs::entity e, const Components::Runtime::ActionAttack& aa, Components::Transform& t)
			{
				if (!aa.mTarget.is_alive())
				{
					EndAction(e, aa.mRequestId);
					return;
				}

				auto targetT = aa.mTarget.get<Components::Transform>();
				auto targetF = aa.mTarget.get<Components::Footprint>();
				auto interactPos = Components::Footprint::GetInteractLocation(t.mPosition, targetT, targetF);
				auto dst2 = (interactPos - t.mPosition).LengthSquared();
				if (dst2 > 1.5f * 1.5f)
				{
					Components::ActionQueue::RequestItem request;
					request.mLocation = Vector3::MoveTowards(interactPos, t.mPosition, 1.0f);
					request.mRequestId = aa.mRequestId;
					request.mRequestId.mActionId = MovementSystem::ActionId;
					mDispatchSystem->BeginAction(e, request);
				}
				else
				{
					aa.mTarget.destruct();
				}
			}
	);
}
float AttackSystem::ScoreRequest(flecs::entity entity, const Components::ActionRequest& action)
{
	if ((action.mActionTypes & Components::ActionTypes::Attack) != 0 && action.mTarget.is_alive())
	{
		auto targetPlayer = action.mTarget.target<Components::Owner>();
		auto selfPlayer = entity.target<Components::Owner>();
		if (targetPlayer != selfPlayer) return 2.0f;
	}
	return ActionSystemBase::ScoreRequest(entity, action);
}
void AttackSystem::BeginInvoke(flecs::entity entity, const Components::ActionQueue::RequestItem& request)
{
	entity.set(Components::Runtime::ActionAttack {
		.mRequestId = request.mRequestId,
		.mTarget = request.mTarget,
	});
}
void AttackSystem::EndInvoke(flecs::entity entity, Components::RequestId request)
{
	entity.remove<Components::Runtime::ActionAttack>();
}

void BuildSystem::Initialise()
{
	const auto& time = *mWorld->GetECS().get<Singleton::Time>();
	mWorld->GetECS().system<Components::Runtime::ActionBuild, Components::Transform>()
		.without<Components::Runtime::ActionMove>()
		.each([&](flecs::entity e, const Components::Runtime::ActionBuild& ab, Components::Transform& t)
			{
				// If target is invalid, cancel the action
				if (!ab.mTarget.is_alive())
				{
					EndAction(e, ab.mRequestId);
					return;
				}

				// Walk to target location
				auto targetT = ab.mTarget.get<Components::Transform>();
				auto targetF = ab.mTarget.get<Components::Footprint>();
				auto interactPos = Components::Footprint::GetInteractLocation(t.mPosition, targetT, targetF);
				auto dst2 = (interactPos - t.mPosition).xz().LengthSquared();
				if (dst2 > 0.5f * 0.5f)
				{
					Components::ActionQueue::RequestItem request;
					request.mLocation = interactPos;
					request.mRequestId = ab.mRequestId;
					request.mRequestId.mActionId = MovementSystem::ActionId;
					mDispatchSystem->BeginAction(e, request);
					return;
				}

				auto construction = ab.mTarget.get_mut<Components::Construction>();
				if (construction != nullptr)
				{
					// Perform construction
					construction->mBuildPoints += time.mSteps;
					ab.mTarget.modified<Components::Construction>();
					if (construction->mBuildPoints < 1000) return;
					// Complete construction
					auto newPrefab = mWorld->GetPrototypes()->GetPrototypePrefab(construction->mProtoId);
					auto target = ab.mTarget;
					auto prefab = target.target(flecs::IsA);
					target.remove(flecs::IsA, prefab);
					target.remove<Components::Construction>();
					target.is_a(newPrefab);
				}

				EndAction(e, ab.mRequestId);
				return;
			}
	);
}
float BuildSystem::ScoreRequest(flecs::entity entity, const Components::ActionRequest& action)
{
	if (action.mTarget.is_alive())
	{
		auto construction = action.mTarget.get<Components::Construction>();
		if (construction != nullptr) return 3.0f;
	}
	return -1.0f;
}
void BuildSystem::BeginInvoke(flecs::entity entity, const Components::ActionQueue::RequestItem& request)
{
	entity.set(Components::Runtime::ActionBuild {
		.mRequestId = request.mRequestId,
		.mTarget = request.mTarget,
	});
}
void BuildSystem::EndInvoke(flecs::entity entity, Components::RequestId request)
{
	entity.remove<Components::Runtime::ActionBuild>();
}
