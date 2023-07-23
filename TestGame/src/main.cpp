#include "Platform.h"
#include "Play.h"

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

    // Create a command buffer
    auto cmdBuffer = play.GetGraphics()->CreateCommandBuffer();

    // Respond to window events and run rendering code
    while (platform.MessagePump() == 0)
    {
        // Update the game
        play.Step();

        // Begin rendering
        cmdBuffer.Reset();

        // Clear screen
        cmdBuffer.ClearRenderTarget(ClearConfig(Color(0.5f, 0.7f, 1.0f, 1.0f), 1.0f));

        // Render the scene
        play.Render(cmdBuffer);

        // Finish rendering
        cmdBuffer.Execute();

        platform.Present();
    }

}
