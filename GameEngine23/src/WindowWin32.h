#pragma once

#include <string>
#include <memory>

#include "WindowBase.h"
#include "Input.h"

#define NOMINMAX
#include <Windows.h>

// A Windows window (using old desktop win32 APIs)
class WindowWin32 :
    public WindowBase
{
    HINSTANCE hInstance;
    HWND hWnd;

    // If configured, can send any input events into this input buffer
    std::shared_ptr<Input> mInput;
    // And can send mouse events to this pointer
    std::shared_ptr<Pointer> mMousePointer;

    std::shared_ptr<Pointer> RequireMousePointer();

public:
    WindowWin32(const std::wstring &name);
    ~WindowWin32() override;

    // Set input buffer to receive input events
    void SetInput(std::shared_ptr<Input> input);

    HWND GetHWND() const { return hWnd; }

    // Get the size of the inner window area (not including border or title bar)
    std::pair<int, int> GetClientSize() const override;

    // Process window messages and then return control to the callee
    // Non-zero values mean the window was closed
    int MessagePump() override;

    // Helper functions for receiving messages from Windows
    static LRESULT CALLBACK _WndProc(HWND hWnd, UINT message, WPARAM wParam, LPARAM lParam);
    static void _MouseButtonEvent(HWND hWnd, WPARAM wParam, LPARAM lParam, unsigned int buttonMask, bool state);
};
