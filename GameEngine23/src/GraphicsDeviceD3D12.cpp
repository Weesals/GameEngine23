#include <span>
#include <vector>

#include "GraphicsDeviceD3D12.h"
#include "D3DResourceCache.h"
#include "D3DShader.h"
#include "Resources.h"

#include <d3dcompiler.h>
#include <unordered_map>
#include <memory>
#include <functional>
#include <utility>
#include <algorithm>
#include <d3dx12.h>
#include <stdexcept>
#include <sstream>

const D3D12_RESOURCE_STATES InitialBufferState = D3D12_RESOURCE_STATE_VERTEX_AND_CONSTANT_BUFFER | D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE | D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;

int gActiveCmdBuffers;

// Handles receiving rendering events from the user application
// and issuing relevant draw commands
class D3DCommandBuffer : public CommandBufferInteropBase {
    GraphicsDeviceD3D12* mDevice;
    D3DGraphicsSurface* mSurface;
    LockMask mFrameHandle;
    D3DResourceCache::CommandAllocator* mCmdAllocator;
    ComPtr<ID3D12GraphicsCommandList6> mCmdList;
    D3DCommandContext mCmdContext;
    InplaceVector<D3DResourceCache::D3DRenderSurfaceView, 8> mFrameBuffers;
    D3DResourceCache::D3DRenderSurfaceView mDepthBuffer;
    std::vector<D3D12_VERTEX_BUFFER_VIEW> tVertexViews;
    RectInt mViewportRect;
    struct D3DRoot {
        const D3DResourceCache::D3DRootSignature* mLastRootSig;
        const D3DResourceCache::D3DPipelineState* mLastPipeline;
        const D3DConstantBuffer* mLastCBs[10];
        D3D12_GPU_DESCRIPTOR_HANDLE mLastResources[32];
    };
    D3DRoot mGraphicsRoot;
    D3DRoot mComputeRoot;
    D3D::BarrierStateManager mBarrierStateManager;
public:
    D3DCommandBuffer(GraphicsDeviceD3D12* device)
        : mDevice(device)
        , mCmdAllocator(nullptr)
    {
        tVertexViews.reserve(4);
    }
    ~D3DCommandBuffer() {
    }
    ID3D12Device* GetD3DDevice() const { return mDevice->GetD3DDevice(); }
    GraphicsDeviceBase* GetGraphics() const override {
        return mDevice;
    }
    // Get this command buffer ready to begin rendering
    virtual void Reset() override {
        mCmdAllocator = mDevice->GetResourceCache().RequireAllocator();
        ++mCmdAllocator->mFenceValue;
        mFrameHandle = 1ull << mCmdAllocator->mId;
        if (mCmdList == nullptr) {
            ThrowIfFailed(GetD3DDevice()
                ->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT,
                    mCmdAllocator->mCmdAllocator.Get(), nullptr, IID_PPV_ARGS(&mCmdList)));
            //ThrowIfFailed(mCmdList->Close());
        }
        else {
            mCmdList->Reset(mCmdAllocator->mCmdAllocator.Get(), nullptr);
        }
        mCmdContext.mCmdList = mCmdList.Get();
        mCmdContext.mLockBits = mFrameHandle;
        mCmdContext.mBarrierStateManager = &mBarrierStateManager;

        mSurface = nullptr;
        mGraphicsRoot = { };
        mComputeRoot = { };
        std::fill(mFrameBuffers.begin(), mFrameBuffers.end(), nullptr);
        mDepthBuffer = { };

