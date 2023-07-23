#include "World.h"

#include <numbers>

#include <FBXImport.h>

void World::Initialise(std::shared_ptr<Material>& rootMaterial)
{
    mLandscape = std::make_shared<Landscape>();
    mLandscape->SetSize(256);
    mLandscape->SetScale(256);
    mLandscape->SetLocation(Vector3(-32, 0, -32));
    mLandscapeRenderer = std::make_shared<LandscapeRenderer>();
    mLandscapeRenderer->Initialise(mLandscape, rootMaterial);

    mLitMaterial = std::make_shared<Material>(Shader(L"res/lit.hlsl"), Shader(L"res/lit.hlsl"));
    mLitMaterial->InheritProperties(rootMaterial);

    mECS.add<Time>();
    auto& time = *mECS.get_mut<Time>();
    time.mDeltaTime = 0.0f;

    // Randomly assign a move target to entities
    mECS.system<Transform, MoveTarget>()
        .each([&](const Transform& t, MoveTarget& m) {
        if ((float)rand() / RAND_MAX < time.mDeltaTime / 4.0f) {
            m.Target = t.Position + Vector3(
                ((float)rand() / RAND_MAX - 0.5f) * 5.0f,
                0.0f,
                ((float)rand() / RAND_MAX - 0.5f) * 5.0f
            );
        }
    });
    // Move towards the move target
    mECS.system<Transform, MoveTarget>()
        .each([&](Transform& t, MoveTarget& m) {
        auto delta = m.Target - t.Position;
        if (delta.LengthSquared() > 0.001f) {
            auto dst = delta.Length();
            auto move = 2.0f * time.mDeltaTime;
            auto turn = 2.0f * time.mDeltaTime;
            t.Position += delta * std::min(move, dst) / dst;
            auto deltaOri = atan2(delta.x, delta.z) - t.Orientation;
            auto twoPi = (float)(std::numbers::pi * 2.0f);
            deltaOri -= std::round(deltaOri / twoPi) * twoPi;
            t.Orientation += (deltaOri < 0 ? -1 : 1) * std::min(std::abs(deltaOri), turn);
        }
    });

    // Placeholder so I remember how to register for entity events
    mECS.observer<Renderable>().event(flecs::OnAdd).each([](flecs::iter& it, size_t i, Renderable& r) {
        if (it.event() == flecs::OnAdd) {
            int a = 0;
        }
    });

    // Create a set of entities
    for (int x = 0; x < 10; ++x) {
        for (int z = 0; z < 10; ++z) {
            auto pos = Vector3(((float)x - 5.0f) * 10.0f, 0.0f, ((float)z - 5.0f) * 10.0f);
            mECS.entity()
                .set(Transform{ pos, 0.0f })
                .set(MoveTarget{ pos })
                .set(Renderable{ });
        }
    }

    mModel = FBXImport::ImportAsModel(L"res/test.fbx");
}
void World::Step(float dt)
{
    auto& time = *mECS.get_mut<Time>();
    time.mDeltaTime = dt;
    time.mTime += dt;
    mECS.progress();
}

void World::Render(CommandBuffer& cmdBuffer)
{
    mLandscapeRenderer->Render(cmdBuffer);
    Identifier iMMat = "Model";
    // Render each entity with Renderable and Transform components
    mECS.each([&](flecs::entity e, const Transform& t, const Renderable& r) {
        // Set the world transform
        auto pos = t.Position;
        pos.y += mLandscape->GetHeightMap().GetHeightAtF(pos.xz());
        auto mat = Matrix::CreateRotationY(t.Orientation)
            * Matrix::CreateTranslation(pos);
        mLitMaterial->SetUniform(iMMat, mat.Transpose());
        // Draw each of the meshes
        for (auto& mesh : mModel->GetMeshes())
            cmdBuffer.DrawMesh(mesh, mLitMaterial);
    });
}

void World::RaycastEntities(Ray& ray, const std::function<void(flecs::entity e)>& onentity)
{
    mECS.filter<Transform>().each([=](flecs::entity e, Transform& t)
        {
            if (ray.GetDistanceSqr(t.Position + Vector3(0.0f, 0.5f, 0.0f)) < 1.0f) onentity(e);
        });
}
