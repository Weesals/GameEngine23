#pragma once

#include "WindowBase.h"
#include "GraphicsDeviceBase.h"
#include "Input.h"

#include <memory>

class NativePlatform
{
	std::shared_ptr<WindowBase> mWindow;
	std::shared_ptr<GraphicsDeviceBase> mGraphics;
	std::shared_ptr<Input> mInput;

public:
	// Load relevant platform systems
	void Initialize();

	// Use to access platform systems
	std::shared_ptr<WindowBase> CreateWindow(const std::wstring_view& name);

	const std::shared_ptr<WindowBase>& GetWindow() const { return mWindow; }
	const std::shared_ptr<GraphicsDeviceBase>& GetGraphics() const { return mGraphics; }
	const std::shared_ptr<Input>& GetInput() const { return mInput; }

	// Call per frame to handle other processing
	int MessagePump();
	void Present();

};

