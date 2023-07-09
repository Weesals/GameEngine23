#include "WindowWin32.h"
#include "GraphicsDeviceD3D12.h"
#include "FBXImport.h"

int APIENTRY wWinMain(_In_ HINSTANCE hInstance,
    _In_opt_ HINSTANCE hPrevInstance,
    _In_ LPWSTR    lpCmdLine,
    _In_ int       nCmdShow)
{

    // Create the window
    WindowWin32 window(L"RTS Demo");

    // Initiaise D3D12
    GraphicsDeviceD3D12 d3d(window);

    // Create input buffer
    auto input = std::make_shared<Input>();
    window.SetInput(input);

    // Load a test model
    auto model = FBXImport::ImportAsModel(L"res/test.fbx");

    // Generate a skybox mesh
    auto skybox = std::make_shared<Mesh>();
    skybox->SetVertexCount(4);
    auto positions = skybox->GetPositions();
    for (int i = 0; i < positions.size(); ++i) {
        positions[i] = Vector3((i % 2) * 2.0f - 1.0f, (i / 2) * 2.0f - 1.0f, 0.0f);
    }
    skybox->SetIndices({ 0, 3, 1, 0, 2, 3, });

    // Load materials
    auto rootMaterial = std::make_shared<Material>();
    auto litMaterial = std::make_shared<Material>(Shader(L"res/lit.hlsl"), Shader(L"res/lit.hlsl"));
    litMaterial->InheritProperties(rootMaterial);
    auto skyMaterial = std::make_shared<Material>(Shader(L"res/skybox.hlsl"), Shader(L"res/skybox.hlsl"));
    skyMaterial->InheritProperties(rootMaterial);

    // Compute material parameters
    auto clientSize = d3d.GetClientSize();
    auto projMat = Matrix::CreatePerspectiveFieldOfView(3.14f / 4.0f, clientSize.x / clientSize.y, 0.1f, 50.0f);
    auto camrMat = Matrix::CreateTranslation(0, 1.0f, 6.0f);
    auto lightVec = Vector3(0.8f, 0.1f, 0.5f);
    lightVec.Normalize();
    rootMaterial->SetUniform("Resolution", clientSize);
    rootMaterial->SetUniform("DayTime", 0.5f);
    rootMaterial->SetUniform("_WorldSpaceLightDir0", lightVec);
    rootMaterial->SetUniform("_LightColor0", 3 * Vector3(1.0f, 0.98f, 0.95f));

    // Create a command buffer
    auto cmdBuffer = d3d.CreateCommandBuffer();

    // Respond to window events and run rendering code
    float time = 0;
    while (window.MessagePump() == 0)
    {
        time += 0.01f;

        // Testing input stuff
        auto pointers = input->GetPointers();
        for (auto pointer : input->GetPointers())
        {
            if (pointer->IsButtonDown(0))
            {
                // If mouse is clicked, allow dragging to move view
                camrMat = camrMat
                    * Matrix::CreateFromAxisAngle(camrMat.Right(), pointer->GetPositionDelta().y * -0.005f)
                    * Matrix::CreateRotationY(pointer->GetPositionDelta().x * -0.005f);
            }
        }

        // Update uniform parameters
        auto viewMat = camrMat.Invert();
        auto viewProjMatrix = viewMat * projMat;
        rootMaterial->SetUniform("ModelView", viewMat.Transpose());
        rootMaterial->SetUniform("ModelViewProjection", viewProjMatrix.Transpose());
        rootMaterial->SetUniform("InvModelViewProjection", viewProjMatrix.Invert().Transpose());
        rootMaterial->SetUniform("Time", time);
        rootMaterial->SetUniform("_ViewSpaceLightDir0", Vector3::TransformNormal(lightVec, viewMat));
        rootMaterial->SetUniform("_ViewSpaceUpVector", viewMat.Up());

        // Render the scene
        cmdBuffer.Reset();

        // Clear screen
        cmdBuffer.ClearRenderTarget(ClearConfig(Color(1.0, 1.0, 1.0, 1.0), 1.0f));

        // Draw test mesh
        for (auto mesh : model->GetMeshes())
            cmdBuffer.DrawMesh(mesh, litMaterial);

        // Draw skybox
        cmdBuffer.DrawMesh(skybox, skyMaterial);

        // Finish rendering
        cmdBuffer.Execute();
        d3d.Present();

        // Tell the input to flush per-frame data
        input->GetMutator().ReceiveTickEvent();
    }

}