        auto srvHeap = mDevice->GetSRVHeap();
        mCmdList->SetDescriptorHeaps(1, &srvHeap);
        ++gActiveCmdBuffers;
    }
    virtual void SetSurface(GraphicsSurface* surface) override {
        mSurface = (D3DGraphicsSurface*)surface;
        auto& waitHandle = mSurface->GetFrameWaitHandle();
        // Wait for whatever allocator was previously rendering to this surface
        mDevice->GetResourceCache().AwaitAllocator(waitHandle);
        waitHandle = mCmdAllocator->CreateWaitHandle();
        auto& cache = mDevice->GetResourceCache();
        auto* backBuffer = &*surface->GetBackBuffer();
        auto d3dRT = mDevice->GetResourceCache().RequireD3DRT(backBuffer);
        D3DResourceCache::D3DRenderSurfaceView view(&mSurface->GetFrameBuffer());
        cache.RequireTextureRTV(view, mFrameHandle);
        cache.SetRenderTargetMapping(backBuffer, *view.mSurface);
        backBuffer->SetResolution(Int2(d3dRT->mDesc.mWidth, d3dRT->mDesc.mHeight));
        backBuffer->SetFormat((BufferFormat)d3dRT->mFormat);
        assert(d3dRT->mBarrierHandle >= 0);
    }
    virtual GraphicsSurface* GetSurface() override {
        return mSurface;
    }
    virtual void SetViewport(RectInt rect) override {
        CD3DX12_VIEWPORT viewport((float)rect.x, (float)rect.y, (float)rect.width, (float)rect.height);
        CD3DX12_RECT scissorRect((LONG)rect.x, (LONG)rect.y, (LONG)(rect.x + rect.width), (LONG)(rect.y + rect.height));

        mCmdList->RSSetViewports(1, &viewport);
        mCmdList->RSSetScissorRects(1, &scissorRect);
        mViewportRect = rect;
    }
    virtual void SetRenderTargets(std::span<RenderTargetBinding> colorTargets, RenderTargetBinding depthTarget) override {
        InplaceVector<D3DResourceCache::D3DRenderSurfaceView, 16> d3dColorTargets;
        D3DResourceCache::D3DRenderSurfaceView d3dDepthTarget = { };
        for (int t = 0; t < colorTargets.size(); ++t) {
            auto target = colorTargets[t];
            auto d3dRt = RequireInitializedRT(target.mTarget);
            assert(target.mTarget != nullptr);
            assert(d3dRt->mBarrierHandle >= 0);
            //if (target.mTarget == nullptr) d3dRt = &mSurface->GetFrameBuffer();
            d3dColorTargets.push_back(D3DResourceCache::D3DRenderSurfaceView(d3dRt, target.mMip, target.mSlice));
        }
        //if (colorTargets.empty() && depthTarget.mTarget == nullptr) d3dColorTargets.push_back(&mSurface->GetFrameBuffer());
        if (depthTarget.mTarget != nullptr)
            d3dDepthTarget = D3DResourceCache::D3DRenderSurfaceView(RequireInitializedRT(depthTarget.mTarget), depthTarget.mMip, depthTarget.mSlice);
        SetD3DRenderTarget(d3dColorTargets, d3dDepthTarget);
    }
    void FlushBarriers() {
        mDevice->GetResourceCache().FlushBarriers(CreateContext());
    }
    const D3DResourceCache::D3DRenderSurface* RequireInitializedRT(const RenderTarget2D* target) {
        auto d3dRt = target != nullptr ? mDevice->GetResourceCache().RequireD3DRT(target) : nullptr;
        if (d3dRt != nullptr && d3dRt->mBuffer == nullptr) {
            auto& cache = mDevice->GetResourceCache();
            auto d3dDevice = mDevice->GetD3DDevice();

            D3D12_RESOURCE_DESC texDesc = { };
            D3D12_CLEAR_VALUE clearValue = { };
            if (BufferFormatType::GetIsDepthBuffer(target->GetFormat())) {
                texDesc = CreateDepthDesc(target->GetResolution(), target->GetFormat(), target->GetMipCount(), target->GetArrayCount());
                clearValue = CD3DX12_CLEAR_VALUE(texDesc.Format, 1.0f, 0);
            }
            else {
                texDesc = CreateTextureDesc(target->GetResolution(), target->GetFormat(), target->GetMipCount(), target->GetArrayCount());
                static FLOAT clearColor[]{ 0.0f, 0.0f, 0.0f, 0.0f };
                clearValue = CD3DX12_CLEAR_VALUE(texDesc.Format, clearColor);
            }
            if (target->GetAllowUnorderedAccess()) {
                texDesc.Flags |= D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS;
            }
            AllocateRTBuffer(d3dRt, texDesc, clearValue);
            d3dRt->mBuffer->SetName(target->GetName().c_str());

            auto viewFmt = (DXGI_FORMAT)target->GetFormat();
            if (viewFmt == DXGI_FORMAT_D24_UNORM_S8_UINT) viewFmt = DXGI_FORMAT_R24_UNORM_X8_TYPELESS;
            if (viewFmt == DXGI_FORMAT_D32_FLOAT) viewFmt = DXGI_FORMAT_R32_FLOAT;
            if (viewFmt == DXGI_FORMAT_D16_UNORM) viewFmt = DXGI_FORMAT_R16_UNORM;

            // Create a shader resource view (SRV) for the texture
            D3D12_SHADER_RESOURCE_VIEW_DESC srvDesc = { .Format = viewFmt, .ViewDimension = D3D12_SRV_DIMENSION_TEXTURE2D };
            srvDesc.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
            srvDesc.Texture2D.MipLevels = target->GetMipCount();
            CD3DX12_CPU_DESCRIPTOR_HANDLE srvHandle(mDevice->GetSRVHeap()->GetCPUDescriptorHandleForHeapStart(), cache.mCBOffset);
            d3dDevice->CreateShaderResourceView(d3dRt->mBuffer.Get(), &srvDesc, srvHandle);
            d3dRt->mSRVOffset = cache.mCBOffset;
            cache.mCBOffset += mDevice->GetDevice().GetDescriptorHandleSizeSRV();
            /*d3dRt->mSRVOffset = cache.GetTextureSRV(d3dRt->mBuffer.Get(),
                viewFmt, false, target->GetArrayCount(), 0xffffffff);*/
        }
        return d3dRt;
    }
    void AllocateRTBuffer(D3DResourceCache::D3DRenderSurface* surface, const D3D12_RESOURCE_DESC& texDesc, const D3D12_CLEAR_VALUE& clearValue) {
        // Create the render target
        ThrowIfFailed(mDevice->GetD3DDevice()->CreateCommittedResource(&D3D::DefaultHeap,
            D3D12_HEAP_FLAG_NONE, &texDesc,
            D3D12_RESOURCE_STATE_COMMON, &clearValue, IID_PPV_ARGS(&surface->mBuffer)));
        surface->mBuffer->SetName(L"Texture RT");
        surface->mDesc.mWidth = (uint16_t)texDesc.Width;
        surface->mDesc.mHeight = (uint16_t)texDesc.Height;
        surface->mDesc.mMips = (uint8_t)texDesc.MipLevels;
        surface->mDesc.mSlices = (uint8_t)texDesc.DepthOrArraySize;
        surface->mFormat = texDesc.Format;
    }
    D3D12_RESOURCE_DESC CreateTextureDesc(Int2 size, BufferFormat fmt, int mipCount, int arrayCount) {
        return CD3DX12_RESOURCE_DESC::Tex2D((DXGI_FORMAT)fmt,
            size.x, size.y, arrayCount, mipCount, 1, 0, D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET);
    }
    D3D12_RESOURCE_DESC CreateDepthDesc(Int2 size, BufferFormat fmt, int mipCount, int arrayCount, bool memoryless = false) {
        auto flags = D3D12_RESOURCE_FLAG_ALLOW_DEPTH_STENCIL;
        if (memoryless) flags |= D3D12_RESOURCE_FLAG_DENY_SHADER_RESOURCE;
        return CD3DX12_RESOURCE_DESC::Tex2D((DXGI_FORMAT)fmt,
            size.x, size.y, arrayCount, mipCount, 1, 0, flags);
    }
    D3DResourceCache::D3DRenderSurface* RequireDepth(Int2 size) {
        auto& cache = mDevice->GetResourceCache();
        auto item = cache.depthBufferPool.find(size);
        if (item != cache.depthBufferPool.end()) return item->second.get();
        item = cache.depthBufferPool.insert(std::make_pair(
            size,
            std::make_unique<D3DResourceCache::D3DRenderSurface>()
        )).first;
        cache.RequireBarrierHandle(item->second.get());
        auto* surface = item->second.get();
        auto texDesc = CreateDepthDesc(size, BufferFormat::FORMAT_D24_UNORM_S8_UINT, 1, 1, true);
        AllocateRTBuffer(surface, texDesc, CD3DX12_CLEAR_VALUE(texDesc.Format, 1.0f, 0));
        auto d3dDevice = mDevice->GetD3DDevice();
        return surface;
    }
    void SetD3DRenderTarget(std::span<const D3DResourceCache::D3DRenderSurfaceView> frameBuffers, D3DResourceCache::D3DRenderSurfaceView depthBuffer) {
        bool same = mDepthBuffer == depthBuffer;
        D3DResourceCache::D3DRenderSurfaceView anyBuffer = depthBuffer;
        for (int i = 0; i < (int)frameBuffers.size(); ++i) {
            auto buffer = frameBuffers[i];
            if (buffer != nullptr) anyBuffer = buffer;
            if (buffer != mFrameBuffers[i]) same = false;
        }
        if (same) return;
        if (anyBuffer != nullptr && depthBuffer == nullptr) depthBuffer = RequireDepth(Int2(anyBuffer.mSurface->mDesc.mWidth, anyBuffer.mSurface->mDesc.mHeight));
        {
            int fbcount = std::max((int)frameBuffers.size(), (int)mFrameBuffers.size());
            for (int i = 0; i < fbcount; ++i) {
                auto srcBuffer = i < frameBuffers.size() ? frameBuffers[i] : nullptr;
                auto& dstBuffer = mFrameBuffers[i];
                if (dstBuffer == srcBuffer) continue;
                if (dstBuffer != nullptr) {
                    mBarrierStateManager.UnlockResourceState(
                        dstBuffer->mBarrierHandle, dstBuffer.GetSubresource(),
                        D3D12_RESOURCE_STATE_RENDER_TARGET, dstBuffer->mDesc);
                }
                dstBuffer = srcBuffer;
                if (dstBuffer != nullptr) {
                    mBarrierStateManager.SetResourceState(dstBuffer->mBuffer.Get(),
                        dstBuffer->mBarrierHandle, dstBuffer.GetSubresource(),
                        D3D::BarrierStateManager::CreateLocked(D3D12_RESOURCE_STATE_RENDER_TARGET),
                        dstBuffer->mDesc);
                }
            }
            if (mDepthBuffer != depthBuffer) {
                mDepthBuffer = depthBuffer;
                if (mDepthBuffer != nullptr) {
                    mBarrierStateManager.SetResourceState(
                        mDepthBuffer->mBuffer.Get(), mDepthBuffer->mBarrierHandle, mDepthBuffer.GetSubresource(),
                        D3D12_RESOURCE_STATE_DEPTH_WRITE, mDepthBuffer->mDesc);
                }
            }
            mFrameBuffers.resize((uint8_t)frameBuffers.size());
            FlushBarriers();
        }

        if (anyBuffer != nullptr) {
            auto& cache = mDevice->GetResourceCache();
            SetViewport(RectInt(0, 0, anyBuffer->mDesc.mWidth, anyBuffer->mDesc.mHeight));

            InplaceVector<D3D12_CPU_DESCRIPTOR_HANDLE, 8> targets;
            for (int i = 0; i < (int)frameBuffers.size(); ++i) {
                auto& surface = cache.RequireTextureRTV(mFrameBuffers[i], mFrameHandle);
                targets.push_back(CD3DX12_CPU_DESCRIPTOR_HANDLE(mDevice->GetRTVHeap()->GetCPUDescriptorHandleForHeapStart(), surface.mRTVOffset));
            }
            auto& depthSurface = cache.RequireTextureRTV(mDepthBuffer, mFrameHandle);
            CD3DX12_CPU_DESCRIPTOR_HANDLE depthHandle(mDevice->GetDSVHeap()->GetCPUDescriptorHandleForHeapStart(), depthSurface.mRTVOffset);
            mCmdList->OMSetRenderTargets(targets.size(), targets.data(), FALSE, &depthHandle);
        }
        else {
            mCmdList->OMSetRenderTargets(0, nullptr, FALSE, nullptr);
        }
        // Dont know if this is required, but NSight showed draw calls failing without it
        // Probably also need to clear bound resource cache?
        mGraphicsRoot = { };
        mComputeRoot = { };
    }
    // Clear the screen
    void ClearRenderTarget(const ClearConfig& clear) override {
        auto& cache = mDevice->GetResourceCache();
        CD3DX12_RECT clearRect((LONG)mViewportRect.x, (LONG)mViewportRect.y,
            (LONG)(mViewportRect.x + mViewportRect.width), (LONG)(mViewportRect.y + mViewportRect.height));
        mDevice->CheckDeviceState();
        for (int i = 0; i < mFrameBuffers.mSize; i++) {
            if (clear.HasClearColor() && mFrameBuffers[i] != nullptr) {
                auto& surface = cache.RequireTextureRTV(mFrameBuffers[i], mFrameHandle);
                CD3DX12_CPU_DESCRIPTOR_HANDLE descriptor(mDevice->GetRTVHeap()->GetCPUDescriptorHandleForHeapStart(),
                    surface.mRTVOffset);
                mCmdList->ClearRenderTargetView(descriptor, clear.ClearColor, 1, &clearRect);
            }
        }
        auto flags = (clear.HasClearDepth() ? D3D12_CLEAR_FLAG_DEPTH : 0)
            | (clear.HasClearScencil() ? D3D12_CLEAR_FLAG_STENCIL : 0);
        if (flags && mDepthBuffer != nullptr) {
            auto& surface = cache.RequireTextureRTV(mDepthBuffer, mFrameHandle);
            CD3DX12_CPU_DESCRIPTOR_HANDLE depth(mDevice->GetDSVHeap()->GetCPUDescriptorHandleForHeapStart(),
                surface.mRTVOffset);
            mCmdList->ClearDepthStencilView(depth, (D3D12_CLEAR_FLAGS)flags,
                clear.ClearDepth, clear.ClearStencil, 1, &clearRect);
        }
    }
    uint64_t GetGlobalPSOHash() const {
        uint64_t hash = (uint64_t)this;
        for (int r = 0; r < mFrameBuffers.mSize; ++r) {
            auto& fb = mFrameBuffers[r];
            hash *= 12345;
            hash += fb.mSurface->mFormat;
        }
        return hash;
    }

    D3DCommandContext& CreateContext() {
        return mCmdContext;
    }

    void* RequireConstantBuffer(std::span<const uint8_t> data, size_t hash) override {
        auto& cache = mDevice->GetResourceCache();
        return cache.RequireConstantBuffer(CreateContext(), data, hash);
    }
    void CopyBufferData(const BufferLayout& buffer, std::span<const RangeInt> ranges) override {
        auto& cache = mDevice->GetResourceCache();
        cache.UpdateBufferData(CreateContext(), buffer, ranges);
    }
    void CopyBufferData(const BufferLayout& source, const BufferLayout& dest, int srcOffset, int dstOffset, int length) override {
        auto& cache = mDevice->GetResourceCache();
        cache.UpdateBufferData(CreateContext(), source, dest, srcOffset, dstOffset, length);
    }
    void CommitTexture(const Texture* texture) {
        auto& cache = mDevice->GetResourceCache();
        cache.RequireCurrentTexture(texture, CreateContext());
    }
    void BindPipelineState(const D3DResourceCache::D3DPipelineState* pipelineState) {
        mDevice->CheckDeviceState();
        if (mGraphicsRoot.mLastPipeline == pipelineState) return;
        // Require and bind a pipeline matching the material config and mesh attributes
        if (mGraphicsRoot.mLastRootSig != pipelineState->mRootSignature) {
            mGraphicsRoot.mLastRootSig = pipelineState->mRootSignature;
            mCmdList->SetGraphicsRootSignature(mGraphicsRoot.mLastRootSig->mRootSignature.Get());
            mCmdList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        }

        mGraphicsRoot.mLastPipeline = pipelineState;
        mCmdList->SetPipelineState(pipelineState->mPipelineState.Get());
    }
    void BindComputePipelineState(const D3DResourceCache::D3DPipelineState* pipelineState) {
        if (mComputeRoot.mLastPipeline == pipelineState) return;
        // Require and bind a pipeline matching the material config and mesh attributes
        if (mComputeRoot.mLastRootSig != pipelineState->mRootSignature) {
            mComputeRoot.mLastRootSig = pipelineState->mRootSignature;
            mCmdList->SetComputeRootSignature(mComputeRoot.mLastRootSig->mRootSignature.Get());
        }

        mComputeRoot.mLastPipeline = pipelineState;
        mCmdList->SetPipelineState(pipelineState->mPipelineState.Get());
    }
    virtual const PipelineLayout* RequirePipeline(
        const ShaderStages& shaders,
        const MaterialState& materialState, std::span<const BufferLayout*> bindings
    ) override {
        mDevice->CheckDeviceState();

        InplaceVector<DXGI_FORMAT> frameBufferFormats;
        for (auto& fb : mFrameBuffers) frameBufferFormats.push_back(fb->mFormat);
        DXGI_FORMAT depthBufferFormat = mDepthBuffer->mFormat;
        auto pipelineState = mDevice->GetResourceCache().RequirePipelineState(
            shaders, materialState, bindings,
            frameBufferFormats, depthBufferFormat
        );
        return pipelineState->mLayout.get();
    }
    virtual const PipelineLayout* RequireComputePSO(
        const CompiledShader& computeShader
    ) override {
        auto pipelineState = mDevice->GetResourceCache().RequireComputePSO(computeShader);
        return pipelineState->mLayout.get();
    }
    int BindConstantBuffers(std::pair<int, const D3DConstantBuffer*> constantBinds[32], std::vector<const ShaderBase::ConstantBuffer*> cbuffers, std::span<const void*> resources, int& r) {
        for (int i = 0; i < cbuffers.size(); ++i) {
            constantBinds[i] = { cbuffers[i]->mBindPoint, (D3DConstantBuffer*)resources[r++] };
        }
        return (int)cbuffers.size();
    }
    int BindResources(std::pair<int, int> bindPoints[32], std::vector<const ShaderBase::ResourceBinding*> bindings, std::span<const void*> resources, int& r) {
        auto& cache = mDevice->GetResourceCache();
        for (int i = 0; i < bindings.size(); ++i) {
            auto* rb = bindings[i];
            auto* resource = (BufferReference*)&resources[r];
            r += 2;
            int bindPoint = rb->mBindPoint;
            int srvOffset = -1;
            if (resource->mType == BufferReference::BufferTypes::Buffer) {
                auto* rbinding = cache.GetBinding((uint64_t)resource->mBuffer);
                assert(rbinding != nullptr); // Did you call CopyBufferData on this resource?
                D3D12_RESOURCE_STATES barrierState = InitialBufferState;
                int offset = resource->mSubresourceId;
                // Explicit offset/count or get buffer count
                int count = rbinding->mCount - resource->mSubresourceId;
                if (resource->mSubresourceCount != -1) count = (uint16_t)resource->mSubresourceCount;
                // Buffer has prefixed count; bind full range and offset start
                if (count == -1) count = rbinding->mSize / rbinding->mStride - (++offset);
                if (rb->mType == ShaderBase::ResourceTypes::R_SBuffer) {
                    srvOffset = cache.GetBufferSRV(*rbinding,
                        offset, count, rbinding->mStride, mFrameHandle);
                } else if (rb->mType == ShaderBase::ResourceTypes::R_UAVBuffer
                    || rb->mType == ShaderBase::ResourceTypes::R_UAVAppend
                    || rb->mType == ShaderBase::ResourceTypes::R_UAVConsume) {
                    srvOffset = cache.GetBufferUAV(rbinding->mBuffer.Get(),
                        count, rbinding->mStride, D3D12_BUFFER_UAV_FLAG_NONE, mFrameHandle);
                    bindPoint += 5;
                    barrierState = D3D12_RESOURCE_STATE_UNORDERED_ACCESS;
                }
                cache.RequireState(CreateContext(), *rbinding, { }, barrierState);
            }
            else if (resource->mType == BufferReference::BufferTypes::RenderTarget) {
                auto viewFmt = (DXGI_FORMAT)resource->mFormat;
                auto* rt = static_cast<RenderTarget2D*>(resource->mBuffer);
                auto* surface = RequireInitializedRT(rt);
                assert(surface->mBuffer != nullptr);
                assert(surface->mBuffer.Get() != nullptr);
                if (viewFmt == DXGI_FORMAT_UNKNOWN) viewFmt = surface->mFormat;
                if (viewFmt == DXGI_FORMAT_D24_UNORM_S8_UINT) viewFmt = DXGI_FORMAT_R24_UNORM_X8_TYPELESS;
                if (viewFmt == DXGI_FORMAT_D32_FLOAT) viewFmt = DXGI_FORMAT_R32_FLOAT;
                if (viewFmt == DXGI_FORMAT_D16_UNORM) viewFmt = DXGI_FORMAT_R16_UNORM;
                D3D12_RESOURCE_STATES barrierState = D3D12_RESOURCE_STATE_UNORDERED_ACCESS;
                if (rb->mType == ShaderBase::ResourceTypes::R_UAVBuffer) {
                    srvOffset = cache.GetUAV(surface->mBuffer.Get(),
                        viewFmt, false, rt->GetArrayCount(), mFrameHandle,
                        resource->mSubresourceId, resource->mSubresourceCount);
                    bindPoint += 5;
                }
                else if (rb->mType == ShaderBase::ResourceTypes::R_Texture) {
                    barrierState = D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE | D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
                    if (mDepthBuffer->mBuffer.Get() == surface->mBuffer.Get()) barrierState |= D3D12_RESOURCE_STATE_DEPTH_READ;
                    srvOffset = cache.GetTextureSRV(surface->mBuffer.Get(),
                        viewFmt, false, rt->GetArrayCount(), mFrameHandle,
                        resource->mSubresourceId, resource->mSubresourceCount);
                }
                mBarrierStateManager.SetResourceState(
                    surface->mBuffer.Get(), surface->mBarrierHandle,
                    -1, barrierState, surface->mDesc);
                mBarrierStateManager.AssertState(surface->mBarrierHandle, barrierState,
                    resource->mSubresourceId, resource->mSubresourceCount);
            }
            else if (resource->mType == BufferReference::BufferTypes::Texture) {
                auto tex = reinterpret_cast<Texture*>(resource->mBuffer);
                if (tex == nullptr || tex->GetSize().x == 0) tex = cache.RequireDefaultTexture();
                auto* d3dTex = cache.RequireTexture(tex, CreateContext());
                if (d3dTex->mBuffer == nullptr) {
                } else if (rb->mType == ShaderBase::ResourceTypes::R_UAVBuffer) {
                    cache.RequireBarrierHandle(d3dTex);
                    mBarrierStateManager.SetResourceState(
                        d3dTex->mBuffer.Get(), d3dTex->mBarrierHandle,
                        resource->mSubresourceId, D3D12_RESOURCE_STATE_UNORDERED_ACCESS,
                        D3D::BarrierMeta(tex->GetMipCount()));

                    int mipC = resource->mSubresourceCount;
                    if (mipC == -1) mipC = tex->GetMipCount() - resource->mSubresourceId;
                    srvOffset = cache.GetUAV(d3dTex->mBuffer.Get(), d3dTex->mFormat,
                        tex->GetSize().z > 1, tex->GetArrayCount(), mFrameHandle,
                        resource->mSubresourceId, mipC);
                    bindPoint += 5;
                }
                else if (rb->mType == ShaderBase::ResourceTypes::R_Texture) {
                    if (d3dTex->mBarrierHandle != D3D::BarrierHandle::Invalid) {
                        mBarrierStateManager.SetResourceState(
                            d3dTex->mBuffer.Get(), d3dTex->mBarrierHandle,
                            -1, D3D12_RESOURCE_STATE_COMMON,
                            D3D::BarrierMeta(tex->GetMipCount()));
                        mBarrierStateManager.AssertState(d3dTex->mBarrierHandle, D3D12_RESOURCE_STATE_COMMON,
                            resource->mSubresourceId, resource->mSubresourceCount);
                    }
                    srvOffset = d3dTex->mSRVOffset;
                }
            }
            if (srvOffset == -1 && rb->mType == ShaderBase::ResourceTypes::R_Texture) {
                auto* d3dTex = cache.RequireCurrentTexture(cache.RequireDefaultTexture(), CreateContext());
                if (d3dTex != nullptr) srvOffset = d3dTex->mSRVOffset;
            }
            if (srvOffset == -1) {
                std::string str = "Failed to find resource for " + rb->mName.GetName();
                MessageBoxA(0, str.c_str(), "Resource error", 0);
                return -1;
            }
            bindPoints[i] = { bindPoint, srvOffset };
        }
        return (int)bindings.size();
    }
    void BindGraphicsState(const D3DResourceCache::D3DPipelineState* pipelineState, std::span<const void*> resources) {
        BindPipelineState(pipelineState);

        int r = 0;
        int stencilRef = -1;
        if (pipelineState->mLayout->mMaterialState.mDepthMode.GetStencilEnable()) {
            stencilRef = (int)(intptr_t)resources[r++];
        }
        // Require and bind constant buffers
        std::pair<int, const D3DConstantBuffer*> constantBinds[32];
        int cCount = BindConstantBuffers(constantBinds, pipelineState->mLayout->mConstantBuffers, resources, r);
        // Require and bind other resources (textures)
        std::pair<int, int> resourceBinds[32];
        int rCount = BindResources(resourceBinds, pipelineState->mLayout->mResources, resources, r);
        if (cCount == -1 || rCount == -1) return;

        FlushBarriers();
        for (int i = 0; i < cCount; ++i) {
            int bindPoint = constantBinds[i].first;
            auto* d3dCB = constantBinds[i].second;
            if (mGraphicsRoot.mLastCBs[bindPoint] == d3dCB) continue;
            mGraphicsRoot.mLastCBs[bindPoint] = d3dCB;
            mCmdList->SetGraphicsRootConstantBufferView(bindPoint, d3dCB->mConstantBuffer->GetGPUVirtualAddress());
        }
        for (int i = 0; i < rCount; ++i) {
            auto handle = CD3DX12_GPU_DESCRIPTOR_HANDLE(mDevice->GetSRVHeap()->GetGPUDescriptorHandleForHeapStart(), resourceBinds[i].second);
            auto bindPoint = resourceBinds[i].first;
            assert(bindPoint < _countof(mGraphicsRoot.mLastResources));
            if (mGraphicsRoot.mLastResources[bindPoint].ptr == handle.ptr) continue;
            mCmdList->SetGraphicsRootDescriptorTable(pipelineState->mRootSignature->mNumConstantBuffers + bindPoint, handle);
            mGraphicsRoot.mLastResources[bindPoint] = handle;
        }
        if (stencilRef >= 0) mCmdList->OMSetStencilRef((UINT)stencilRef);
    }
    void BindComputeState(const D3DResourceCache::D3DPipelineState* pipelineState, std::span<const void*> resources) {
        BindComputePipelineState(pipelineState);
        int r = 0;
        // Require and bind constant buffers
        std::pair<int, const D3DConstantBuffer*> constantBinds[32];
        int cCount = BindConstantBuffers(constantBinds, pipelineState->mLayout->mConstantBuffers, resources, r);
        // Require and bind other resources (textures)
        std::pair<int, int> resourceBinds[32];
        int rCount = BindResources(resourceBinds, pipelineState->mLayout->mResources, resources, r);
        if (cCount == -1 || rCount == -1) return;

        FlushBarriers();
        for (int i = 0; i < cCount; ++i) {
            int bindPoint = constantBinds[i].first;
            auto* d3dCB = constantBinds[i].second;
            if (mComputeRoot.mLastCBs[bindPoint] == d3dCB) continue;
            mComputeRoot.mLastCBs[bindPoint] = d3dCB;
            mCmdList->SetComputeRootConstantBufferView(bindPoint, d3dCB->mConstantBuffer->GetGPUVirtualAddress());
        }
        for (int i = 0; i < rCount; ++i) {
            auto handle = CD3DX12_GPU_DESCRIPTOR_HANDLE(mDevice->GetSRVHeap()->GetGPUDescriptorHandleForHeapStart(), resourceBinds[i].second);
            auto bindingId = pipelineState->mRootSignature->mNumConstantBuffers + resourceBinds[i].first;
            assert(bindingId < _countof(mComputeRoot.mLastResources));
            if (mComputeRoot.mLastResources[bindingId].ptr == handle.ptr) continue;
            mCmdList->SetComputeRootDescriptorTable(bindingId, handle);
            mComputeRoot.mLastResources[bindingId] = handle;
        }
    }
    void BindVertexIndexBuffers(std::span<const BufferLayout*> bindings, D3D12_INDEX_BUFFER_VIEW& indexView, int& indexCount) {
        indexCount = -1;
        indexView = D3D12_INDEX_BUFFER_VIEW{
            .BufferLocation = 0,
            .SizeInBytes = 0,
            .Format = DXGI_FORMAT_UNKNOWN,
        };
        if (bindings.empty()) {
            mCmdList->IASetIndexBuffer(&indexView);
        }
        else {
            tVertexViews.clear();
            auto& cache = mDevice->GetResourceCache();
            cache.ComputeElementData(bindings, CreateContext(), tVertexViews, indexView, indexCount);
            FlushBarriers();
            mCmdList->IASetVertexBuffers(0, (uint32_t)tVertexViews.size(), tVertexViews.data());
            if (indexView.Format != DXGI_FORMAT_UNKNOWN) mCmdList->IASetIndexBuffer(&indexView);
        }
    }
    void DrawMesh(std::span<const BufferLayout*> bindings, const PipelineLayout* state, std::span<const void*> resources, const DrawConfig& config, int instanceCount = 1, const char* name = nullptr) override {
        auto* pipelineState = (D3DResourceCache::D3DPipelineState*)state->mPipelineHash;
        if (pipelineState == nullptr) return;

        static Identifier indirectArgsName("INDIRECTARGS");
        if (pipelineState->mType == 1) {
            DispatchMesh(bindings, state, resources, config, instanceCount, name);
            return;
        } else if (bindings[0]->mElements[0].mBindName == indirectArgsName) {
            const BufferLayout* argsBinding = bindings[0];
            bindings = bindings.subspan(1);
            DrawIndirect(*argsBinding, bindings, state, resources, config, instanceCount, name);
            return;
        }
        assert(instanceCount > 0);

        BindGraphicsState(pipelineState, resources);

        int indexCount;
        D3D12_INDEX_BUFFER_VIEW indexView;
        BindVertexIndexBuffers(bindings, indexView, indexCount);
        if (config.mIndexCount >= 0) indexCount = config.mIndexCount;

        if (indexView.Format != DXGI_FORMAT_UNKNOWN)
            mCmdList->DrawIndexedInstanced(indexCount, std::max(1, instanceCount), config.mIndexBase, 0, config.mInstanceBase);
        else
            mCmdList->DrawInstanced(indexCount, std::max(1, instanceCount), config.mIndexBase, config.mInstanceBase);

        mDevice->GetResourceCache().mStatistics.DrawInstanced(instanceCount);
    }
    void DispatchMesh(std::span<const BufferLayout*> bindings, const PipelineLayout* state, std::span<const void*> resources, const DrawConfig& config, int instanceCount = 1, const char* name = nullptr) override {
        auto* pipelineState = (D3DResourceCache::D3DPipelineState*)state->mPipelineHash;
        if (pipelineState == nullptr) return;

        BindGraphicsState(pipelineState, resources);

        int indexCount;
        D3D12_INDEX_BUFFER_VIEW indexView;
        BindVertexIndexBuffers(bindings, indexView, indexCount);
        if (config.mIndexCount >= 0) indexCount = config.mIndexCount;

        mCmdList->DispatchMesh(
            std::max(1, (indexCount + 127) / 128),
            std::max(1, instanceCount),
            1
        );

        mDevice->GetResourceCache().mStatistics.DrawInstanced(instanceCount);
    }
    void DrawIndirect(const BufferLayout& argsBuffer, std::span<const BufferLayout*> bindings, const PipelineLayout* state, std::span<const void*> resources, const DrawConfig& config, int maxInstanceCount = 1, const char* name = nullptr) override {
        auto* pipelineState = (D3DResourceCache::D3DPipelineState*)state->mPipelineHash;
        if (pipelineState == nullptr) return;
        auto& cache = mDevice->GetResourceCache();

        BindGraphicsState(pipelineState, resources);

        int indexCount;
        D3D12_INDEX_BUFFER_VIEW indexView;
        BindVertexIndexBuffers(bindings, indexView, indexCount);
        if (config.mIndexCount >= 0) indexCount = config.mIndexCount;
        static ComPtr<ID3D12CommandSignature> mIndirectSig;
        if (mIndirectSig == nullptr) {
            D3D12_INDIRECT_ARGUMENT_DESC argumentDescs[1] = {
                D3D12_INDIRECT_ARGUMENT_DESC{.Type = D3D12_INDIRECT_ARGUMENT_TYPE_DRAW_INDEXED},
            };
            D3D12_COMMAND_SIGNATURE_DESC sigDesc = {
                .ByteStride = sizeof(D3D12_DRAW_INDEXED_ARGUMENTS),
                .NumArgumentDescs = _countof(argumentDescs),
                .pArgumentDescs = argumentDescs,
                .NodeMask = 0,
            };
            mDevice->GetD3DDevice()->CreateCommandSignature(&sigDesc,
                nullptr, IID_PPV_ARGS(&mIndirectSig));
        }

        auto& argsBinding = cache.RequireBinding(argsBuffer);
        //RangeInt range(0, 5 * 4);
        //cache.UpdateBufferData(CreateContext(), argsBuffer, std::span<RangeInt>(&range, 1));
        //cache.RequireBuffer(argsBuffer, argsBinding, mFrameHandle);
        cache.RequireState(CreateContext(), argsBinding, argsBuffer, D3D12_RESOURCE_STATE_INDIRECT_ARGUMENT);

        FlushBarriers();

        mCmdList->ExecuteIndirect(
            mIndirectSig.Get(), 1,
            argsBinding.mBuffer.Get(), argsBuffer.mOffset,
            nullptr, 0
        );
    }
    void DispatchCompute(const PipelineLayout* state, std::span<const void*> resources, Int3 groups) override {
        auto* pipelineState = (D3DResourceCache::D3DPipelineState*)state->mPipelineHash;
        if (pipelineState == nullptr) return;

        BindComputeState(pipelineState, resources);

        mCmdList->Dispatch(groups.x, groups.y, groups.z);
    }
    Readback CreateReadback(const RenderTarget2D* rt) {
        auto d3dRT = mDevice->GetResourceCache().RequireD3DRT(rt);
        auto context = CreateContext();
        context.mLockBits |= (1ull << 63);
        auto readback = mDevice->GetResourceCache().CreateReadback(context, *d3dRT);
        return Readback{ (uint64_t)readback };
    }
    int GetReadbackResult(const Readback& readback) {
        return mDevice->GetResourceCache().GetReadbackState((ID3D12Resource*)readback.mHandle);
    }
    int CopyAndDisposeReadback(Readback& readback, std::span<uint8_t> dest) {
        return mDevice->GetResourceCache().CopyAndDisposeReadback((ID3D12Resource*)readback.mHandle, dest);
    }
    // Send the commands to the GPU
    // TODO: Should this be automatic?
    void Execute() override {
        D3DResourceCache::D3DRenderSurface* presentSurface = nullptr;
        if (mSurface != nullptr) {
            presentSurface = mDevice->GetResourceCache().RequireD3DRT(&*mSurface->GetBackBuffer());
            //presentSurface = d3dRT;// mSurface->GetFrameBuffer();
        }

        SetD3DRenderTarget({ }, D3DResourceCache::D3DRenderSurfaceView());

        if (presentSurface != nullptr) {
            mBarrierStateManager.SetResourceState(
                presentSurface->mBuffer.Get(), presentSurface->mBarrierHandle, 0,
                D3D12_RESOURCE_STATE_PRESENT, presentSurface->mDesc);
            presentSurface->mBuffer.Reset();
        }
        FlushBarriers();

        ThrowIfFailed(mCmdList->Close());

        auto* cmdQueue = mDevice->GetDevice().GetCmdQueue();

        ID3D12CommandList* ppCommandLists[] = { mCmdList.Get(), };
        cmdQueue->ExecuteCommandLists(_countof(ppCommandLists), ppCommandLists);
        ThrowIfFailed(cmdQueue->Signal(mCmdAllocator->mFence.Get(), mCmdAllocator->GetHeadFrame()));
        --gActiveCmdBuffers;
    }

};

