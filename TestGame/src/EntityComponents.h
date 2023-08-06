#pragma once

#include <MathTypes.h>
#include <bitset>
#include <memory>

#include <Material.h>
#include <Model.h>
#include <flecs.h>

// Represents an amount of a specific resource
// (ie. for a cost, for carrying resources, for generating resources)
struct ResourceSet
{
    int mResourceId : 4;
    int mAmount : 28;
    ResourceSet(int resourceId = -1, int amount = 0)
        : mResourceId(resourceId), mAmount(amount) {}
};

namespace Actions
{
    enum ActionTypes : uint8_t
    {
        None = 0x00,
        Move = 0x01, Build = 0x02, Gather = 0x04, GatherDrop = 0x08,
        Attack = 0x30, AttackMelee = 0x10, AttackRanged = 0x20,
        All = 0x7f,
    };
    struct RequestId
    {
        int mActionId;
        int mRequestId;
        bool operator ==(const RequestId o) const { return mRequestId == -1 || o.mRequestId == -1 || (mActionId == o.mActionId && mRequestId == o.mRequestId); }
        bool operator !=(const RequestId o) const { return !(*this == o); }
        static RequestId MakeAll() { return RequestId{ .mActionId = -1, .mRequestId = -1, }; }
    };
    struct ActionRequest
    {
        // -1: No preference. >=0: Force a specific action based on its Id
        int mActionTypeId;
        // Which general action types this request can match
        ActionTypes mActionTypes;
        flecs::entity mTarget;
        Vector3 mLocation;
        int mActionData;
    };
}

// Components accessible to all systems
namespace Singleton
{
    struct Time {
        float mDeltaTime;
        float mTime;
        int mSteps;
    };
}

// Components not related to physical entities in the world
namespace MetaComponents
{
    struct PlayerData
    {
        std::string mName;
        std::vector<ResourceSet> mResources;
        int mPlayerId;
        PlayerData() { }
        PlayerData(const std::string_view& name, int playerId)
            : mName(name), mPlayerId(playerId)
        {
            for (int i = 0; i < 4; ++i)
                mResources.push_back(ResourceSet{ i, 100 });
        }
        void DeliverResource(ResourceSet res)
        {
            auto item = std::find_if(mResources.begin(), mResources.end(), [=](auto r) { return r.mResourceId == res.mResourceId; });
            if (item == mResources.end()) mResources.push_back(res);
            else item->mAmount += res.mAmount;
        }
    };
}

// Components used to select or filter entities
namespace Tags
{
    struct Villager {};
    struct RequireAge
    {
        int mAge;
        static RequireAge MakeNone() { return { .mAge = -1 }; }
    };
    // Various utility things
    struct Flags
    {
        // Only 1 item should exist in the world (per player)
        bool mSingular : 1;
        // Item should randomly rotate when placed
        bool mRotateOnPlace : 1;
        // Item belongs to Gaia
        bool mDefaultGaia : 1;
    };
}

// Components for ECS
namespace Components
{
    // Who owns this entity (is a Pair with the players Entity)
    struct Owner { };

    // Position/orientation pair, all physical entities have this
    struct Transform
    {
        Vector3 mPosition;
        float mOrientation;
        Transform() { }
        Transform(Vector3 pos, float ori = 0.0f) : mPosition(pos), mOrientation(ori) { }
        Matrix GetMatrix() const {
            return Matrix::CreateRotationY(mOrientation)
                * Matrix::CreateTranslation(mPosition);
        }
    };

    // How much space this entity occupies
    struct Footprint
    {
        Vector2 mSize;
        float mHeight;
        static Vector3 GetInteractLocation(Vector3 from, const Transform* targetT, const Footprint* targetF)
        {
            auto interactPos = targetT->mPosition;
            // Fallback to distance (within 0.5 units)
            if (targetF == nullptr) return Vector3::MoveTowards(interactPos, from, 0.5f);
            // Get nearest point in footprint
            auto xz = Vector2::Clamp(from.xz(), interactPos.xz() - targetF->mSize / 2.0f, interactPos.xz() + targetF->mSize / 2.0f);
            interactPos.x = xz.x;
            interactPos.z = xz.y;
            return interactPos;
        }
    };
    // How this object reveals the world
    struct LineOfSight
    {
        float mRange;
    };
    // How much health/armour this entity has
    struct Durability
    {
        int mBaseHitPoints;
    };
    // How this entity moves
    struct Mobility
    {
        float mSpeed;
        float mTurnSpeed;
    };
    // Can receive player resources
    struct Dropsite
    {
        std::bitset<16> mResourceMask;
        static Dropsite All() { return Dropsite{ true }; }
    };
    // A resource source
    struct Stockpile
    {
        std::vector<ResourceSet> mResources;
    };
    // Can build things
    struct Builds
    {
        std::vector<std::string> mBuilds;
    };
    // Can train things
    struct Trains
    {
        std::vector<std::string> mTrains;
    };
    // Can research upgrades
    struct Techs
    {
        std::vector<std::string> mTechs;
    };
    // Can gather from stockpiles
    struct Gathers
    {
        std::vector<ResourceSet> mGathers;
        ResourceSet mHolding;
    };
    // This entity wanders around randomly (ie. wild animals)
    struct Wanders { };

    // This is a construction lot; will mutate into the final structure
    struct Construction
    {
        int mBuildPoints;
        // What this construction turns into after built
        int mProtoId;
    };

    // Properties related to redering this entity
    struct Renderable
    {
        // What mesh to use for rendering
        int mModelId;
    };


    // Store the queue of actions sent to this entity
    // (probably by the player right-clicking things)
    struct ActionQueue
    {
        struct RequestItem : public Actions::ActionRequest
        {
            Actions::RequestId mRequestId;
        };
        std::vector<RequestItem> mRequests;
    };

}
