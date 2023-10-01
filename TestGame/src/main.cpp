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

    // Run the game loop
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
extern "C" {
    void Draw() {
        main();
    }
}