#include "NativePlatform.h"

#include "WindowWin32.h"
#include "GraphicsDeviceD3D12.h"

void NativePlatform::Initialize()
{
    // Initiaise graphics
    auto device = std::make_shared<GraphicsDeviceD3D12>();

    // Create input buffer and link to window
    //auto input = std::make_shared<Input>();

    // Set the platform references
    mGraphics = device;
    //mInput = input;
}

std::shared_ptr<WindowBase> NativePlatform::CreateWindow(const std::wstring_view& name) {
    auto window = std::make_shared<WindowWin32>(name.data());
    //window->SetInput(mInput);
    return window;
}

int NativePlatform::MessagePump()
{
    return WindowWin32::MessagePump();
    //return mWindow->MessagePump();
}
void NativePlatform::Present()
{
    //mGraphics->Present();
    // Tell the input to flush per-frame data
    //mInput->GetMutator().ReceiveTickEvent();
}

