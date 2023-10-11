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