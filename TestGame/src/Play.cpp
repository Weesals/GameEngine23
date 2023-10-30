#include "Play.h"

#include <numbers>
#include <FBXImport.h>
#include "ui/CanvasImGui.h"

#include "InputInteractions.h"
#include "UIGraphicsDebug.h"
#include "UIPlay.h"

void Skybox::Initialise(const std::shared_ptr<Material>& rootMaterial)
{
    // Generate a skybox mesh
    mMesh = std::make_shared<Mesh>("Skybox");
    mMesh->SetVertexCount(4);
    auto positions = mMesh->GetPositionsV();
    for (int i = 0; i < positions.size(); ++i)
        positions[i] = Vector3((i % 2) * 2.0f - 1.0f, (i / 2) * 2.0f - 1.0f, 0.0f);

    mMesh->SetIndices(std::span<const int>({ 0, 3, 1, 0, 2, 3, }));

    // Load the skybox material
    mMaterial = std::make_shared<Material>(L"assets/skybox.hlsl");
    mMaterial->InheritProperties(rootMaterial);
}

void Play::Initialise(Platform& platform)
{
    // Get references we need from the platform
    mGraphics = platform.GetGraphics();
    mInput = platform.GetInput();

    // Create UI
    auto clientSize = mGraphics->GetClientSize();
    mCanvas = std::make_shared<CanvasImGui>();
    mCanvas->SetSize(clientSize);
    mPlayUI = std::make_shared<UIPlay>(this);
    mCanvas->AppendChild(mPlayUI);
    mCanvas->AppendChild(std::make_shared<UIGraphicsDebug>(mGraphics));

    // Set up input interactions
    mInputDispatcher = std::make_shared<InputDispatcher>();
    mInputDispatcher->Initialise(mInput);
    mInputDispatcher->RegisterInteraction(std::make_shared<SelectInteraction>(this), true);
    mInputDispatcher->RegisterInteraction(std::make_shared<OrderInteraction>(this), true);
    mInputDispatcher->RegisterInteraction(std::make_shared<CameraInteraction>(this), true);
    mInputDispatcher->RegisterInteraction(std::make_shared<TerrainPaintInteraction>(this), true);
    mInputDispatcher->RegisterInteraction(std::make_shared<PlacementInteraction>(this), true);
    mInputDispatcher->RegisterInteraction(std::make_shared<CanvasInterceptInteraction>(mCanvas), true);

    // Create root resources
    mRootMaterial = std::make_shared<RootMaterial>();

    // Initialise other things
    mSelection = std::make_shared<SelectionManager>();
    mSelectionRenderer = std::make_shared<SelectionRenderer>(mSelection, mRootMaterial);

    mSkybox = std::make_shared<Skybox>();
    mSkybox->Initialise(mRootMaterial);

    // Compute material parameters
    auto lightVec = Vector3(0.8f, 0.1f, -0.5f).Normalize();
    mCamera.SetOrientation(
        Quaternion::CreateFromAxisAngle(Vector3::Right, 45.0f * (float)std::numbers::pi / 180.0f) *
        Quaternion::CreateFromAxisAngle(Vector3::Up, 30.0f * (float)std::numbers::pi / 180.0f)
    );
    mCamera.SetPosition(Vector3::Transform(Vector3(0.0f, 0.0f, -90.0f), mCamera.GetOrientation()));
    mCamera.SetFOV(15.0f * (float)std::numbers::pi / 180.0f);
    mCamera.SetAspect(clientSize.x / clientSize.y);
    mCamera.SetNearPlane(70.0f);
    mCamera.SetFarPlane(110.0f);

    mSunLight = std::make_shared<DirectionalLight>();

    mRootMaterial->SetUniform("DayTime", 0.5f);
    mRootMaterial->SetUniform("_WorldSpaceLightDir0", lightVec);
    mRootMaterial->SetUniform("_LightColor0", 4 * Vector3(1.0f, 0.98f, 0.95f));
    std::vector<Color> playerColors = {
        Color(1.0f, 0.8f, 0.5f),
        Color(0.1f, 0.2f, 1.0f),
        Color(1.0f, 0.2f, 0.1f),
        Color(0.1f, 1.0f, 0.2f),
    };
    mRootMaterial->SetUniform("_PlayerColors", playerColors);

    mRootMaterial->SetResolution(clientSize);
    mRootMaterial->SetView(mCamera.GetViewMatrix());
    mRootMaterial->SetProjection(mCamera.GetProjectionMatrix());

    mScene = std::make_shared<RetainedScene>();
    mBasePass = std::make_shared<RenderPass>("Base");
    mShadowPass = std::make_shared<RenderPass>("Shadow");
    mBasePass->mRetainedRenderer->SetScene(mScene);
    mBasePass->mOverrideMaterial = std::make_shared<Material>();
    mShadowPass->mRetainedRenderer->SetScene(mScene);
    mShadowPass->mRenderTarget = mSunLight->GetShadowBuffer();
    mShadowPass->mOverrideMaterial = mSunLight->GetRenderPassMaterialOverride();
    mRenderPasses = std::make_shared<RenderPassList>(mScene);
    mRenderPasses->mPasses.push_back(mShadowPass.get());
    mRenderPasses->mPasses.push_back(mBasePass.get());
    mBasePass->mOverrideMaterial->SetUniformTexture("ShadowMap", mShadowPass->mRenderTarget);
    mBasePass->mOverrideMaterial->SetComputedUniform<Matrix>("ShadowViewProjection", [=](auto& context) {
        return mShadowPass->mView * mShadowPass->mProjection;
    });
    mBasePass->mOverrideMaterial->SetComputedUniform<Matrix>("ShadowIVViewProjection", [=](auto& context) {
        return mBasePass->mView.Invert() * mShadowPass->mView * mShadowPass->mProjection;
    });

    // Initialise world
    mWorld = std::make_shared<World>();
    mWorld->Initialise(mRootMaterial, mRenderPasses);

    // Setup user interactions
    mActionDispatch = std::make_shared<Systems::ActionDispatchSystem>(mWorld.get());
    mActionDispatch->Initialise();
    mActionDispatch->RegisterAction<Systems::TrainingSystem>();
    mActionDispatch->RegisterAction<Systems::MovementSystem>();
    mActionDispatch->RegisterAction<Systems::AttackSystem>();
    mActionDispatch->RegisterAction<Systems::BuildSystem>();
    mActionDispatch->RegisterAction<Systems::GatherSystem>();
}

