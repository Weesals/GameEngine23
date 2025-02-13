#include "WindowWin32.h"

#include <windowsx.h>
#include <sstream>
#include "GraphicsDeviceD3D12.h"

WCHAR szWindowClass[] = L"RTSWINDOW";

WindowWin32::WindowWin32(const std::wstring &name, HWND parent)
{
    hInstance = GetModuleHandle(NULL);

    static bool classRegistered = false;
    if (!classRegistered) {
        SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

        classRegistered = true;
        // Register a class for our window to be a member of
        WNDCLASSW wcex = {};
        wcex.lpfnWndProc = WindowWin32::_WndProc;
        wcex.hInstance = hInstance;
        wcex.lpszClassName = szWindowClass;
        wcex.hCursor = LoadCursor(nullptr, IDC_ARROW);
        //HBRUSH blackBrush = CreateSolidBrush(RGB(32, 32, 32));
        wcex.hbrBackground = (HBRUSH)GetStockObject(DKGRAY_BRUSH);
        RegisterClassW(&wcex);
    }

    // Create a standard overlapped window
    hWnd = CreateWindowExW(0,
        szWindowClass, name.c_str(), parent != 0 ? WS_CHILDWINDOW | WS_VISIBLE : WS_OVERLAPPEDWINDOW,
        CW_USEDEFAULT, CW_USEDEFAULT, CW_USEDEFAULT, CW_USEDEFAULT,
        parent, nullptr, hInstance, nullptr);
    //SendMessage(hWnd, WM_SETREDRAW, false, 0);
    if (!hWnd) {
        DWORD error = GetLastError();
        wchar_t* errorMsg = nullptr;
        FormatMessageW(FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS, NULL, error, MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT), (LPWSTR)&errorMsg, 0, NULL);
        MessageBox(NULL, errorMsg, L"CreateWindowExW Failed", MB_ICONERROR);
        LocalFree(errorMsg);

        OutputDebugStringA("CreateWindow failed with ");
        char buffer[32];
        _itoa_s(error, buffer, _countof(buffer));
        OutputDebugStringA(buffer);
        OutputDebugStringA("\n");
    }

    //SetWindowLong(hWnd, GWL_EXSTYLE, GetWindowLong(hWnd, GWL_EXSTYLE) | WS_EX_LAYERED);
    //SetLayeredWindowAttributes(hWnd, 0, 255, LWA_ALPHA);

    // Focus the window
    //ShowWindow(hWnd, SW_SHOWDEFAULT);
    UpdateWindow(hWnd);

    // Set a pointer to this object so that messages can be forwarded
    SetWindowLongPtr(hWnd, GWLP_USERDATA, (LONG_PTR)this);

    EnableMouseInPointer(TRUE);
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

void WindowWin32::SetVisible(bool visible) {
    auto hwnd = GetHWND();
    ShowWindow(hwnd, visible ? SW_SHOW : SW_HIDE);
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
    DestroyWindow(hWnd);
}

std::shared_ptr<Pointer> WindowWin32::RequireMousePointer()
{
    if (mMousePointer == nullptr && mInput != nullptr) {
        mMousePointer = RequirePointer(-1);
    }
    return mMousePointer;
}
std::shared_ptr<Pointer> WindowWin32::RequirePointer(int id)
{
    auto pointer = mPointersById.find(id);
    if (pointer == mPointersById.end()) {
        if (mInput == nullptr) return nullptr;
        pointer = mPointersById.insert({ id, mInput->AllocatePointer(id) }).first;
    }
    return pointer->second;
}

