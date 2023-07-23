#pragma once

#include <utility>

// A window allows display of graphics and receiving input
// Is extended for platform-specific variants
class WindowBase
{

public:
	virtual ~WindowBase() { }

	// Get the size of the client area (not including borders or title bar)
	virtual std::pair<int, int> GetClientSize() const = 0;

	// Evaluate window messages and return non-zero if the window is closed
	// (return is the error or success code of the window close event)
	virtual int MessagePump() = 0;

};

