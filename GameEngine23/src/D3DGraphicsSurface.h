#pragma once

#include <algorithm>
#include <memory>
#include <atomic>
#include <mutex>

#include "D3DGraphicsDevice.h"
#include "D3DShader.h"
#include "GraphicsUtility.h"
#include "D3DUtility.h"
#include "Material.h"

#include "D3DResourceCache.h"

class D3DGraphicsSurface : public GraphicsSurface {
    // This renderer supports 2 backbuffers
    static const int FrameCount = 2;

    struct BackBuffer : D3DResourceCache::D3DRenderSurface {
        //ComPtr<ID3D12Resource> mBuffer;
        //std::shared_ptr<RenderTarget2D> mRenderTarget;
        // Used to track when a frame is complete
        D3DAllocatorHandle mAllocatorHandle;
    };

    D3DGraphicsDevice& mDevice;
    D3DResourceCache& mCache;

    // Size of the client rect of the window
    Int2 mResolution;
    std::shared_ptr<RenderTarget2D> mRenderTarget;

    // Each frame needs its own allocator
    //ComPtr<ID3D12CommandAllocator> mCmdAllocator[FrameCount];
    BackBuffer mFrameBuffers[FrameCount];

    // Current frame being rendered (wraps to the number of back buffers)
    int mBackBufferIndex;

    // Fence to wait for frames to render
    HANDLE mFenceEvent;
    ComPtr<ID3D12Fence> mFence;

    int mDenyPresentRef = 0;
    bool mIsOccluded = false;
public:
    ComPtr<IDXGISwapChain3> mSwapChain;

    D3DGraphicsSurface(D3DGraphicsDevice& device, D3DResourceCache& cache, HWND hWnd);
    ~D3DGraphicsSurface();
    IDXGISwapChain3* GetSwapChain() const { return mSwapChain.Get(); }
    Int2 GetResolution() const override { return mResolution; }
    void SetResolution(Int2 res) override;
    void ResizeSwapBuffers();

    //ID3D12CommandAllocator* GetCmdAllocator() const { return mCmdAllocator[mBackBufferIndex].Get(); }
    const BackBuffer& GetFrameBuffer() const { return mFrameBuffers[mBackBufferIndex]; }
    D3DAllocatorHandle& GetFrameWaitHandle() { return mFrameBuffers[mBackBufferIndex].mAllocatorHandle; }
    const std::shared_ptr<RenderTarget2D>& GetBackBuffer() const override;

    int GetBackBufferIndex() const { return mBackBufferIndex; }

    bool GetIsOccluded() const override;
    void RegisterDenyPresent(int delta = 1) override;

    int Present() override;
    void WaitForGPU() override;

};
