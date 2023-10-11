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

// From DirectXTK wiki
inline void ThrowIfFailed(HRESULT hr)
{
    if (FAILED(hr))
    {
        throw std::exception();
    }
}

// Handles receiving rendering events from the user application
// and issuing relevant draw commands
class D3DCommandBuffer : public CommandBufferInteropBase {
    GraphicsDeviceD3D12* mDevice;
    ComPtr<ID3D12GraphicsCommandList> mCmdList;
    ID3D12RootSignature* mLastRootSig;
    const D3DResourceCache::D3DPipelineState* mLastPipeline;
    const D3DConstantBuffer* mLastCBs[10];
    UINT64 mLastResources[10];
    const D3DResourceCache::D3DRT* mBoundRT;
    std::vector<D3D12_VERTEX_BUFFER_VIEW> tVertexViews;
public:
    D3DCommandBuffer(GraphicsDeviceD3D12* device) : mDevice(device), mBoundRT(nullptr) {
        D3D12_COMMAND_QUEUE_DESC queueDesc = {};
        queueDesc.Flags = D3D12_COMMAND_QUEUE_FLAG_NONE;
        queueDesc.Type = D3D12_COMMAND_LIST_TYPE_DIRECT;
        ThrowIfFailed(device->GetD3DDevice()
            ->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT,
                device->GetCmdAllocator(), nullptr, IID_PPV_ARGS(&mCmdList)));
        ThrowIfFailed(mCmdList->Close());
    }
    ID3D12Device* GetD3DDevice() const { return mDevice->GetD3DDevice(); }
    GraphicsDeviceBase* GetGraphics() const override
    {
        return mDevice;
    }
    // Get this command buffer ready to begin rendering
    void Reset() override
    {
        mCmdList->Reset(mDevice->GetCmdAllocator(), nullptr);

        SetD3DRenderTarget(&mDevice->GetBackBuffer());
        mLastRootSig = nullptr;
        mLastPipeline = nullptr;
        std::fill(mLastCBs, mLastCBs + _countof(mLastCBs), nullptr);
        std::fill(mLastResources, mLastResources + _countof(mLastResources), 0);
    }
    void SetRenderTarget(const RenderTarget2D* target) {
        if (target == nullptr) {
            SetD3DRenderTarget(&mDevice->GetBackBuffer());
            return;
        }
        auto d3dRt = target != nullptr ? mDevice->GetResourceCache().RequireD3DRT(target) : nullptr;
        if (d3dRt != nullptr && d3dRt->mFrameBuffer.mBuffer == nullptr) {
            auto& cache = mDevice->GetResourceCache();
            auto d3dDevice = mDevice->GetD3DDevice();

            auto heapParams = CD3DX12_HEAP_PROPERTIES(D3D12_HEAP_TYPE_DEFAULT);

            // Create the render target
            auto rtvDesc = CD3DX12_RESOURCE_DESC::Tex2D(DXGI_FORMAT_R8G8B8A8_UNORM,
                (long)target->GetResolution().x, (long)target->GetResolution().y,
                1, 0, 1, 0, D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET);
            FLOAT clearColor[] {0.0f, 0.0f, 0.0f, 0.0f};
            auto rtvClear = CD3DX12_CLEAR_VALUE(rtvDesc.Format, clearColor);
            d3dDevice->CreateCommittedResource(&heapParams, D3D12_HEAP_FLAG_NONE, &rtvDesc,
                D3D12_RESOURCE_STATE_COMMON, &rtvClear, IID_PPV_ARGS(&d3dRt->mFrameBuffer.mBuffer));
            d3dRt->mFrameBuffer.mBuffer->SetName(L"Texture RT");
            d3dRt->mFrameBuffer.mWidth = (int)rtvDesc.Width;
            d3dRt->mFrameBuffer.mHeight = (int)rtvDesc.Height;

            // Create the depth stencil target
            auto dsvDesc = CD3DX12_RESOURCE_DESC::Tex2D(DXGI_FORMAT_D32_FLOAT,
                (long)target->GetResolution().x, (long)target->GetResolution().y,
                1, 0, 1, 0, D3D12_RESOURCE_FLAG_ALLOW_DEPTH_STENCIL);
            auto dsvClear = CD3DX12_CLEAR_VALUE(dsvDesc.Format, 1.0f, 0);
            ThrowIfFailed(d3dDevice->CreateCommittedResource(&heapParams, D3D12_HEAP_FLAG_NONE, &dsvDesc,
                D3D12_RESOURCE_STATE_DEPTH_READ, &dsvClear, IID_PPV_ARGS(&d3dRt->mDepthBuffer.mBuffer)
            ));
            d3dRt->mDepthBuffer.mBuffer->SetName(L"Depth RT");
            d3dRt->mDepthBuffer.mWidth = (int)dsvDesc.Width;
            d3dRt->mDepthBuffer.mHeight = (int)dsvDesc.Height;

            auto rtvDescSize = mDevice->GetDevice().GetDescriptorHandleSizeRTV();
            auto dsvDescSize = mDevice->GetDevice().GetDescriptorHandleSizeDSV();
            auto srvDescSize = mDevice->GetDevice().GetDescriptorHandleSizeSRV();

            // Create texture view
            D3D12_RENDER_TARGET_VIEW_DESC rtvViewDesc = { .Format = rtvDesc.Format, .ViewDimension = D3D12_RTV_DIMENSION_TEXTURE2D };
            d3dDevice->CreateRenderTargetView(d3dRt->mFrameBuffer.mBuffer.Get(), &rtvViewDesc,
                CD3DX12_CPU_DESCRIPTOR_HANDLE(mDevice->GetRTVHeap()->GetCPUDescriptorHandleForHeapStart(), cache.mRTOffset));
            d3dRt->mFrameBuffer.mRTVOffset = cache.mRTOffset;
            d3dRt->mFrameBuffer.mSRVOffset = -100;
            cache.mRTOffset += rtvDescSize;

            // Create depth view
            D3D12_DEPTH_STENCIL_VIEW_DESC dsViewDesc = { .Format = dsvDesc.Format, .ViewDimension = D3D12_DSV_DIMENSION_TEXTURE2D };
            d3dDevice->CreateDepthStencilView(d3dRt->mDepthBuffer.mBuffer.Get(), &dsViewDesc,
                CD3DX12_CPU_DESCRIPTOR_HANDLE(mDevice->GetDSVHeap()->GetCPUDescriptorHandleForHeapStart(), cache.mDSOffset));
            d3dRt->mDepthBuffer.mRTVOffset = cache.mDSOffset;
            d3dRt->mDepthBuffer.mSRVOffset = -100;
            cache.mDSOffset += dsvDescSize;

            // Create a shader resource view (SRV) for the texture
            D3D12_SHADER_RESOURCE_VIEW_DESC srvDesc = {.Format = rtvDesc.Format, .ViewDimension = D3D12_SRV_DIMENSION_TEXTURE2D};
            srvDesc.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
            srvDesc.Texture2D.MipLevels = 1;
            CD3DX12_CPU_DESCRIPTOR_HANDLE srvHandle(mDevice->GetSRVHeap()->GetCPUDescriptorHandleForHeapStart(), cache.mCBOffset);
            d3dDevice->CreateShaderResourceView(d3dRt->mFrameBuffer.mBuffer.Get(), &srvDesc, srvHandle);
            d3dRt->mFrameBuffer.mSRVOffset = cache.mCBOffset;
            cache.mCBOffset += srvDescSize;
        }
        SetD3DRenderTarget(d3dRt);
    }
    void SetD3DRenderTarget(const D3DResourceCache::D3DRT* d3dRt) {
        if (mBoundRT == d3dRt) return;
        if (mBoundRT != nullptr) {
            if (mBoundRT == &mDevice->GetBackBuffer()) {
                D3D12_RESOURCE_BARRIER rtTransition[] = {
                    CD3DX12_RESOURCE_BARRIER::Transition(mBoundRT->mFrameBuffer.mBuffer.Get(), D3D12_RESOURCE_STATE_RENDER_TARGET, D3D12_RESOURCE_STATE_PRESENT),
                };
                mCmdList->ResourceBarrier(_countof(rtTransition), rtTransition);
            }
            else {
                D3D12_RESOURCE_BARRIER rtTransition[] = {
                    CD3DX12_RESOURCE_BARRIER::Transition(mBoundRT->mFrameBuffer.mBuffer.Get(), D3D12_RESOURCE_STATE_RENDER_TARGET, D3D12_RESOURCE_STATE_COMMON),
                    CD3DX12_RESOURCE_BARRIER::Transition(mBoundRT->mDepthBuffer.mBuffer.Get(), D3D12_RESOURCE_STATE_DEPTH_WRITE, D3D12_RESOURCE_STATE_DEPTH_READ)
                };
                mCmdList->ResourceBarrier(_countof(rtTransition), rtTransition);
            }
        }

        mBoundRT = d3dRt;

        if (mBoundRT != nullptr) {
            if (mBoundRT == &mDevice->GetBackBuffer()) {
                D3D12_RESOURCE_BARRIER rtTransition[] = {
                    CD3DX12_RESOURCE_BARRIER::Transition(mBoundRT->mFrameBuffer.mBuffer.Get(), D3D12_RESOURCE_STATE_PRESENT, D3D12_RESOURCE_STATE_RENDER_TARGET),
                };
                mCmdList->ResourceBarrier(_countof(rtTransition), rtTransition);
            }
            else {
                D3D12_RESOURCE_BARRIER rtTransition[] = {
                    CD3DX12_RESOURCE_BARRIER::Transition(mBoundRT->mFrameBuffer.mBuffer.Get(), D3D12_RESOURCE_STATE_COMMON, D3D12_RESOURCE_STATE_RENDER_TARGET),
                    CD3DX12_RESOURCE_BARRIER::Transition(mBoundRT->mDepthBuffer.mBuffer.Get(), D3D12_RESOURCE_STATE_DEPTH_READ, D3D12_RESOURCE_STATE_DEPTH_WRITE)
                };
                mCmdList->ResourceBarrier(_countof(rtTransition), rtTransition);
            }
        }

        if (mBoundRT != nullptr) {
            auto clientSize = mDevice->GetClientSize();
            CD3DX12_VIEWPORT viewport(0.0f, 0.0f, (float)mBoundRT->mFrameBuffer.mWidth, (float)mBoundRT->mFrameBuffer.mHeight);
            CD3DX12_RECT scissorRect(0, 0, (LONG)mBoundRT->mFrameBuffer.mWidth, (LONG)mBoundRT->mFrameBuffer.mHeight);

            mCmdList->RSSetViewports(1, &viewport);
            mCmdList->RSSetScissorRects(1, &scissorRect);

            CD3DX12_CPU_DESCRIPTOR_HANDLE rtHandle(mDevice->GetRTVHeap()->GetCPUDescriptorHandleForHeapStart(), mBoundRT->mFrameBuffer.mRTVOffset);
            CD3DX12_CPU_DESCRIPTOR_HANDLE depthHandle(mDevice->GetDSVHeap()->GetCPUDescriptorHandleForHeapStart(), mBoundRT->mDepthBuffer.mRTVOffset);
            mCmdList->OMSetRenderTargets(1, &rtHandle, FALSE, &depthHandle);
        }
    }
    // Clear the screen
    void ClearRenderTarget(const ClearConfig& clear) override
    {
        mDevice->CheckDeviceState();
        if (clear.HasClearColor())
        {
            CD3DX12_CPU_DESCRIPTOR_HANDLE descriptor(mDevice->GetRTVHeap()->GetCPUDescriptorHandleForHeapStart(),
                mBoundRT->mFrameBuffer.mRTVOffset);
            mCmdList->ClearRenderTargetView(descriptor, clear.ClearColor, 0, nullptr);
        }
        auto flags = (clear.HasClearDepth() ? D3D12_CLEAR_FLAG_DEPTH : 0)
            | (clear.HasClearScencil() ? D3D12_CLEAR_FLAG_STENCIL : 0);
        if (flags)
        {
            CD3DX12_CPU_DESCRIPTOR_HANDLE depth(mDevice->GetDSVHeap()->GetCPUDescriptorHandleForHeapStart(),
                mBoundRT->mDepthBuffer.mRTVOffset);
            mCmdList->ClearDepthStencilView(depth, (D3D12_CLEAR_FLAGS)flags,
                clear.ClearDepth, clear.ClearStencil, 0, nullptr);
        }
    }

    void* RequireConstantBuffer(std::span<const uint8_t> data) override
    {
        auto& cache = mDevice->GetResourceCache();
        return cache.RequireConstantBuffer(data);
    }
    void CopyBufferData(GraphicsBufferBase* buffer, const std::span<RangeInt>& ranges) override
    {
        auto& cache = mDevice->GetResourceCache();
        cache.UpdateBufferData(mCmdList.Get(), buffer, ranges);
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
    void DrawMesh(std::span<const BufferLayout*> bindings, const PipelineLayout* state, std::span<const void*> resources, const DrawConfig& config, int instanceCount = 1, const char* name = nullptr) override
    {
        mDevice->CheckDeviceState();

        auto& cache = mDevice->GetResourceCache();
        auto* pipelineState = (D3DResourceCache::D3DPipelineState*)state->mPipelineHash;

        BindPipelineState(pipelineState);

        // Require and bind constant buffers
        for (int i = 0; i < pipelineState->mConstantBuffers.size(); ++i)
        {
            auto cb = pipelineState->mConstantBuffers[i];
            auto d3dCB = (D3DConstantBuffer*)resources[i];
            if (mLastCBs[cb->mBindPoint] == d3dCB) continue;
            mLastCBs[cb->mBindPoint] = d3dCB;
            mCmdList->SetGraphicsRootConstantBufferView(cb->mBindPoint, d3dCB->mConstantBuffer->GetGPUVirtualAddress());
        }
        // Require and bind other resources (textures)
        int roff = (int)pipelineState->mConstantBuffers.size();
        for (int i = 0; i < pipelineState->mResourceBindings.size(); ++i)
        {
            auto* rb = pipelineState->mResourceBindings[i];
            auto* resource = resources[roff + i];
            D3DResourceCache::D3DBufferWithSRV* buffer = nullptr;
            if (rb->mType == ShaderBase::ResourceTypes::R_SBuffer) {
                buffer = cache.RequireCurrentBuffer((GraphicsBufferBase*)resource, mCmdList.Get());
            }
            else {
                auto* textureBase = (TextureBase*)resource;
                if (auto rt = dynamic_cast<RenderTarget2D*>(textureBase)) {
                    buffer = &cache.RequireD3DRT(rt)->mFrameBuffer;
                } else {
                    auto tex = dynamic_cast<Texture*>(textureBase);
                    buffer = cache.RequireCurrentTexture(tex, mCmdList.Get());
                }
            }
            if (buffer == nullptr) break;
            auto rootSig = pipelineState->mRootSignature;
            auto handle = mDevice->GetSRVHeap()->GetGPUDescriptorHandleForHeapStart();
            handle.ptr += buffer->mSRVOffset;
            auto bindingId = rootSig->mNumConstantBuffers + rb->mBindPoint;
            if (mLastResources[bindingId] == handle.ptr) continue;
            mCmdList->SetGraphicsRootDescriptorTable(bindingId, handle);
            mLastResources[bindingId] = handle.ptr;
        }

        tVertexViews.clear();
        tVertexViews.reserve(2);
        D3D12_INDEX_BUFFER_VIEW indexView;
        int indexCount = -1;
        cache.ComputeElementData(bindings, mCmdList.Get(), tVertexViews, indexView, indexCount);
        mCmdList->IASetVertexBuffers(0, (uint32_t)tVertexViews.size(), tVertexViews.data());
        mCmdList->IASetIndexBuffer(&indexView);
        
        // Issue the draw calls
        if (config.mIndexCount >= 0) indexCount = config.mIndexCount;
        mCmdList->DrawIndexedInstanced(indexCount, std::max(1, instanceCount), config.mIndexBase, 0, 0);
        cache.mStatistics.mDrawCount++;
        cache.mStatistics.mInstanceCount += instanceCount;
    }
    // Send the commands to the GPU
    // TODO: Should this be automatic?
    void Execute() override
    {
        SetD3DRenderTarget(nullptr);
        ThrowIfFailed(mCmdList->Close());

        ID3D12CommandList* ppCommandLists[] = { mCmdList.Get(), };
        mDevice->GetDevice().GetCmdQueue()->ExecuteCommandLists(_countof(ppCommandLists), ppCommandLists);
    }

};

