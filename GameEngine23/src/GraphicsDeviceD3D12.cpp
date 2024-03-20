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

// Handles receiving rendering events from the user application
// and issuing relevant draw commands
class D3DCommandBuffer : public CommandBufferInteropBase {
    GraphicsDeviceD3D12* mDevice;
    D3DGraphicsSurface* mSurface;
    int mFrameHandle;
    D3DResourceCache::CommandAllocator* mCmdAllocator;
    ComPtr<ID3D12GraphicsCommandList> mCmdList;
    ID3D12RootSignature* mLastRootSig;
    const D3DResourceCache::D3DPipelineState* mLastPipeline;
    const D3DConstantBuffer* mLastCBs[10];
    D3D12_GPU_DESCRIPTOR_HANDLE mLastResources[10];
    InplaceVector<D3DResourceCache::D3DRenderSurfaceView, 8> mFrameBuffers;
    D3DResourceCache::D3DRenderSurfaceView mDepthBuffer;
    std::vector<D3D12_VERTEX_BUFFER_VIEW> tVertexViews;
    std::vector<D3D12_RESOURCE_BARRIER> mDelayedBarriers;
    RectInt mViewportRect;
public:
    D3DCommandBuffer(GraphicsDeviceD3D12* device)
        : mDevice(device)
        , mCmdAllocator(nullptr)
    {
    }
    ID3D12Device* GetD3DDevice() const { return mDevice->GetD3DDevice(); }
    GraphicsDeviceBase* GetGraphics() const override {
        return mDevice;
    }
    // Get this command buffer ready to begin rendering
    virtual void Reset() override {
        mCmdAllocator = mDevice->GetResourceCache().RequireAllocator();
        if (mCmdList == nullptr) {
            D3D12_COMMAND_QUEUE_DESC queueDesc = {};
            queueDesc.Flags = D3D12_COMMAND_QUEUE_FLAG_NONE;
            queueDesc.Type = D3D12_COMMAND_LIST_TYPE_DIRECT;
            ThrowIfFailed(GetD3DDevice()
                ->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT,
                    mCmdAllocator->mCmdAllocator.Get(), nullptr, IID_PPV_ARGS(&mCmdList)));
            //ThrowIfFailed(mCmdList->Close());
        }
        else {
            mCmdList->Reset(mCmdAllocator->mCmdAllocator.Get(), nullptr);
        }

