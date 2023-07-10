#include <d3dcompiler.h>
#include <unordered_map>
#include <memory>
#include <functional>
#include <tuple>
#include <algorithm>

#include "GraphicsDeviceD3D12.h"
#include "D3DConstantBufferCache.h"
#include <d3dx12.h>
#include "D3DShader.h"
#include "Resources.h"

#pragma comment(lib, "d3d12.lib")
#pragma comment(lib, "dxgi.lib")
#pragma comment(lib, "d3dcompiler.lib")

// From DirectXTK wiki
inline void ThrowIfFailed(HRESULT hr)
{
    if (FAILED(hr))
    {
        throw std::exception();
    }
}

template<class T>
static void CopyElements(void* dest, std::span<T> source, int offset, int stride)
{
    *(__int8**)&dest += offset;
    for (int i = 0; i < source.size(); ++i) {
        std::memcpy((__int8*)dest + i * stride, &source[i], sizeof(source[i]));
    }
}
static unsigned int PostIncrement(int& v, int a) { int t = v; v += a; return t; }
template<class K, class T>
static T* GetOrCreate(std::unordered_map<K, std::unique_ptr<T>>& map, const K key)
{
    auto i = map.find(key);
    if (i != map.end()) return i->second.get();
    auto newItem = new T();
    map.insert(std::make_pair(key, newItem));
    return newItem;
}

// Allocate or retrieve a container for GPU buffers for this item
D3DResourceCache::D3DMesh* D3DResourceCache::RequireD3DMesh(const Mesh& mesh)
{
    return GetOrCreate(meshMapping, &mesh);
}
D3DShader* D3DResourceCache::RequireShader(const Shader& shader, const std::string& entrypoint)
{
    auto pathId = shader.GetIdentifier();
    auto entryPointId = Resources::RequireStringId(entrypoint);
    ShaderKey key = { pathId, entryPointId };
    return GetOrCreate(shaderMapping, key);
}
D3DResourceCache::D3DPipelineState* D3DResourceCache::RequirePipelineState(const Shader& vs, const Shader& ps, size_t hash)
{
    auto sourceVSId = vs.GetIdentifier();
    auto sourcePSId = ps.GetIdentifier();
    auto key = hash ^ std::hash<int>()(sourceVSId) ^ std::hash<int>()(sourcePSId);
    return GetOrCreate(pipelineMapping, key);
}