GraphicsDeviceD3D12::GraphicsDeviceD3D12(const std::shared_ptr<WindowWin32>& window)
    : mWindow(window)
    , mDevice(*window)
    , mCache(mDevice, mStatistics)
{
    auto mD3DDevice = mDevice.GetD3DDevice();
    auto mSwapChain = mDevice.GetSwapChain();
    // Create fence for frame synchronisation
    mBackBufferIndex = mSwapChain->GetCurrentBackBufferIndex();
    for (int i = 0; i < FrameCount; ++i) mFenceValues[i] = 0;
    ThrowIfFailed(mD3DDevice->CreateFence(mFenceValues[mBackBufferIndex], D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&mFence)));
    ++mFenceValues[mBackBufferIndex];
    mFenceEvent = CreateEvent(nullptr, FALSE, FALSE, nullptr);
    if (mFenceEvent == nullptr) ThrowIfFailed(HRESULT_FROM_WIN32(GetLastError()));
    //WaitForGPU();

    auto clientSize = mDevice.GetClientSize();

    // Create a RTV for each frame.
    for (UINT n = 0; n < FrameCount; n++)
    {
        auto& frameBuffer = mFrameBuffers[n].mFrameBuffer;
        ThrowIfFailed(mSwapChain->GetBuffer(n, IID_PPV_ARGS(&frameBuffer.mBuffer)));
        auto handle = CD3DX12_CPU_DESCRIPTOR_HANDLE(mDevice.GetRTVHeap()->GetCPUDescriptorHandleForHeapStart(), mCache.mRTOffset);
        mD3DDevice->CreateRenderTargetView(frameBuffer.mBuffer.Get(), nullptr, handle);
        frameBuffer.mRTVOffset = mCache.mRTOffset;
        frameBuffer.mWidth = (int)clientSize.x;
        frameBuffer.mHeight = (int)clientSize.y;
        ThrowIfFailed(mD3DDevice->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&mCmdAllocator[n])));
        mCache.mRTOffset += mDevice.GetDescriptorHandleSizeRTV();
    }

    // Create the depth buffer
    {
        ComPtr<ID3D12Resource> depthTarget;
        auto heapParams = CD3DX12_HEAP_PROPERTIES(D3D12_HEAP_TYPE_DEFAULT);
        auto dsvDesc = CD3DX12_RESOURCE_DESC::Tex2D(DXGI_FORMAT_D32_FLOAT, (long)clientSize.x, (long)clientSize.y, 1, 0, 1, 0, D3D12_RESOURCE_FLAG_ALLOW_DEPTH_STENCIL);
        auto depthOptimizedClearValue = CD3DX12_CLEAR_VALUE(dsvDesc.Format, 1.0f, 0);
        ThrowIfFailed(mD3DDevice->CreateCommittedResource(
            &heapParams,
            D3D12_HEAP_FLAG_NONE,
            &dsvDesc,
            D3D12_RESOURCE_STATE_DEPTH_WRITE,
            &depthOptimizedClearValue,
            IID_PPV_ARGS(&depthTarget)
        ));
        mD3DDevice->CreateDepthStencilView(depthTarget.Get(), nullptr, mDevice.GetDSVHeap()->GetCPUDescriptorHandleForHeapStart());
        for (UINT n = 0; n < FrameCount; ++n) {
            auto& depthBuffer = mFrameBuffers[n].mDepthBuffer;
            depthBuffer.mBuffer = depthTarget;
            depthBuffer.mRTVOffset = mCache.mDSOffset;
            depthBuffer.mWidth = (int)clientSize.x;
            depthBuffer.mHeight = (int)clientSize.y;
        }
        mCache.mDSOffset += mDevice.GetDescriptorHandleSizeDSV();
    }
}
GraphicsDeviceD3D12::~GraphicsDeviceD3D12()
{
    WaitForGPU();
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
const PipelineLayout* GraphicsDeviceD3D12::RequirePipeline(std::span<const BufferLayout*> bindings, std::span<const Material*> materials, const IdentifierWithName& renderPass)
{
    CheckDeviceState();

    auto pipelineState = mCache.RequirePipelineState(materials, bindings, renderPass);
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
}

// Flip the backbuffer and wait until a frame is available to be rendered
void GraphicsDeviceD3D12::Present()
{
    auto hr = mDevice.GetSwapChain()->Present(1, 0);

    if (hr == DXGI_ERROR_DEVICE_REMOVED || hr == DXGI_ERROR_DEVICE_RESET)
    {
        CheckDeviceState();
        return;

        // Reset all cached resources
        //mCache = D3DResourceCache(mDevice);
        // Reset the entire d3d device
        //mDevice = D3DGraphicsDevice(*mWindow);
    }
    else
    {
        ThrowIfFailed(hr);
    }
    WaitForFrame();
}

// Wait for the earliest submitted frame to be finished and ready to be rendered into
void GraphicsDeviceD3D12::WaitForFrame()
{
    // Schedule a Signal command in the queue.
    const UINT64 currentFenceValue = mFenceValues[mBackBufferIndex];
    ThrowIfFailed(mDevice.GetCmdQueue()->Signal(mFence.Get(), currentFenceValue));

    // Update the frame index.
    mBackBufferIndex = mDevice.GetSwapChain()->GetCurrentBackBufferIndex();

    // If the next frame is not ready to be rendered yet, wait until it is ready.
    auto fenceVal = mFence->GetCompletedValue();
    if (fenceVal < mFenceValues[mBackBufferIndex])
    {
        ThrowIfFailed(mFence->SetEventOnCompletion(mFenceValues[mBackBufferIndex], mFenceEvent));
        WaitForSingleObjectEx(mFenceEvent, INFINITE, FALSE);
    }

    // Set the fence value for the next frame.
    mFenceValues[mBackBufferIndex] = currentFenceValue + 1;
    mCmdAllocator[mBackBufferIndex]->Reset();
    mCache.SetResourceLockIds(fenceVal, currentFenceValue);
}
// Wait for all GPU operations? Taken from the samples
void GraphicsDeviceD3D12::WaitForGPU()
{
    // Schedule a Signal command in the queue.
    ThrowIfFailed(mDevice.GetCmdQueue()->Signal(mFence.Get(), mFenceValues[mBackBufferIndex]));

    // Wait until the fence has been processed.
    ThrowIfFailed(mFence->SetEventOnCompletion(mFenceValues[mBackBufferIndex], mFenceEvent));
    WaitForSingleObjectEx(mFenceEvent, INFINITE, FALSE);

    // Increment the fence value for the current frame.
    mFenceValues[mBackBufferIndex]++;
}
