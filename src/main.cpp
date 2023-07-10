#include "game/Platform.h"
#include "game/Play.h"

#include <Windows.h>

int APIENTRY wWinMain(_In_ HINSTANCE hInstance,
    _In_opt_ HINSTANCE hPrevInstance,
    _In_ LPWSTR    lpCmdLine,
    _In_ int       nCmdShow)
{
    // Initialise platform-specific objects
    Platform platform;
    platform.Initialize();

    // Initialise play
    Play play;
    play.Initialise(platform);

    // Initialise world
    auto world = std::make_shared<World>();
    world->Initialise(play.mRootMaterial);
    play.mWorld = world;

    // Create a command buffer
    auto cmdBuffer = play.mGraphics->CreateCommandBuffer();

    // Respond to window events and run rendering code
    while (play.mWindow->MessagePump() == 0)
    {
        // Update the game
        play.Step();

        // Begin rendering
        cmdBuffer.Reset();

        // Clear screen
        cmdBuffer.ClearRenderTarget(ClearConfig(Color(1.0, 1.0, 1.0, 1.0), 1.0f));

        // Render the scene
        play.Render(cmdBuffer);

        // Finish rendering
        cmdBuffer.Execute();
        play.mGraphics->Present();

        // Tell the input to flush per-frame data
        play.mInput->GetMutator().ReceiveTickEvent();
    }

}
