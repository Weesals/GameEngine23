#pragma once

#include <unordered_map>
#include <algorithm>
#include <memory>
#include <deque>
#include <map>
#include <sstream>

#include "D3DGraphicsDevice.h"
#include "D3DShader.h"
#include "GraphicsUtility.h"
#include "D3DUtility.h"
#include "Material.h"

class D3DGraphicsSurface;

// Stores a cache of Constant Buffers that have been generated
// so that they can be efficiently reused where appropriate
    // The GPU data for a set of shaders, rendering state, and vertex attributes
struct D3DConstantBuffer {
    ComPtr<ID3D12Resource> mConstantBuffer;
    D3D12_GPU_DESCRIPTOR_HANDLE mConstantBufferHandle;
    int mSize;
    int mDetatchRefCount;
};

class D3DResourceCache {
public:
    inline static const char* StrVSProfile = "vs_6_0";
    inline static const char* StrPSProfile = "ps_6_0";

    struct D3DBuffer {
        ComPtr<ID3D12Resource> mBuffer;
        int mRevision = -1;
        int mSRVOffset = -1;
    };
    struct D3DTexture : public D3DBuffer {
        DXGI_FORMAT mFormat;
    };
    struct D3DRenderSurface : public D3DTexture {
        struct SubresourceData {
            int mRTVOffset = -1;
        };
        SubresourceData mMip0;
        std::vector<SubresourceData> mMipN;
        D3D::TextureDescription mDesc;
        D3D::BarrierHandle mHandle = D3D::BarrierHandle::Invalid;
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
        bool RequireState(T& barriers, D3D::BarrierStateManager& manager, D3D12_RESOURCE_STATES state, int subresourceId = -1) const {
            return manager.SetResourceState(barriers, mBuffer.Get(), mHandle, subresourceId, state, mDesc);
        }
        void UnlockState(D3D::BarrierStateManager& manager, D3D12_RESOURCE_STATES state, int subresourceId = -1) const {
            manager.UnlockResourceState(mHandle, subresourceId, state, mDesc);
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
        int GetSubresource() const { return mSurface->mDesc.GetSubresource(mMip, mSlice); }
    };
    // Each PSO must fit within one of these
    // TODO: Make them more broad and select the smallest appropriate one
    struct D3DRootSignature {
        ComPtr<ID3D12RootSignature> mRootSignature;
        int mNumConstantBuffers;
        int mNumResources;
        int GetNumBindings() const { return mNumConstantBuffers + mNumResources; }
    };
    // The GPU data for a set of shaders, rendering state, and vertex attributes
    struct D3DPipelineState {
        D3DRootSignature* mRootSignature;
        ComPtr<ID3D12PipelineState> mPipelineState;
        // NOTE: Is unsafe if D3DShader is unloaded;
        // Should not be possible but may change in the future
        // TODO: Address this
        std::vector<const ShaderBase::ConstantBuffer*> mConstantBuffers;
        std::vector<const ShaderBase::ResourceBinding*> mResourceBindings;
        std::vector<D3D12_INPUT_ELEMENT_DESC> mInputElements;

        size_t mHash = 0;
        std::unique_ptr<PipelineLayout> mLayout;
    };
    struct D3DBinding : public D3DBuffer {
        D3D12_GPU_VIRTUAL_ADDRESS mGPUMemory;
        int mSize = -1;
        int mStride, mCount;
        BufferLayout::Usage mUsage;
        D3D::BarrierHandle mHandle = D3D::BarrierHandle::Invalid;
    };

    struct CommandAllocator {
        ComPtr<ID3D12CommandAllocator> mCmdAllocator;
        uint64_t mFrameLocks = 0;
    };

    // If no texture is specified, use this
    std::shared_ptr<Texture> mDefaultTexture;
    int mRTOffset;
    int mDSOffset;
    int mCBOffset;

    std::unordered_map<Int2, std::unique_ptr<D3DRenderSurface>> depthBufferPool;

    D3D::BarrierStateManager mBarrierStateManager;
    int mResourceCount = 0;

private:
    D3DGraphicsDevice& mD3D12;

    // Storage for the GPU resources of each application type
    // TODO: Register for destruction of the application type
    // and clean up GPU resources
    D3DRootSignature mRootSignature;
    std::unordered_map<size_t, std::unique_ptr<D3DPipelineState>> pipelineMapping;
    std::unordered_map<ShaderKey, std::unique_ptr<D3DShader>> shaderMapping;
    std::unordered_map<const Texture*, std::unique_ptr<D3DTexture>> textureMapping;
    std::unordered_map<const RenderTarget2D*, std::unique_ptr<D3DRenderSurface>> rtMapping;
    std::map<size_t, std::unique_ptr<D3DBinding>> mBindings;
    PerFrameItemStore<D3DConstantBuffer> mConstantBufferCache;
    PerFrameItemStore<D3DRenderSurface::SubresourceData> mResourceViewCache;
    PerFrameItemStoreNoHash<ComPtr<ID3D12Resource>, 2> mUploadBufferCache;
    PerFrameItemStoreNoHash<ComPtr<ID3D12Resource>> mDelayedRelease;
    std::vector<uint8_t> mTempData;

    std::vector<size_t> mFrameBitPool;
    std::vector<CommandAllocator> mCommandAllocators;
    std::vector<std::shared_ptr<D3DGraphicsSurface>> mInflightSurfaces;

public:
    RenderStatistics& mStatistics;