GraphicsDeviceD3D12::GraphicsDeviceD3D12()
    : mDevice()
    , mCache(mDevice, mStatistics)
{
    const auto& d3dDevice = mDevice.GetD3DDevice();
    D3D12_FEATURE_DATA_D3D12_OPTIONS options = {};
    ThrowIfFailed(d3dDevice->CheckFeatureSupport(D3D12_FEATURE_D3D12_OPTIONS, &options, sizeof(options)));
    if (!options.MinPrecisionSupport) {
        OutputDebugString(L"[Graphics] MinPrecision NOT supported\n");
    }

    D3D12_FEATURE_DATA_D3D12_OPTIONS7 features = {};
    ThrowIfFailed(d3dDevice->CheckFeatureSupport(D3D12_FEATURE_D3D12_OPTIONS7, &features, sizeof(features)));
    if (features.MeshShaderTier == D3D12_MESH_SHADER_TIER_NOT_SUPPORTED) {
        OutputDebugString(L"[Graphics] Mesh Shaders NOT supported!\n");
    }

    mCapabilities.mComputeShaders = true;
    mCapabilities.mMeshShaders = features.MeshShaderTier == D3D12_MESH_SHADER_TIER_1;
    mCapabilities.mMinPrecision = options.MinPrecisionSupport;
}
GraphicsDeviceD3D12::~GraphicsDeviceD3D12() {
}
#ifdef _DEBUG
#include <dxgidebug.h>
#endif
GraphicsDeviceD3D12::DisposeGuard::~DisposeGuard() {
#ifdef _DEBUG       // Check for memory leaks
    IDXGIDebug1* pDebug = nullptr;
    if (SUCCEEDED(DXGIGetDebugInterface1(0, IID_PPV_ARGS(&pDebug)))) {
        pDebug->ReportLiveObjects(DXGI_DEBUG_ALL, DXGI_DEBUG_RLO_FLAGS(DXGI_DEBUG_RLO_DETAIL | DXGI_DEBUG_RLO_IGNORE_INTERNAL));
        pDebug->Release();
    }
#endif
}

