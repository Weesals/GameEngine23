#pragma once

#include "GraphicsDeviceBase.h"

#define NOMINMAX
#include <d3d12.h>
#include <dxgi1_6.h>

#include "WindowWin32.h"
#include "D3DGraphicsDevice.h"
#include "D3DShader.h"
#include "D3DConstantBufferCache.h"
#include "GraphicsBuffer.h"

#include <wrl/client.h>
using Microsoft::WRL::ComPtr;

class D3DResourceCache {
public:
    inline static const char* StrVSProfile = "vs_5_0";
    inline static const char* StrPSProfile = "ps_5_0";

    struct D3DBuffer
    {
        ComPtr<ID3D12Resource> mBuffer;
    };
    struct D3DVBView
    {
        D3DBuffer* mBuffer;
        std::vector<D3D12_INPUT_ELEMENT_DESC> mLayout;
        D3D12_VERTEX_BUFFER_VIEW mBufferView;
        int mRevision;
    };
    struct D3DIBView
    {
        D3DBuffer* mBuffer;
        D3D12_INDEX_BUFFER_VIEW mBufferView;
        int mRevision;
    };
    template<class View>
    struct D3DBufferWithView
    {
        ComPtr<ID3D12Resource> mBuffer;
        View mView;
        bool IsValidForSize(int size) { return (int)mView.SizeInBytes >= size && mBuffer != nullptr; }
    };
    struct D3DBufferWithSRV : public D3DBuffer
    {
        int mSRVOffset;
        int mRevision;
    };
    // The GPU data for a mesh
    struct D3DMesh
    {
        std::vector<const BufferLayout*> mBindingLayout;
        std::vector<D3D12_INPUT_ELEMENT_DESC> mVertElements;
        std::vector<D3D12_VERTEX_BUFFER_VIEW> mVertexViews;
        D3D12_INDEX_BUFFER_VIEW mIndexView;
        int mRevision;
    };
    struct ResourceBindingCache
    {
        int mRefCount;
        RangeInt mSlots;
    };
    struct ResourceSets
    {
        SparseArray<void*> mBindingSlots;
        SparseArray<ResourceBindingCache> mBindingCache;
        int Require(std::vector<void*>& bindings)
        {
            int bindingId = -1;
            for (auto it = mBindingCache.begin(); it != mBindingCache.end(); ++it)
            {
                if (it->mSlots.length != bindings.size()) continue;
                if (std::equal(bindings.begin(), bindings.end(), &mBindingSlots[it->mSlots.start])) continue;
                bindingId = it.GetIndex();
                break;
            }
            if (bindingId == -1)
            {
                bindingId = mBindingCache.Add(
                    D3DResourceCache::ResourceBindingCache{
                        .mRefCount = 0,
                        .mSlots = mBindingSlots.AddRange(bindings),
                    }
                );
            }
            return bindingId;
        }
        void Remove(int id)
        {
            mBindingSlots.Return(mBindingCache[id].mSlots);
            mBindingCache.Return(id);
        }
    };
    // Each PSO must fit within one of these
    // TODO: Make them more broad and select the smallest appropriate one
    struct D3DRootSignature
    {
        ComPtr<ID3D12RootSignature> mRootSignature;
        int mNumConstantBuffers;
        int mNumResources;
        int GetNumBindings() const { return mNumConstantBuffers + mNumResources; }
    };
    // The GPU data for a set of shaders, rendering state, and vertex attributes
    struct D3DPipelineState
    {
        D3DRootSignature* mRootSignature;
        ComPtr<ID3D12PipelineState> mPipelineState;
        // NOTE: Is unsafe if D3DShader is unloaded;
        // Should not be possible but may change in the future
        // TODO: Address this
        std::vector<const ShaderBase::ConstantBuffer*> mConstantBuffers;
        std::vector<const ShaderBase::ResourceBinding*> mResourceBindings;
        std::vector<D3D12_INPUT_ELEMENT_DESC> mInputElements;

        size_t mHash;
        ResourceSets mCBBindings;
        ResourceSets mRSBindings;
        std::unique_ptr<PipelineLayout> mLayout;
    };
    struct D3DBinding {
        ComPtr<ID3D12Resource> mBuffer;
        int mSize = -1;
        int mRevision = 0;
        BufferLayout::Usage mUsage;
    };

    // If no texture is specified, use this
    std::shared_ptr<Texture> mDefaultTexture;

private:
    D3DGraphicsDevice& mD3D12;

