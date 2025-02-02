#pragma once

#include <unordered_map>
#include <algorithm>
#include <memory>
#include <deque>
#include <map>
#include <atomic>
#include <mutex>

#include "D3DGraphicsDevice.h"
#include "D3DShader.h"
#include "GraphicsUtility.h"
#include "D3DUtility.h"
#include "Material.h"

// Stores a cache of Constant Buffers that have been generated
// so that they can be efficiently reused where appropriate
struct D3DConstantBufferPooled {
    ComPtr<ID3D12Resource> mConstantBuffer;
    int mRevision;
};
struct D3DConstantBuffer {
    int mConstantBufferIndex;
    int mConstantBufferRevision;
    int mOffset;
};

struct D3DAllocatorHandle {
    int mAllocatorId;
    UINT64 mFenceValue;
    D3DAllocatorHandle(int allocId = -1, UINT64 fenceValue = 0) : mAllocatorId(allocId), mFenceValue(fenceValue) { }
};

struct D3DCommandContext {
    ID3D12GraphicsCommandList* mCmdList;
    D3D::BarrierStateManager* mBarrierStateManager;
    LockMask mLockBits;

    operator ID3D12GraphicsCommandList* () const { return mCmdList; }
    ID3D12GraphicsCommandList* operator -> () const { return mCmdList; }
};

class D3DResourceCache {
public:
    struct D3DBuffer {
        ComPtr<ID3D12Resource> mBuffer;
        int mRevision = -1;
        int mSRVOffset = -1;
    };
    struct D3DTexture : public D3DBuffer {
        DXGI_FORMAT mFormat;
        D3D::BarrierHandle mBarrierHandle = D3D::BarrierHandle::Invalid;
        D3DTexture() : mFormat(DXGI_FORMAT_UNKNOWN) { }
    };
    struct D3DRenderSurface : public D3DTexture {
        D3D::TextureDescription mDesc;
        Delegate<>::Reference mOnDispose;
    };
    struct D3DRenderSurfaceView {
        const D3DRenderSurface* mSurface;
        int mMip, mSlice;
        D3DRenderSurfaceView(const D3DRenderSurfaceView& other) = default;
        D3DRenderSurfaceView(const D3DRenderSurface* surface = nullptr, int mip = 0, int slice = 0)
            : mSurface(surface), mMip(mip), mSlice(slice) { }
        bool operator == (const D3DRenderSurfaceView& other) const = default;
        D3DRenderSurfaceView& operator = (const D3DRenderSurface* surface) { return *this = D3DRenderSurfaceView(surface); }
        const D3DRenderSurface* operator -> () const { return mSurface; }
        int GetSubresource() const { return mSurface->mDesc.GetSubresource(mMip, mSlice); }
    };
    // Each PSO must fit within one of these
    // TODO: Make them more broad and select the smallest appropriate one
    struct D3DRootSignature {
        ComPtr<ID3D12RootSignature> mRootSignature;
        int mNumConstantBuffers;
        int mSRVCount;
        int mUAVCount;
        int mNumResources;
        int GetNumBindings() const { return mNumConstantBuffers + mNumResources; }
    };
    // The GPU data for a set of shaders, rendering state, and vertex attributes
    struct D3DPipelineState {
        size_t mHash = 0;
        D3DRootSignature* mRootSignature;
        ComPtr<ID3D12PipelineState> mPipelineState;
        std::unique_ptr<PipelineLayout> mLayout;
        std::vector<D3D12_INPUT_ELEMENT_DESC> mInputElements;
        int mType = 0;
        MaterialState mMaterialState;
        virtual ~D3DPipelineState() {}
    };
    struct D3DPipelineRaytrace : D3DPipelineState {
        ComPtr<ID3D12Resource> mShaderIDs;
        ComPtr<ID3D12StateObject> mRaytracePSO;
    };
    struct D3DBinding : public D3DBuffer {
        D3D12_GPU_VIRTUAL_ADDRESS mGPUMemory;
        int mSize = -1;
        int mStride;
        int mCount;     // -1 for Append/Consume (count prefixed within buffer)
        D3D12_RESOURCE_STATES mState;
    };

