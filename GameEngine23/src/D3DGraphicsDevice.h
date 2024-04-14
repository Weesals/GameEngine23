#pragma once

#include "WindowWin32.h"

#include <d3d12.h>
#include <dxgi1_4.h>

#include <wrl/client.h>
using Microsoft::WRL::ComPtr;

void ThrowIfFailed(HRESULT hr);

// This wraps and alows access to raw D3D types
// It should never be used directly by the client application
class D3DGraphicsDevice
{
private:
    ComPtr<ID3D12Device2> mD3DDevice;
    ComPtr<IDXGIFactory4> mD3DFactory;
    ComPtr<ID3D12CommandQueue> mCmdQueue;

    ComPtr<ID3D12DescriptorHeap> mRTVHeap;
    ComPtr<ID3D12DescriptorHeap> mDSVHeap;
    ComPtr<ID3D12DescriptorHeap> mSRVHeap;
    ComPtr<ID3D12DescriptorHeap> mSamplerHeap;

    int mDescriptorHandleSizeRTV;
    int mDescriptorHandleSizeSRV;
    int mDescriptorHandleSizeDSV;

public:
    D3DGraphicsDevice();
    ~D3DGraphicsDevice();

    void CheckDeviceState() const;

    ID3D12Device2* GetD3DDevice() const { return mD3DDevice.Get(); }
    IDXGIFactory4* GetFactory() const { return mD3DFactory.Get(); }
    ID3D12DescriptorHeap* GetRTVHeap() const { return mRTVHeap.Get(); }
    ID3D12DescriptorHeap* GetDSVHeap() const { return mDSVHeap.Get(); }
    ID3D12DescriptorHeap* GetSRVHeap() const { return mSRVHeap.Get(); }
    int GetDescriptorHandleSizeRTV() const { return mDescriptorHandleSizeRTV; }
    int GetDescriptorHandleSizeDSV() const { return mDescriptorHandleSizeDSV; }
    int GetDescriptorHandleSizeSRV() const { return mDescriptorHandleSizeSRV; }
    ID3D12CommandQueue* GetCmdQueue() const { return mCmdQueue.Get(); }
};
