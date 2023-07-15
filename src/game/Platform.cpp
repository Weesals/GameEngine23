#include "Platform.h"

#include <algorithm>
#include <vector>

#include "../WindowWin32.h"

#include "../GraphicsDeviceMulti.h"

#if defined(VULKAN)
# include "../GraphicsDeviceVulkan.h"
# include "../GraphicsDeviceD3D12.h"
#else
# include "../GraphicsDeviceD3D12.h"
#endif

void Platform::Initialize()
{
    // Create the window
    auto window = std::make_shared<WindowWin32>(L"RTS Demo");

    // Initiaise graphics
#if defined(VULKAN)
    auto device = std::make_shared<GraphicsDeviceVulkan>(window);
#else
    auto device = std::make_shared<GraphicsDeviceD3D12>(window);
#endif

    // Create input buffer and link to window
    auto input = std::make_shared<Input>();
    window->SetInput(input);

    // Set the platform references
    mWindow = window;
    mGraphics = device;
    mInput = input;
}

int Platform::MessagePump()
{
    return mWindow->MessagePump();
}
void Platform::Present()
{
    mGraphics->Present();
    // Tell the input to flush per-frame data
    mInput->GetMutator().ReceiveTickEvent();
}
