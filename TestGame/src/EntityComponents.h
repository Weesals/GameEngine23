#pragma once

#include <MathTypes.h>
#include <bitset>
#include <memory>

#include <Material.h>
#include <Model.h>
#include <flecs.h>

struct ResourceSet
{
    int mResourceId : 4;
    int mAmount : 28;
};

namespace Singleton
{
    struct Time {
        float mDeltaTime;
        float mTime;
        int mSteps;
    };
}

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
    };
}

// Components for ECS
namespace Components
{
    struct Owner { };

    struct Wanderer { };

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
    };
    // How this object reveals the world
    struct LineOfSight
    {
        float mRange;
    };
    struct Durability
    {
        int mBaseHitPoints;
    };
    struct Mobility
    {
        float mSpeed;
        float mTurnSpeed;
    };
    struct Dropsite
    {
        std::bitset<16> mResourceMask;
        static Dropsite All() { return Dropsite{ true }; }
    };
    struct Stockpile
    {
        std::vector<ResourceSet> mResources;
    };
    struct Builds
    {
        std::vector<std::string> mBuilds;
    };
    struct Trains
    {
        std::vector<std::string> mTrains;
    };
    struct Flags
    {
        // Only 1 item should exist in the world (per player)
        bool mSingular : 1;
        // Item should randomly rotate when placed
        bool mRotateOnPlace : 1;
        // Item belongs to Gaia
        bool mDefaultGaia : 1;
    };

    struct Renderable
    {
        int mModelId;
    };


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
    struct ActionQueue
    {
        struct RequestItem : public ActionRequest
        {
            RequestId mRequestId;
        };
        std::vector<RequestItem> mRequests;
    };

}
