#pragma once

#include "WindowWin32.h"

#include <d3d12.h>
#include <dxgi1_4.h>

#include <wrl/client.h>
using Microsoft::WRL::ComPtr;

// This wraps and alows access to raw D3D types
// It should never be used directly by the client application
class D3DGraphicsDevice
{
    // This renderer supports 2 backbuffers
    static const int FrameCount = 2;

    ComPtr<ID3D12Device> mD3DDevice;
    ComPtr<IDXGISwapChain3> mSwapChain;
    ComPtr<ID3D12CommandQueue> mCmdQueue;

    ComPtr<ID3D12DescriptorHeap> mRTVHeap;
    ComPtr<ID3D12DescriptorHeap> mDSVHeap;
    ComPtr<ID3D12DescriptorHeap> mSRVHeap;
    ComPtr<ID3D12DescriptorHeap> mSamplerHeap;

    int mDescriptorHandleSizeRTV;
    int mDescriptorHandleSizeSRV;
    // Size of the client rect of the window
    std::pair<int, int> mClientSize;

public:
    D3DGraphicsDevice(const WindowWin32& window);
    ~D3DGraphicsDevice();

    ID3D12Device* GetD3DDevice() const { return mD3DDevice.Get(); }
    ID3D12DescriptorHeap* GetRTVHeap() const { return mRTVHeap.Get(); }
    ID3D12DescriptorHeap* GetDSVHeap() const { return mDSVHeap.Get(); }
    ID3D12DescriptorHeap* GetSRVHeap() const { return mSRVHeap.Get(); }
    int GetDescriptorHandleSizeRTV() const { return mDescriptorHandleSizeRTV; }
    int GetDescriptorHandleSizeSRV() const { return mDescriptorHandleSizeSRV; }
    IDXGISwapChain3* GetSwapChain() const { return mSwapChain.Get(); }
    ID3D12CommandQueue* GetCmdQueue() const { return mCmdQueue.Get(); }

    Vector2 GetClientSize() const { return Vector2((float)mClientSize.first, (float)mClientSize.second); }
};

