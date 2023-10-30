#include "Platform.h"
#include "Play.h"
#include "UIGraphicsDebug.h"

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
        auto gfxDbg = play.GetCanvas()->FindChild<UIGraphicsDebug>();

        // Update the game
        auto now = steady_clock::now();
        play.Step();
        gfxDbg->AppendStepTimer(steady_clock::now() - now);

        now = steady_clock::now();
        // Begin rendering
        cmdBuffer.Reset();

        // Render the scene
        play.Render(cmdBuffer);

        // Finish rendering
        cmdBuffer.Execute();

        gfxDbg->AppendRenderTimer(steady_clock::now() - now);

        platform.Present();
    }
}
