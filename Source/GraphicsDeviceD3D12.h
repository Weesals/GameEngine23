#pragma once

#include "GraphicsDeviceBase.h"
#include "WindowWin32.h"
#include <d3d12.h>
#include <dxgi1_6.h>
#include <tuple>

#include <wrl/client.h>
using Microsoft::WRL::ComPtr;

class D3DInterop;

// A D3D12 renderer
class GraphicsDeviceD3D12 :
    public GraphicsDeviceBase
{
    // Interop needs to access internals to facilitate
    // issuing draw commands
    friend class D3DInterop;
    
    // This renderer supports 2 backbuffers
    static const int FrameCount = 2;

    ComPtr<IDXGIFactory6> mDXGIFactory;
    ComPtr<ID3D12Device> mD3DDevice;
    ComPtr<IDXGISwapChain3> mSwapChain;
    ComPtr<ID3D12CommandQueue> mCmdQueue;
    ComPtr<ID3D12Resource> mRenderTargets[FrameCount];
    ComPtr<ID3D12Resource> mDepthTarget;

    ComPtr<ID3D12RootSignature> mRootSignature;
    ComPtr<ID3D12DescriptorHeap> mRTVHeap;
    ComPtr<ID3D12DescriptorHeap> mDSVHeap;
    ComPtr<ID3D12DescriptorHeap> mCBVSrvHeap;
    ComPtr<ID3D12DescriptorHeap> mSamplerHeap;

    int mDescriptorHandleSize;
    // Size of the client rect of the window
    std::tuple<int, int> mClientSize;

    // Current frame being rendered (wraps to the number of back buffers)
    int mFrameId;
    // Fence to wait for frames to render
    HANDLE mFenceEvent;
    ComPtr<ID3D12Fence> mFence;
    // Used to track when a frame is complete
    UINT64 mFenceValues[FrameCount];
    // Each frame needs its own allocator
    ComPtr<ID3D12CommandAllocator> mCmdAllocator[FrameCount];

public:
    GraphicsDeviceD3D12(const WindowWin32& window);
    ~GraphicsDeviceD3D12() override;

    ID3D12Device* GetD3DDevice() const { return mD3DDevice.Get(); }
    ID3D12RootSignature* GetRootSignature() const { return mRootSignature.Get(); }
    ID3D12DescriptorHeap* GetCBHeap() const { return mCBVSrvHeap.Get(); }
    int GetDescriptorHandleSize() const { return mDescriptorHandleSize; }

    Vector2 GetClientSize() const { return Vector2((float)std::get<0>(mClientSize), (float)std::get<1>(mClientSize)); }

    CommandBuffer CreateCommandBuffer() override;
    void Present() override;
    void WaitForFrame();
    void WaitForGPU();

};