    D3DResourceCache(D3DGraphicsDevice& d3d12, RenderStatistics& statistics);
    int RequireFrameHandle(size_t frameHash);
    void AddInFlightSurface(const std::shared_ptr<D3DGraphicsSurface>& surface);
    CommandAllocator* RequireAllocator();
    void UnlockFrame(size_t frameHash);
    void ClearDelayedData();
    ID3D12Resource* AllocateUploadBuffer(size_t size, int lockBits);
    void CreateBuffer(ComPtr<ID3D12Resource>& buffer, int size, int lockBits);
    bool RequireBuffer(const BufferLayout& binding, D3DBinding& d3dBin, int lockBits);
    D3DResourceCache::D3DBinding* GetBinding(uint64_t bindingIdentifier);
    D3DResourceCache::D3DBinding& RequireBinding(const BufferLayout& buffer);
    void UpdateBufferData(ID3D12GraphicsCommandList* cmdList, int lockBits, const BufferLayout& buffer, std::span<const RangeInt> ranges);

    void ComputeElementLayout(std::span<const BufferLayout*> bindings,
        std::vector<D3D12_INPUT_ELEMENT_DESC>& inputElements);
    void CopyBufferData(ID3D12GraphicsCommandList* cmdList, int lockBits,
        const BufferLayout& binding, D3DBinding& d3dBin, int itemSize, int byteOffset, int byteSize);
    void ComputeElementData(std::span<const BufferLayout*> bindings,
        ID3D12GraphicsCommandList* cmdList, int lockBits,
        std::vector<D3D12_VERTEX_BUFFER_VIEW>& inputViews,
        D3D12_INDEX_BUFFER_VIEW& indexView, int& indexCount);

    D3DRenderSurface* RequireD3DRT(const RenderTarget2D* rt);
    void SetRenderTargetMapping(const RenderTarget2D* rt, const D3DRenderSurface& surface);
    D3DTexture* RequireD3DTexture(const Texture& tex);
    D3DPipelineState* GetOrCreatePipelineState(size_t hash);
    D3DPipelineState* RequirePipelineState(
        const CompiledShader& vertexShader, const CompiledShader& pixelShader,
        const MaterialState& materialState, std::span<const BufferLayout*> bindings,
        std::span<DXGI_FORMAT> frameBufferFormats, DXGI_FORMAT depthBufferFormat
    );
    D3DConstantBuffer* RequireConstantBuffer(ID3D12GraphicsCommandList* cmdList, int lockBits, std::span<const uint8_t> data, size_t hash);
    D3DRenderSurface::SubresourceData& RequireTextureRTV(D3DRenderSurfaceView& view, int lockBits);

    D3D12_RESOURCE_DESC GetTextureDesc(const Texture& tex);
    int GetTextureSRV(ID3D12Resource* buffer, DXGI_FORMAT fmt, bool is3D, int arrayCount, int lockBits, int mipB = 0, int mipC = -1);
    void UpdateTextureData(D3DTexture* d3dTex, const Texture& tex, ID3D12GraphicsCommandList* cmdList, int lockBits);
    D3DTexture* RequireDefaultTexture(ID3D12GraphicsCommandList* cmdList, int lockBits);
    D3DTexture* RequireCurrentTexture(const Texture* tex, ID3D12GraphicsCommandList* cmdList, int lockBits);
};



class D3DGraphicsSurface : public GraphicsSurface {
    // This renderer supports 2 backbuffers
    static const int FrameCount = 2;

    struct BackBuffer : D3DResourceCache::D3DRenderSurface {
        //ComPtr<ID3D12Resource> mBuffer;
        //std::shared_ptr<RenderTarget2D> mRenderTarget;
    };

    D3DGraphicsDevice& mDevice;

    // Size of the client rect of the window
    Int2 mResolution;
    std::shared_ptr<RenderTarget2D> mRenderTarget;

    // Used to track when a frame is complete
    UINT64 mFenceValues[FrameCount];

    // Each frame needs its own allocator
    //ComPtr<ID3D12CommandAllocator> mCmdAllocator[FrameCount];
    BackBuffer mFrameBuffers[FrameCount];

    // Current frame being rendered (wraps to the number of back buffers)
    int mBackBufferIndex;
    UINT64 mLockFrame;

    // Fence to wait for frames to render
    HANDLE mFenceEvent;
    ComPtr<ID3D12Fence> mFence;

    int mDenyPresentRef = 0;
    bool mIsOccluded = false;
public:
    ComPtr<IDXGISwapChain3> mSwapChain;
    D3DGraphicsSurface(D3DGraphicsDevice& device, HWND hWnd);
    ~D3DGraphicsSurface();
    IDXGISwapChain3* GetSwapChain() const { return mSwapChain.Get(); }
    Int2 GetResolution() const override { return mResolution; }
    void SetResolution(Int2 res) override;
    void ResizeSwapBuffers();

    //ID3D12CommandAllocator* GetCmdAllocator() const { return mCmdAllocator[mBackBufferIndex].Get(); }
    const BackBuffer& GetFrameBuffer() const { return mFrameBuffers[mBackBufferIndex]; }
    const std::shared_ptr<RenderTarget2D>& GetBackBuffer() const override;

    int GetBackBufferIndex() const { return mBackBufferIndex; }
    int GetBackFrameIndex() const;

    UINT64 GetHeadFrame() const;
    UINT64 GetLockFrame() const;
    UINT64 ConsumeFrame(UINT64 untilFrame);

    bool GetIsOccluded() const override;
    void RegisterDenyPresent(int delta = 1) override;

    int Present() override;
    int WaitForFrame() override;
    void WaitForGPU() override;

};
