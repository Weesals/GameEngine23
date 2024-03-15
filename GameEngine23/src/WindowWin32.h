#pragma once

#include <string>
#include <memory>

#include "WindowBase.h"
#include "Input.h"

#define NOMINMAX
#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#undef CreateWindow

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
    virtual void SetInput(const std::shared_ptr<Input>& input) override;

    HWND GetHWND() const { return hWnd; }

    WindowStatus GetStatus() const override;

    // Get the size of the inner window area (not including border or title bar)
    Int2 GetClientSize() const override;
    void SetClientSize(Int2 size) override;

    // Process window messages and then return control to the callee
    // Non-zero values mean the window was closed
    static int MessagePump();

    void Close() override;

    // Helper functions for receiving messages from Windows
    static LRESULT CALLBACK _WndProc(HWND hWnd, UINT message, WPARAM wParam, LPARAM lParam);
    static void _MouseButtonEvent(HWND hWnd, WPARAM wParam, LPARAM lParam, unsigned int buttonMask, bool state);
};