void GraphicsDeviceD3D12::CheckDeviceState() const {
    auto remove = GetD3DDevice()->GetDeviceRemovedReason();
    if (remove != S_OK) {
        WCHAR* errorString = nullptr;
        auto reason = mDevice.GetD3DDevice()->GetDeviceRemovedReason();
        FormatMessage(FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_IGNORE_INSERTS,
            nullptr, reason, MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT),
            (LPWSTR)&errorString, 0, nullptr);
        OutputDebugStringW(errorString);
        throw "Device is lost!";
    }
}

std::wstring GraphicsDeviceD3D12::GetDeviceName() const {
    auto* factory = mDevice.GetFactory();
    auto luid = mDevice.GetD3DDevice()->GetAdapterLuid();
    ComPtr<IDXGIAdapter1> pAdapter = nullptr;
    for (UINT adapterIndex = 0; factory->EnumAdapters1(adapterIndex, &pAdapter) != DXGI_ERROR_NOT_FOUND; ++adapterIndex) {
        DXGI_ADAPTER_DESC1 adapterDesc;
        if (SUCCEEDED(pAdapter->GetDesc1(&adapterDesc))) {
            if (memcmp(&adapterDesc.AdapterLuid, &luid, sizeof(luid)) == 0)
                return adapterDesc.Description;
        }
    }
    return L"";
}

