#include "Play.h"

#include "../FBXImport.h"

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
    mMaterial = std::make_shared<Material>(Shader(L"res/skybox.hlsl"), Shader(L"res/skybox.hlsl"));
    mMaterial->InheritProperties(rootMaterial);
}

void Play::Initialise(Platform& platform)
{
    // Get references we need from the platform
    mGraphics = platform.GetGraphics();
    mInput = platform.GetInput();

    // Create root resources
    mRootMaterial = std::make_shared<Material>();

    mSkybox = std::make_shared<Skybox>();
    mSkybox->Initialise(mRootMaterial);

    // Compute material parameters
    auto clientSize = mGraphics->GetClientSize();
    auto lightVec = Vector3(0.8f, 0.1f, 0.5f).Normalize();
    mCamera.SetPosition(Vector3(0.0f, 2.0f, 10.0f));
    mCamera.SetAspect(clientSize.x / clientSize.y);

    mRootMaterial->SetUniform("Resolution", clientSize);
    mRootMaterial->SetUniform("DayTime", 0.5f);
    mRootMaterial->SetUniform("_WorldSpaceLightDir0", lightVec);
    mRootMaterial->SetUniform("_LightColor0", 3 * Vector3(1.0f, 0.98f, 0.95f));

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
        return Vector3::TransformNormal(lightDir, context.GetUniform<Matrix>(iVMat).Transpose());
    });
    mRootMaterial->SetComputedUniform<Vector3>("_ViewSpaceUpVector", [=](auto context) {
        return context.GetUniform<Matrix>(iMVMat).Transpose().Up();
    });

    mWorld = std::make_shared<World>();

    // Initialise world
    mWorld->Initialise(mRootMaterial);
}

void Play::Step()
{
    // Calculate delta time
    auto now = steady_clock::now();
    float dt = (now - mTimePoint).count() / (float)(1000 * 1000 * 1000);
    if (dt > 1000) dt = 0.0f;
    mTimePoint = now;
    mTime += dt;

    // Testing input stuff
    for (auto& pointer : mInput->GetPointers())
    {
        // On right-click, allow dragging to move view
        if (pointer->IsButtonDown(1))
        {
            auto pos = mCamera.GetPosition();
            auto rot = mCamera.GetOrientation();
            auto newRot =
                Quaternion::CreateFromAxisAngle(Vector3::Right, pointer->GetPositionDelta().y * -0.005f)
                * rot
                * Quaternion::CreateFromAxisAngle(Vector3::Up, pointer->GetPositionDelta().x * -0.005f);
            pos = Vector3::Transform(pos, rot.Inverse() * newRot);
            mCamera.SetPosition(pos);
            mCamera.SetOrientation(newRot);
        }
        // On left-click, move all entities under the cursor to 0,0,0
        if (pointer->IsButtonPress(0))
        {
            Ray ray = mCamera.ViewportToRay(pointer->mPositionCurrent / mGraphics->GetClientSize());
            mWorld->RaycastEntities(ray, [=](flecs::entity e)
                {
                    auto t = e.get_mut<Transform>();
                    t->Position = Vector3::Zero;
                });
        }
    }

    // Update uniform parameters
    auto projMat = mCamera.GetProjectionMatrix();
    auto viewMat = mCamera.GetViewMatrix();
    mRootMaterial->SetUniform("Projection", projMat.Transpose());
    mRootMaterial->SetUniform("View", viewMat.Transpose());
    mRootMaterial->SetUniform("Time", mTime);
    mWorld->Step(dt);
}

void Play::Render(CommandBuffer& cmdBuffer)
{
    // Render the world
    mWorld->Render(cmdBuffer);
    // Render the skybox
    cmdBuffer.DrawMesh(mSkybox->mMesh, mSkybox->mMaterial);
}