// Generate the GPU resources required for rendering a mesh
void D3DResourceCache::UpdateMeshData(const Mesh& mesh, D3DGraphicsDevice& d3d12)
{
    auto device = d3d12.GetD3DDevice();

    // Get d3d cache instance
    auto d3dMesh = RequireD3DMesh(mesh);
    // Get vertex attributes
    d3dMesh->mVertElements.clear();
    int vertexStride = GenerateElementDesc(mesh, d3dMesh->mVertElements);

    // Compute buffer sizes
    int vbufferByteSize = vertexStride * mesh.GetVertexCount();
    int ibufferByteSize = sizeof(int) * mesh.GetIndexCount();

    // Allocate vertex and index buffers
    auto heapProps = CD3DX12_HEAP_PROPERTIES(D3D12_HEAP_TYPE_UPLOAD);
    auto resDesc = CD3DX12_RESOURCE_DESC::Buffer(vbufferByteSize);
    ThrowIfFailed(device->CreateCommittedResource(
        &heapProps,
        D3D12_HEAP_FLAG_NONE,
        &resDesc,
        D3D12_RESOURCE_STATE_GENERIC_READ,
        nullptr,
        IID_PPV_ARGS(&d3dMesh->mVertexBuffer)));

    resDesc = CD3DX12_RESOURCE_DESC::Buffer(ibufferByteSize);
    ThrowIfFailed(device->CreateCommittedResource(
        &heapProps,
        D3D12_HEAP_FLAG_NONE,
        &resDesc,
        D3D12_RESOURCE_STATE_GENERIC_READ,
        nullptr,
        IID_PPV_ARGS(&d3dMesh->mIndexBuffer)));

    // Copy data into buffer
    UINT8* mappedData;
    CD3DX12_RANGE readRange(0, 0);
    ThrowIfFailed(d3dMesh->mVertexBuffer->Map(0, &readRange, reinterpret_cast<void**>(&mappedData)));
    CopyVertexData(mesh, mappedData, vertexStride);
    d3dMesh->mVertexBuffer->Unmap(0, nullptr);

    ThrowIfFailed(d3dMesh->mIndexBuffer->Map(0, &readRange, reinterpret_cast<void**>(&mappedData)));
    auto inds = mesh.GetIndices();
    std::transform(inds.begin(), inds.end(), (int*)mappedData, [](auto i) { return i; });
    d3dMesh->mIndexBuffer->Unmap(0, nullptr);

    // Create views into buffers
    d3dMesh->mVertexBufferView = { d3dMesh->mVertexBuffer->GetGPUVirtualAddress(), (UINT)vbufferByteSize, (UINT)vertexStride };
    d3dMesh->mIndexBufferView = { d3dMesh->mIndexBuffer->GetGPUVirtualAddress(), (UINT)ibufferByteSize, DXGI_FORMAT_R32_UINT };
    // Track that the mesh is nwo up to date
    d3dMesh->mRevision = mesh.GetRevision();
}
// Generate a descriptor of the required vertex attributes for this mesh
int D3DResourceCache::GenerateElementDesc(const Mesh& mesh, std::vector<D3D12_INPUT_ELEMENT_DESC>& vertDesc)
{
    int offset = 0;
    if (!mesh.GetPositions().empty())
        vertDesc.push_back({ "POSITION", 0, DXGI_FORMAT_R32G32B32_FLOAT, 0, PostIncrement(offset, 12), D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA, 0 });
    if (!mesh.GetNormals().empty())
        vertDesc.push_back({ "NORMAL",   0, DXGI_FORMAT_R32G32B32_FLOAT, 0, PostIncrement(offset, 12), D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA, 0 });
    if (!mesh.GetUVs().empty())
        vertDesc.push_back({ "TEXCOORD", 0, DXGI_FORMAT_R32G32_FLOAT,    0, PostIncrement(offset, 8),  D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA, 0 });
    if (!mesh.GetColors().empty())
        vertDesc.push_back({ "COLOR", 0, DXGI_FORMAT_R32G32B32A32_FLOAT, 0, PostIncrement(offset, 16),  D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA, 0 });
    return offset;
}
// Copy mesh data so that it matches a generated descriptor
void D3DResourceCache::CopyVertexData(const Mesh& mesh, void* buffer, int stride)
{
    int offset = 0;
    auto positions = mesh.GetPositions();
    if (!positions.empty()) CopyElements(buffer, positions, PostIncrement(offset, 12), stride);
    auto normals = mesh.GetNormals();
    if (!normals.empty()) CopyElements(buffer, normals, PostIncrement(offset, 12), stride);
    auto uvs = mesh.GetUVs();
    if (!uvs.empty()) CopyElements(buffer, uvs, PostIncrement(offset, 8), stride);
    auto colors = mesh.GetColors();
    if (!colors.empty()) CopyElements(buffer, colors, PostIncrement(offset, 16), stride);
}
void D3DResourceCache::SetResourceLockIds(UINT64 lockFrameId, UINT64 writeFrameId)
{
    mConstantBufferCache.SetResourceLockIds(lockFrameId, writeFrameId);
}
// Ensure a mesh is ready to be rendered by the GPU
D3DResourceCache::D3DMesh* D3DResourceCache::RequireMesh(const Mesh& mesh, D3DGraphicsDevice& d3d12)
{
    auto d3dMesh = RequireD3DMesh(mesh);
    if (d3dMesh->mRevision != mesh.GetRevision())
        UpdateMeshData(mesh, d3d12);
    return d3dMesh;
}
// Ensure a material is ready to be rendererd by the GPU (with the specified vertex layout)
D3DResourceCache::D3DPipelineState* D3DResourceCache::RequirePipelineState(const Material& material, std::span<D3D12_INPUT_ELEMENT_DESC> vertElements, D3DGraphicsDevice& d3d12)
{
    // Get the relevant shaders
    auto& sourceVS = material.GetVertexShader();
    auto& sourcePS = material.GetPixelShader();

    // Find (or create) a pipeline that matches these requirements
    size_t hash = 0;
    for (auto& el : vertElements) hash ^= std::hash<void*>()((void*)el.SemanticName);
    auto pipelineState = RequirePipelineState(sourceVS, sourcePS, hash);
    if (pipelineState->mPipelineState == nullptr)
    {
        auto device = d3d12.GetD3DDevice();

        // Make sure shaders are compiled
        auto vShader = RequireShader(sourceVS, StrVSEntryPoint);
        auto pShader = RequireShader(sourcePS, StrPSEntryPoint);
        if (vShader->mShader == nullptr) {
            vShader->CompileFromFile(sourceVS.GetPath(), StrVSEntryPoint, "vs_5_0");
        }
        if (pShader->mShader == nullptr) {
            pShader->CompileFromFile(sourceVS.GetPath(), StrPSEntryPoint, "ps_5_0");
        }

        // Create the D3D pipeline
        D3D12_GRAPHICS_PIPELINE_STATE_DESC psoDesc = {};
        psoDesc.InputLayout = { vertElements.data(), (unsigned int)vertElements.size() };
        psoDesc.pRootSignature = d3d12.GetRootSignature();
        psoDesc.VS = CD3DX12_SHADER_BYTECODE(vShader->mShader.Get());
        psoDesc.PS = CD3DX12_SHADER_BYTECODE(pShader->mShader.Get());
        psoDesc.RasterizerState = CD3DX12_RASTERIZER_DESC(D3D12_DEFAULT);
        psoDesc.BlendState = CD3DX12_BLEND_DESC(D3D12_DEFAULT);
        psoDesc.DepthStencilState = CD3DX12_DEPTH_STENCIL_DESC1(D3D12_DEFAULT);
        psoDesc.SampleMask = UINT_MAX;
        psoDesc.PrimitiveTopologyType = D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE;
        psoDesc.NumRenderTargets = 1;
        psoDesc.RTVFormats[0] = DXGI_FORMAT_R8G8B8A8_UNORM;
        psoDesc.DSVFormat = DXGI_FORMAT_D32_FLOAT;
        psoDesc.SampleDesc.Count = 1;
        ThrowIfFailed(device->CreateGraphicsPipelineState(&psoDesc, IID_PPV_ARGS(&pipelineState->mPipelineState)));

        // Collect constant buffers required by the shaders
        // TODO: Throw an error if different constant buffers
        // are required in the same bind point
        for (auto l : { &vShader->mConstantBuffers, &pShader->mConstantBuffers })
        {
            for (auto& cb : *l)
            {
                if (pipelineState->mConstantBuffers.size() <= cb.mBindPoint)
                    pipelineState->mConstantBuffers.resize(cb.mBindPoint + 1);
                pipelineState->mConstantBuffers[cb.mBindPoint] = &cb;
            }
        }
    }
    return pipelineState;
}
D3DConstantBufferCache::D3DConstantBuffer* D3DResourceCache::RequireConstantBuffer(const D3DShader::ConstantBuffer& cb, const Material& material, D3DGraphicsDevice& d3d12)
{
    return mConstantBufferCache.RequireConstantBuffer(material, cb, d3d12);
}