CommandBuffer GraphicsDeviceD3D12::CreateCommandBuffer() {
    return CommandBuffer(new D3DCommandBuffer(this));
}
std::shared_ptr<GraphicsSurface> GraphicsDeviceD3D12::CreateSurface(WindowBase* window) {
    return std::make_shared<D3DGraphicsSurface>(GetDevice(), GetResourceCache(), ((WindowWin32*)window)->GetHWND());
}

CompiledShader GraphicsDeviceD3D12::CompileShader(const std::wstring_view& path, const std::string_view& entry,
    const std::string_view& profile, std::span<const MacroValue> macros) {
    D3DShader d3dshader;
    d3dshader.CompileFromFile(path.data(), entry.data(), profile.data(), macros);
    CompiledShader compiled;
    int size = (int)d3dshader.mShader->GetBufferSize();
    auto blob = compiled.AllocateBuffer(size);
    std::memcpy(blob.data(), d3dshader.mShader->GetBufferPointer(), size);
    compiled.SetName(path);
    compiled.GetReflection() = d3dshader.mReflection;
    return compiled;
}
CompiledShader GraphicsDeviceD3D12::CompileShader(const std::string_view& source, const std::string_view& entry,
    const std::string_view& profile, const std::wstring_view& dbgFilename) {
    D3DShader d3dshader;
    d3dshader.CompileFromSource(source, entry, profile, dbgFilename);
    if (d3dshader.mShader == nullptr) return { };
    CompiledShader compiled;
    int size = (int)d3dshader.mShader->GetBufferSize();
    auto blob = compiled.AllocateBuffer(size);
    std::memcpy(blob.data(), d3dshader.mShader->GetBufferPointer(), size);
    compiled.GetReflection() = d3dshader.mReflection;
    return compiled;
}

void GraphicsDeviceD3D12::WaitForGPU() {
    auto& cache = GetResourceCache();
    while (true) {
        auto handle = cache.GetFirstBusyAllocator();
        if (handle.mAllocatorId < 0) break;
        cache.AwaitAllocator(handle);
    }
}
