#include "Platform.h"

#include "../WindowWin32.h"
#include "../GraphicsDeviceD3D12.h"

void Platform::Initialize()
{
    // Create the window
    auto window = std::make_shared<WindowWin32>(L"RTS Demo");

    // Initiaise D3D12
    auto device = std::make_shared<GraphicsDeviceD3D12>(*window.get());

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