        mSurface = nullptr;
        mLastRootSig = nullptr;
        mLastPipeline = nullptr;
        std::fill(mLastCBs, mLastCBs + _countof(mLastCBs), nullptr);
        std::fill(mLastResources, mLastResources + _countof(mLastResources), D3D12_GPU_DESCRIPTOR_HANDLE{ .ptr = 0, });
        std::fill(mFrameBuffers.begin(), mFrameBuffers.end(), nullptr);
        mDepthBuffer = { };
    }
    virtual std::shared_ptr<GraphicsSurface> CreateSurface(WindowBase* window) override {
        auto surface = std::make_shared<D3DGraphicsSurface>(mDevice->GetDevice(), ((WindowWin32*)window)->GetHWND());
        return surface;
    }
    virtual void SetSurface(GraphicsSurface* surface) override {
        mSurface = (D3DGraphicsSurface*)surface;
        mFrameHandle = 1ull << mDevice->GetResourceCache().RequireFrameHandle((size_t)mSurface + (mSurface->GetBackFrameIndex() & 31));
        mCmdAllocator->mFrameLocks |= mFrameHandle;
        auto& cache = mDevice->GetResourceCache();
        auto* backBuffer = &*surface->GetBackBuffer();
        auto d3dRT = mDevice->GetResourceCache().RequireD3DRT(backBuffer);
        /*mSurface->GetFrameBuffer().RequireState(mDelayedBarriers,
            mDevice->GetResourceCache().mBarrierStateManager, D3D12_RESOURCE_STATE_COMMON, 0);*/
        D3DResourceCache::D3DRenderSurfaceView view(&mSurface->GetFrameBuffer());
        cache.RequireTextureRTV(view, mFrameHandle);
        //*d3dRT = mSurface->GetFrameBuffer();
        cache.SetRenderTargetMapping(backBuffer, mSurface->GetFrameBuffer());
        backBuffer->SetResolution(Int2(d3dRT->mDesc.mWidth, d3dRT->mDesc.mHeight));
        backBuffer->SetFormat((BufferFormat)d3dRT->mFormat);
        cache.AddInFlightSurface(std::dynamic_pointer_cast<D3DGraphicsSurface>(mSurface->This()));
        assert(d3dRT->mHandle >= 0);
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
            assert(d3dRt->mHandle >= 0);
            //if (target.mTarget == nullptr) d3dRt = &mSurface->GetFrameBuffer();
            d3dColorTargets.push_back(D3DResourceCache::D3DRenderSurfaceView(d3dRt, target.mMip, target.mSlice));
        }
        //if (colorTargets.empty() && depthTarget.mTarget == nullptr) d3dColorTargets.push_back(&mSurface->GetFrameBuffer());
        if (depthTarget.mTarget != nullptr)
            d3dDepthTarget = D3DResourceCache::D3DRenderSurfaceView(RequireInitializedRT(depthTarget.mTarget), depthTarget.mMip, depthTarget.mSlice);
        SetD3DRenderTarget(d3dColorTargets, d3dDepthTarget);
    }
    void FlushBarriers() {
        if (mDelayedBarriers.empty()) return;
        mCmdList->ResourceBarrier((UINT)mDelayedBarriers.size(), mDelayedBarriers.data());
        mDelayedBarriers.clear();
    }
    const D3DResourceCache::D3DRenderSurface* RequireInitializedRT(const RenderTarget2D* target) {
        auto d3dRt = target != nullptr ? mDevice->GetResourceCache().RequireD3DRT(target) : nullptr;
        if (d3dRt != nullptr && d3dRt->mBuffer == nullptr) {
            auto& cache = mDevice->GetResourceCache();
            auto d3dDevice = mDevice->GetD3DDevice();

            if (BufferFormatType::GetIsDepthBuffer(target->GetFormat())) {
                AllocateDepthBuffer(d3dRt, target->GetResolution(), target->GetFormat(), target->GetMipCount(), target->GetArrayCount());
            }
            else {
                AllocateRenderTarget(d3dRt, target->GetResolution(), target->GetFormat(), target->GetMipCount(), target->GetArrayCount());
            }
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
        mDevice->GetD3DDevice()->CreateCommittedResource(&D3D::DefaultHeap,
            D3D12_HEAP_FLAG_NONE, &texDesc,
            D3D12_RESOURCE_STATE_COMMON, &clearValue, IID_PPV_ARGS(&surface->mBuffer));
        surface->mBuffer->SetName(L"Texture RT");
        surface->mDesc.mWidth = (uint16_t)texDesc.Width;
        surface->mDesc.mHeight = (uint16_t)texDesc.Height;
        surface->mDesc.mMips = (uint8_t)texDesc.MipLevels;
        surface->mDesc.mSlices = (uint8_t)texDesc.DepthOrArraySize;
        surface->mFormat = texDesc.Format;
    }
    void AllocateRenderTarget(D3DResourceCache::D3DRenderSurface* surface, Int2 size, BufferFormat fmt, int mipCount, int arrayCount) {
        auto texDesc = CD3DX12_RESOURCE_DESC::Tex2D((DXGI_FORMAT)fmt,
            size.x, size.y, arrayCount, mipCount, 1, 0, D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET);
        FLOAT clearColor[]{ 0.0f, 0.0f, 0.0f, 0.0f };
        AllocateRTBuffer(surface, texDesc, CD3DX12_CLEAR_VALUE(texDesc.Format, clearColor));
    }
    void AllocateDepthBuffer(D3DResourceCache::D3DRenderSurface* surface, Int2 size, BufferFormat fmt, int mipCount, int arrayCount, bool memoryless = false) {
        auto flags = D3D12_RESOURCE_FLAG_ALLOW_DEPTH_STENCIL;
        if (memoryless) flags |= D3D12_RESOURCE_FLAG_DENY_SHADER_RESOURCE;
        auto texDesc = CD3DX12_RESOURCE_DESC::Tex2D((DXGI_FORMAT)fmt,
            size.x, size.y, arrayCount, mipCount, 1, 0, flags);
        AllocateRTBuffer(surface, texDesc, CD3DX12_CLEAR_VALUE(texDesc.Format, 1.0f, 0));
    }
    D3DResourceCache::D3DRenderSurface* RequireDepth(Int2 size) {
        auto& cache = mDevice->GetResourceCache();
        auto item = cache.depthBufferPool.find(size);
        if (item != cache.depthBufferPool.end()) return item->second.get();
        item = cache.depthBufferPool.insert(std::make_pair(
            size,
            std::make_unique<D3DResourceCache::D3DRenderSurface>()
        )).first;
        item->second->mHandle = cache.mResourceCount++;
        auto* surface = item->second.get();
        AllocateDepthBuffer(surface, size, BufferFormat::FORMAT_D24_UNORM_S8_UINT, 1, 1, true);
        auto d3dDevice = mDevice->GetD3DDevice();
        return surface;
    }
    void SetD3DRenderTarget(std::span<const D3DResourceCache::D3DRenderSurfaceView> frameBuffers, D3DResourceCache::D3DRenderSurfaceView depthBuffer) {
        bool same = mDepthBuffer == depthBuffer;
        auto& cache = mDevice->GetResourceCache();
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
                    int subresource = dstBuffer.GetSubresource();
                    dstBuffer->UnlockState(cache.mBarrierStateManager,
                        D3D12_RESOURCE_STATE_RENDER_TARGET, subresource);
                }
                dstBuffer = srcBuffer;
                if (dstBuffer != nullptr) {
                    int subresource = dstBuffer.GetSubresource();
                    dstBuffer->RequireState(mDelayedBarriers, cache.mBarrierStateManager,
                        D3D::BarrierStateManager::CreateLocked(D3D12_RESOURCE_STATE_RENDER_TARGET), subresource);
                }
            }
            if (mDepthBuffer != depthBuffer) {
                //if (mDepthBuffer != nullptr) mDepthBuffer->RequireState(barriers, D3D12_RESOURCE_STATE_DEPTH_READ);
                mDepthBuffer = depthBuffer;
                if (mDepthBuffer != nullptr) mDepthBuffer->RequireState(mDelayedBarriers,
                    mDevice->GetResourceCache().mBarrierStateManager, D3D12_RESOURCE_STATE_DEPTH_WRITE,
                    mDepthBuffer.GetSubresource());
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
        } else {
            mCmdList->OMSetRenderTargets(0, nullptr, FALSE, nullptr);
        }
    }
    // Clear the screen
    void ClearRenderTarget(const ClearConfig& clear) override
    {
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

    void* RequireConstantBuffer(std::span<const uint8_t> data, size_t hash) override {
        auto& cache = mDevice->GetResourceCache();
        return cache.RequireConstantBuffer(mCmdList.Get(), mFrameHandle, data, hash);
    }
    void CopyBufferData(const BufferLayout& buffer, std::span<const RangeInt> ranges) override {
        auto& cache = mDevice->GetResourceCache();
        cache.UpdateBufferData(mCmdList.Get(), mFrameHandle, buffer, ranges);
    }
    void BindPipelineState(D3DResourceCache::D3DPipelineState* pipelineState)
    {
        if (mLastPipeline == pipelineState) return;
        // Require and bind a pipeline matching the material config and mesh attributes
        if (mLastRootSig != pipelineState->mRootSignature->mRootSignature.Get())
        {
            mLastRootSig = pipelineState->mRootSignature->mRootSignature.Get();
            mCmdList->SetGraphicsRootSignature(mLastRootSig);
            auto srvHeap = mDevice->GetSRVHeap();
            mCmdList->SetDescriptorHeaps(1, &srvHeap);
            mCmdList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        }

        mLastPipeline = pipelineState;
        mCmdList->SetPipelineState(pipelineState->mPipelineState.Get());
    }
    virtual const PipelineLayout* RequirePipeline(
        const CompiledShader& vertexShader, const CompiledShader& pixelShader,
        const MaterialState& materialState, std::span<const BufferLayout*> bindings
    ) override
    {
        mDevice->CheckDeviceState();

        InplaceVector<DXGI_FORMAT> frameBufferFormats;
        for (auto& fb : mFrameBuffers) frameBufferFormats.push_back(fb->mFormat);
        DXGI_FORMAT depthBufferFormat = mDepthBuffer->mFormat;
        auto pipelineState = mDevice->GetResourceCache().RequirePipelineState(
            vertexShader, pixelShader, materialState, bindings,
            frameBufferFormats, depthBufferFormat
        );
        if (pipelineState->mLayout == nullptr)
        {
            pipelineState->mLayout = std::make_unique<PipelineLayout>();
            pipelineState->mLayout->mRootHash = (size_t)pipelineState->mRootSignature;
            pipelineState->mLayout->mPipelineHash = pipelineState->mPipelineState != nullptr ? (size_t)pipelineState : 0;
            pipelineState->mLayout->mConstantBuffers = pipelineState->mConstantBuffers;
            pipelineState->mLayout->mResources = pipelineState->mResourceBindings;
            for (auto& b : bindings) pipelineState->mLayout->mBindings.push_back(b);
            pipelineState->mLayout->mMaterialState = materialState;
        }
        return pipelineState->mLayout.get();
    }
    void DrawMesh(std::span<const BufferLayout*> bindings, const PipelineLayout* state, std::span<const void*> resources, const DrawConfig& config, int instanceCount = 1, const char* name = nullptr) override
    {
        auto* pipelineState = (D3DResourceCache::D3DPipelineState*)state->mPipelineHash;
        if (pipelineState == nullptr) return;
        auto& cache = mDevice->GetResourceCache();

        mDevice->CheckDeviceState();

        BindPipelineState(pipelineState);

        int r = 0;
        int stencilRef = -1;
        if (pipelineState->mLayout->mMaterialState.mDepthMode.GetStencilEnable()) {
            stencilRef = (int)(intptr_t)resources[r++];
        }
        // Require and bind constant buffers
        for (int i = 0; i < pipelineState->mConstantBuffers.size(); ++i) {
            auto cb = pipelineState->mConstantBuffers[i];
            auto d3dCB = (D3DConstantBuffer*)resources[r++];
            if (mLastCBs[cb->mBindPoint] == d3dCB) continue;
            mLastCBs[cb->mBindPoint] = d3dCB;
            mCmdList->SetGraphicsRootConstantBufferView(cb->mBindPoint, d3dCB->mConstantBuffer->GetGPUVirtualAddress());
        }
        // Require and bind other resources (textures)
        for (int i = 0; i < pipelineState->mResourceBindings.size(); ++i) {
            auto* rb = pipelineState->mResourceBindings[i];
            auto* resource = (BufferReference*)&resources[r++];
            ++r;
            int srvOffset = -1;
            if (rb->mType == ShaderBase::ResourceTypes::R_SBuffer) {
                assert(resource->mType == BufferReference::BufferTypes::Buffer);
                auto* rbinding = cache.GetBinding((uint64_t)resource->mBuffer);
                assert(rbinding != nullptr); // "Did you call CopyBufferData on this resource?");
                srvOffset = rbinding->mSRVOffset;
            }
            else {
                if (resource->mType == BufferReference::BufferTypes::RenderTarget) {
                    auto* rt = static_cast<RenderTarget2D*>(resource->mBuffer);
                    auto* surface = cache.RequireD3DRT(rt);
                    if (surface->mBuffer != nullptr) {
                        assert(surface->mBuffer.Get() != nullptr);
                        D3D12_RESOURCE_STATES barrierState = D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE | D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
                        if (mDepthBuffer->mBuffer.Get() == surface->mBuffer.Get()) barrierState |= D3D12_RESOURCE_STATE_DEPTH_READ;
                        surface->RequireState(mDelayedBarriers, mDevice->GetResourceCache().mBarrierStateManager,
                            barrierState, -1);
                        //srvOffset = surface->mSRVOffset;
                        auto viewFmt = surface->mFormat;
                        if (viewFmt == DXGI_FORMAT_D24_UNORM_S8_UINT) viewFmt = DXGI_FORMAT_R24_UNORM_X8_TYPELESS;
                        if (viewFmt == DXGI_FORMAT_D32_FLOAT) viewFmt = DXGI_FORMAT_R32_FLOAT;
                        if (viewFmt == DXGI_FORMAT_D16_UNORM) viewFmt = DXGI_FORMAT_R16_UNORM;
                        srvOffset = cache.GetTextureSRV(surface->mBuffer.Get(),
                            viewFmt, false, rt->GetArrayCount(), mFrameHandle,
                            resource->mSubresourceId, resource->mSubresourceCount);
                        FlushBarriers();
                    }
                } else {
                    auto tex = reinterpret_cast<Texture*>(resource->mBuffer);
                    srvOffset = cache.RequireCurrentTexture(tex, mCmdList.Get(), mFrameHandle)->mSRVOffset;
                }
                if (srvOffset == -1) {
                    // TODO: Print log message (first time)
                    srvOffset = cache.RequireDefaultTexture(mCmdList.Get(), mFrameHandle)->mSRVOffset;
                }
            }
            if (srvOffset == -1) break;
            auto rootSig = pipelineState->mRootSignature;
            auto handle = mDevice->GetSRVHeap()->GetGPUDescriptorHandleForHeapStart();
            handle.ptr += srvOffset;
            auto bindingId = rootSig->mNumConstantBuffers + rb->mBindPoint;
            if (mLastResources[bindingId].ptr == handle.ptr) continue;
            mCmdList->SetGraphicsRootDescriptorTable(bindingId, handle);
            mLastResources[bindingId] = handle;
        }

        int indexCount = -1;
        D3D12_INDEX_BUFFER_VIEW indexView{
            .BufferLocation = 0,
            .SizeInBytes = 0,
            .Format = DXGI_FORMAT_UNKNOWN,
        };
        if (bindings.empty()) {
            mCmdList->IASetIndexBuffer(&indexView);
        }
        else {
            tVertexViews.clear();
            tVertexViews.reserve(2);
            cache.ComputeElementData(bindings, mCmdList.Get(), mFrameHandle, tVertexViews, indexView, indexCount);
            mCmdList->IASetVertexBuffers(0, (uint32_t)tVertexViews.size(), tVertexViews.data());
            if (indexView.Format != DXGI_FORMAT_UNKNOWN) mCmdList->IASetIndexBuffer(&indexView);
        }
        
        // Issue the draw calls
        if (config.mIndexCount >= 0) indexCount = config.mIndexCount;
        if (stencilRef >= 0) mCmdList->OMSetStencilRef((UINT)stencilRef);
        if (indexView.Format != DXGI_FORMAT_UNKNOWN)
            mCmdList->DrawIndexedInstanced(indexCount, std::max(1, instanceCount), config.mIndexBase, 0, 0);
        else
            mCmdList->DrawInstanced(indexCount, std::max(1, instanceCount), config.mIndexBase, 0);
        cache.mStatistics.mDrawCount++;
        cache.mStatistics.mInstanceCount += instanceCount;
    }
    // Send the commands to the GPU
    // TODO: Should this be automatic?
    void Execute() override
    {
        auto d3dRT = mDevice->GetResourceCache().RequireD3DRT(&*mSurface->GetBackBuffer());
        auto& backBuffer = *d3dRT;// mSurface->GetFrameBuffer();

        SetD3DRenderTarget({ }, D3DResourceCache::D3DRenderSurfaceView());

        backBuffer.RequireState(mDelayedBarriers, mDevice->GetResourceCache().mBarrierStateManager,
            D3D12_RESOURCE_STATE_PRESENT, 0);
        backBuffer.mBuffer.Reset();
        FlushBarriers();

        ThrowIfFailed(mCmdList->Close());

        ID3D12CommandList* ppCommandLists[] = { mCmdList.Get(), };
        mDevice->GetDevice().GetCmdQueue()->ExecuteCommandLists(_countof(ppCommandLists), ppCommandLists);
        //mDevice->GetResourceCache().mBarrierStateManager.Clear();
    }

};

