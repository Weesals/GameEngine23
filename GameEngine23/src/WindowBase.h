#pragma once

#include <utility>
#include <memory>
#include "MathTypes.h"

class Input;

// A window allows display of graphics and receiving input
// Is extended for platform-specific variants
class WindowBase
{

public:
	virtual ~WindowBase() { }

	enum WindowStatus { Alive, Closed, };

	virtual WindowStatus GetStatus() const = 0;

	// Get the size of the client area (not including borders or title bar)
	virtual Int2 GetClientSize() const = 0;
	virtual void SetClientSize(Int2 size) = 0;

	virtual void SetInput(const std::shared_ptr<Input>& input) { }

	// Evaluate window messages and return non-zero if the window is closed
	// (return is the error or success code of the window close event)
	//virtual int MessagePump() = 0;

	virtual void Close() { }

};

