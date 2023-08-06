#include "World.h"

#include <numbers>
#include <random>

#include <FBXImport.h>
#include <Geometry.h>

void WorldEffects::HighlightEntity(flecs::entity e, const HighlightConfig& highlight)
{
    assert(highlight.mBegin != 0); // Highlight should have a time assigned
    mEntityHighlights[e] = highlight;
}
Color WorldEffects::GetHighlightFor(flecs::entity e, std::clock_t time)
{
    auto item = mEntityHighlights.find(e);
    if (item != mEntityHighlights.end())
    {
        const auto& highlight = item->second;
        time -= highlight.mBegin;
        if (time > highlight.mDuration) mEntityHighlights.erase(item);
        else
        {
            int tick = time * (highlight.mCount * 2) / highlight.mDuration;
            if ((tick & 0x01) == 0) return highlight.mColor;
        }
    }
    return Color(0.0f, 0.0f, 0.0f, 0.0f);
}

void World::Initialise(std::shared_ptr<Material>& rootMaterial)
{
    mLandscape = std::make_shared<Landscape>();
    mLandscape->SetSize(256);
    mLandscape->SetScale(512);
    mLandscape->SetLocation(Vector3(-64, 0, -64));
    mLandscapeRenderer = std::make_shared<LandscapeRenderer>();
    mLandscapeRenderer->Initialise(mLandscape, rootMaterial);

    mLitMaterial = std::make_shared<Material>(std::make_shared<Shader>(L"assets/lit.hlsl"), std::make_shared<Shader>(L"assets/lit.hlsl"));
    mLitMaterial->InheritProperties(rootMaterial);

    mECS.add<Singleton::Time>();
    auto& time = *mECS.get_mut<Singleton::Time>();
    time.mDeltaTime = 0.0f;

    mPrototypes = std::make_shared<Prototypes>();
    mPrototypes->Load(mECS);
    mMutatedProtos = std::make_shared<MutatedPrototypes>();
    mMutatedProtos->Load(&mECS, mPrototypes);

    auto playerBase = mECS.entity("Player Base")
        .set(MetaComponents::PlayerData("Unknown Player", 0));
    for (int i = 0; i < 4; ++i)
    {
        std::string name = "Player #0";
        name.back() += i;
        auto player = mECS.entity(name.c_str())
            .is_a(playerBase)
            .set(MetaComponents::PlayerData(name, i));
        auto bundle = mMutatedProtos->CrateStateBundle(name);
        player.set(MutatedPrototypes::UsesBundle{.mBundleId = bundle});
        mPlayerEntities.push_back(player);
    }

    // Randomly assign a move target to entities
    mECS.system<Components::Transform>()
        .with<Components::Mobility>()
        .with<Components::Wanders>()
        .without<Components::Runtime::ActionMove>()
        .each([&](flecs::entity e, const Components::Transform& t) {
        // Choose a target at random times
        if ((float)rand() / RAND_MAX < time.mDeltaTime / 4.0f) {
            e.set(Components::Runtime::ActionMove {
                .mLocation = (t.mPosition + Vector3(
                    ((float)rand() / RAND_MAX - 0.5f) * 5.0f,
                    0.0f,
                    ((float)rand() / RAND_MAX - 0.5f) * 5.0f
                ))
            });
        }
    });

    // Placeholder so I remember how to register for entity events
    mECS.observer<Components::Renderable>().event(flecs::OnAdd).each([](flecs::iter& it, size_t i, Components::Renderable& r) {
        if (it.event() == flecs::OnAdd) {
            int a = 0;
        }
    });

    // Create a set of entities
    auto deerProtoId = mPrototypes->GetPrototypeId("Deer");
    auto treeProtoId = mPrototypes->GetPrototypeId("Tree");
    std::random_device rd;
    std::mt19937 gen(rd());
    std::uniform_real<float> rnd(-1.0f, 1.0f);
    auto SpawnInGroups = [&](int protoId, flecs::entity owner, int groupCount, int itemCount, float groupRange, float itemRange)
    {
        for (int g = 0; g < groupCount; ++g)
        {
            auto groupPos = Vector3(rnd(gen), 0.0f, rnd(gen)) * groupRange;
            if (groupPos.LengthSquared() < 10 * 10) continue;
            for (int i = 0; i < itemCount; ++i)
            {
                auto pos = groupPos + Vector3(rnd(gen), 0.0f, rnd(gen)) * itemRange;
                SpawnEntity(protoId, owner, Components::Transform{ pos, 0.0f });
            }
        }
    };
    SpawnInGroups(deerProtoId, GetPlayer(0), 10, 4, 50.0f, 5.0f);
    SpawnInGroups(treeProtoId, GetPlayer(0), 10, 4, 50.0f, 6.0f);
    SpawnEntity(mPrototypes->GetPrototypeId("Town Centre"), GetPlayer(1),
        Components::Transform{ Vector3::Zero, (float)std::numbers::pi });
}
void World::Step(float dt)
{
    auto& time = *mECS.get_mut<Singleton::Time>();
    time.mDeltaTime = dt;
    time.mTime += dt;
    time.mSteps = (int)(dt * 1000);
    mECS.progress();
}