GraphicsDeviceD3D12::GraphicsDeviceD3D12()
    : mDevice()
    , mCache(mDevice, mStatistics)
{
    //WaitForGPU();
}
GraphicsDeviceD3D12::~GraphicsDeviceD3D12()
{
}

void GraphicsDeviceD3D12::CheckDeviceState() const
{
    auto remove = GetD3DDevice()->GetDeviceRemovedReason();
    if (remove != S_OK)
    {
        WCHAR* errorString = nullptr;
        auto reason = mDevice.GetD3DDevice()->GetDeviceRemovedReason();
        FormatMessage(FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_IGNORE_INSERTS,
            nullptr, reason, MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT),
            (LPWSTR)&errorString, 0, nullptr);
        OutputDebugStringW(errorString);
        throw "Device is lost!";
    }
}

CommandBuffer GraphicsDeviceD3D12::CreateCommandBuffer()
{
    return CommandBuffer(new D3DCommandBuffer(this));
}

CompiledShader GraphicsDeviceD3D12::CompileShader(const std::wstring_view& path, const std::string_view& entry,
    const std::string_view& profile, std::span<const MacroValue> macros) {
    CompiledShader compiled;
    bool wasCreated = false;
    DxcDefine d3dMacros[128];
    auto count = std::min(macros.size(), _countof(d3dMacros) - 1);
    for (int m = 0; m < count; ++m) {
        d3dMacros[m] = DxcDefine{
            .Name = macros[m].mName.GetWName().c_str(),
            .Value = macros[m].mValue.GetWName().c_str(),
        };
    }
    d3dMacros[count] = { };
    D3DShader d3dshader;
    d3dshader.CompileFromFile(path.data(), entry.data(), profile.data(), d3dMacros);
    auto blob = compiled.AllocateBuffer((int)d3dshader.mShader->GetBufferSize());
    std::memcpy(blob.data(), d3dshader.mShader->GetBufferPointer(), blob.size());
    compiled.SetName(path);
    compiled.GetReflection() = d3dshader.mReflection;
    return compiled;
}

