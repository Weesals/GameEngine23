#include "WindowWin32.h"

#include <windowsx.h>
#include "GraphicsDeviceD3D12.h"

WCHAR szWindowClass[] = L"RTSWINDOW";

WindowWin32::WindowWin32(const std::wstring &name)
{
    hInstance = GetModuleHandle(NULL);

    // Register a class for our window to be a member of
    WNDCLASSW wcex = {};
    wcex.lpfnWndProc = WindowWin32::_WndProc;
    wcex.hInstance = hInstance;
    wcex.lpszClassName = szWindowClass;
    wcex.hCursor = LoadCursor(nullptr, IDC_ARROW);
    RegisterClassW(&wcex);

    // Create a standard overlapped window
    hWnd = CreateWindowW(szWindowClass, name.c_str(), WS_OVERLAPPEDWINDOW | WS_VISIBLE,
        CW_USEDEFAULT, CW_USEDEFAULT, CW_USEDEFAULT, CW_USEDEFAULT,
        nullptr, nullptr, hInstance, nullptr);

    // Focus the window
    //ShowWindow(hWnd, SW_SHOWDEFAULT);
    UpdateWindow(hWnd);

    // Set a pointer to this object so that messages can be forwarded
    SetWindowLongPtr(hWnd, GWLP_USERDATA, (LONG_PTR)this);
}

WindowWin32::~WindowWin32()
{
    SetWindowLongPtr(hWnd, GWLP_USERDATA, (LONG_PTR)nullptr);
    CloseWindow(hWnd);
}

void WindowWin32::SetInput(std::shared_ptr<Input> input)
{
    mInput = input;
}

std::pair<int, int> WindowWin32::GetClientSize() const
{
    RECT clientRect;
    GetClientRect(GetHWND(), &clientRect);
    return std::pair<int, int>(
        (uint32_t)(clientRect.right - clientRect.left),
        (uint32_t)(clientRect.bottom - clientRect.top)
    );
}

int WindowWin32::MessagePump()
{
    MSG msg;
    while ((PeekMessage(&msg, NULL, 0U, 0U, PM_REMOVE) != 0)) {
        TranslateMessage(&msg);
        DispatchMessage(&msg);

        // The window has closed, return the exit code
        // (cannot be 1)
        if (msg.message == WM_QUIT) return msg.wParam == 0 ? 1 : (int)msg.wParam;
    }
    return 0;
}

std::shared_ptr<Pointer> WindowWin32::RequireMousePointer()
{
    if (mMousePointer == nullptr && mInput != nullptr) mMousePointer = mInput->AllocatePointer(-1);
    return mMousePointer;
}

LRESULT CALLBACK WindowWin32::_WndProc(HWND hWnd, UINT message, WPARAM wParam, LPARAM lParam)
{
    switch (message)
    {
    case WM_PAINT: {
        PAINTSTRUCT ps;
        HDC hdc = BeginPaint(hWnd, &ps);
        EndPaint(hWnd, &ps);
    } break;
    // Receive mouse events
    case WM_LBUTTONDOWN: {
        _MouseButtonEvent(hWnd, wParam, lParam, 0x01, true);
    } break;
    case WM_LBUTTONUP: {
        _MouseButtonEvent(hWnd, wParam, lParam, 0x01, false);
    } break;
    case WM_RBUTTONDOWN: {
        _MouseButtonEvent(hWnd, wParam, lParam, 0x02, true);
    } break;
    case WM_RBUTTONUP: {
        _MouseButtonEvent(hWnd, wParam, lParam, 0x02, false);
    } break;
    case WM_MBUTTONDOWN: {
        _MouseButtonEvent(hWnd, wParam, lParam, 0x04, true);
    } break;
    case WM_MBUTTONUP: {
        _MouseButtonEvent(hWnd, wParam, lParam, 0x04, false);
    } break;
    case WM_MOUSEMOVE: {
        auto window = reinterpret_cast<WindowWin32*>(GetWindowLongPtr(hWnd, GWLP_USERDATA));
        auto pointer = window->RequireMousePointer();
        if (pointer) pointer->ReceiveMoveEvent(Vector2((float)GET_X_LPARAM(lParam), (float)GET_Y_LPARAM(lParam)));
    } break;
    // Receive key events
    case WM_KEYDOWN: {
        auto window = reinterpret_cast<WindowWin32*>(GetWindowLongPtr(hWnd, GWLP_USERDATA));
        if (window != nullptr && window->mInput != nullptr) {
            window->mInput->GetMutator().ReceiveKeyEvent((int)wParam, true);
        }
    } break;
    case WM_KEYUP: {
        auto window = reinterpret_cast<WindowWin32*>(GetWindowLongPtr(hWnd, GWLP_USERDATA));
        if (window != nullptr && window->mInput != nullptr) {
            window->mInput->GetMutator().ReceiveKeyEvent((int)wParam, false);
        }
    } break;
    case WM_DESTROY:
        PostQuitMessage(0);
        break;
    default:
        return DefWindowProc(hWnd, message, wParam, lParam);
    }
    return 0;
}

void WindowWin32::_MouseButtonEvent(HWND hWnd, WPARAM wParam, LPARAM lParam, unsigned int buttonMask, bool state)
{
    // Update mouse button state
    auto window = reinterpret_cast<WindowWin32*>(GetWindowLongPtr(hWnd, GWLP_USERDATA));
    auto pointer = window->RequireMousePointer();
    if (pointer == nullptr) return;
    pointer->ReceiveMoveEvent(Vector2((float)GET_X_LPARAM(lParam), (float)GET_Y_LPARAM(lParam)));
    pointer->ReceiveButtonEvent(buttonMask, state);

    // Extend input to outside of window
    if (state) SetCapture(hWnd); else ReleaseCapture();
}
