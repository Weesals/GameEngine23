#include "World.h"

#include <numbers>
#include <random>

#include <FBXImport.h>
#include <Geometry.h>

#include "MaterialEvaluator.h"
#include <chrono>
using steady_clock = std::chrono::steady_clock;
using time_point = std::chrono::time_point<steady_clock>;

// Must match StructuredBuffer in shader
struct RetainedData {
    Matrix mWorld;
    Matrix mUnused;
    Color mHighlight;
    Color mUnused2;
};

void WorldEffects::HighlightEntity(flecs::entity e, const HighlightConfig& highlight)
{
    assert(highlight.mBegin != 0); // Highlight should have a time assigned
    mEntityHighlights[e] = highlight;
}
Color WorldEffects::GetHighlightFor(flecs::entity e, int time)
{
    auto item = mEntityHighlights.find(e);
    if (item != mEntityHighlights.end())
    {
        const auto& highlight = item->second;
        int tick = ComputeResult(highlight, time);
        if (tick == -2) mEntityHighlights.erase(item);
        else if ((tick & 0x01) == 0) return highlight.mColor;
    }
    return Color(0.0f, 0.0f, 0.0f, 0.0f);
}
int WorldEffects::ComputeResult(const HighlightConfig& highlight, int time)
{
    time -= highlight.mBegin;
    return time < 0 ? -1
        : time > highlight.mDuration ? -2
        : time >= 0 ? time * (highlight.mCount * 2) / highlight.mDuration
        : -1;
}
void WorldEffects::GetModified(std::set<flecs::entity>& entities, int oldTime, int newTime)
{
    for (auto& item : mEntityHighlights)
        if (ComputeResult(item.second, oldTime) != ComputeResult(item.second, newTime))
            entities.insert(item.first);
}

void World::Initialise(const std::shared_ptr<Material>& rootMaterial, const std::shared_ptr<RetainedRenderer>& scene)
{
    mScene = scene;

    mLandscape = std::make_shared<Landscape>();
    mLandscape->SetSize(256);
    mLandscape->SetScale(512);
    mLandscape->SetLocation(Vector3(-64, 0, -64));
    mLandscapeRenderer = std::make_shared<LandscapeRenderer>();
    mLandscapeRenderer->Initialise(mLandscape, rootMaterial);

    mLitMaterial = std::make_shared<Material>(L"assets/retained.hlsl");
    mLitMaterial->InheritProperties(rootMaterial);
    mLitMaterial->SetUniform("Model", Matrix::Identity);
    mLitMaterial->SetUniform("Highlight", Vector4::Zero);

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

    // Add/remove entities from Scene
    mECS.observer<Components::Renderable>()
        .event(flecs::OnAdd)
        .each([this](flecs::iter& it, size_t i, Components::Renderable& r) {
        if (it.event() == flecs::OnAdd) {
            mMovedEntities.insert(it.entity(i));
        }
    });
    mECS.observer<Components::Renderable>()
        .event(flecs::OnRemove)
        .each([this](flecs::iter& it, size_t i, Components::Renderable& r) {
        if (it.event() == flecs::OnRemove) {
            for (auto& instId : r.mInstanceIds)
                mScene->RemoveInstance(instId);
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
    SpawnInGroups(deerProtoId, GetPlayer(0), 50, 4, 50.0f, 5.0f);
    SpawnInGroups(treeProtoId, GetPlayer(0), 10, 4, 50.0f, 6.0f);
    SpawnEntity(mPrototypes->GetPrototypeId("Town Centre"), GetPlayer(1),
        Components::Transform{ Vector3::Zero, (float)std::numbers::pi });
}
void World::Step(float dt)
{
    auto& time = *mECS.get_mut<Singleton::Time>();
    int otime = time.mSteps;
    time.mDeltaTime = dt;
    time.mTime += dt;
    time.mDeltaSteps = (int)(dt * 1000);
    time.mSteps += time.mDeltaSteps;
    mECS.progress();
    mWorldEffects.GetModified(mMovedEntities, otime, time.mSteps);
}

void World::Render(CommandBuffer& cmdBuffer)
{
    auto& time = *mECS.get_mut<Singleton::Time>();
    for (auto it = mMovedEntities.begin(); it != mMovedEntities.end(); ++it) {
        auto e = *it;
        auto& r = *e.get_mut<Components::Renderable>();
        const auto& t = *e.get<Components::Transform>();
        auto& material = mLitMaterial;
        auto& model = mPrototypes->GetModel(r.mModelId);
        int i = 0;
        for (auto& mesh : model->GetMeshes()) {
            auto& meshMat = mesh->GetMaterial();
            RetainedData data;
            data.mWorld = t.GetMatrix();
            data.mHighlight = mWorldEffects.GetHighlightFor(e, time.mSteps);
            if (i >= r.mInstanceIds.size()) {
                if (meshMat != nullptr) meshMat->InheritProperties(material);
                Material* useMat = meshMat.get();
                if (useMat == nullptr) useMat = material.get();
                r.mInstanceIds.push_back(mScene->AppendInstance(mesh.get(), useMat, sizeof(data)));
                if (meshMat != nullptr) meshMat->RemoveInheritance(material);
            }
            mScene->UpdateInstanceData(r.mInstanceIds[i], data);
            ++i;
        }
    }
    mMovedEntities.clear();
    mLandscapeRenderer->Render(cmdBuffer);
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
    auto& time = *mECS.get_mut<Singleton::Time>();
    tconfig.mBegin = time.mSteps;
    mWorldEffects.HighlightEntity(e, tconfig);
    mMovedEntities.insert(e);
}

void World::NotifyMovedEntity(flecs::entity e)
{
    mMovedEntities.insert(e);
}
