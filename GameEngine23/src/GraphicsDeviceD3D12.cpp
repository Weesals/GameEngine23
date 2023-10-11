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
    std::vector<D3D12_VERTEX_BUFFER_VIEW> tVertexViews;
public:
    D3DCommandBuffer(GraphicsDeviceD3D12* device) : mDevice(device) {
        D3D12_COMMAND_QUEUE_DESC queueDesc = {};
        queueDesc.Flags = D3D12_COMMAND_QUEUE_FLAG_NONE;
        queueDesc.Type = D3D12_COMMAND_LIST_TYPE_DIRECT;
        ThrowIfFailed(device->GetD3DDevice()
            ->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT,
                device->GetCmdAllocator(), nullptr, IID_PPV_ARGS(&mCmdList)));
        ThrowIfFailed(mCmdList->Close());
    }
    ID3D12Device* GetD3DDevice() const { return mDevice->GetD3DDevice(); }
    void SetResourceBarrier(const D3D12_RESOURCE_STATES StateBefore, const D3D12_RESOURCE_STATES StateAfter) {
        auto barrier = CD3DX12_RESOURCE_BARRIER::Transition(mDevice->GetBackBuffer(), StateBefore, StateAfter);
        mCmdList->ResourceBarrier(1, &barrier);
    }
    GraphicsDeviceBase* GetGraphics() const override
    {
        return mDevice;
    }
    // Get this command buffer ready to begin rendering
    void Reset() override
    {
        auto clientSize = mDevice->GetClientSize();
        CD3DX12_VIEWPORT viewport(0.0f, 0.0f, clientSize.x, clientSize.y);
        CD3DX12_RECT scissorRect(0, 0, (LONG)clientSize.x, (LONG)clientSize.y);

        mCmdList->Reset(mDevice->GetCmdAllocator(), nullptr);
        mCmdList->RSSetViewports(1, &viewport);
        mCmdList->RSSetScissorRects(1, &scissorRect);

        SetResourceBarrier(D3D12_RESOURCE_STATE_PRESENT, D3D12_RESOURCE_STATE_RENDER_TARGET);
        CD3DX12_CPU_DESCRIPTOR_HANDLE descriptor(mDevice->GetRTVHeap()->GetCPUDescriptorHandleForHeapStart(),
            mDevice->GetBackBufferIndex(), mDevice->GetDescriptorHandleSizeRTV());
        CD3DX12_CPU_DESCRIPTOR_HANDLE depth(mDevice->GetDSVHeap()->GetCPUDescriptorHandleForHeapStart());
        mCmdList->OMSetRenderTargets(1, &descriptor, FALSE, &depth);
        mLastRootSig = nullptr;
        mLastPipeline = nullptr;
        std::fill(mLastCBs, mLastCBs + _countof(mLastCBs), nullptr);
    }
    // Clear the screen
    void ClearRenderTarget(const ClearConfig& clear) override
    {
        mDevice->CheckDeviceState();
        if (clear.HasClearColor())
        {
            CD3DX12_CPU_DESCRIPTOR_HANDLE descriptor(mDevice->GetRTVHeap()->GetCPUDescriptorHandleForHeapStart(),
                mDevice->GetBackBufferIndex(), mDevice->GetDescriptorHandleSizeRTV());
            mCmdList->ClearRenderTargetView(descriptor, clear.ClearColor, 0, nullptr);
        }
        auto flags = (clear.HasClearDepth() ? D3D12_CLEAR_FLAG_DEPTH : 0)
            | (clear.HasClearScencil() ? D3D12_CLEAR_FLAG_STENCIL : 0);
        if (flags)
        {
            CD3DX12_CPU_DESCRIPTOR_HANDLE depth(mDevice->GetDSVHeap()->GetCPUDescriptorHandleForHeapStart());
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
    void DrawMesh(std::span<const BufferLayout*> bindings, const PipelineLayout* state, std::span<void*> resources, const DrawConfig& config, int instanceCount = 1) override
    {
        mDevice->CheckDeviceState();

        auto& cache = mDevice->GetResourceCache();
        auto* pipelineState = (D3DResourceCache::D3DPipelineState*)state->mPipelineHash;

        tVertexViews.clear();
        tVertexViews.reserve(2);
        D3D12_INDEX_BUFFER_VIEW indexView;
        int indexCount = -1;
        cache.ComputeElementData(bindings, mCmdList.Get(), tVertexViews, indexView, indexCount);

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
            D3DResourceCache::D3DBufferWithSRV* buffer =
                rb->mType == ShaderBase::ResourceTypes::R_Texture
                ? buffer = cache.RequireCurrentTexture((Texture*)resource, mCmdList.Get())
                : cache.RequireCurrentBuffer((GraphicsBufferBase*)resource, mCmdList.Get());
            if (buffer == nullptr) return;
            auto rootSig = pipelineState->mRootSignature;
            auto handle = mDevice->GetSRVHeap()->GetGPUDescriptorHandleForHeapStart();
            handle.ptr += buffer->mSRVOffset;
            auto bindingId = rootSig->mNumConstantBuffers + rb->mBindPoint;
            if (mLastResources[bindingId] == handle.ptr) continue;
            mCmdList->SetGraphicsRootDescriptorTable(bindingId, handle);
            mLastResources[bindingId] = handle.ptr;
        }

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
        SetResourceBarrier(D3D12_RESOURCE_STATE_RENDER_TARGET, D3D12_RESOURCE_STATE_PRESENT);
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
    mFenceEvent = CreateEvent(nullptr, FALSE, FALSE, nullptr);
    if (mFenceEvent == nullptr) ThrowIfFailed(HRESULT_FROM_WIN32(GetLastError()));
    //WaitForGPU();

    auto descriptorSize = mDevice.GetDescriptorHandleSizeRTV();
    auto clientSize = mDevice.GetClientSize();

    // Create a RTV for each frame.
    for (UINT n = 0; n < FrameCount; n++)
    {
        ThrowIfFailed(mSwapChain->GetBuffer(n, IID_PPV_ARGS(&mRenderTargets[n])));
        auto handle = CD3DX12_CPU_DESCRIPTOR_HANDLE(mDevice.GetRTVHeap()->GetCPUDescriptorHandleForHeapStart(), n, descriptorSize);
        mD3DDevice->CreateRenderTargetView(mRenderTargets[n].Get(), nullptr, handle);
        ThrowIfFailed(mD3DDevice->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&mCmdAllocator[n])));
    }

    // Create the depth buffer
    {
        auto heapParams = CD3DX12_HEAP_PROPERTIES(D3D12_HEAP_TYPE_DEFAULT);
        auto texParams = CD3DX12_RESOURCE_DESC::Tex2D(DXGI_FORMAT_D32_FLOAT, (long)clientSize.x, (long)clientSize.y, 1, 0, 1, 0, D3D12_RESOURCE_FLAG_ALLOW_DEPTH_STENCIL);
        auto depthOptimizedClearValue = CD3DX12_CLEAR_VALUE(DXGI_FORMAT_D32_FLOAT, 1.0f, 0);
        ThrowIfFailed(mD3DDevice->CreateCommittedResource(
            &heapParams,
            D3D12_HEAP_FLAG_NONE,
            &texParams,
            D3D12_RESOURCE_STATE_DEPTH_WRITE,
            &depthOptimizedClearValue,
            IID_PPV_ARGS(&mDepthTarget)
        ));
        mD3DDevice->CreateDepthStencilView(mDepthTarget.Get(), nullptr, mDevice.GetDSVHeap()->GetCPUDescriptorHandleForHeapStart());
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
const PipelineLayout* GraphicsDeviceD3D12::RequirePipeline(std::span<const BufferLayout*> bindings, const Material* material)
{
    CheckDeviceState();

    auto pipelineState = mCache.RequirePipelineState(*material, bindings);
    if (pipelineState->mLayout == nullptr)
    {
        pipelineState->mLayout = std::make_unique<PipelineLayout>();
        pipelineState->mLayout->mRootHash = (size_t)pipelineState->mRootSignature;
        pipelineState->mLayout->mPipelineHash = (size_t)pipelineState;
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
