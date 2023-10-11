#pragma once

#include <vector>
#include <flecs.h>

#include <GraphicsDeviceBase.h>
#include <RetainedRenderer.h>
#include <Model.h>
#include <MathTypes.h>

#include "Landscape.h"
#include "LandscapeRenderer.h"
#include "EntityComponents.h"
#include "EntitySystems.h"
#include "Prototypes.h"


class WorldEffects
{
public:
    struct HighlightConfig {
        std::clock_t mBegin;
        Color mColor;
        int mCount;
        int mDuration;
        static HighlightConfig MakeDefault() { return { .mColor = Color(0.25f, 0.25f, 0.25f, 0.5f), .mCount = 1, .mDuration = 500, }; }
    };

    void HighlightEntity(flecs::entity e, const HighlightConfig& highlight);
    Color GetHighlightFor(flecs::entity e, int time);
    int ComputeResult(const HighlightConfig& config, int time);
    void GetModified(std::set<flecs::entity>& entities, int oldTime, int newTime);

private:
    std::map<flecs::entity, HighlightConfig> mEntityHighlights;
};

class World
{
    WorldEffects mWorldEffects;

    // The landscape
    std::shared_ptr<Landscape> mLandscape;
    std::shared_ptr<LandscapeRenderer> mLandscapeRenderer;

    // Entities are stored in an ECS world
    flecs::world mECS;
    std::shared_ptr<Prototypes> mPrototypes;
    std::shared_ptr<MutatedPrototypes> mMutatedProtos;
    std::vector<flecs::entity> mPlayerEntities;

    // Placeholder assets for rendering the world
    std::shared_ptr<Material> mLitMaterial;
    std::shared_ptr<RetainedRenderer> mScene;

    std::set<flecs::entity> mMovedEntities;

public:
    // Initialise world entities and other systems
    void Initialise(const std::shared_ptr<Material>& rootMaterial, const std::shared_ptr<RetainedRenderer>& scene);

    flecs::entity GetPlayer(int id) const { return mPlayerEntities[id]; }

    const std::shared_ptr<Landscape>& GetLandscape() const { return mLandscape; }
    const std::shared_ptr<LandscapeRenderer>& GetLandscapeRenderer() const { return mLandscapeRenderer; }
    flecs::world& GetECS() { return mECS; }
    const std::shared_ptr<Prototypes>& GetPrototypes() const { return mPrototypes; }
    const std::shared_ptr<MutatedPrototypes>& GetMutatedProtos() const { return mMutatedProtos; }
    const std::shared_ptr<Material>& GetLitMaterial() const { return mLitMaterial; }

    // Update all systems of the world
    void Step(float dt);

    // Render the game world
    void Render(CommandBuffer& cmdBuffer, RenderPassList passes);

    // Calls the callback for every entity that this ray intersects
    void RaycastEntities(Ray& ray, const std::function<void(flecs::entity e, float)>& onentity) const;
    flecs::entity RaycastEntity(Ray& ray) const;

    // Spawn an enity with the specified properties
    flecs::entity SpawnEntity(int protoId, flecs::entity owner, const Components::Transform& pos);

    void FlashEntity(flecs::entity e, const WorldEffects::HighlightConfig& config);

    void NotifyMovedEntity(flecs::entity e);

};
