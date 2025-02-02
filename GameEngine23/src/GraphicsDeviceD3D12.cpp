#include <span>
#include <vector>
#include <memory>
#include <utility>
#include <algorithm>
#include <stdexcept>

#include "GraphicsDeviceD3D12.h"
#include "D3DResourceCache.h"
#include "D3DGraphicsSurface.h"
#include "D3DRaytracing.h"
#include "D3DShader.h"
#include "Resources.h"

#include <d3dcompiler.h>
#include <d3dx12.h>

// Handle dynamically loading WinPixEventRuntime.dll
#include "PIXDynamic.cpp"

extern void* SimpleProfilerMarker(const char* name);
extern void SimpleProfilerMarkerEnd(void* zone);

D3DRaytracing raytracing;

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
    D3DResourceCache::CBBumpAllocator cbBumpAllocator;
public:
    D3DCommandBuffer(GraphicsDeviceD3D12* device)
        : mDevice(device)
        , mCmdAllocator(nullptr)
    {
        cbBumpAllocator.mBumpConstantBuffer = -1;
        tVertexViews.reserve(4);
    }
    ~D3DCommandBuffer() {
    }
    ID3D12Device* GetD3DDevice() const { return mDevice->GetD3DDevice(); }
    GraphicsDeviceBase* GetGraphics() const override {
        return mDevice;
    }
    virtual void BeginScope(const std::wstring_view& name) override {
        PIXMarkerBegin(mCmdList.Get(), name);
    }
    virtual void EndScope() override {
        PIXMarkerEnd(mCmdList.Get());
    }
    // Get this command buffer ready to begin rendering
    virtual void Reset() override {
        mCmdAllocator = mDevice->GetResourceCache().RequireAllocator();
        mFrameHandle = 1ull << mCmdAllocator->mId;
        if (mCmdList == nullptr) {
            ThrowIfFailed(GetD3DDevice()
                ->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT,
                    mCmdAllocator->mCmdAllocator.Get(), nullptr, IID_PPV_ARGS(&mCmdList)));
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
    }
    virtual void SetSurface(GraphicsSurface* surface) override {
        SetD3DRenderTargets({ }, D3DResourceCache::D3DRenderSurfaceView());

        if (mSurface != nullptr) {
            D3DResourceCache::D3DRenderSurface* presentSurface
                = mDevice->GetResourceCache().RequireD3DRT(&*mSurface->GetBackBuffer());
            mBarrierStateManager.SetResourceState(
                presentSurface->mBuffer.Get(), presentSurface->mBarrierHandle, 0,
                D3D12_RESOURCE_STATE_PRESENT, presentSurface->mDesc);
            presentSurface->mBuffer.Reset();
        }

        mSurface = (D3DGraphicsSurface*)surface;

        if (mSurface != nullptr) {
            // Wait for whatever allocator was previously rendering to this surface
            auto& waitHandle = mSurface->GetFrameWaitHandle();
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
            d3dColorTargets.push_back(D3DResourceCache::D3DRenderSurfaceView(d3dRt, target.mMip, target.mSlice));
        }
        if (depthTarget.mTarget != nullptr)
            d3dDepthTarget = D3DResourceCache::D3DRenderSurfaceView(RequireInitializedRT(depthTarget.mTarget), depthTarget.mMip, depthTarget.mSlice);

        SetD3DRenderTargets(d3dColorTargets, d3dDepthTarget);
    }
    void FlushBarriers() {
        mDevice->GetResourceCache().FlushBarriers(CreateContext());
    }

    const D3DResourceCache::D3DRenderSurface* RequireInitializedRT(const RenderTarget2D* target) {
        auto d3dRt = target != nullptr ? mDevice->GetResourceCache().RequireD3DRT(target) : nullptr;
        if (d3dRt != nullptr && d3dRt->mBuffer == nullptr) {
            auto isDepth = BufferFormatType::GetIsDepthBuffer(target->GetFormat());
            auto texDesc = CD3DX12_RESOURCE_DESC::Tex2D((DXGI_FORMAT)target->GetFormat(),
                target->GetResolution().x, target->GetResolution().y,
                target->GetArrayCount(), target->GetMipCount(), 1, 0,
                isDepth ? D3D12_RESOURCE_FLAG_ALLOW_DEPTH_STENCIL : D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET);

            //if (memoryless) texDesc.Flags |= D3D12_RESOURCE_FLAG_DENY_SHADER_RESOURCE;
            if (target->GetAllowUnorderedAccess()) texDesc.Flags |= D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS;

            static FLOAT clearColor[]{ 0.0f, 0.0f, 0.0f, 0.0f };
            D3D12_CLEAR_VALUE clearValue = isDepth ? CD3DX12_CLEAR_VALUE(texDesc.Format, 1.0f, 0)
                : CD3DX12_CLEAR_VALUE(texDesc.Format, clearColor);

            OutputDebugStringA("Allocating texture\n");
            assert(d3dRt->mFormat != (DXGI_FORMAT)(-1));

            // Create the render target
            ThrowIfFailed(mDevice->GetD3DDevice()->CreateCommittedResource(&D3D::DefaultHeap,
                D3D12_HEAP_FLAG_NONE, &texDesc,
                D3D12_RESOURCE_STATE_COMMON, &clearValue, IID_PPV_ARGS(&d3dRt->mBuffer)));
            d3dRt->mBuffer->SetName(target->GetName().c_str());
            d3dRt->mDesc.mWidth = (uint16_t)texDesc.Width;
            d3dRt->mDesc.mHeight = (uint16_t)texDesc.Height;
            d3dRt->mDesc.mMips = (uint8_t)texDesc.MipLevels;
            d3dRt->mDesc.mSlices = (uint8_t)texDesc.DepthOrArraySize;
            d3dRt->mFormat = texDesc.Format;

            auto* device = mDevice;
            d3dRt->mOnDispose = const_cast<RenderTarget2D*>(target)->OnDestroy.Add([=]() {
                if (d3dRt->mBuffer == nullptr) return;
                OutputDebugStringA("Disposing texture ");
                OutputDebugString(target->GetName().c_str());
                OutputDebugStringA("\n");
                auto& cache = device->GetResourceCache();
                cache.DestroyD3DRT(target, 0);
            });
        }
        return d3dRt;
    }
    void SetD3DRenderTargets(std::span<const D3DResourceCache::D3DRenderSurfaceView> frameBuffers, D3DResourceCache::D3DRenderSurfaceView depthBuffer) {
        bool same = mDepthBuffer == depthBuffer && frameBuffers.size() == mFrameBuffers.size();
        same = same && std::equal(frameBuffers.begin(), frameBuffers.end(), mFrameBuffers.begin());
        if (same) return;

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

        auto& cache = mDevice->GetResourceCache();
        //SetViewport(RectInt(0, 0, anyBuffer->mDesc.mWidth, anyBuffer->mDesc.mHeight));

        InplaceVector<D3D12_CPU_DESCRIPTOR_HANDLE, 8> targets;
        for (int i = 0; i < (int)frameBuffers.size(); ++i) {
            auto& surface = cache.RequireTextureRTV(mFrameBuffers[i], mFrameHandle);
            assert((uint32_t)surface.mRTVOffset < 4096);
            targets.push_back(CD3DX12_CPU_DESCRIPTOR_HANDLE(mDevice->GetRTVHeap()->GetCPUDescriptorHandleForHeapStart(), surface.mRTVOffset));
        }
        auto depthRTV = mDepthBuffer != nullptr ? cache.RequireTextureRTV(mDepthBuffer, mFrameHandle).mRTVOffset : -1;
        CD3DX12_CPU_DESCRIPTOR_HANDLE depthHandle(mDevice->GetDSVHeap()->GetCPUDescriptorHandleForHeapStart(), depthRTV);
        mCmdList->OMSetRenderTargets(targets.size(), targets.data(), FALSE, depthRTV == -1 ? nullptr : &depthHandle);

        // Dont know if this is required, but NSight showed draw calls failing without it
        // Probably also need to clear bound resource cache?
        mGraphicsRoot = { };
        mComputeRoot = { };
    }
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
            hash = hash * 12345 + fb.mSurface->mFormat;
        }
        return hash;
    }

    D3DCommandContext& CreateContext() {
        return mCmdContext;
    }

    void* RequireConstantBuffer(std::span<const uint8_t> data, size_t hash) override {
        auto& cache = mDevice->GetResourceCache();
        return cache.RequireConstantBuffer(CreateContext(), data, hash, cbBumpAllocator);
    }
    UINT64 GetBufferGPUAddress(const BufferLayout& buffer) {
        auto& cache = mDevice->GetResourceCache();
        auto binding = cache.RequireBinding(buffer);
        return binding.mGPUMemory;
    }
    void CopyBufferData(const BufferLayout& buffer, std::span<const RangeInt> ranges) override {
        auto& cache = mDevice->GetResourceCache();
        cache.UpdateBufferData(CreateContext(), buffer, ranges);
    }
    void CopyBufferData(const BufferLayout& source, const BufferLayout& dest, int srcOffset, int dstOffset, int length) override {
        auto& cache = mDevice->GetResourceCache();
        cache.CopyBufferData(CreateContext(), source, dest, srcOffset, dstOffset, length);
    }
    void CommitTexture(const Texture* texture) {
        auto& cache = mDevice->GetResourceCache();
        cache.RequireCurrentTexture(texture, CreateContext());
    }

    void BindPipelineState(const D3DResourceCache::D3DPipelineState* pipelineState) {
        if (mGraphicsRoot.mLastPipeline == pipelineState) return;
        mDevice->CheckDeviceState();
        // Require and bind a pipeline matching the material config and mesh attributes
        if (mGraphicsRoot.mLastRootSig != pipelineState->mRootSignature) {
            mGraphicsRoot.mLastRootSig = pipelineState->mRootSignature;
            mCmdList->SetGraphicsRootSignature(mGraphicsRoot.mLastRootSig->mRootSignature.Get());
            mCmdList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        }

        // Depth buffer might be in Read mode, ensure it is write mode if required
        if (pipelineState->mMaterialState.mDepthMode.GetDepthWrite()) {
            assert(mDepthBuffer != nullptr);
            if (mDepthBuffer != nullptr) {
                mBarrierStateManager.SetResourceState(
                    mDepthBuffer->mBuffer.Get(), mDepthBuffer->mBarrierHandle, mDepthBuffer.GetSubresource(),
                    D3D12_RESOURCE_STATE_DEPTH_WRITE, mDepthBuffer->mDesc);
            }
        }

        mGraphicsRoot.mLastPipeline = pipelineState;
        mComputeRoot.mLastPipeline = nullptr;
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
        mGraphicsRoot.mLastPipeline = nullptr;
        if (pipelineState->mPipelineState == nullptr) {
            auto raytrace = (const D3DResourceCache::D3DPipelineRaytrace*)pipelineState;
            mCmdList->SetPipelineState1(raytrace->mRaytracePSO.Get());
        }
        else {
            mCmdList->SetPipelineState(pipelineState->mPipelineState.Get());
        }
    }

    virtual const PipelineLayout* RequirePipeline(
        const ShaderStages& shaders,
        const MaterialState& materialState, std::span<const BufferLayout*> bindings
    ) override {
        mDevice->CheckDeviceState();

        InplaceVector<DXGI_FORMAT> frameBufferFormats;
        for (auto& fb : mFrameBuffers) frameBufferFormats.push_back(fb->mFormat);
        auto depthBufferFormat = mDepthBuffer != nullptr ? mDepthBuffer->mFormat : DXGI_FORMAT_UNKNOWN;
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
    const PipelineLayout* RequireRaytracePSO(const CompiledShader& rayGenShader, const CompiledShader& hitShader, const CompiledShader& missShader) override {
        auto pipelineState = mDevice->GetResourceCache().RequireRaytracePSO(rayGenShader, hitShader, missShader);
        return pipelineState->mLayout.get();
    }

    int BindConstantBuffers(std::pair<int, const D3DConstantBuffer*> constantBinds[32], std::vector<const ShaderBase::ConstantBuffer*> cbuffers, std::span<const void*> resources, int& r) {
        cbBumpAllocator.mBumpConstantBuffer = -1;
        for (int i = 0; i < cbuffers.size(); ++i) {
            constantBinds[i] = { cbuffers[i]->mBindPoint, (D3DConstantBuffer*)resources[r++] };
        }
        return (int)cbuffers.size();
    }

    int BindResources(std::tuple<int, int, UINT64> bindPoints[32], std::vector<const ShaderBase::ResourceBinding*> bindings, std::span<const void*> resources, int& r
        , int srvBindOffset, int uavBindOffset) {
        auto& cache = mDevice->GetResourceCache();
        for (int i = 0; i < bindings.size(); ++i) {
            auto* rb = bindings[i];
            auto* resource = (BufferReference*)&resources[r];
            r += 2;
            int bindPoint = rb->mBindPoint;
            int bindOffset = 0;
            int srvOffset = -1;
            UINT64 addr = 0;
            if (resource->mType == BufferReference::BufferTypes::Buffer) {
                auto* rbinding = cache.GetBinding((uint64_t)resource->mBuffer);
                assert(rbinding != nullptr); // Did you call CopyBufferData on this resource?
                D3D12_RESOURCE_STATES barrierState = D3D12_RESOURCE_STATE_VERTEX_AND_CONSTANT_BUFFER | D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE | D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
                // Explicit offset/count or get buffer count (skipping first item for "count" storage)
                int offset = resource->mSubresourceId;
                int count = resource->mSubresourceCount != -1 ? (uint16_t)resource->mSubresourceCount
                    : rbinding->mCount != -1 ? rbinding->mCount - offset
                    : rbinding->mSize / rbinding->mStride - (++offset);
                if (rb->mType == ShaderBase::ResourceTypes::R_SBuffer) {
                    srvOffset = cache.GetBufferSRV(*rbinding,
                        offset, count, rbinding->mStride, mFrameHandle);
                    bindOffset = srvBindOffset;
                } else if (rb->mType == ShaderBase::ResourceTypes::R_UAVBuffer
                    || rb->mType == ShaderBase::ResourceTypes::R_UAVAppend
                    || rb->mType == ShaderBase::ResourceTypes::R_UAVConsume) {
                    srvOffset = cache.GetBufferUAV(rbinding->mBuffer.Get(),
                        count, rbinding->mStride, D3D12_BUFFER_UAV_FLAG_NONE, mFrameHandle);
                    bindOffset = uavBindOffset;
                    barrierState = D3D12_RESOURCE_STATE_UNORDERED_ACCESS;
                }
                cache.RequireState(CreateContext(), *rbinding, { }, barrierState);
            }
            else if (resource->mType == BufferReference::BufferTypes::RenderTarget) {
                auto* rt = static_cast<RenderTarget2D*>(resource->mBuffer);
                auto* d3dTex = RequireInitializedRT(rt);
                assert(d3dTex->mBuffer != nullptr);
                assert(d3dTex->mBuffer.Get() != nullptr);
                assert(d3dTex->mFormat != (DXGI_FORMAT)(-1));
                auto viewFmt = (DXGI_FORMAT)resource->mFormat;
                if (viewFmt == DXGI_FORMAT_UNKNOWN) viewFmt = d3dTex->mFormat;
                if (viewFmt == DXGI_FORMAT_D24_UNORM_S8_UINT) viewFmt = DXGI_FORMAT_R24_UNORM_X8_TYPELESS;
                if (viewFmt == DXGI_FORMAT_D32_FLOAT) viewFmt = DXGI_FORMAT_R32_FLOAT;
                if (viewFmt == DXGI_FORMAT_D16_UNORM) viewFmt = DXGI_FORMAT_R16_UNORM;
                D3D12_RESOURCE_STATES barrierState = D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE | D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
                int resOffset = 0, resCount = resource->mSubresourceCount;
                if (resCount == -1) resCount = rt->GetMipCount() - resource->mSubresourceId;
                if (rb->mType == ShaderBase::ResourceTypes::R_UAVBuffer) {
                    barrierState = D3D12_RESOURCE_STATE_UNORDERED_ACCESS;
                    srvOffset = cache.GetUAV(d3dTex->mBuffer.Get(), viewFmt,
                        false, rt->GetArrayCount(), mFrameHandle, resOffset, resCount);
                    bindOffset = uavBindOffset;
                }
                else if (rb->mType == ShaderBase::ResourceTypes::R_Texture) {
                    if (mDepthBuffer != nullptr && mDepthBuffer->mBuffer.Get() == d3dTex->mBuffer.Get()) barrierState |= D3D12_RESOURCE_STATE_DEPTH_READ;
                    srvOffset = cache.GetTextureSRV(d3dTex->mBuffer.Get(), viewFmt,
                        false, rt->GetArrayCount(), mFrameHandle, resOffset, resCount);
                    bindOffset = srvBindOffset;
                }
                mBarrierStateManager.SetResourceState(
                    d3dTex->mBuffer.Get(), d3dTex->mBarrierHandle,
                    -1, barrierState, d3dTex->mDesc);
            }
            else if (resource->mType == BufferReference::BufferTypes::Texture) {
                auto tex = reinterpret_cast<Texture*>(resource->mBuffer);
                if (tex == nullptr || tex->GetSize().x == 0) tex = cache.RequireDefaultTexture();
                auto* d3dTex = cache.RequireTexture(tex, CreateContext());
                D3D12_RESOURCE_STATES barrierState = D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE | D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
                if (d3dTex->mBuffer == nullptr) {
                } else if (rb->mType == ShaderBase::ResourceTypes::R_UAVBuffer) {
                    int resOffset = 0, resCount = resource->mSubresourceCount;
                    if (resCount == -1) resCount = tex->GetMipCount() - resource->mSubresourceId;

                    cache.RequireBarrierHandle(d3dTex);
                    barrierState = D3D12_RESOURCE_STATE_UNORDERED_ACCESS;
                    srvOffset = cache.GetUAV(d3dTex->mBuffer.Get(), d3dTex->mFormat,
                        tex->GetSize().z > 1, tex->GetArrayCount(), mFrameHandle, resOffset, resCount);
                    bindOffset = uavBindOffset;
                }
                else if (rb->mType == ShaderBase::ResourceTypes::R_Texture) {
                    srvOffset = cache.RequireTextureSRV(*d3dTex, mFrameHandle);
                    if (d3dTex->mBuffer != nullptr) {
                        //srvOffset = -2;
                        //addr = d3dTex->mBuffer->GetGPUVirtualAddress();
                    }
                    bindOffset = srvBindOffset;
                }
                if (d3dTex->mBarrierHandle != D3D::BarrierHandle::Invalid) {
                    mBarrierStateManager.SetResourceState(
                        d3dTex->mBuffer.Get(), d3dTex->mBarrierHandle,
                        -1, barrierState, D3D::BarrierMeta(tex->GetMipCount()));
                }
            }
            else if (resource->mType == BufferReference::BufferTypes::GPUAddress) {
                auto d3daccl = (D3DAccelerationStructure*)resource->mBuffer;
                assert(rb->mType == ShaderBase::ResourceTypes::R_RTAS);
                addr = d3daccl->GetGPUAddress();
                srvOffset = -2;
                bindOffset = srvBindOffset;
            }
            if (srvOffset == -1 && rb->mType == ShaderBase::ResourceTypes::R_Texture) {
                auto* d3dTex = cache.RequireCurrentTexture(cache.RequireDefaultTexture(), CreateContext());
                if (d3dTex != nullptr) {
                    srvOffset = cache.RequireTextureSRV(*d3dTex, mFrameHandle);
                    bindOffset = srvBindOffset;
                }
            }
            if (srvOffset == -1) {
                std::string str = "Failed to find resource for " + rb->mName.GetName();
                MessageBoxA(0, str.c_str(), "Resource error", 0);
                return -1;
            }
            assert(bindPoint >= 0);
            bindPoints[i] = { bindPoint + bindOffset, srvOffset, addr };
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
        std::tuple<int, int, UINT64> resourceBinds[32];
        int rCount = BindResources(resourceBinds, pipelineState->mLayout->mResources, resources, r,
            pipelineState->mRootSignature->mNumConstantBuffers, pipelineState->mRootSignature->mNumConstantBuffers + pipelineState->mRootSignature->mSRVCount);
        if (cCount == -1 || rCount == -1) return;

        FlushBarriers();
        auto& cache = mDevice->GetResourceCache();
        for (int i = 0; i < cCount; ++i) {
            int bindPoint = constantBinds[i].first;
            auto* d3dCB = constantBinds[i].second;
            if (mGraphicsRoot.mLastCBs[bindPoint] == d3dCB) continue;
            mGraphicsRoot.mLastCBs[bindPoint] = d3dCB;
            auto* constantBuffer = cache.GetConstantBuffer(d3dCB->mConstantBufferIndex).Get();
            mCmdList->SetGraphicsRootConstantBufferView(bindPoint, constantBuffer->GetGPUVirtualAddress() + d3dCB->mOffset);
        }
        for (int i = 0; i < rCount; ++i) {
            auto bindPoint = std::get<0>(resourceBinds[i]);
            assert(bindPoint < _countof(mGraphicsRoot.mLastResources));
            auto srvOffset = std::get<1>(resourceBinds[i]);
            if (srvOffset == -2) {
                mCmdList->SetGraphicsRootShaderResourceView(bindPoint, std::get<2>(resourceBinds[i]));
                mGraphicsRoot.mLastResources[bindPoint] = {};
            }
            else {
                auto handle = CD3DX12_GPU_DESCRIPTOR_HANDLE(mDevice->GetSRVHeap()->GetGPUDescriptorHandleForHeapStart(), srvOffset);
                if (mGraphicsRoot.mLastResources[bindPoint].ptr == handle.ptr) continue;
                mCmdList->SetGraphicsRootDescriptorTable(bindPoint, handle);
                //mCmdList->SetGraphicsRootShaderResourceView(bindPoint, handle.ptr);
                mGraphicsRoot.mLastResources[bindPoint] = handle;
            }
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
        std::tuple<int, int, UINT64> resourceBinds[32];
        int rCount = BindResources(resourceBinds, pipelineState->mLayout->mResources, resources, r,
            pipelineState->mRootSignature->mNumConstantBuffers, pipelineState->mRootSignature->mNumConstantBuffers + pipelineState->mRootSignature->mSRVCount);
        if (cCount == -1 || rCount == -1) return;

        FlushBarriers();
        auto& cache = mDevice->GetResourceCache();
        for (int i = 0; i < cCount; ++i) {
            int bindPoint = constantBinds[i].first;
            auto* d3dCB = constantBinds[i].second;
            if (mComputeRoot.mLastCBs[bindPoint] == d3dCB) continue;
            mComputeRoot.mLastCBs[bindPoint] = d3dCB;
            auto* constantBuffer = cache.GetConstantBuffer(d3dCB->mConstantBufferIndex).Get();
            mCmdList->SetComputeRootConstantBufferView(bindPoint, constantBuffer->GetGPUVirtualAddress() + d3dCB->mOffset);
        }
        for (int i = 0; i < rCount; ++i) {
            auto bindPoint = std::get<0>(resourceBinds[i]);
            assert(bindPoint < _countof(mGraphicsRoot.mLastResources));
            auto srvOffset = std::get<1>(resourceBinds[i]);
            if (srvOffset == -2) {
                mCmdList->SetComputeRootShaderResourceView(bindPoint, std::get<2>(resourceBinds[i]));
                mComputeRoot.mLastResources[bindPoint] = {};
            }
            else {
                auto handle = CD3DX12_GPU_DESCRIPTOR_HANDLE(mDevice->GetSRVHeap()->GetGPUDescriptorHandleForHeapStart(), srvOffset);
                assert(bindPoint < _countof(mComputeRoot.mLastResources));
                if (mComputeRoot.mLastResources[bindPoint].ptr == handle.ptr) continue;
                mCmdList->SetComputeRootDescriptorTable(bindPoint, handle);
                mComputeRoot.mLastResources[bindPoint] = handle;
            }
        }
    }

    void BindVertexIndexBuffers(std::span<const BufferLayout*> bindings, D3D12_INDEX_BUFFER_VIEW& indexView, int& indexCount) {
        indexCount = -1;
        indexView = D3D12_INDEX_BUFFER_VIEW{ .BufferLocation = 0, .SizeInBytes = 0, .Format = DXGI_FORMAT_UNKNOWN, };
        tVertexViews.clear();
        if (!bindings.empty()) {
            auto& cache = mDevice->GetResourceCache();
            cache.ComputeElementData(bindings, CreateContext(), tVertexViews, indexView, indexCount);
        }
    }

    void ApplyVertexIndexBuffers(D3D12_INDEX_BUFFER_VIEW& indexView) {
        mCmdList->IASetVertexBuffers(0, (uint32_t)tVertexViews.size(), tVertexViews.data());
        mCmdList->IASetIndexBuffer(&indexView);
    }

    void DrawMesh(std::span<const BufferLayout*> bindings, const PipelineLayout* state, std::span<const void*> resources, const DrawConfig& config, int instanceCount = 1, const char* name = nullptr) override {
        assert(mViewportRect.width > 0 && mViewportRect.height > 0);
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

        int indexCount;
        D3D12_INDEX_BUFFER_VIEW indexView;
        BindVertexIndexBuffers(bindings, indexView, indexCount);
        BindGraphicsState(pipelineState, resources);
        FlushBarriers();
        ApplyVertexIndexBuffers(indexView);
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

        int indexCount;
        D3D12_INDEX_BUFFER_VIEW indexView;
        BindVertexIndexBuffers(bindings, indexView, indexCount);
        BindGraphicsState(pipelineState, resources);
        FlushBarriers();
        ApplyVertexIndexBuffers(indexView);
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

        if (cache.mIndirectSig == nullptr) {
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
                nullptr, IID_PPV_ARGS(&cache.mIndirectSig));
        }

        auto& argsBinding = cache.RequireBinding(argsBuffer);
        cache.RequireState(CreateContext(), argsBinding, argsBuffer, D3D12_RESOURCE_STATE_INDIRECT_ARGUMENT);

        int indexCount;
        D3D12_INDEX_BUFFER_VIEW indexView;
        BindVertexIndexBuffers(bindings, indexView, indexCount);
        BindGraphicsState(pipelineState, resources);
        FlushBarriers();
        ApplyVertexIndexBuffers(indexView);
        if (config.mIndexCount >= 0) indexCount = config.mIndexCount;

        mCmdList->ExecuteIndirect(
            cache.mIndirectSig.Get(), 1,
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
    void DispatchRaytrace(const PipelineLayout* state, std::span<const void*> resources, Int3 size) {
        auto* pipelineState = (D3DResourceCache::D3DPipelineRaytrace*)state->mPipelineHash;
        if (pipelineState == nullptr) return;

        BindComputeState(pipelineState, resources);

        auto shaderIDs = pipelineState->mShaderIDs.Get();

        D3D12_DISPATCH_RAYS_DESC dispatchDesc = {
            .RayGenerationShaderRecord = {
                .StartAddress = shaderIDs->GetGPUVirtualAddress(),
                .SizeInBytes = D3D12_SHADER_IDENTIFIER_SIZE_IN_BYTES},
            .MissShaderTable = {
                .StartAddress = shaderIDs->GetGPUVirtualAddress() +
                                D3D12_RAYTRACING_SHADER_TABLE_BYTE_ALIGNMENT,
                .SizeInBytes = D3D12_SHADER_IDENTIFIER_SIZE_IN_BYTES},
            .HitGroupTable = {
                .StartAddress = shaderIDs->GetGPUVirtualAddress() +
                                2 * D3D12_RAYTRACING_SHADER_TABLE_BYTE_ALIGNMENT,
                .SizeInBytes = D3D12_SHADER_IDENTIFIER_SIZE_IN_BYTES},
            .Width = (UINT)size.x,
            .Height = (UINT)size.y,
            .Depth = (UINT)size.z,
        };
        mCmdList->DispatchRays(&dispatchDesc);
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
    template<typename T>
    static void increment_shared(const std::shared_ptr<T>& ptr) {
        uint64_t data[4] = { };
        (std::shared_ptr<T>&)data[0] = ptr;
    }
    virtual intptr_t CreateBLAS(const BufferLayout& vertexBuffer, const BufferLayout& indexBuffer) override {
        auto& cache = mDevice->GetResourceCache();
        cache.RequireState(CreateContext(), cache.RequireBinding(vertexBuffer), vertexBuffer, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
        cache.RequireState(CreateContext(), cache.RequireBinding(indexBuffer), indexBuffer, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
        cache.FlushBarriers(CreateContext());
        auto posElement = vertexBuffer.GetElements()[0];
        D3D12_GPU_VIRTUAL_ADDRESS_AND_STRIDE vertAddr = {
            .StartAddress = GetBufferGPUAddress(vertexBuffer),
            .StrideInBytes = (UINT64)vertexBuffer.CalculateBufferStride(),
        };
        D3D12_GPU_VIRTUAL_ADDRESS indAddr = GetBufferGPUAddress(indexBuffer);
        auto blas = raytracing.MakeBLAS(mDevice->GetD3DDevice(), mCmdList.Get(),
            vertAddr, (DXGI_FORMAT)posElement.mFormat, vertexBuffer.mCount,
            indAddr, (DXGI_FORMAT)indexBuffer.GetElements()[0].mFormat, indexBuffer.mCount);
        increment_shared(blas);
        return (intptr_t)blas.get();
    }
    virtual intptr_t CreateTLAS(const BufferLayout& instanceBuffer) override {
        auto& cache = mDevice->GetResourceCache();
        auto binding = cache.RequireBinding(instanceBuffer);
        cache.RequireState(CreateContext(), binding, instanceBuffer, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
        cache.FlushBarriers(CreateContext());
        auto tlas = raytracing.MakeTLAS(mDevice->GetD3DDevice(), mCmdList.Get(),
            binding.mBuffer.Get(), binding.mCount, nullptr);
        increment_shared(tlas);
        return (intptr_t)tlas.get();
    }

    // Send the commands to the GPU
    // TODO: Should this be automatic?
    void Execute() override {
        SetSurface(nullptr);
        FlushBarriers();

        ThrowIfFailed(mCmdList->Close());

        auto* cmdQueue = mDevice->GetDevice().GetCmdQueue();

        ID3D12CommandList* ppCommandLists[] = { mCmdList.Get(), };
        cmdQueue->ExecuteCommandLists(_countof(ppCommandLists), ppCommandLists);
        ThrowIfFailed(cmdQueue->Signal(mCmdAllocator->mFence.Get(), mCmdAllocator->GetHeadFrame()));
    }

};

GraphicsDeviceD3D12::GraphicsDeviceD3D12()
    : mDevice()
    , mCache(mDevice, mStatistics)
{
    auto* zone = SimpleProfilerMarker("Feature Detection");
    D3D12_FEATURE_DATA_D3D12_OPTIONS options = {};
    D3D12_FEATURE_DATA_D3D12_OPTIONS5 features5 = {};
    D3D12_FEATURE_DATA_D3D12_OPTIONS7 features = {};

    const auto& d3dDevice = mDevice.GetD3DDevice();
    ThrowIfFailed(d3dDevice->CheckFeatureSupport(D3D12_FEATURE_D3D12_OPTIONS, &options, sizeof(options)));
    ThrowIfFailed(d3dDevice->CheckFeatureSupport(D3D12_FEATURE_D3D12_OPTIONS5, &features5, sizeof(features5)));
    ThrowIfFailed(d3dDevice->CheckFeatureSupport(D3D12_FEATURE_D3D12_OPTIONS7, &features, sizeof(features)));

    if (!options.MinPrecisionSupport) OutputDebugString(L"[Graphics] MinPrecision NOT supported\n");
    if (features.MeshShaderTier == D3D12_MESH_SHADER_TIER_NOT_SUPPORTED) OutputDebugString(L"[Graphics] Mesh Shaders NOT supported!\n");
    if (features5.RaytracingTier < D3D12_RAYTRACING_TIER_1_0) OutputDebugString(L"[Graphics] Raytracing NOT supported!\n");

    mCapabilities.mComputeShaders = true;
    mCapabilities.mMeshShaders = features.MeshShaderTier == D3D12_MESH_SHADER_TIER_1;
    mCapabilities.mMinPrecision = options.MinPrecisionSupport;
    mCapabilities.mRaytracingSupported = features5.RaytracingTier >= D3D12_RAYTRACING_TIER_1_0;
    SimpleProfilerMarkerEnd(zone);
}
GraphicsDeviceD3D12::~GraphicsDeviceD3D12() { }

#ifdef _DEBUG       // Check for memory leaks
#include <dxgidebug.h>
GraphicsDeviceD3D12::DisposeGuard::~DisposeGuard() {
    IDXGIDebug1* pDebug = nullptr;
    if (SUCCEEDED(DXGIGetDebugInterface1(0, IID_PPV_ARGS(&pDebug)))) {
        pDebug->ReportLiveObjects(DXGI_DEBUG_ALL, DXGI_DEBUG_RLO_FLAGS(DXGI_DEBUG_RLO_DETAIL | DXGI_DEBUG_RLO_IGNORE_INTERNAL));
        pDebug->Release();
    }
}
#else
GraphicsDeviceD3D12::DisposeGuard::~DisposeGuard() { }
#endif

void GraphicsDeviceD3D12::CheckDeviceState() const {
    auto reason = GetD3DDevice()->GetDeviceRemovedReason();
    if (reason == S_OK) return;

    WCHAR* errorString = nullptr;
    FormatMessage(FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_IGNORE_INSERTS,
        nullptr, reason, MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT),
        (LPWSTR)&errorString, 0, nullptr);
    OutputDebugStringW(errorString);
    throw "Device is lost!";
}

std::wstring GraphicsDeviceD3D12::GetDeviceName() const {
    auto luid = mDevice.GetD3DDevice()->GetAdapterLuid();
    ComPtr<IDXGIAdapter1> pAdapter = nullptr;
    for (UINT adapterIndex = 0; mDevice.GetFactory()->EnumAdapters1(adapterIndex, &pAdapter) != DXGI_ERROR_NOT_FOUND; ++adapterIndex) {
        DXGI_ADAPTER_DESC1 adapterDesc;
        if (!SUCCEEDED(pAdapter->GetDesc1(&adapterDesc))) continue;
        if (memcmp(&adapterDesc.AdapterLuid, &luid, sizeof(luid)) != 0) continue;

        return adapterDesc.Description;
    }
    return L"";
}

CommandBuffer GraphicsDeviceD3D12::CreateCommandBuffer() {
    return CommandBuffer(new D3DCommandBuffer(this));
}
std::shared_ptr<GraphicsSurface> GraphicsDeviceD3D12::CreateSurface(WindowBase* window) {
    return std::make_shared<D3DGraphicsSurface>(GetDevice(), GetResourceCache(), ((WindowWin32*)window)->GetHWND());
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