/*const PipelineLayout* GraphicsDeviceD3D12::RequirePipeline(
    const Shader& vertexShader, const Shader& pixelShader,
    const MaterialState& materialState, std::span<const BufferLayout*> bindings,
    std::span<const MacroValue> macros, const IdentifierWithName& renderPass
)
{
    CheckDeviceState();

    InplaceVector<DXGI_FORMAT> fbFormats;
    //fbFormats.push_back(mPrimarySurface.GetFrameBuffer().mFormat);
    auto pipelineState = mCache.RequirePipelineState(vertexShader, pixelShader, materialState, bindings, macros, renderPass,
        fbFormats, DXGI_FORMAT_D24_UNORM_S8_UINT);
    if (pipelineState->mLayout == nullptr)
    {
        pipelineState->mLayout = std::make_unique<PipelineLayout>();
        pipelineState->mLayout->mRenderPass = renderPass;
        pipelineState->mLayout->mRootHash = (size_t)pipelineState->mRootSignature;
        pipelineState->mLayout->mPipelineHash = pipelineState->mPipelineState != nullptr ? (size_t)pipelineState : 0;
        pipelineState->mLayout->mConstantBuffers = pipelineState->mConstantBuffers;
        pipelineState->mLayout->mResources = pipelineState->mResourceBindings;
        for (auto& b : bindings) pipelineState->mLayout->mBindings.push_back(b);
    }
    return pipelineState->mLayout.get();
}*/

// Flip the backbuffer and wait until a frame is available to be rendered
/*void GraphicsDeviceD3D12::Present() {
    int disposedFrame = mPrimarySurface.Present();
    mCache.UnlockFrame((size_t)&mPrimarySurface + disposedFrame);
}
*/