// Handles receiving rendering events from the user application
// and issuing relevant draw commands
class D3DInterop : public CommandBufferInteropBase {
    GraphicsDeviceD3D12* mDevice;
    ComPtr<ID3D12GraphicsCommandList> mCmdList;
public:
    D3DInterop(GraphicsDeviceD3D12* device) : mDevice(device) {
        D3D12_COMMAND_QUEUE_DESC queueDesc = {};
        queueDesc.Flags = D3D12_COMMAND_QUEUE_FLAG_NONE;
        queueDesc.Type = D3D12_COMMAND_LIST_TYPE_DIRECT;
        ThrowIfFailed(device->GetD3DDevice()
            ->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT,
                device->GetCmdAllocator(), nullptr, IID_PPV_ARGS(&mCmdList)));
        mCmdList->Close();
    }
    ID3D12Device* GetD3DDevice() const { return mDevice->GetD3DDevice(); }
    void SetResourceBarrier(const D3D12_RESOURCE_STATES StateBefore, const D3D12_RESOURCE_STATES StateAfter) {
        auto barrier = CD3DX12_RESOURCE_BARRIER::Transition(mDevice->GetBackBuffer(), StateBefore, StateAfter);
        mCmdList->ResourceBarrier(1, &barrier);
    }
    // Get this command buffer ready to begin rendering
    void Reset() override
    {
        auto clientSize = mDevice->GetClientSize();
        CD3DX12_VIEWPORT viewport(0.0f, 0.0f, clientSize.x, clientSize.y);
        CD3DX12_RECT scissorRect(0, 0, (LONG)clientSize.x, (LONG)clientSize.y);

        mCmdList->Reset(mDevice->GetCmdAllocator(), nullptr);
        mCmdList->SetGraphicsRootSignature(mDevice->GetRootSignature());
        mCmdList->RSSetViewports(1, &viewport);
        mCmdList->RSSetScissorRects(1, &scissorRect);
        SetResourceBarrier(D3D12_RESOURCE_STATE_PRESENT, D3D12_RESOURCE_STATE_RENDER_TARGET);

        auto descriptor = mDevice->GetRTVHeap()->GetCPUDescriptorHandleForHeapStart();
        descriptor.ptr += mDevice->GetDescriptorHandleSize() * mDevice->GetFrameId();
        auto depth = mDevice->GetDSVHeap()->GetCPUDescriptorHandleForHeapStart();
        mCmdList->OMSetRenderTargets(1, &descriptor, FALSE, &depth);
    }
    // Clear the screen
    void ClearRenderTarget(const ClearConfig& clear) override
    {
        if (clear.HasClearColor())
        {
            auto descriptor = mDevice->GetRTVHeap()->GetCPUDescriptorHandleForHeapStart();
            descriptor.ptr += mDevice->GetDescriptorHandleSize() * mDevice->GetFrameId();
            mCmdList->ClearRenderTargetView(descriptor, clear.ClearColor, 0, nullptr);
        }
        auto flags = (clear.HasClearDepth() ? D3D12_CLEAR_FLAG_DEPTH : 0)
            | (clear.HasClearScencil() ? D3D12_CLEAR_FLAG_STENCIL : 0);
        if (flags)
        {
            auto depth = mDevice->GetDSVHeap()->GetCPUDescriptorHandleForHeapStart();
            mCmdList->ClearDepthStencilView(depth, (D3D12_CLEAR_FLAGS)flags,
                clear.ClearDepth, clear.ClearStencil, 0, nullptr);
        }
    }

    const D3DResourceCache::D3DPipelineState* mLastPipeline;
    const D3DResourceCache::D3DMesh* mLastMesh;
    const D3DConstantBufferCache::D3DConstantBuffer* mLastCBs[10];

    // Draw a mesh with the specified material
    void DrawMesh(std::shared_ptr<Mesh> mesh, std::shared_ptr<Material> material) override
    {
        auto& cache = mDevice->GetResourceCache();
        auto d3dMesh = cache.RequireMesh(*mesh.get(), mDevice->GetDevice());
        auto pipelineState = cache.RequirePipelineState(*material, d3dMesh->mVertElements, mDevice->GetDevice());

        if (mLastPipeline != pipelineState) {
            mLastPipeline = pipelineState;
            mCmdList->SetPipelineState(pipelineState->mPipelineState.Get());

            auto heap = mDevice->GetCBHeap();
            mCmdList->SetDescriptorHeaps(1, &heap);

            mCmdList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        }

        for(int i = 0; i < pipelineState->mConstantBuffers.size(); ++i)
        {
            auto cb = pipelineState->mConstantBuffers[i];
            if (cb == nullptr) continue;
            auto d3dCB = cache.RequireConstantBuffer(*cb, *material, mDevice->GetDevice());
            if (mLastCBs[i] == d3dCB) continue;
            mLastCBs[i] = d3dCB;
            mCmdList->SetGraphicsRootDescriptorTable(cb->mBindPoint, d3dCB->mConstantBufferHandle);
        }

        if (mLastMesh != d3dMesh) {
            mLastMesh = d3dMesh;
            mCmdList->IASetVertexBuffers(0, 1, &d3dMesh->mVertexBufferView);
            mCmdList->IASetIndexBuffer(&d3dMesh->mIndexBufferView);
        }
        mCmdList->DrawIndexedInstanced(mesh->GetIndexCount(), 1, 0, 0, 0);
    }
    // Send the commands to the GPU
    // TODO: Should this be automatic?
    void Execute() override
    {
        SetResourceBarrier(D3D12_RESOURCE_STATE_RENDER_TARGET, D3D12_RESOURCE_STATE_PRESENT);
        mCmdList->Close();

        ID3D12CommandList* ppCommandLists[] = { mCmdList.Get(), };
        mDevice->GetDevice().GetCmdQueue()->ExecuteCommandLists(_countof(ppCommandLists), ppCommandLists);
    }

};

