#pragma once

// A window allows display of graphics and receiving input
// Is extended for platform-specific variants
class WindowBase
{

public:
	virtual ~WindowBase() { }
	virtual int MessagePump() = 0;

};

