#pragma once

#include <WindowBase.h>
#include <GraphicsDeviceBase.h>
#include <Input.h>

// This class hides platform-specific variants behind a cpp file
// so that they do not need to be #included elsewhere
// Its purpose is to reduce compile time, and also collect
// platform specific code in one place
class Platform
{
	std::shared_ptr<WindowBase> mWindow;
	std::shared_ptr<GraphicsDeviceBase> mGraphics;
	std::shared_ptr<Input> mInput;

public:
	// Load relevant platform systems
	void Initialize();

	// Use to access platform systems
	std::shared_ptr<WindowBase>& GetWindow() { return mWindow; }
	std::shared_ptr<GraphicsDeviceBase>& GetGraphics() { return mGraphics; }
	std::shared_ptr<Input>& GetInput() { return mInput; }

	// Call per frame to handle other processing
	int MessagePump();
	void Present();

};
