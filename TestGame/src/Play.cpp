#include "Play.h"

#include "InputInteractions.h"

#include <numbers>
#include <FBXImport.h>

void Skybox::Initialise(std::shared_ptr<Material>& rootMaterial)
{
    // Generate a skybox mesh
    mMesh = std::make_shared<Mesh>();
    mMesh->SetVertexCount(4);
    auto positions = mMesh->GetPositions();
    for (int i = 0; i < positions.size(); ++i)
        positions[i] = Vector3((i % 2) * 2.0f - 1.0f, (i / 2) * 2.0f - 1.0f, 0.0f);

    mMesh->SetIndices({ 0, 3, 1, 0, 2, 3, });

    // Load the skybox material
    mMaterial = std::make_shared<Material>(std::make_shared<Shader>(L"assets/skybox.hlsl"), std::make_shared<Shader>(L"assets/skybox.hlsl"));
    mMaterial->InheritProperties(rootMaterial);
}

void Play::Initialise(Platform& platform)
{
    // Get references we need from the platform
    mGraphics = platform.GetGraphics();
    mInput = platform.GetInput();

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
    mRootMaterial = std::make_shared<Material>();

    auto clientSize = mGraphics->GetClientSize();
    mCanvas = std::make_shared<Canvas>();
    mCanvas->SetSize(clientSize);
    mPlayUI = std::make_shared<UIPlay>(this);
    mCanvas->AppendChild(mPlayUI);

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

    mRootMaterial->SetUniform("Resolution", clientSize);
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

    Identifier iMMat = "Model";
    Identifier iVMat = "View";
    Identifier iPMat = "Projection";
    Identifier iMVMat = "ModelView";
    Identifier iMVPMat = "ModelViewProjection";
    Identifier iLightDir = "_WorldSpaceLightDir0";
    mRootMaterial->SetUniform("Model", Matrix::Identity);
    mRootMaterial->SetComputedUniform<Matrix>("ModelView", [=](auto context) {
        auto m = context.GetUniform<Matrix>(iMMat);
        auto v = context.GetUniform<Matrix>(iVMat);
        return (v * m);
    });
    mRootMaterial->SetComputedUniform<Matrix>("ViewProjection", [=](auto context) {
        auto v = context.GetUniform<Matrix>(iVMat);
        auto p = context.GetUniform<Matrix>(iPMat);
        return (p * v);
    });
    mRootMaterial->SetComputedUniform<Matrix>("ModelViewProjection", [=](auto context) {
        auto mv = context.GetUniform<Matrix>(iMVMat);
        auto p = context.GetUniform<Matrix>(iPMat);
        return (p * mv);
    });
    mRootMaterial->SetComputedUniform<Matrix>("InvModelViewProjection", [=](auto context) {
        auto mvp = context.GetUniform<Matrix>(iMVPMat);
        return mvp.Invert();
    });
    mRootMaterial->SetComputedUniform<Vector3>("_ViewSpaceLightDir0", [=](auto context) {
        auto lightDir = context.GetUniform<Vector3>(iLightDir);
        auto view = context.GetUniform<Matrix>(iVMat).Transpose();
        return Vector3::TransformNormal(lightDir, view);
    });
    mRootMaterial->SetComputedUniform<Vector3>("_ViewSpaceUpVector", [=](auto context) {
        return context.GetUniform<Matrix>(iVMat).Transpose().Up();
    });

    mWorld = std::make_shared<World>();

    // Initialise world
    mWorld->Initialise(mRootMaterial);

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
    float dt = std::min((now - mTimePoint).count() / (float)(1000 * 1000 * 1000), 1000.0f);
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
    mRootMaterial->SetUniform("Projection", mCamera.GetProjectionMatrix().Transpose());
    mRootMaterial->SetUniform("View", mCamera.GetViewMatrix().Transpose());
    mRootMaterial->SetUniform("Time", mTime);
    mWorld->Step(dt);
}

void Play::Render(CommandBuffer& cmdBuffer)
{
    // Render the world
    mWorld->Render(cmdBuffer);
    mSelectionRenderer->Render(cmdBuffer);
    mOnRender.Invoke(cmdBuffer);
    // Render the skybox
    cmdBuffer.DrawMesh(mSkybox->mMesh, mSkybox->mMaterial);

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