void Play::Step()
{
    // Calculate delta time
    auto now = steady_clock::now();
    float dt = std::min((now - mTimePoint).count() / (float)(1000 * 1000 * 1000), 1.0f);
#if defined(_DEBUG)
    if (mInput->IsKeyDown('Q')) dt *= 10.0f;
#endif
    mTimePoint = now;
    mTime += dt;

    // Allow keyboard camera movement
    auto camInput = Vector2::Zero;
    camInput.x = (float)mInput->IsKeyDown('A') - (float)mInput->IsKeyDown('D');
    camInput.y = (float)mInput->IsKeyDown('W') - (float)mInput->IsKeyDown('S');
    mCamera.MovePlanar(camInput, dt);

    // Process input
    mCanvas->Update(mInput);
    mInputDispatcher->Update();

    // Update uniform parameters
    mRootMaterial->SetUniform("Time", mTime);
    mRootMaterial->SetUniform("View", mCamera.GetViewMatrix());
    mRootMaterial->SetUniform("Projection", mCamera.GetProjectionMatrix());
    mWorld->Step(dt);
}

void Play::Render(CommandBuffer& cmdBuffer)
{
    mBasePass->mRenderQueue.Clear();
    mBasePass->UpdateViewProj(mCamera.GetViewMatrix(), mCamera.GetProjectionMatrix());
    mBasePass->UpdateViewProj(
        Matrix::CreateLookAt(Vector3(0, 5, -10), Vector3(0, 0, 0), Vector3(0, 1, 0)),
        Matrix::CreatePerspectiveFieldOfView(1.0f, 1.0, 1.0f, 500.0f)
    );
    mRootMaterial->SetUniform("View", mBasePass->mView);
    mRootMaterial->SetUniform("Projection", mBasePass->mProjection);

    // Create shadow projection based on frustum near/far corners
    auto frustum = mBasePass->mFrustum;
    Vector3 corners[8];
    frustum.GetCorners(corners);
    Matrix lightViewMatrix = Matrix::CreateLookAt(Vector3(20, 50, -100), Vector3(0, -5, 0), Vector3::Up);
    for (auto& corner : corners) corner = Vector3::Transform(corner, lightViewMatrix);
    auto lightFMin = std::accumulate(corners, corners + 8, Vector3(std::numeric_limits<float>::max()),
        [](auto a, auto v) { return Vector3::Min(a, v); });
    auto lightFMax = std::accumulate(corners, corners + 8, Vector3(std::numeric_limits<float>::lowest()),
        [](auto a, auto v) { return Vector3::Max(a, v); });
    // Or project onto terrain if smaller
    frustum.IntersectPlane(Vector3::Up, 0.0f, corners);
    frustum.IntersectPlane(Vector3::Up, 5.0f, corners + 4);
    for (auto& corner : corners) corner = Vector3::Transform(corner, lightViewMatrix);
    auto lightTMin = std::accumulate(corners, corners + 8, Vector3(std::numeric_limits<float>::max()),
        [](auto a, auto v) { return Vector3::Min(a, v); });
    auto lightTMax = std::accumulate(corners, corners + 8, Vector3(std::numeric_limits<float>::lowest()),
        [](auto a, auto v) { return Vector3::Max(a, v); });

    auto lightMin = Vector3::Max(lightFMin, lightTMin);
    auto lightMax = Vector3::Min(lightFMax, lightTMax);

    lightViewMatrix.Translation(lightViewMatrix.Translation() - (lightMin + lightMax) / 2.0f);
    auto lightSize = lightMax - lightMin;
    mShadowPass->mRenderQueue.Clear();
    mShadowPass->UpdateViewProj(
        lightViewMatrix,
        Matrix::CreateOrthographic(lightSize.x, lightSize.y, -lightSize.z / 2.0f, lightSize.z / 2.0f)
    );
    mShadowPass->mOverrideMaterial->SetUniform("View", mShadowPass->mView);
    mShadowPass->mOverrideMaterial->SetUniform("Projection", mShadowPass->mProjection);

    // Render the world
    mWorld->Render(cmdBuffer, *mRenderPasses);
    mSelectionRenderer->Render(cmdBuffer, *mRenderPasses);

    // Draw the skybox
    mBasePass->mRenderQueue.AppendMesh("Skybox", cmdBuffer, mSkybox->mMesh.get(), mSkybox->mMaterial.get());

    // Draw retained meshes
    mScene->SubmitGPUMemory(cmdBuffer);

    // Render the render passes
    for (auto* pass : mRenderPasses->mPasses)
    {
        pass->mRetainedRenderer->SubmitToRenderQueue(cmdBuffer, pass->mRenderQueue, pass->mFrustum);
        cmdBuffer.SetRenderTarget(pass->mRenderTarget.get());
        cmdBuffer.ClearRenderTarget(ClearConfig(Color(0.0f, 0.0f, 0.0f, 0.0f), 1.0f));

        pass->mRenderQueue.Render(cmdBuffer);
    }

    mOnRender.Invoke(cmdBuffer);
    //cmdBuffer.DrawMesh(mSkybox->mMesh.get(), mSkybox->mMaterial.get());

    // Render UI
    mCanvas->Render(cmdBuffer);
}

