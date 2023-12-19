#pragma once

#include <unordered_map>
#include <algorithm>
#include <memory>
#include <deque>
#include <map>

#include "D3DGraphicsDevice.h"
#include "D3DShader.h"
#include "GraphicsUtility.h"
#include "Material.h"

// Stores a cache of Constant Buffers that have been generated
// so that they can be efficiently reused where appropriate
    // The GPU data for a set of shaders, rendering state, and vertex attributes
struct D3DConstantBuffer
{
    ComPtr<ID3D12Resource> mConstantBuffer;
    D3D12_GPU_DESCRIPTOR_HANDLE mConstantBufferHandle;
    int mSize;
    int mDetatchRefCount;
};

class D3DResourceCache {
public:
    inline static const char* StrVSProfile = "vs_5_0";
    inline static const char* StrPSProfile = "ps_5_0";

    struct D3DBuffer
    {
        ComPtr<ID3D12Resource> mBuffer;
        DXGI_FORMAT mFormat;
    };
    struct D3DVBView
    {
        D3DBuffer* mBuffer;
        std::vector<D3D12_INPUT_ELEMENT_DESC> mLayout;
        D3D12_VERTEX_BUFFER_VIEW mBufferView;
        int mRevision = 0;
    };
    struct D3DIBView
    {
        D3DBuffer* mBuffer;
        D3D12_INDEX_BUFFER_VIEW mBufferView;
        int mRevision = 0;
    };
    template<class View>
    struct D3DBufferWithView
    {
        ComPtr<ID3D12Resource> mBuffer;
        View mView;
        bool IsValidForSize(int size) { return (int)mView.SizeInBytes >= size && mBuffer != nullptr; }
    };
    struct D3DBufferWithSRV : public D3DBuffer {
        int mSRVOffset;
        int mRevision = 0;
    };
    struct D3DRenderSurface : public D3DBufferWithSRV {
        struct SubresourceData {
            int mRTVOffset = -1;
            mutable D3D12_RESOURCE_STATES mState = D3D12_RESOURCE_STATE_COMMON;
        };
        SubresourceData mMip0;
        std::vector<SubresourceData> mMipN;
        int mWidth, mHeight;
        SubresourceData& RequireSubResource(int subresource) {
            if (subresource == 0) return mMip0;
            --subresource;
            if (subresource >= mMipN.size()) mMipN.resize(subresource + 1);
            return mMipN[subresource];
        }
        const SubresourceData& RequireSubResource(int subresource) const {
            return const_cast<D3DRenderSurface*>(this)->RequireSubResource(subresource);
        }
        template<class T>
        bool RequireState(T& barriers, D3D12_RESOURCE_STATES state, int subresourceId) const {
            auto& subresource = RequireSubResource(subresourceId);
            if (subresource.mState == state) return false;
            D3D12_RESOURCE_BARRIER barrier;
            barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
            barrier.Flags = D3D12_RESOURCE_BARRIER_FLAG_NONE;
            barrier.Transition.pResource = mBuffer.Get();
            barrier.Transition.StateBefore = subresource.mState;
            barrier.Transition.StateAfter = state;
            barrier.Transition.Subresource = subresourceId;
            barriers.push_back(barrier);
            subresource.mState = state;
            return true;
        }
    };
    struct D3DRenderSurfaceView {
        const D3DRenderSurface* mSurface;
        int mMip, mSlice;
        D3DRenderSurfaceView() : mSurface(nullptr), mMip(0), mSlice(0) { }
        D3DRenderSurfaceView(const D3DRenderSurface* surface) : mSurface(surface), mMip(0), mSlice(0) { }
        D3DRenderSurfaceView(const D3DRenderSurfaceView& other) = default;
        D3DRenderSurfaceView(const D3DRenderSurface* surface, int mip, int slice)
            : mSurface(surface), mMip(mip), mSlice(slice) { }
        bool operator == (const D3DRenderSurfaceView& other) const = default;
        D3DRenderSurfaceView& operator = (const D3DRenderSurface* surface) { return *this = D3DRenderSurfaceView(surface); }
        const D3DRenderSurface* operator -> () const { return mSurface; }
    };
    struct CachedSRV {
        int mType;
        int mSRVOffset;
    };
    // The GPU data for a mesh
    struct D3DMesh
    {
        std::vector<const BufferLayout*> mBindingLayout;
        std::vector<D3D12_INPUT_ELEMENT_DESC> mVertElements;
        std::vector<D3D12_VERTEX_BUFFER_VIEW> mVertexViews;
        D3D12_INDEX_BUFFER_VIEW mIndexView;
        int mRevision = 0;
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

