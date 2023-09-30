#include "Platform.h"
#include "Play.h"

#include "RetainedRenderer.h"

int main()
{
    // Initialise platform-specific objects
    Platform platform;
    platform.Initialize();

    // Initialise play
    Play play;
    play.Initialise(platform);
    // Create a command buffer
    auto cmdBuffer = play.GetGraphics()->CreateCommandBuffer();

    /*RetainedRenderer renderer(play.GetGraphics());
    auto testMesh = ResourceLoader::GetSingleton().LoadModel(L"assets/SM_TownCentre.fbx");
    auto testMat = std::make_shared<Material>(L"assets/retained.hlsl");
    auto testTex = ResourceLoader::GetSingleton().LoadTexture(L"assets/T_ToonBuildingsAtlas.png");
    testMat->InheritProperties(play.GetWorld()->GetLitMaterial());
    testMat->SetUniform("Model", Matrix::Identity);
    testMat->SetUniform("Texture", testTex);
    testMat->SetUniform("Highlight", Color(0.0f, 0.0f, 0.0f, 0.0f));//*/

    // Run the game loop
    while (platform.MessagePump() == 0)
    {
        // Update the game
        play.Step();

        // Begin rendering
        cmdBuffer.Reset();

        // Clear screen
        cmdBuffer.ClearRenderTarget(ClearConfig(Color(0.5f, 0.7f, 1.0f, 1.0f), 1.0f));

        /*if (renderer.GetBatchCount() == 0) {
            renderer.AppendMeshDraw(testMesh->GetMeshes()[0].get(), testMat.get());
            testMat->SetUniform("Model", Matrix::CreateTranslation(10, 0, 0));
            renderer.AppendMeshDraw(testMesh->GetMeshes()[0].get(), testMat.get());
            testMat->SetUniform("Model", Matrix::CreateTranslation(10, 0, 10));
            renderer.AppendMeshDraw(testMesh->GetMeshes()[0].get(), testMat.get());
        }//*/

        // Render the scene
        play.Render(cmdBuffer);
        //renderer.Render(cmdBuffer);

        // Finish rendering
        cmdBuffer.Execute();

        platform.Present();
    }
}
