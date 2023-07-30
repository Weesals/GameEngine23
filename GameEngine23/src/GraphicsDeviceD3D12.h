#pragma once

#include "GraphicsDeviceBase.h"

#define NOMINMAX
#include <d3d12.h>
#include <dxgi1_6.h>

#include "WindowWin32.h"
#include "D3DGraphicsDevice.h"
#include "D3DShader.h"
#include "D3DConstantBufferCache.h"

#include <wrl/client.h>
using Microsoft::WRL::ComPtr;

class D3DResourceCache {
    std::string StrVSEntryPoint = "VSMain";
    std::string StrPSEntryPoint = "PSMain";

public:
    // The GPU data for a mesh
    struct D3DMesh
    {
        std::vector<D3D12_INPUT_ELEMENT_DESC> mVertElements;
        int mRevision;
        ComPtr<ID3D12Resource> mVertexBuffer;
        D3D12_VERTEX_BUFFER_VIEW mVertexBufferView;
        ComPtr<ID3D12Resource> mIndexBuffer;
        D3D12_INDEX_BUFFER_VIEW mIndexBufferView;
    };
    struct D3DTexture
    {
        ComPtr<ID3D12Resource> mTexture;
        int mSRVOffset;
        int mRevision;
    };
    // The GPU data for a set of shaders, rendering state, and vertex attributes
    struct D3DPipelineState
    {
        ComPtr<ID3D12PipelineState> mPipelineState;
        // NOTE: Is unsafe if D3DShader is unloaded;
        // Should not be possible but may change in the future
        // TODO: Address this
        std::vector<D3DShader::ConstantBuffer*> mConstantBuffers;
        std::vector<D3DShader::ResourceBinding*> mResourceBindings;
    };

    // If no texture is specified, use this
    std::shared_ptr<Texture> mDefaultTexture;

private:
    D3DGraphicsDevice& mD3D12;

    // Storage for the GPU resources of each application type
    // TODO: Register for destruction of the application type
    // and clean up GPU resources
    std::unordered_map<const Mesh*, std::unique_ptr<D3DMesh>> meshMapping;
    std::unordered_map<const Texture*, std::unique_ptr<D3DTexture>> textureMapping;
    std::unordered_map<ShaderKey, std::unique_ptr<D3DShader>> shaderMapping;
    std::unordered_map<size_t, std::unique_ptr<D3DPipelineState>> pipelineMapping;
    D3DConstantBufferCache mConstantBufferCache;
    PerFrameItemStoreNoHash<ComPtr<ID3D12Resource>> mUploadBufferCache;
    int mCBOffset;

    D3DShader* RequireShader(const Shader& shader, const std::string& entrypoint);
    D3DPipelineState* RequirePipelineState(const Shader& vs, const Shader& ps, size_t hash);
    int GenerateElementDesc(const Mesh& mesh, std::vector<D3D12_INPUT_ELEMENT_DESC>& vertDesc);
    void CopyVertexData(const Mesh& mesh, void* buffer, int stride);
    ID3D12Resource* AllocateUploadBuffer(int size);
public:
    D3DResourceCache(D3DGraphicsDevice& d3d12);
    void SetResourceLockIds(UINT64 lockFrameId, UINT64 writeFrameId);

    D3DMesh* RequireD3DMesh(const Mesh& mesh);
    D3DTexture* RequireD3DTexture(const Texture& mesh);
    D3DPipelineState* RequirePipelineState(const Material& material, std::span<D3D12_INPUT_ELEMENT_DESC> vertElements);
    D3DConstantBuffer* RequireConstantBuffer(const D3DShader::ConstantBuffer& cb, const Material& material);

    void UpdateMeshData(D3DMesh* d3dMesh, const Mesh& mesh, ID3D12GraphicsCommandList* cmdList);
    void UpdateTextureData(D3DTexture* d3dTex, const Texture& tex, ID3D12GraphicsCommandList* cmdList);
};

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
    // And render target
    ComPtr<ID3D12Resource> mRenderTargets[FrameCount];
    // And they can share a depth target
    ComPtr<ID3D12Resource> mDepthTarget;

public:
    GraphicsDeviceD3D12(std::shared_ptr<WindowWin32>& window);
    ~GraphicsDeviceD3D12() override;

    D3DGraphicsDevice& GetDevice() { return mDevice; }
    ID3D12Device* GetD3DDevice() const { return mDevice.GetD3DDevice(); }
    ID3D12RootSignature* GetRootSignature() const { return mDevice.GetRootSignature(); }
    ID3D12DescriptorHeap* GetRTVHeap() const { return mDevice.GetRTVHeap(); }
    ID3D12DescriptorHeap* GetDSVHeap() const { return mDevice.GetDSVHeap(); }
    ID3D12DescriptorHeap* GetSRVHeap() const { return mDevice.GetSRVHeap(); }
    int GetDescriptorHandleSize() const { return mDevice.GetDescriptorHandleSize(); }
    IDXGISwapChain3* GetSwapChain() const { return mDevice.GetSwapChain(); }

    ID3D12CommandAllocator* GetCmdAllocator() const { return mCmdAllocator[mBackBufferIndex].Get(); }
    ID3D12Resource* GetBackBuffer() const { return mRenderTargets[mBackBufferIndex].Get(); }

    D3DResourceCache& GetResourceCache() { return mCache; }

    int GetBackBufferIndex() const { return mBackBufferIndex; }
    Vector2 GetClientSize() const { return mDevice.GetClientSize(); }

    CommandBuffer CreateCommandBuffer() override;
    void Present() override;
    void WaitForFrame();
    void WaitForGPU();

};