LRESULT CALLBACK WindowWin32::_WndProc(HWND hWnd, UINT message, WPARAM wParam, LPARAM lParam)
{
    if (message == WM_NCHITTEST) {
        auto window = reinterpret_cast<WindowWin32*>(GetWindowLongPtr(hWnd, GWLP_USERDATA));
        //char buffer[32]; sprintf_s(buffer, "CHTEST %llx %d\n", hWnd, window->mInput != nullptr);
        //OutputDebugStringA(buffer);
        if (window->mInput == nullptr) return HTTRANSPARENT;
        return HTCLIENT;
    }
    /*std::stringstream str;
    str << " >> " << std::hex << " M ";
    switch (message) {
    case WM_PAINT: str << "WM_PAINT"; break;
    case WM_SYSKEYDOWN: str << "WM_SYSKEYDOWN"; break;
    case WM_SYSKEYUP: str << "WM_SYSKEYUP"; break;
    case WM_GETICON: str << "WM_GETICON"; break;
    case WM_DWMNCRENDERINGCHANGED: str << "WM_DWMNCRENDERINGCHANGED"; break;
    case WM_SYSCOMMAND: str << "WM_SYSCOMMAND"; break;
    case WM_SETCURSOR: str << "WM_SETCURSOR"; break;
    case WM_INITMENU: str << "WM_INITMENU"; break;
    case WM_ENTERMENULOOP: str << "WM_ENTERMENULOOP"; break;
    case WM_EXITMENULOOP: str << "WM_EXITMENULOOP"; break;
    case WM_WINDOWPOSCHANGING: str << "WM_WINDOWPOSCHANGING"; break;
    case WM_WINDOWPOSCHANGED: str << "WM_WINDOWPOSCHANGED"; break;
    case WM_NCACTIVATE: str << "WM_NCACTIVATE"; break;
    case WM_MENUSELECT: str << "WM_MENUSELECT"; break;
    case WM_ENTERIDLE: str << "WM_ENTERIDLE"; break;
    case WM_CAPTURECHANGED: str << "WM_CAPTURECHANGED"; break;
    case WM_CLOSE: str << "WM_CLOSE"; break;
    case WM_NCHITTEST: str << "WM_NCHITTEST"; break;
    case WM_IME_SETCONTEXT: str << "WM_IME_SETCONTEXT"; break;
    case WM_IME_NOTIFY: str << "WM_IME_NOTIFY"; break;
    case WM_ACTIVATEAPP: str << "WM_ACTIVATEAPP"; break;
    case WM_ACTIVATE: str << "WM_ACTIVATE"; break;
    case WM_SETFOCUS: str << "WM_SETFOCUS"; break;
    case WM_KILLFOCUS: str << "WM_KILLFOCUS"; break;
    case WM_GETOBJECT: str << "WM_GETOBJECT"; break;
    case WM_MOUSEMOVE: str << "WM_MOUSEMOVE"; break;
    case WM_GETMINMAXINFO: str << "WM_GETMINMAXINFO"; break;
    case WM_LBUTTONDOWN: str << "WM_LBUTTONDOWN"; break;
    case WM_LBUTTONUP: str << "WM_LBUTTONUP"; break;
    case WM_MOUSEWHEEL: str << "WM_MOUSEWHEEL"; break;
    case WM_POINTERUP: str << "WM_POINTERUP"; break;
    case WM_POINTERDOWN: str << "WM_POINTERDOWN"; break;
    case WM_POINTERUPDATE: str << "WM_POINTERUPDATE"; break;
    case WM_POINTERWHEEL: str << "WM_POINTERWHEEL"; break;
    default: str << message; break;
    }
    str << "  " << wParam << " " << lParam << std::endl;
    OutputDebugStringA(str.str().c_str());*/
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
        //std::stringstream str;
        //str << wParam << " S " << state << std::endl;
        //OutputDebugStringA(str.str().c_str());
    };
    static auto UpdatePointerInfo = [](WindowWin32* window, const std::shared_ptr<Pointer>& pointer, POINTER_INFO& pointerInfo) {
        pointer->mDeviceType = pointerInfo.pointerType;
    };
    static auto ReceivePointerMove = [](HWND hWnd, const std::shared_ptr<Pointer>& pointer, POINTER_INFO& pointerInfo) {
        //POINT p = { GET_X_LPARAM(lParam), GET_Y_LPARAM(lParam) };
        POINT p = pointerInfo.ptPixelLocation;
        ScreenToClient(hWnd, &p);
        pointer->ReceiveMoveEvent(Vector2((float)p.x, (float)p.y));
    };
    static auto ReceivePointerState = [&](WindowWin32* window, HWND hWnd, const std::shared_ptr<Pointer>& pointer, POINTER_INFO& pointerInfo, bool state) {
        int buttonMask = 1 << ((pointerInfo.ButtonChangeType - 1) >> 1);
        // Update mouse button state
        ReceivePointerMove(hWnd, pointer, pointerInfo);
        pointer->ReceiveButtonEvent(buttonMask, state);

        UpdatePointerInfo(window, pointer, pointerInfo);

        // Extend input to outside of window
        if (state) SetCapture(hWnd); else ReleaseCapture();
    };

    switch (message) {
    case WM_PAINT: {
        /*PAINTSTRUCT ps;
        HDC hdc = BeginPaint(hWnd, &ps);
        HBRUSH blackBrush = CreateSolidBrush(RGB(0, 0, 0));
        FillRect(hdc, &ps.rcPaint, blackBrush);
        DeleteObject(blackBrush);
        EndPaint(hWnd, &ps);*/
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
    case WM_POINTERDOWN: case WM_POINTERUP: {
        auto window = reinterpret_cast<WindowWin32*>(GetWindowLongPtr(hWnd, GWLP_USERDATA));
        //char buffer[32]; sprintf_s(buffer, "PDOWN %llx %d\n", hWnd, window->mInput != nullptr);
        //OutputDebugStringA(buffer);
        POINTER_INFO pointerInfo;
        if (GetPointerInfo(GET_POINTERID_WPARAM(wParam), &pointerInfo)) {}
        auto pointer = window->RequirePointer(pointerInfo.pointerId);
        if (pointer == nullptr) break;
        ReceivePointerState(window, hWnd, pointer, pointerInfo, message == WM_POINTERDOWN);
        if (message == WM_POINTERUP && pointerInfo.pointerType == PT_TOUCH) {
            window->mPointersById.erase(pointerInfo.pointerId);
        }
    } break;
    case WM_POINTERUPDATE: {
        auto window = reinterpret_cast<WindowWin32*>(GetWindowLongPtr(hWnd, GWLP_USERDATA));
        POINTER_INFO pointerInfo;
        if (GetPointerInfo(GET_POINTERID_WPARAM(wParam), &pointerInfo)) {}
        auto pointer = window->RequirePointer(pointerInfo.pointerId);
        if (pointer == nullptr) break;
        ReceivePointerMove(hWnd, pointer, pointerInfo);
    } break;
    case WM_POINTERWHEEL: {
        auto window = reinterpret_cast<WindowWin32*>(GetWindowLongPtr(hWnd, GWLP_USERDATA));
        POINTER_INFO pointerInfo;
        if (GetPointerInfo(GET_POINTERID_WPARAM(wParam), &pointerInfo)) { }
        auto pointer = window->RequirePointer(pointerInfo.pointerId);
        if (pointer == nullptr) break;
        ReceivePointerMove(hWnd, pointer, pointerInfo);
        pointer->ReceiveMouseScroll(GET_WHEEL_DELTA_WPARAM(wParam));
        UpdatePointerInfo(window, pointer, pointerInfo);
    } break;
    /*case WM_LBUTTONDOWN: {
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
    case WM_MOUSEWHEEL: {
        auto window = reinterpret_cast<WindowWin32*>(GetWindowLongPtr(hWnd, GWLP_USERDATA));
        auto pointer = window->RequireMousePointer();
        if (pointer) {
            LONG_PTR extraInfo = GetMessageExtraInfo();
            bool isTouch = ((extraInfo & SIGNATURE_MASK) == MI_WP_SIGNATURE);
            std::stringstream str;
            str << std::hex << extraInfo << std::endl;
            OutputDebugStringA(str.str().c_str());
            // This is offset - seems to be in screen space
            //pointer->ReceiveMoveEvent(Vector2((float)GET_X_LPARAM(lParam), (float)GET_Y_LPARAM(lParam)));
            pointer->ReceiveMouseScroll(GET_WHEEL_DELTA_WPARAM(wParam));
        }
    } return 0;*/
    // Receive key events
    case WM_SYSKEYDOWN:
    case WM_SYSKEYUP: {
        auto window = reinterpret_cast<WindowWin32*>(GetWindowLongPtr(hWnd, GWLP_USERDATA));
        if (window != nullptr && window->mInput != nullptr) {
            SysKeyEvent(window, wParam, lParam, message == WM_SYSKEYDOWN);
            if (wParam == VK_MENU && message == WM_SYSKEYUP) {
                DefWindowProc(hWnd, WM_LBUTTONDOWN, 1, 2040198);
                DefWindowProc(hWnd, WM_LBUTTONUP, 1, 2040198);
            }
        }
    } break;
    case WM_KEYDOWN:
    case WM_KEYUP: {
        auto window = reinterpret_cast<WindowWin32*>(GetWindowLongPtr(hWnd, GWLP_USERDATA));
        if (window != nullptr && window->mInput != nullptr) {
            if (wParam <= VK_CONTROL) UpdateModifiers(window);
            window->mInput->GetMutator().ReceiveKeyEvent((int)wParam, message == WM_KEYDOWN);
        }
        //std::stringstream str;
        //str << wParam << " M " << message << std::endl;
        //OutputDebugStringA(str.str().c_str());
    } return 0;
    case WM_KILLFOCUS: {
        auto window = reinterpret_cast<WindowWin32*>(GetWindowLongPtr(hWnd, GWLP_USERDATA));
        if (window != nullptr && window->mInput != nullptr) {
            auto mutator = window->mInput->GetMutator();
            for (auto key : window->mInput->GetDownKeys()) {
                mutator.ReceiveKeyEvent(key.KeyId, false);
            }
        }
    } break;
    case WM_CHAR: {
        auto window = reinterpret_cast<WindowWin32*>(GetWindowLongPtr(hWnd, GWLP_USERDATA));
        if (window != nullptr && window->mInput != nullptr) {
            window->mInput->GetMutator().ReceiveCharEvent((wchar_t)wParam);
        }
    } return 0;
    case WM_SYSCOMMAND: {
        if (wParam == SC_KEYMENU && lParam == 0) return 0;
    } break;
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