// Send an action request (move, attack, etc.) to selected entities
void Play::SendActionRequest(const Actions::ActionRequest& request)
{
    for (auto entity : mSelection->GetSelection())
    {
        if (!entity.is_alive()) continue;
        SendActionRequest(entity, request);
    }
}
// Send an action request (move, attack, etc.) to the specified entity
void Play::SendActionRequest(flecs::entity entity, const Actions::ActionRequest& request)
{
    auto* queue = entity.get_mut<Components::ActionQueue>();
    if (queue == nullptr) {
        entity.set(Components::ActionQueue());
        queue = entity.get_mut<Components::ActionQueue>();
    }
    Components::ActionQueue::RequestItem item;
    *(Actions::ActionRequest*)&item = request;
    item.mRequestId.mRequestId = 0;
    queue->mRequests.push_back(item);
    entity.modified<Components::ActionQueue>();

    mActionDispatch->CancelAction(entity, Actions::RequestId::MakeAll());
}

// Begin placing a building (or other placeable)
void Play::BeginPlacement(int protoId)
{
    const auto& placement = mInputDispatcher->FindInteraction<PlacementInteraction>();
    placement->SetPlacementProtoId(protoId);
}
int Play::GetPlacementProtoId() const
{
    const auto& placement = mInputDispatcher->FindInteraction<PlacementInteraction>();
    return placement->GetPlacementProtoId();
}

// Allow external systems to render objects
Play::OnRenderDelegate::Reference Play::RegisterOnRender(const OnRenderDelegate::Function& fn)
{
    return mOnRender.Add(fn);
}
