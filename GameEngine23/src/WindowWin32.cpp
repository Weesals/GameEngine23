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

void WindowWin32::SetInput(const std::shared_ptr<Input>& input) {
    mInput = input;
}

WindowBase::WindowStatus WindowWin32::GetStatus() const {
    return IsWindowEnabled(hWnd) ? WindowBase::WindowStatus::Alive
        : WindowBase::WindowStatus::Closed;
}

Int2 WindowWin32::GetClientSize() const {
    RECT clientRect;
    GetClientRect(GetHWND(), &clientRect);
    return Int2(
        (uint32_t)(clientRect.right - clientRect.left),
        (uint32_t)(clientRect.bottom - clientRect.top)
    );
}
void WindowWin32::SetClientSize(Int2 size) {
    auto hwnd = GetHWND();
    auto style = GetWindowStyle(hwnd);
    RECT rect;
    GetClientRect(hwnd, &rect);
    rect.right = rect.left + size.x;
    rect.bottom = rect.top + size.y;
    AdjustWindowRectEx(&rect, GetWindowLong(hWnd, GWL_STYLE), FALSE, GetWindowLong(hWnd, GWL_EXSTYLE));
    SetWindowPos(hwnd, NULL, 0, 0, rect.right - rect.left, rect.bottom - rect.top, SWP_NOZORDER | SWP_NOACTIVATE | SWP_NOMOVE);
    UpdateWindow(hwnd);
}

void WindowWin32::RegisterMovedCallback(void (*Callback)(), bool enable) {
    mMovedCallbacks.push_back(Callback);
}

int WindowWin32::MessagePump() {
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

void WindowWin32::Close()
{
    CloseWindow(hWnd);
}

std::shared_ptr<Pointer> WindowWin32::RequireMousePointer()
{
    if (mMousePointer == nullptr && mInput != nullptr) mMousePointer = mInput->AllocatePointer(-1);
    return mMousePointer;
}

LRESULT CALLBACK WindowWin32::_WndProc(HWND hWnd, UINT message, WPARAM wParam, LPARAM lParam)
{
    static auto UpdateModifiers = [](WindowWin32* window) {
        static unsigned short keys[] = { VK_LSHIFT, VK_RSHIFT, VK_LCONTROL, VK_RCONTROL, /*VK_LMENU, VK_RMENU, Handled by Syskey */ };
        static unsigned long long KeyMask = 0;
        for (auto key : keys) {
            auto keyMask = 1ull << (key - keys[0]);
            auto state = GetKeyState(key) < 0;
            auto prevState = (KeyMask & keyMask) != 0;
            if (state == prevState) continue;
            if (state) KeyMask |= keyMask; else KeyMask &= ~keyMask;
            window->mInput->GetMutator().ReceiveKeyEvent(key, state);
        }
    };
    static auto SysKeyEvent = [](WindowWin32* window, UINT_PTR wParam, UINT_PTR lParam, bool state) {
        if (wParam == VK_MENU) {
            auto extended = lParam & (1 << 24);
            window->mInput->GetMutator().ReceiveKeyEvent(extended ? VK_RMENU : VK_LMENU, state);
        }
        window->mInput->GetMutator().ReceiveKeyEvent((int)wParam, state);
    };

    switch (message) {
    case WM_PAINT: {
        //PAINTSTRUCT ps;
        //HDC hdc = BeginPaint(hWnd, &ps);
        //EndPaint(hWnd, &ps);
        ValidateRect(hWnd, nullptr);
    } return 0;
    case WM_SIZE:
    case WM_MOVE: {
        auto window = reinterpret_cast<WindowWin32*>(GetWindowLongPtr(hWnd, GWLP_USERDATA));
        if (window != nullptr) {
            for (auto& callback : window->mMovedCallbacks) {
                callback();
            }
        }
    } break;
    // Receive mouse events
    case WM_LBUTTONDOWN: {
        _MouseButtonEvent(hWnd, wParam, lParam, 0x01, true);
    } return 0;
    case WM_LBUTTONUP: {
        _MouseButtonEvent(hWnd, wParam, lParam, 0x01, false);
    } return 0;
    case WM_RBUTTONDOWN: {
        _MouseButtonEvent(hWnd, wParam, lParam, 0x02, true);
    } return 0;
    case WM_RBUTTONUP: {
        _MouseButtonEvent(hWnd, wParam, lParam, 0x02, false);
    } return 0;
    case WM_MBUTTONDOWN: {
        _MouseButtonEvent(hWnd, wParam, lParam, 0x04, true);
    } return 0;
    case WM_MBUTTONUP: {
        _MouseButtonEvent(hWnd, wParam, lParam, 0x04, false);
    } return 0;
    case WM_MOUSEMOVE: {
        auto window = reinterpret_cast<WindowWin32*>(GetWindowLongPtr(hWnd, GWLP_USERDATA));
        auto pointer = window->RequireMousePointer();
        if (pointer) pointer->ReceiveMoveEvent(Vector2((float)GET_X_LPARAM(lParam), (float)GET_Y_LPARAM(lParam)));
    } return 0;
    // Receive key events
    case WM_SYSKEYDOWN:
    case WM_SYSKEYUP: {
        auto window = reinterpret_cast<WindowWin32*>(GetWindowLongPtr(hWnd, GWLP_USERDATA));
        if (window != nullptr && window->mInput != nullptr) {
            SysKeyEvent(window, wParam, lParam, message == WM_SYSKEYDOWN);
        }
    } break;
    case WM_KEYDOWN:
    case WM_KEYUP: {
        auto window = reinterpret_cast<WindowWin32*>(GetWindowLongPtr(hWnd, GWLP_USERDATA));
        if (window != nullptr && window->mInput != nullptr) {
            if (wParam <= VK_CONTROL) UpdateModifiers(window);
            window->mInput->GetMutator().ReceiveKeyEvent((int)wParam, message == WM_KEYDOWN);
        }
    } return 0;
    case WM_CHAR: {
        auto window = reinterpret_cast<WindowWin32*>(GetWindowLongPtr(hWnd, GWLP_USERDATA));
        if (window != nullptr && window->mInput != nullptr) {
            window->mInput->GetMutator().ReceiveCharEvent((wchar_t)wParam);
        }
    } return 0;
    case WM_DESTROY:
        PostQuitMessage(0);
        return 0;
    }
    return DefWindowProc(hWnd, message, wParam, lParam);
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
