#include "EntitySystems.h"

#include <algorithm>
#include <numbers>

using namespace Systems;

bool ActionSystemBase::RequireInteract(flecs::entity source, flecs::entity target, Actions::RequestId requestId)
{
	// If target is invalid, cancel the action
	if (!target.is_alive())
	{
		EndAction(source, requestId);
		return false;
	}

	// Walk to target location
	auto sourceT = source.get<Components::Transform>();
	auto targetT = target.get<Components::Transform>();
	auto targetF = target.get<Components::Footprint>();
	auto interactPos = Components::Footprint::GetInteractLocation(sourceT->mPosition, targetT, targetF);
	auto dst2 = (interactPos - sourceT->mPosition).xz().LengthSquared();
	if (dst2 > 0.5f * 0.5f)
	{
		Components::ActionQueue::RequestItem request;
		request.mLocation = interactPos;
		request.mRequestId = requestId;
		request.mRequestId.mActionId = MovementSystem::ActionId;
		mDispatchSystem->BeginAction(source, request);
		return false;
	}
	return true;
}
void ActionSystemBase::Bind(ActionDispatchSystem* dispatchSystem)
{
	mDispatchSystem = dispatchSystem;
}
void ActionSystemBase::EndAction(flecs::entity e, Actions::RequestId requestId)
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
int ActionDispatchSystem::GetActionForRequest(flecs::entity e, const Actions::ActionRequest& request) {
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
void ActionDispatchSystem::EndAction(flecs::entity e, Actions::RequestId request)
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
void ActionDispatchSystem::CancelAction(flecs::entity e, Actions::RequestId request)
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
				ta.mTrainPoints += time.mDeltaSteps;
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
void TrainingSystem::EndInvoke(flecs::entity entity, Actions::RequestId request)
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
				if (dst <= 0.0f) return;
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
				mWorld->NotifyMovedEntity(e);
			});
}
float MovementSystem::ScoreRequest(flecs::entity entity, const Actions::ActionRequest& action)
{
	if ((action.mActionTypes & Actions::ActionTypes::Move) != 0) return 1.0f;
	return ActionSystemBase::ScoreRequest(entity, action);
}
void MovementSystem::BeginInvoke(flecs::entity entity, const Components::ActionQueue::RequestItem& request)
{
	entity.set(Components::Runtime::ActionMove {
		.mRequestId = request.mRequestId,
		.mLocation = request.mLocation,
	});
}
void MovementSystem::EndInvoke(flecs::entity entity, Actions::RequestId request)
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
				// Need to be close enough to interact
				if (!RequireInteract(e, aa.mTarget, aa.mRequestId)) return;

				aa.mTarget.destruct();
			}
	);
}
float AttackSystem::ScoreRequest(flecs::entity entity, const Actions::ActionRequest& action)
{
	if ((action.mActionTypes & Actions::ActionTypes::Attack) != 0 && action.mTarget.is_alive())
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
void AttackSystem::EndInvoke(flecs::entity entity, Actions::RequestId request)
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
				// Need to be close enough to interact
				if (!RequireInteract(e, ab.mTarget, ab.mRequestId)) return;

				auto construction = ab.mTarget.get_mut<Components::Construction>();
				if (construction != nullptr)
				{
					// Perform construction
					construction->mBuildPoints += time.mDeltaSteps;
					ab.mTarget.modified<Components::Construction>();
					if (construction->mBuildPoints < 1000) return;
					// Complete construction
					auto bundleId = MutatedPrototypes::GetBundleIdFromEntity(ab.mTarget);
					auto newPrefab = mWorld->GetMutatedProtos()->RequireMutatedPrefab(bundleId, construction->mProtoId);
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
float BuildSystem::ScoreRequest(flecs::entity entity, const Actions::ActionRequest& action)
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
void BuildSystem::EndInvoke(flecs::entity entity, Actions::RequestId request)
{
	entity.remove<Components::Runtime::ActionBuild>();
}

void GatherSystem::Initialise()
{
	const auto& time = *mWorld->GetECS().get<Singleton::Time>();
	mWorld->GetECS().system<Components::Runtime::ActionGather, Components::Gathers, Components::Transform>()
		.without<Components::Runtime::ActionMove>()
		.each([&](flecs::entity e, Components::Runtime::ActionGather& ag, Components::Gathers& g, Components::Transform& t)
			{
				if (g.mHolding.mAmount < 10)
				{
					// Need to be close enough to interact
					if (!RequireInteract(e, ag.mTarget, ag.mRequestId)) return;

					auto stockpile = ag.mTarget.get_mut<Components::Stockpile>();
					if (stockpile != nullptr && !stockpile->mResources.empty())
					{
						auto& res = stockpile->mResources.front();
						ag.mStrikeSteps += time.mDeltaSteps;
						int stepsPerStrike = 1000;
						auto ticks = ag.mStrikeSteps / stepsPerStrike;
						ticks = std::min(std::min(res.mAmount, ticks), 10 - g.mHolding.mAmount);
						ag.mStrikeSteps -= ticks * stepsPerStrike;
						res.mAmount -= ticks;
						if (g.mHolding.mResourceId != res.mResourceId)
							g.mHolding = ResourceSet(res.mResourceId, 0);
						g.mHolding.mAmount += ticks;
						return;
					}
				}
				else
				{
					// Try to find the nearest drop target
					if (!ag.mDropTarget.is_alive())
					{
						float nearest2 = std::numeric_limits<float>::max();
						flecs::entity nearest = flecs::entity::null();
						mWorld->GetECS().each([&](flecs::entity e, Components::Dropsite d, const Components::Transform& dt)
							{
								auto dst2 = Vector3::DistanceSquared(dt.mPosition, t.mPosition);
								if (dst2 < nearest2)
								{
									nearest2 = dst2;
									nearest = e;
								}
							}
						);
						ag.mDropTarget = nearest;
					}
					// Need to be close enough to interact
					if (!RequireInteract(e, ag.mDropTarget, ag.mRequestId)) return;
					auto owner = ag.mDropTarget.target<Components::Owner>();
					auto* pdata = owner.get_mut<MetaComponents::PlayerData>();
					pdata->DeliverResource(g.mHolding);
					g.mHolding = ResourceSet();
					return;
				}
				EndAction(e, ag.mRequestId);
				return;
			}
	);
}
float GatherSystem::ScoreRequest(flecs::entity entity, const Actions::ActionRequest& action)
{
	if (action.mTarget.is_alive())
	{
		auto stockpile = action.mTarget.get<Components::Stockpile>();
		if (stockpile != nullptr) return 3.0f;
	}
	return -1.0f;
}
void GatherSystem::BeginInvoke(flecs::entity entity, const Components::ActionQueue::RequestItem& request)
{
	entity.set(Components::Runtime::ActionGather {
		.mRequestId = request.mRequestId,
		.mTarget = request.mTarget,
	});
}
void GatherSystem::EndInvoke(flecs::entity entity, Actions::RequestId request)
{
	entity.remove<Components::Runtime::ActionGather>();
}