    struct CommandAllocator {
        int mId;
        ComPtr<ID3D12CommandAllocator> mCmdAllocator;
        // Fence to wait for frames to render
        HANDLE mFenceEvent;
        ComPtr<ID3D12Fence> mFence;
        UINT64 mFenceValue;
        UINT64 mLockFrame;
        D3DAllocatorHandle CreateWaitHandle() { return D3DAllocatorHandle(mId, mFenceValue); }
        bool HasLockedFrames() const { return mFenceValue != mLockFrame; }
        UINT64 GetHeadFrame() const { return mFenceValue; }
        UINT64 GetLockFrame() const { return mFence->GetCompletedValue(); }
        bool ConsumeFrame(UINT64 untilFrame) {
            UINT64 oldValue = mLockFrame;
            return oldValue != untilFrame && std::atomic_ref<UINT64>(mLockFrame)
                .compare_exchange_weak(oldValue, untilFrame) && mFenceValue == untilFrame;
        }
    };

private:
    struct ShaderResourceView {
        ID3D12Resource* mResource = nullptr;
        int mSRVOffset = -1;
        D3D12_SHADER_RESOURCE_VIEW_DESC mLastUse;
    };
    struct RenderTargetView {
        ID3D12Resource* mResource = nullptr;
        int mRTVOffset = -1;
    };
    struct D3DReadback {
        ComPtr<ID3D12Resource> mResource;
    };
    template<class K, class T>
    class ResourceMap {
    public:
        std::mutex mMutex;
        std::unordered_map<K, std::unique_ptr<T>> mMap;
        template<class TValue>
        TValue* GetOrCreate(const K key, bool& wasCreated) {
            wasCreated = false;
            std::scoped_lock lock(mMutex);
            auto i = mMap.find(key);
            if (i != mMap.end()) return dynamic_cast<TValue*>(i->second.get());
            auto* newItem = new TValue();
            mMap.insert(std::make_pair(key, newItem));
            wasCreated = true;
            return newItem;
        }
        T* GetOrCreate(const K key, bool& wasCreated) {
            return GetOrCreate<T>(key, wasCreated);
        }
        T* GetOrCreate(const K key) {
            bool wasCreated;
            return GetOrCreate(key, wasCreated);
        }
        void Delete(const K key) {
            std::scoped_lock lock(mMutex);
            mMap.erase(key);
        }
    };

    D3DGraphicsDevice& mD3D12;

    D3DRootSignature mRootSignature;
    D3DRootSignature mComputeRootSignature;
    D3DRootSignature mRaytraceRootSignature;
    ResourceMap<size_t, D3DPipelineState> pipelineMapping;
    ResourceMap<const Texture*, D3DTexture> textureMapping;
    ResourceMap<const RenderTarget2D*, D3DRenderSurface> rtMapping;
    std::unordered_map<size_t, std::unique_ptr<D3DBinding>> mBindings;
    PerFrameItemStoreNoHash<D3DConstantBufferPooled> mConstantBufferPool;
    PerFrameItemStore<D3DConstantBuffer> mConstantBufferCache;
    PerFrameItemStore<ShaderResourceView> mResourceViewCache;
    PerFrameItemStore<RenderTargetView> mTargetViewCache;
    PerFrameItemStoreNoHash<D3DReadback> mReadbackBufferCache;
    PerFrameItemStoreNoHash<ComPtr<ID3D12Resource>> mUploadBufferCache;
    PerFrameItemStoreNoHash<ComPtr<ID3D12Resource>> mDelayedRelease;

    std::vector<std::shared_ptr<CommandAllocator>> mCommandAllocators;

    std::mutex mResourceMutex;

    // If no texture is specified, use this
    std::shared_ptr<Texture> mDefaultTexture;
    std::atomic<int> mRTOffset;
    std::atomic<int> mDSOffset;
    std::atomic<int> mCBOffset;

    // Used for generating unique barrier ids
    std::atomic<int> mLastBarrierId = 0;

public:
    RenderStatistics& mStatistics;
    ComPtr<ID3D12CommandSignature> mIndirectSig;

    D3DResourceCache(D3DGraphicsDevice& d3d12, RenderStatistics& statistics);
    void PushAllocator(D3DAllocatorHandle& handle);
    int AwaitAllocator(D3DAllocatorHandle handle);
    void ClearAllocator(D3DAllocatorHandle handle);
    D3DAllocatorHandle GetFirstBusyAllocator();
    CommandAllocator* RequireAllocator();
    LockMask CheckInflightFrames();
    void UnlockFrame(size_t frameHash);
    ID3D12Resource* AllocateUploadBuffer(size_t size, LockMask lockBits);
    ID3D12Resource* AllocateUploadBuffer(size_t size, LockMask lockBits, int& itemIndex);
    ID3D12Resource* AllocateReadbackBuffer(size_t size, LockMask lockBits);
    bool RequireBuffer(const BufferLayout& binding, D3DBinding& d3dBin, LockMask lockBits);
    D3DResourceCache::D3DBinding* GetBinding(uint64_t bindingIdentifier);
    D3DResourceCache::D3DBinding& RequireBinding(const BufferLayout& buffer);
    void UpdateBufferData(D3DCommandContext& cmdList, const BufferLayout& buffer, std::span<const RangeInt> ranges);
    void CopyBufferData(D3DCommandContext& cmdList, const BufferLayout& source, const BufferLayout& dest, int srcOffset, int dstOffset, int length);