void World::Render(CommandBuffer& cmdBuffer)
{
    mLandscapeRenderer->Render(cmdBuffer);
    Identifier iMMat = "Model";
    auto time = std::clock();
    // Render each entity with Renderable and Transform components
    mECS.each([&](flecs::entity e, const Components::Transform& t, const Components::Renderable& r) {
        // Set the world transform
        auto pos = t.mPosition;
        auto mat = t.GetMatrix();
        mLitMaterial->SetUniform(iMMat, mat.Transpose());
        auto highlight = mWorldEffects.GetHighlightFor(e, time);
        mLitMaterial->SetUniform("Highlight", highlight);
        // Draw each of the meshes
        auto model = mPrototypes->GetModel(r.mModelId);
        model->Render(cmdBuffer, mLitMaterial);
    });
}

void World::RaycastEntities(Ray& ray, const std::function<void(flecs::entity e, float)>& onentity) const
{
    mECS.filter<Components::Transform>().each([=](flecs::entity e, const Components::Transform& t)
        {
            const auto& footprint = e.get<Components::Footprint>();
            bool match = false;
            if (footprint != nullptr)
            {
                auto pos = t.mPosition;
                pos.y += footprint->mHeight / 2.0f;
                float t;
                if (Geometry::RayBoxIntersection(ray, pos, Vector3(footprint->mSize, footprint->mHeight).xzy(), t))
                {
                    onentity(e, t);
                }
            }
            else
            {
                if (ray.GetDistanceSqr(t.mPosition + Vector3(0.0f, 0.5f, 0.0f)) < 1.0f)
                {
                    float rayLen2 = ray.Direction.LengthSquared();
                    onentity(e, Vector3::Dot(t.mPosition + Vector3(0.0f, 0.5f, 0.0f) - ray.Origin, ray.Direction) / rayLen2);
                }
            }
        });
}
flecs::entity World::RaycastEntity(Ray& ray) const
{
    flecs::entity nearest;
    float nearestDst = std::numeric_limits<float>::max();
    RaycastEntities(ray, [&](flecs::entity e, float d)
        {
            if (d > nearestDst) return;
            nearest = e;
            nearestDst = d;
        }
    );
    return nearest;
}

flecs::entity World::SpawnEntity(int protoId, flecs::entity owner, const Components::Transform& tform)
{
    if (protoId == -1) return flecs::entity::null();
    auto bundleId = MutatedPrototypes::GetBundleIdFromEntity(owner);
    return mECS.entity()
        .is_a(mMutatedProtos->RequireMutatedPrefab(bundleId, protoId))
        .add<Components::Owner>(owner)
        .set(tform);
}

void World::FlashEntity(flecs::entity e, const WorldEffects::HighlightConfig& config)
{
    WorldEffects::HighlightConfig tconfig = config;
    tconfig.mBegin = std::clock();
    mWorldEffects.HighlightEntity(e, tconfig);
}
