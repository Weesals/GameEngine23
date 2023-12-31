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
    std::shared_ptr<WindowWin32> mWindow;

    D3DGraphicsDevice mDevice;
    D3DGraphicsSurface mPrimarySurface;
    D3DResourceCache mCache;

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
    IDXGISwapChain3* GetSwapChain() const { return mPrimarySurface.GetSwapChain(); }

    D3DResourceCache& GetResourceCache() { return mCache; }

    Int2 GetResolution() const override { return mPrimarySurface.GetResolution(); }
    void SetResolution(Int2 res) override;

    CommandBuffer CreateCommandBuffer() override;
    const PipelineLayout* RequirePipeline(
        const Shader& vertexShader, const Shader& pixelShader,
        const MaterialState& materialState, std::span<const BufferLayout*> bindings,
        std::span<const MacroValue> macros, const IdentifierWithName& renderPass
    ) override;
    void Present() override;
    void WaitForGPU();

};

