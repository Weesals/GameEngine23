#include "NativePlatform.h"

#include "WindowWin32.h"
#include "GraphicsDeviceD3D12.h"

void NativePlatform::Initialize()
{
    // Create the window
    auto window = std::make_shared<WindowWin32>(L"RTS Demo");
    // Initiaise graphics
    auto device = std::make_shared<GraphicsDeviceD3D12>(window);

    // Create input buffer and link to window
    auto input = std::make_shared<Input>();
    window->SetInput(input);

    // Set the platform references
    mWindow = window;
    //mGraphics = device;
    mInput = input;
}

int NativePlatform::MessagePump()
{
    return mWindow->MessagePump();
}
void NativePlatform::Present()
{
    mGraphics->Present();
    // Tell the input to flush per-frame data
    mInput->GetMutator().ReceiveTickEvent();
}

