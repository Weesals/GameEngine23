#pragma once

#include "GraphicsDeviceBase.h"

#define NOMINMAX
#include <d3d12.h>
#include <dxgi1_6.h>

#include "WindowWin32.h"
#include "D3DGraphicsDevice.h"
#include "D3DShader.h"
#include "D3DResourceCache.h"
#include "GraphicsBuffer.h"

#include <wrl/client.h>
using Microsoft::WRL::ComPtr;


// A D3D12 renderer
class GraphicsDeviceD3D12 :
    public GraphicsDeviceBase
{
    static const int FrameCount = 2;
    std::shared_ptr<WindowWin32> mWindow;

    D3DGraphicsDevice mDevice;
    D3DResourceCache mCache;

    // Current frame being rendered (wraps to the number of back buffers)
    int mBackBufferIndex;
    // Fence to wait for frames to render
    HANDLE mFenceEvent;
    ComPtr<ID3D12Fence> mFence;
    // Used to track when a frame is complete
    UINT64 mFenceValues[FrameCount];

    // Each frame needs its own allocator
    ComPtr<ID3D12CommandAllocator> mCmdAllocator[FrameCount];
    D3DResourceCache::D3DRenderSurface mFrameBuffers[FrameCount];

public:
    GraphicsDeviceD3D12(const std::shared_ptr<WindowWin32>& window);
    ~GraphicsDeviceD3D12() override;

    void CheckDeviceState() const;

    D3DGraphicsDevice& GetDevice() { return mDevice; }
    ID3D12Device* GetD3DDevice() const { return mDevice.GetD3DDevice(); }
    ID3D12DescriptorHeap* GetRTVHeap() const { return mDevice.GetRTVHeap(); }
    ID3D12DescriptorHeap* GetDSVHeap() const { return mDevice.GetDSVHeap(); }
    ID3D12DescriptorHeap* GetSRVHeap() const { return mDevice.GetSRVHeap(); }
    int GetDescriptorHandleSizeRTV() const { return mDevice.GetDescriptorHandleSizeRTV(); }
    int GetDescriptorHandleSizeDSV() const { return mDevice.GetDescriptorHandleSizeDSV(); }
    int GetDescriptorHandleSizeSRV() const { return mDevice.GetDescriptorHandleSizeSRV(); }
    IDXGISwapChain3* GetSwapChain() const { return mDevice.GetSwapChain(); }

    ID3D12CommandAllocator* GetCmdAllocator() const { return mCmdAllocator[mBackBufferIndex].Get(); }
    const D3DResourceCache::D3DRenderSurface& GetBackBuffer() const { return mFrameBuffers[mBackBufferIndex]; }

    D3DResourceCache& GetResourceCache() { return mCache; }

    int GetBackBufferIndex() const { return mBackBufferIndex; }
    Int2 GetResolution() const override { return mDevice.GetResolution(); }
    void SetResolution(Int2 res) override;

    CommandBuffer CreateCommandBuffer() override;
    const PipelineLayout* RequirePipeline(
        const Shader& vertexShader, const Shader& pixelShader,
        const MaterialState& materialState, std::span<const BufferLayout*> bindings,
        const IdentifierWithName& renderPass
    ) override;
    void Present() override;
    void WaitForFrame();
    void WaitForGPU();

};