GraphicsDeviceD3D12::GraphicsDeviceD3D12(const WindowWin32& window)
    : mDevice(window)
{
    auto mD3DDevice = mDevice.GetD3DDevice();
    auto mSwapChain = mDevice.GetSwapChain();
    // Create fence for frame synchronisation
    mFrameId = mSwapChain->GetCurrentBackBufferIndex();
    for (int i = 0; i < FrameCount; ++i) mFenceValues[i] = 0;
    ThrowIfFailed(mD3DDevice->CreateFence(mFenceValues[mFrameId]++, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&mFence)));
    mFenceEvent = CreateEvent(nullptr, FALSE, FALSE, nullptr);
    if (mFenceEvent == nullptr) ThrowIfFailed(HRESULT_FROM_WIN32(GetLastError()));
    WaitForGPU();

    auto descriptorSize = mDevice.GetDescriptorHandleSize();
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
        CD3DX12_CLEAR_VALUE depthOptimizedClearValue(DXGI_FORMAT_D32_FLOAT, 1.0f, 0);
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
}


CommandBuffer GraphicsDeviceD3D12::CreateCommandBuffer()
{
    return CommandBuffer(new D3DInterop(this));
}

// Flip the backbuffer and wait until a frame is available to be rendered
void GraphicsDeviceD3D12::Present()
{
    mDevice.GetSwapChain()->Present(1, 0);
    WaitForFrame();
}

