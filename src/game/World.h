#pragma once

#include <vector>
#include <flecs.h>

#include "Entity.h"
#include "../GraphicsDeviceBase.h"
#include "../Model.h"
#include "../Math.h"

// Components for ECS
struct Transform {
    Vector3 Position;
    float Orientation;
};
struct MoveTarget {
    Vector3 Target;
};
struct Renderable {
    int v;
};

class World
{
    struct Time {
        float mDeltaTime;
        float mTime;
    };
public:
    // Entities are stored in an ECS world
    flecs::world mECS;

    // Placeholder assets for rendering the world
    std::shared_ptr<Material> mLitMaterial;
    std::shared_ptr<Model> mModel;

    // Initialise world entities and other systems
    void Initialise(std::shared_ptr<Material>& rootMaterial);

    // Update all systems of the world
    void Step(float dt);

    // Render the game world
    void Render(CommandBuffer& cmdBuffer);

};

