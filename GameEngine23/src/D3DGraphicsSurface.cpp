#include "D3DGraphicsSurface.h"

#include <d3dx12.h>

extern void* SimpleProfilerMarker(const char* name);
extern void SimpleProfilerMarkerEnd(void* zone);

D3DGraphicsSurface::D3DGraphicsSurface(D3DGraphicsDevice& device, D3DResourceCache& cache, HWND hWnd)
    : mDevice(device)
    , mCache(cache)
{
    // Check the window for how large the backbuffer should be
    RECT rect;
    GetClientRect(hWnd, &rect);
    mResolution = Int2(rect.right - rect.left, rect.bottom - rect.top);
    mRenderTarget = std::make_shared<RenderTarget2D>(std::wstring_view(L"BackBuffer"));
    mRenderTarget->SetFormat(BufferFormat::FORMAT_R8G8B8A8_UNORM_SRGB);

    // Create the swap chain
    DXGI_SWAP_CHAIN_DESC1 swapChainDesc = {};
    swapChainDesc.Width = mResolution.x;
    swapChainDesc.Height = mResolution.y;
    swapChainDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    swapChainDesc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    swapChainDesc.BufferCount = FrameCount;
    swapChainDesc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;
    swapChainDesc.SampleDesc = DefaultSampleDesc();
    //swapChainDesc.Flags = DXGI_SWAP_CHAIN_FLAG_ALLOW_TEARING;

    auto* swapChainMarker = SimpleProfilerMarker("Create SwapChain");
    ComPtr<IDXGISwapChain1> swapChain;
    auto* d3dFactory = device.GetFactory();
    auto* cmdQueue = device.GetCmdQueue();
    ThrowIfFailed(d3dFactory->CreateSwapChainForHwnd(cmdQueue, hWnd, &swapChainDesc, nullptr, nullptr, &swapChain));
    ThrowIfFailed(swapChain.As(&mSwapChain));
    mSwapChain->SetColorSpace1(DXGI_COLOR_SPACE_RGB_FULL_G22_NONE_P709);
    SimpleProfilerMarkerEnd(swapChainMarker);

    // Create fence for frame synchronisation
    mBackBufferIndex = mSwapChain->GetCurrentBackBufferIndex();

    // This grabs references for the surface frame bufers
    SetResolution(GetResolution());
}
D3DGraphicsSurface::~D3DGraphicsSurface() {
    WaitForGPU();
}
void D3DGraphicsSurface::SetResolution(Int2 resolution) {
    auto* mD3DDevice = mDevice.GetD3DDevice();
    if (mResolution != resolution) {
        WaitForGPU();
        for (UINT n = 0; n < FrameCount; n++) {
            mCache.InvalidateBufferSRV(mFrameBuffers[n]);
            mFrameBuffers[n].mBuffer.Reset();
            // Need to reset the allocator too
            mCache.ClearAllocator(mFrameBuffers[n].mAllocatorHandle);
        }
        mResolution = resolution;
        mRenderTarget->SetResolution(resolution);
        OutputDebugStringA("Resizing buffers\n");
        ResizeSwapBuffers();
        mBackBufferIndex = mSwapChain->GetCurrentBackBufferIndex();
    }
    auto* frameBufferMarker = SimpleProfilerMarker("Get Frame Buffers");
    // Create a RTV for each frame.
    for (UINT n = 0; n < FrameCount; n++) {
        auto& frameBuffer = mFrameBuffers[n];
        frameBuffer.mDesc = {
            .mWidth = (uint16_t)mResolution.x, .mHeight = (uint16_t)mResolution.y,
            .mMips = 1, .mSlices = 1
        };
        frameBuffer.mFormat = DXGI_FORMAT_R8G8B8A8_UNORM;
        if (frameBuffer.mBuffer == nullptr) {
            ThrowIfFailed(mSwapChain->GetBuffer(n, IID_PPV_ARGS(&frameBuffer.mBuffer)));
            wchar_t name[] = L"Frame Buffer 0";
            name[_countof(name) - 2] = '0' + n;
            frameBuffer.mBuffer->SetName(name);
        }
    }
    SimpleProfilerMarkerEnd(frameBufferMarker);
}
void D3DGraphicsSurface::ResizeSwapBuffers() {
    mCache.PurgeSRVs(0);
    //mSwapChain->Present(1, DXGI_PRESENT_RESTART);
    auto hr = mSwapChain->ResizeBuffers(0, (UINT)mResolution.x, (UINT)mResolution.y, DXGI_FORMAT_UNKNOWN, 0);
    ThrowIfFailed(hr);
}

const std::shared_ptr<RenderTarget2D>& D3DGraphicsSurface::GetBackBuffer() const { return mRenderTarget; }
bool D3DGraphicsSurface::GetIsOccluded() const { return mIsOccluded; }
void D3DGraphicsSurface::RegisterDenyPresent(int delta) { mDenyPresentRef += delta; }

// Flip the backbuffer and wait until a frame is available to be rendered
int D3DGraphicsSurface::Present() {
    if (mDenyPresentRef > 0) {
        // Relevant code moved to D3DUtility.cpp
        //static auto VBlankHandle = getVBlankHandle();
        //D3DKMTWaitForVerticalBlankEvent(&VBlankHandle);
    }
    else {
        auto& allocatorHandle = mFrameBuffers[mBackBufferIndex].mAllocatorHandle;
        if (allocatorHandle.mAllocatorId == -1) return -1;
        RECT rects = { 0, 0, 10, 10 };
        DXGI_PRESENT_PARAMETERS params = { };
        params.DirtyRectsCount = mDenyPresentRef > 0 ? 1 : 0;
        params.pDirtyRects = &rects;
        params.pScrollOffset = nullptr;
        params.pScrollRect = nullptr;
        //mDenyPresentRef > 0 ? DXGI_PRESENT_DO_NOT_SEQUENCE | DXGI_PRESENT_TEST : 
        //DXGI_PRESENT_ALLOW_TEARING
        auto hr = mSwapChain->Present(1, mDenyPresentRef > 0 ? DXGI_PRESENT_DO_NOT_SEQUENCE : 0);
        mCache.PushAllocator(allocatorHandle);

        if ((hr == DXGI_STATUS_OCCLUDED) != mIsOccluded) {
            mIsOccluded = hr == DXGI_STATUS_OCCLUDED;
            mDenyPresentRef += mIsOccluded ? 1 : -1;
        }
        if (hr == DXGI_ERROR_DEVICE_REMOVED || hr == DXGI_ERROR_DEVICE_RESET) {
            mDevice.CheckDeviceState();
            OutputDebugStringA("Failed to Present()! TODO: Implement\n");
            return -1;

            // Reset all cached resources
            //mCache = D3DResourceCache(mDevice);
            // Reset the entire d3d device
            //mDevice = D3DGraphicsDevice(*mWindow);
        }
        else {
            ThrowIfFailed(hr);
        }
    }

    // Update the frame index.
    mBackBufferIndex = mSwapChain->GetCurrentBackBufferIndex();
    return 0;
}

// Wait for all GPU operations? Taken from the samples
void D3DGraphicsSurface::WaitForGPU() {
    for (UINT n = 0; n < FrameCount; n++) {
        mCache.AwaitAllocator(mFrameBuffers[n].mAllocatorHandle);
    }
}