// Wait for the earliest submitted frame to be finished and ready to be rendered into
void GraphicsDeviceD3D12::WaitForFrame()
{
    // Schedule a Signal command in the queue.
    const UINT64 currentFenceValue = mFenceValues[mFrameId];
    ThrowIfFailed(mDevice.GetCmdQueue()->Signal(mFence.Get(), currentFenceValue));

    // Update the frame index.
    mFrameId = mDevice.GetSwapChain()->GetCurrentBackBufferIndex();

    // If the next frame is not ready to be rendered yet, wait until it is ready.
    auto fenceVal = mFence->GetCompletedValue();
    if (fenceVal < mFenceValues[mFrameId])
    {
        ThrowIfFailed(mFence->SetEventOnCompletion(mFenceValues[mFrameId], mFenceEvent));
        WaitForSingleObjectEx(mFenceEvent, INFINITE, FALSE);
    }

    // Set the fence value for the next frame.
    mFenceValues[mFrameId] = currentFenceValue + 1;
    mCmdAllocator[mFrameId]->Reset();
    mCache.SetResourceLockIds(fenceVal, currentFenceValue);
}
// Wait for all GPU operations? Taken from the samples
void GraphicsDeviceD3D12::WaitForGPU()
{
    // Schedule a Signal command in the queue.
    ThrowIfFailed(mDevice.GetCmdQueue()->Signal(mFence.Get(), mFenceValues[mFrameId]));

    // Wait until the fence has been processed.
    ThrowIfFailed(mFence->SetEventOnCompletion(mFenceValues[mFrameId], mFenceEvent));
    WaitForSingleObjectEx(mFenceEvent, INFINITE, FALSE);

    // Increment the fence value for the current frame.
    mFenceValues[mFrameId]++;
}
