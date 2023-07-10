#pragma once

#include "../WindowBase.h"
#include "../GraphicsDeviceBase.h"
#include "../Input.h"

// This class hides platform-specific variants behind a cpp file
// so that they do not need to be #included elsewhere
// Its purpose is to reduce compile time, and also collect
// platform specific code in one place
class Platform
{
public:
	std::shared_ptr<WindowBase> mWindow;
	std::shared_ptr<GraphicsDeviceBase> mGraphicsDevice;
	std::shared_ptr<Input> mInput;

	void Initialize();

};