    ID3D12Resource* CreateReadback(D3DCommandContext& cmdList, const D3DRenderSurface& surface);
    D3DReadback* GetReadback(ID3D12Resource* resource, LockMask& outLockHandle);
    int GetReadbackState(ID3D12Resource* readback);
    int CopyAndDisposeReadback(ID3D12Resource* resource, std::span<uint8_t> dest);

    void ComputeElementLayout(std::span<const BufferLayout*> bindings,
        std::vector<D3D12_INPUT_ELEMENT_DESC>& inputElements);
    void CopyBufferData(D3DCommandContext& cmdList, const BufferLayout& binding, D3DBinding& d3dBin, int itemSize, int byteOffset, int byteSize);
    void ComputeElementData(std::span<const BufferLayout*> bindings,
        D3DCommandContext& cmdList,
        std::vector<D3D12_VERTEX_BUFFER_VIEW>& inputViews,
        D3D12_INDEX_BUFFER_VIEW& indexView, int& indexCount);

    D3DRenderSurface* RequireD3DRT(const RenderTarget2D* rt);
    void DestroyD3DRT(const RenderTarget2D* rt, LockMask lockBits);
    void PurgeSRVs(LockMask lockBits);
    void SetRenderTargetMapping(const RenderTarget2D* rt, const D3DRenderSurface& surface);
    D3DPipelineState* GetOrCreatePipelineState(size_t hash);
    D3DPipelineState* RequirePipelineState(
        const ShaderStages& shaders,
        const MaterialState& materialState, std::span<const BufferLayout*> bindings,
        std::span<DXGI_FORMAT> frameBufferFormats, DXGI_FORMAT depthBufferFormat
    );
    D3DPipelineState* RequireComputePSO(const CompiledShader& shader);
    D3DPipelineState* RequireRaytracePSO(const CompiledShader& rayGenShader, const CompiledShader& hitShader, const CompiledShader& missShader);
    struct CBBumpAllocator {
        int mBumpConstantBuffer;
        int mBumpConstantConsume;
    };
    D3DConstantBuffer* RequireConstantBuffer(D3DCommandContext& cmdList, std::span<const uint8_t> data, size_t hash, CBBumpAllocator& bumpAllocator);
    ComPtr<ID3D12Resource>& GetConstantBuffer(int index);
    RenderTargetView& RequireTextureRTV(D3DRenderSurfaceView& view, LockMask lockBits);
    int RequireTextureSRV(D3DTexture& texture, LockMask lockBits);
    void InvalidateBufferSRV(D3DBuffer& buffer);
    void ClearBufferSRV(D3DBuffer& buffer, LockMask lockBits);

    D3D12_RESOURCE_DESC GetTextureDesc(const Texture& tex);
    int GetTextureSRV(ID3D12Resource* buffer, DXGI_FORMAT fmt, bool is3D, int arrayCount, LockMask lockBits, int mipB = 0, int mipC = -1);
    int GetBufferSRV(D3DBinding& buffer, int offset, int count, int stride, LockMask lockBits);
    int GetUAV(ID3D12Resource* buffer, DXGI_FORMAT fmt, bool is3D, int arrayCount, LockMask lockBits, int mipB = 0, int mipC = -1);
    int GetBufferUAV(ID3D12Resource* buffer, int arrayCount, int stride, D3D12_BUFFER_UAV_FLAGS flags, LockMask lockBits);
    void RequireBarrierHandle(D3DTexture* d3dTex);
    void UpdateTextureData(D3DTexture* d3dTex, const Texture& tex, D3DCommandContext& cmdList);
    Texture* RequireDefaultTexture();
    D3DTexture* RequireTexture(const Texture* tex, D3DCommandContext& cmdList);
    D3DTexture* RequireCurrentTexture(const Texture* tex, D3DCommandContext& cmdList);

    void RequireState(D3DCommandContext& cmdList, D3DBinding& buffer, const BufferLayout& binding, D3D12_RESOURCE_STATES state);
    void FlushBarriers(D3DCommandContext& cmdList);

    void DelayResourceDispose(const ComPtr<ID3D12Resource>& resource, LockMask lockBits);
};