    // Storage for the GPU resources of each application type
    // TODO: Register for destruction of the application type
    // and clean up GPU resources
    D3DRootSignature mRootSignature;
    std::unordered_map<const Mesh*, std::unique_ptr<D3DMesh>> meshMapping;
    std::unordered_map<const Texture*, std::unique_ptr<D3DBufferWithSRV>> textureMapping;
    std::unordered_map<ShaderKey, std::unique_ptr<D3DShader>> shaderMapping;
    std::unordered_map<size_t, std::unique_ptr<D3DPipelineState>> pipelineMapping;
    std::map<size_t, std::unique_ptr<D3DBinding>> mBindings;
    D3DConstantBufferCache mConstantBufferCache;
    PerFrameItemStoreNoHash<ComPtr<ID3D12Resource>> mUploadBufferCache;
    int mCBOffset;

public:
    D3DResourceCache(D3DGraphicsDevice& d3d12);
    void SetResourceLockIds(UINT64 lockFrameId, UINT64 writeFrameId);
    ID3D12Resource* AllocateUploadBuffer(int size);
    void CreateBuffer(ComPtr<ID3D12Resource>& buffer, int size);
    void CopyBufferData(ID3D12GraphicsCommandList* cmdList, GraphicsBufferBase* buffer, const std::span<RangeInt>& ranges);

    void ComputeElementLayout(std::span<const BufferLayout*> bindings,
        std::vector<D3D12_INPUT_ELEMENT_DESC>& inputElements);
    void ComputeElementData(std::span<const BufferLayout*> bindings,
        ID3D12GraphicsCommandList* cmdList,
        std::vector<D3D12_VERTEX_BUFFER_VIEW>& inputViews,
        D3D12_INDEX_BUFFER_VIEW& indexView, int& indexCount);

    D3DMesh* RequireD3DMesh(const Mesh& mesh);
    D3DBufferWithSRV* RequireD3DBuffer(const Texture& mesh);
    D3DShader* RequireShader(const Shader& shader, const std::string& profile);
    D3DPipelineState* GetOrCreatePipelineState(const Shader& vs, const Shader& ps, size_t hash);
    D3DPipelineState* RequirePipelineState(const Material& material, std::span<const BufferLayout*> bindings);
    D3DConstantBuffer* RequireConstantBuffer(const ShaderBase::ConstantBuffer& cb, const Material& material);
    D3DConstantBuffer* RequireConstantBuffer(std::span<const uint8_t> data);

    void UpdateTextureData(D3DBufferWithSRV* d3dTex, const Texture& tex, ID3D12GraphicsCommandList* cmdList);
    void RequireD3DBuffer(D3DBufferWithSRV* d3dBuf, const GraphicsBufferBase& buffer, ID3D12GraphicsCommandList* cmdList);
    void UpdateBufferData(D3DBufferWithSRV* d3dBuf, const GraphicsBufferBase& buffer, ID3D12GraphicsCommandList* cmdList);
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
    GraphicsDeviceD3D12(const std::shared_ptr<WindowWin32>& window);
    ~GraphicsDeviceD3D12() override;

    void CheckDeviceState() const;

    D3DGraphicsDevice& GetDevice() { return mDevice; }
    ID3D12Device* GetD3DDevice() const { return mDevice.GetD3DDevice(); }
    ID3D12DescriptorHeap* GetRTVHeap() const { return mDevice.GetRTVHeap(); }
    ID3D12DescriptorHeap* GetDSVHeap() const { return mDevice.GetDSVHeap(); }
    ID3D12DescriptorHeap* GetSRVHeap() const { return mDevice.GetSRVHeap(); }
    int GetDescriptorHandleSizeRTV() const { return mDevice.GetDescriptorHandleSizeRTV(); }
    int GetDescriptorHandleSizeSRV() const { return mDevice.GetDescriptorHandleSizeSRV(); }
    IDXGISwapChain3* GetSwapChain() const { return mDevice.GetSwapChain(); }

    ID3D12CommandAllocator* GetCmdAllocator() const { return mCmdAllocator[mBackBufferIndex].Get(); }
    ID3D12Resource* GetBackBuffer() const { return mRenderTargets[mBackBufferIndex].Get(); }

    D3DResourceCache& GetResourceCache() { return mCache; }

    int GetBackBufferIndex() const { return mBackBufferIndex; }
    Vector2 GetClientSize() const { return mDevice.GetClientSize(); }

    CommandBuffer CreateCommandBuffer() override;
    const PipelineLayout* RequirePipeline(std::span<const BufferLayout*> bindings, const Material* material) override;
    ShaderBase::ShaderReflection* RequireReflection(Shader& shader) override;
    void Present() override;
    void WaitForFrame();
    void WaitForGPU();

};