        size_t mHash = 0;
        ResourceSets mCBBindings;
        ResourceSets mRSBindings;
        std::unique_ptr<PipelineLayout> mLayout;
    };
    struct D3DBinding {
        ComPtr<ID3D12Resource> mBuffer;
        D3D12_GPU_VIRTUAL_ADDRESS mGPUMemory;
        int mSize = -1;
        int mRevision = 0;
        BufferLayout::Usage mUsage;
    };

    // If no texture is specified, use this
    std::shared_ptr<Texture> mDefaultTexture;
    int mRTOffset;
    int mDSOffset;
    int mCBOffset;

    std::unordered_map<Int2, std::unique_ptr<D3DRenderSurface>> depthBufferPool;

private:
    D3DGraphicsDevice& mD3D12;

    // Storage for the GPU resources of each application type
    // TODO: Register for destruction of the application type
    // and clean up GPU resources
    D3DRootSignature mRootSignature;
    std::unordered_map<const Mesh*, std::unique_ptr<D3DMesh>> meshMapping;
    std::unordered_map<const RenderTarget2D*, std::unique_ptr<D3DRenderSurface>> rtMapping;
    std::unordered_map<const Texture*, std::unique_ptr<D3DBufferWithSRV>> textureMapping;
    std::unordered_map<ShaderKey, std::unique_ptr<D3DShader>> shaderMapping;
    std::unordered_map<size_t, std::unique_ptr<D3DPipelineState>> pipelineMapping;
    std::map<size_t, std::unique_ptr<D3DBinding>> mBindings;
    PerFrameItemStore<D3DConstantBuffer> mConstantBufferCache;
    PerFrameItemStore<CachedSRV> mResourceViewCache;
    PerFrameItemStoreNoHash<ComPtr<ID3D12Resource>, 2> mUploadBufferCache;
    PerFrameItemStoreNoHash<ComPtr<ID3D12Resource>> mDelayedRelease;
    std::vector<uint8_t> mTempData;

public:
    RenderStatistics& mStatistics;

    D3DResourceCache(D3DGraphicsDevice& d3d12, RenderStatistics& statistics);
    void SetResourceLockIds(UINT64 lockFrameId, UINT64 writeFrameId);
    ID3D12Resource* AllocateUploadBuffer(int size);
    void CreateBuffer(ComPtr<ID3D12Resource>& buffer, int size);
    void UpdateBufferData(ID3D12GraphicsCommandList* cmdList, GraphicsBufferBase* buffer, const std::span<RangeInt>& ranges);

    void ComputeElementLayout(std::span<const BufferLayout*> bindings,
        std::vector<D3D12_INPUT_ELEMENT_DESC>& inputElements);
    void ComputeElementData(std::span<const BufferLayout*> bindings,
        ID3D12GraphicsCommandList* cmdList,
        std::vector<D3D12_VERTEX_BUFFER_VIEW>& inputViews,
        D3D12_INDEX_BUFFER_VIEW& indexView, int& indexCount);

    D3DRenderSurface* RequireD3DRT(const RenderTarget2D* rt);
    D3DMesh* RequireD3DMesh(const Mesh& mesh);
    D3DBufferWithSRV* RequireD3DBuffer(const Texture& mesh);
    D3DShader* RequireShader(const Shader& shader, const std::string& profile, std::span<const MacroValue> macros, const IdentifierWithName& renderPass);
    D3DPipelineState* GetOrCreatePipelineState(const Shader& vs, const Shader& ps, size_t hash);
    D3DPipelineState* RequirePipelineState(
        const Shader& vertexShader, const Shader& pixelShader,
        const MaterialState& materialState, std::span<const BufferLayout*> bindings,
        std::span<const MacroValue> macros, const IdentifierWithName& renderPass,
        std::span<DXGI_FORMAT> frameBufferFormats, DXGI_FORMAT depthBufferFormat
    );
    D3DConstantBuffer* RequireConstantBuffer(const ShaderBase::ConstantBuffer& cb, const Material& material);
    D3DConstantBuffer* RequireConstantBuffer(std::span<const uint8_t> data);

    void UpdateTextureData(D3DBufferWithSRV* d3dTex, const Texture& tex, ID3D12GraphicsCommandList* cmdList);
    void RequireD3DBuffer(D3DBufferWithSRV* d3dBuf, const GraphicsBufferBase& buffer, ID3D12GraphicsCommandList* cmdList);
    void UpdateBufferData(D3DBufferWithSRV* d3dBuf, const GraphicsBufferBase& buffer, ID3D12GraphicsCommandList* cmdList);
    D3DBufferWithSRV* RequireCurrentTexture(const Texture* tex, ID3D12GraphicsCommandList* cmdList);
    D3DBufferWithSRV* RequireCurrentBuffer(const GraphicsBufferBase* tex, ID3D12GraphicsCommandList* cmdList);
};
