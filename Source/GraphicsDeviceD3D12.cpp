#include <d3dcompiler.h>
#include <unordered_map>
#include <memory>
#include <functional>
#include <tuple>
#include <algorithm>

#include "GraphicsDeviceD3D12.h"
#include <d3dx12.h>
#include "D3DShader.h"

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

// Many items require GPU resources to be allocated and managed
// to be rendered. This class generates and manages those resources.
class D3DResourceCache {
public:
    // The GPU data for a mesh
    struct D3DMesh
    {
        std::vector<D3D12_INPUT_ELEMENT_DESC> mVertElements;
        int mRevision;
        ComPtr<ID3D12Resource> mVertexBuffer;
        D3D12_VERTEX_BUFFER_VIEW mVertexBufferView;
        ComPtr<ID3D12Resource> mIndexBuffer;
        D3D12_INDEX_BUFFER_VIEW mIndexBufferView;
    };
    // The GPU data for a set of shaders, rendering state, and vertex attributes
    struct  D3DPipelineState
    {
        ComPtr<ID3D12PipelineState> mPipelineState;
        ComPtr<ID3D12Resource> mConstantBuffer;
        D3D12_GPU_DESCRIPTOR_HANDLE mConstantBufferHandle;
    };

private:
    // Storage for the GPU resources of each application type
    // TODO: Register for destruction of the application type
    // and clean up GPU resources
    std::unordered_map<const Mesh*, std::unique_ptr<D3DMesh>> meshMapping;
    std::unordered_map<ShaderKey, std::unique_ptr<D3DShader>> shaderMapping;
    std::unordered_map<size_t, std::unique_ptr<D3DPipelineState>> pipelineMapping;
    int mCBOffset;

    // Allocate or retrieve a container for GPU buffers for this item
    D3DMesh* RequireD3DMesh(const Mesh& mesh)
    {
        return GetOrCreate(meshMapping, &mesh);
    }
    D3DShader* RequireShader(const Shader& shader, const std::string& entrypoint)
    {
        ShaderKey key = { shader.GetPath(), entrypoint };
        return GetOrCreate(shaderMapping, key);
    }
    D3DPipelineState* RequirePipelineState(const D3DShader* vshader, const D3DShader* pshader, size_t hash)
    {
        auto key = hash ^ std::hash<void*>()((void*)vshader) ^ std::hash<void*>()((void*)pshader);
        return GetOrCreate(pipelineMapping, key);
    }

    // Generate the GPU resources required for rendering a mesh
    void UpdateMeshData(const Mesh& mesh, GraphicsDeviceD3D12& d3d12)
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
        d3dMesh->mIndexBufferView = { d3dMesh->mIndexBuffer->GetGPUVirtualAddress(), (UINT)vbufferByteSize, DXGI_FORMAT_R32_UINT };
        // Track that the mesh is nwo up to date
        d3dMesh->mRevision = mesh.GetRevision();
    }
    // Generate a descriptor of the required vertex attributes for this mesh
    int GenerateElementDesc(const Mesh& mesh, std::vector<D3D12_INPUT_ELEMENT_DESC>& vertDesc)
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
    void CopyVertexData(const Mesh& mesh, void* buffer, int stride) {
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
public:
    // Ensure a mesh is ready to be rendered by the GPU
    D3DMesh* RequireMesh(const Mesh& mesh, GraphicsDeviceD3D12& d3d12)
    {
        auto d3dMesh = RequireD3DMesh(mesh);
        if (d3dMesh->mRevision != mesh.GetRevision())
            UpdateMeshData(mesh, d3d12);
        return d3dMesh;
    }
    // Ensure a material is ready to be rendererd by the GPU (with the specified vertex layout)
    D3DPipelineState* RequirePipelineState(const Material& material, std::span<D3D12_INPUT_ELEMENT_DESC> vertElements, GraphicsDeviceD3D12& d3d12)
    {
        auto device = d3d12.GetD3DDevice();

        // Make sure shaders are compiled
        auto sourceVS = material.GetVertexShader();
        auto sourcePS = material.GetVertexShader();
        auto vShader = RequireShader(sourceVS, "VSMain");
        auto pShader = RequireShader(sourcePS, "PSMain");
        if (vShader->mShader == nullptr) {
            vShader->CompileFromFile(sourceVS.GetPath(), "VSMain", "vs_5_0");
        }
        if (pShader->mShader == nullptr) {
            pShader->CompileFromFile(sourceVS.GetPath(), "PSMain", "ps_5_0");
        }

        // Find (or create) a pipeline that matches these requirements
        size_t hash = 0;
        for (auto el : vertElements) hash ^= std::hash<void*>()((void*)el.SemanticName);
        auto pipelineState = RequirePipelineState(vShader, pShader, hash);
        if (pipelineState->mPipelineState == nullptr)
        {
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
        }
        // Generate a constant buffer
        // TODO: Support more than 1 constant buffer, share constant buffers between pipelines
        // TODO: Cache binary data so that it is reused most efficiently
        if (pipelineState->mConstantBuffer == nullptr)
        {
            auto cbSize = 256;// (cb.size() + 255) & ~255;
            CD3DX12_HEAP_PROPERTIES heapProperties(D3D12_HEAP_TYPE_UPLOAD);
            CD3DX12_RESOURCE_DESC resourceDesc = CD3DX12_RESOURCE_DESC::Buffer(cbSize);
            device->CreateCommittedResource(
                &heapProperties,
                D3D12_HEAP_FLAG_NONE,
                &resourceDesc,
                D3D12_RESOURCE_STATE_GENERIC_READ,
                nullptr,
                IID_PPV_ARGS(&pipelineState->mConstantBuffer)
            );
            D3D12_GPU_VIRTUAL_ADDRESS constantBufferAddress = pipelineState->mConstantBuffer->GetGPUVirtualAddress();
            D3D12_CONSTANT_BUFFER_VIEW_DESC constantBufferView;
            constantBufferView.BufferLocation = constantBufferAddress;
            constantBufferView.SizeInBytes = cbSize;
            // Get the descriptor heap handle for the constant buffer view
            auto cbvHandle = d3d12.GetCBHeap()->GetCPUDescriptorHandleForHeapStart();
            auto gbvHandle = d3d12.GetCBHeap()->GetGPUDescriptorHandleForHeapStart();
            cbvHandle.ptr += mCBOffset;
            gbvHandle.ptr += mCBOffset;
            device->CreateConstantBufferView(&constantBufferView, cbvHandle);
            pipelineState->mConstantBufferHandle = gbvHandle;
            mCBOffset += d3d12.GetDescriptorHandleSize();
        }

        // Copy data into the constant buffer
        // TODO: Only copy data when it is changed
        for (auto l : { vShader->mConstantBuffers, pShader->mConstantBuffers })
        {
            for (auto cb : l)
            {
                UINT8* cbDataBegin;
                pipelineState->mConstantBuffer->Map(0, nullptr, reinterpret_cast<void**>(&cbDataBegin));
                for (auto var : cb.mValues)
                {
                    auto varData = material.GetUniformBinaryData(var.mName);
                    memcpy(cbDataBegin + var.mOffset, varData.data(), varData.size());
                }
                pipelineState->mConstantBuffer->Unmap(0, nullptr);
            }
        }
        return pipelineState;
    }

private:
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
        auto newMesh = new T();
        map.insert(std::make_pair(key, newMesh));
        return newMesh;
    }
};

// Handles receiving rendering events from the user application
// and issuing relevant draw commands
class D3DInterop : public CommandBufferInteropBase {
    GraphicsDeviceD3D12* mDevice;
    ComPtr<ID3D12GraphicsCommandList> mCmdList;
    D3DResourceCache mCache;
public:
    D3DInterop(GraphicsDeviceD3D12* device) : mDevice(device) {
        D3D12_COMMAND_QUEUE_DESC queueDesc = {};
        queueDesc.Flags = D3D12_COMMAND_QUEUE_FLAG_NONE;
        queueDesc.Type = D3D12_COMMAND_LIST_TYPE_DIRECT;
        ThrowIfFailed(device->GetD3DDevice()
            ->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT,
                device->mCmdAllocator[device->mFrameId].Get(), nullptr, IID_PPV_ARGS(&mCmdList)));
        mCmdList->Close();
    }
    ID3D12Device* GetD3DDevice() const { return mDevice->mD3DDevice.Get(); }
    void SetResourceBarrier(const D3D12_RESOURCE_STATES StateBefore, const D3D12_RESOURCE_STATES StateAfter) {
        auto barrier = CD3DX12_RESOURCE_BARRIER::Transition(mDevice->mRenderTargets[mDevice->mFrameId].Get(), StateBefore, StateAfter);
        mCmdList->ResourceBarrier(1, &barrier);
    }
    // Get this command buffer ready to begin rendering
    void Reset() override
    {
        auto clientSize = mDevice->mClientSize;
        CD3DX12_VIEWPORT viewport(0.0f, 0.0f, (float)std::get<0>(clientSize), (float)std::get<1>(clientSize));
        CD3DX12_RECT scissorRect(0, 0, std::get<0>(clientSize), std::get<1>(clientSize));

        mCmdList->Reset(mDevice->mCmdAllocator[mDevice->mFrameId].Get(), nullptr);
        mCmdList->SetGraphicsRootSignature(mDevice->mRootSignature.Get());
        mCmdList->RSSetViewports(1, &viewport);
        mCmdList->RSSetScissorRects(1, &scissorRect);
        SetResourceBarrier(D3D12_RESOURCE_STATE_PRESENT, D3D12_RESOURCE_STATE_RENDER_TARGET);

        auto descriptor = mDevice->mRTVHeap->GetCPUDescriptorHandleForHeapStart();
        descriptor.ptr += mDevice->mDescriptorHandleSize * mDevice->mFrameId;
        auto depth = mDevice->mDSVHeap->GetCPUDescriptorHandleForHeapStart();
        mCmdList->OMSetRenderTargets(1, &descriptor, FALSE, &depth);
    }
    // Clear the screen
    void ClearRenderTarget(const ClearConfig& clear) override
    {
        if (clear.HasClearColor())
        {
            auto descriptor = mDevice->mRTVHeap->GetCPUDescriptorHandleForHeapStart();
            descriptor.ptr += mDevice->mDescriptorHandleSize * mDevice->mFrameId;
            mCmdList->ClearRenderTargetView(descriptor, clear.ClearColor, 0, nullptr);
        }
        auto flags = (clear.HasClearDepth() ? D3D12_CLEAR_FLAG_DEPTH : 0)
            | (clear.HasClearScencil() ? D3D12_CLEAR_FLAG_STENCIL : 0);
        if (flags)
        {
            auto depth = mDevice->mDSVHeap->GetCPUDescriptorHandleForHeapStart();
            mCmdList->ClearDepthStencilView(depth, (D3D12_CLEAR_FLAGS)flags,
                clear.ClearDepth, clear.ClearStencil, 0, nullptr);
        }
    }

    // Draw a mesh with the specified material
    void DrawMesh(std::shared_ptr<Mesh> mesh, std::shared_ptr<Material> material) override
    {
        auto d3dMesh = mCache.RequireMesh(*mesh.get(), *mDevice);
        auto pipelineState = mCache.RequirePipelineState(*material.get(), d3dMesh->mVertElements, *mDevice);

        mCmdList->SetPipelineState(pipelineState->mPipelineState.Get());

        auto heap = mDevice->GetCBHeap();
        mCmdList->SetDescriptorHeaps(1, &heap);
        mCmdList->SetGraphicsRootDescriptorTable(0, pipelineState->mConstantBufferHandle);

        mCmdList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        mCmdList->IASetVertexBuffers(0, 1, &d3dMesh->mVertexBufferView);
        mCmdList->IASetIndexBuffer(&d3dMesh->mIndexBufferView);
        mCmdList->DrawIndexedInstanced(mesh->GetIndexCount(), 1, 0, 0, 0);
    }
    // Send the commands to the GPU
    // TODO: Should this be automatic?
    void Execute() override
    {
        SetResourceBarrier(D3D12_RESOURCE_STATE_RENDER_TARGET, D3D12_RESOURCE_STATE_PRESENT);
        mCmdList->Close();

        ID3D12CommandList* ppCommandLists[] = { mCmdList.Get(), };
        mDevice->mCmdQueue->ExecuteCommandLists(_countof(ppCommandLists), ppCommandLists);
    }

};

// Initialise D3D with the specified window
GraphicsDeviceD3D12::GraphicsDeviceD3D12(const WindowWin32& window)
{
    CoInitialize(nullptr);

	auto hWnd = window.GetHWND();

    UINT dxgiFactoryFlags = 0;

    // Enable debug mode in debug builds
#if defined(_DEBUG)
    {
        ComPtr<ID3D12Debug> debugController;
        if (SUCCEEDED(D3D12GetDebugInterface(IID_PPV_ARGS(&debugController))))
        {
            debugController->EnableDebugLayer();
            dxgiFactoryFlags |= DXGI_CREATE_FACTORY_DEBUG;
        }
    }
#endif
    ThrowIfFailed(CreateDXGIFactory2(dxgiFactoryFlags, IID_PPV_ARGS(&mDXGIFactory)));

    // Create the device
    ThrowIfFailed(D3D12CreateDevice(nullptr, D3D_FEATURE_LEVEL_11_0, IID_PPV_ARGS(&mD3DDevice)));

    // Create the command queue
    D3D12_COMMAND_QUEUE_DESC queueDesc = {};
    queueDesc.Flags = D3D12_COMMAND_QUEUE_FLAG_NONE;
    queueDesc.Type = D3D12_COMMAND_LIST_TYPE_DIRECT;
    ThrowIfFailed(mD3DDevice->CreateCommandQueue(&queueDesc, IID_PPV_ARGS(&mCmdQueue)));

    // Check the window for how large the backbuffer should be
    RECT clientRect;
    GetClientRect(hWnd, &clientRect);
    mClientSize = std::make_tuple(clientRect.right - clientRect.left, clientRect.bottom - clientRect.top);

    // Create the swap chain
    DXGI_SWAP_CHAIN_DESC1 swapChainDesc = {};
    swapChainDesc.BufferCount = FrameCount;
    swapChainDesc.Width = std::get<0>(mClientSize);
    swapChainDesc.Height = std::get<1>(mClientSize);
    swapChainDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    swapChainDesc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    swapChainDesc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;
    swapChainDesc.SampleDesc.Count = 1;

    ComPtr<IDXGISwapChain1> swapChain;
    ThrowIfFailed(mDXGIFactory->CreateSwapChainForHwnd(mCmdQueue.Get(), hWnd, &swapChainDesc, nullptr, nullptr, &swapChain));
    ThrowIfFailed(swapChain.As(&mSwapChain));
    mFrameId = mSwapChain->GetCurrentBackBufferIndex();

    // Create descriptor heaps.
    {
        // Describe and create a render target view (RTV) descriptor heap.
        D3D12_DESCRIPTOR_HEAP_DESC rtvHeapDesc = {};
        rtvHeapDesc.NumDescriptors = FrameCount;
        rtvHeapDesc.Type = D3D12_DESCRIPTOR_HEAP_TYPE_RTV;
        rtvHeapDesc.Flags = D3D12_DESCRIPTOR_HEAP_FLAG_NONE;
        ThrowIfFailed(mD3DDevice->CreateDescriptorHeap(&rtvHeapDesc, IID_PPV_ARGS(&mRTVHeap)));

        // Describe and create a depth stencil view (DSV) descriptor heap.
        // Each frame has its own depth stencils (to write shadows onto) 
        // and then there is one for the scene itself.
        D3D12_DESCRIPTOR_HEAP_DESC dsvHeapDesc = {};
        dsvHeapDesc.NumDescriptors = FrameCount;
        dsvHeapDesc.Type = D3D12_DESCRIPTOR_HEAP_TYPE_DSV;
        dsvHeapDesc.Flags = D3D12_DESCRIPTOR_HEAP_FLAG_NONE;
        ThrowIfFailed(mD3DDevice->CreateDescriptorHeap(&dsvHeapDesc, IID_PPV_ARGS(&mDSVHeap)));

        // Describe and create a shader resource view (SRV) and constant 
        // buffer view (CBV) descriptor heap.  Heap layout: null views, 
        // object diffuse + normal textures views, frame 1's shadow buffer, 
        // frame 1's 2x constant buffer, frame 2's shadow buffer, frame 2's 
        // 2x constant buffers, etc...
        const UINT nullSrvCount = 2;        // Null descriptors are needed for out of bounds behavior reads.
        const UINT cbvCount = FrameCount * 2;
        const UINT srvCount = 10 + (FrameCount * 1);
        D3D12_DESCRIPTOR_HEAP_DESC cbvSrvHeapDesc = {};
        cbvSrvHeapDesc.NumDescriptors = nullSrvCount + cbvCount + srvCount;
        cbvSrvHeapDesc.Type = D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV;
        cbvSrvHeapDesc.Flags = D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE;
        ThrowIfFailed(mD3DDevice->CreateDescriptorHeap(&cbvSrvHeapDesc, IID_PPV_ARGS(&mCBVSrvHeap)));

        // Describe and create a sampler descriptor heap.
        // NOTE: Not currently used; no texture support
        D3D12_DESCRIPTOR_HEAP_DESC samplerHeapDesc = {};
        samplerHeapDesc.NumDescriptors = 2;        // One clamp and one wrap sampler.
        samplerHeapDesc.Type = D3D12_DESCRIPTOR_HEAP_TYPE_SAMPLER;
        samplerHeapDesc.Flags = D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE;
        ThrowIfFailed(mD3DDevice->CreateDescriptorHeap(&samplerHeapDesc, IID_PPV_ARGS(&mSamplerHeap)));
        
        mDescriptorHandleSize = mD3DDevice->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_RTV);
    }

    // Create a RTV for each frame.
    for (UINT n = 0; n < FrameCount; n++)
    {
        ThrowIfFailed(mSwapChain->GetBuffer(n, IID_PPV_ARGS(&mRenderTargets[n])));
        auto handle = CD3DX12_CPU_DESCRIPTOR_HANDLE(mRTVHeap->GetCPUDescriptorHandleForHeapStart(), n, mDescriptorHandleSize);
        mD3DDevice->CreateRenderTargetView(mRenderTargets[n].Get(), nullptr, handle);
        ThrowIfFailed(mD3DDevice->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&mCmdAllocator[n])));
    }

    // Create the depth buffer
    {
        auto heapParams = CD3DX12_HEAP_PROPERTIES(D3D12_HEAP_TYPE_DEFAULT);
        auto texParams = CD3DX12_RESOURCE_DESC::Tex2D(DXGI_FORMAT_D32_FLOAT, swapChainDesc.Width, swapChainDesc.Height, 1, 0, 1, 0, D3D12_RESOURCE_FLAG_ALLOW_DEPTH_STENCIL);
        CD3DX12_CLEAR_VALUE depthOptimizedClearValue(DXGI_FORMAT_D32_FLOAT, 1.0f, 0);
        ThrowIfFailed(mD3DDevice->CreateCommittedResource(
            &heapParams,
            D3D12_HEAP_FLAG_NONE,
            &texParams,
            D3D12_RESOURCE_STATE_DEPTH_WRITE,
            &depthOptimizedClearValue,
            IID_PPV_ARGS(&mDepthTarget)
        ));
        mD3DDevice->CreateDepthStencilView(mDepthTarget.Get(), nullptr, mDSVHeap->GetCPUDescriptorHandleForHeapStart());
    }

    // Create fence for frame synchronisation
    ThrowIfFailed(mD3DDevice->CreateFence(mFenceValues[mFrameId], D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&mFence)));
    mFenceValues[mFrameId]++;
    mFenceEvent = CreateEvent(nullptr, FALSE, FALSE, nullptr);
    if (mFenceEvent == nullptr) ThrowIfFailed(HRESULT_FROM_WIN32(GetLastError()));
    WaitForGPU();

    D3D12_FEATURE_DATA_ROOT_SIGNATURE featureData = {};

    // This is the highest version the sample supports. If CheckFeatureSupport succeeds, the HighestVersion returned will not be greater than this.
    featureData.HighestVersion = D3D_ROOT_SIGNATURE_VERSION_1_1;
    if (FAILED(mD3DDevice->CheckFeatureSupport(D3D12_FEATURE_ROOT_SIGNATURE, &featureData, sizeof(featureData))))
        featureData.HighestVersion = D3D_ROOT_SIGNATURE_VERSION_1_0;

    // Unsure what to do here.. We should allocate the maximum we need? But not too much?
    // TODO: Investigate more
    CD3DX12_DESCRIPTOR_RANGE1 cbvR0(D3D12_DESCRIPTOR_RANGE_TYPE_CBV, 1, 0, 0, D3D12_DESCRIPTOR_RANGE_FLAG_DATA_STATIC);
    CD3DX12_DESCRIPTOR_RANGE1 cbvR1(D3D12_DESCRIPTOR_RANGE_TYPE_CBV, 1, 1, 0, D3D12_DESCRIPTOR_RANGE_FLAG_DATA_STATIC);
    CD3DX12_ROOT_PARAMETER1 rootParameters[2];
    rootParameters[0].InitAsDescriptorTable(1, &cbvR0, D3D12_SHADER_VISIBILITY_ALL);
    rootParameters[1].InitAsDescriptorTable(1, &cbvR1, D3D12_SHADER_VISIBILITY_ALL);

    CD3DX12_VERSIONED_ROOT_SIGNATURE_DESC rootSignatureDesc;
    rootSignatureDesc.Init_1_1(_countof(rootParameters), rootParameters, 0, nullptr, D3D12_ROOT_SIGNATURE_FLAG_ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT);

    ComPtr<ID3DBlob> signature;
    ComPtr<ID3DBlob> error;
    ThrowIfFailed(D3DX12SerializeVersionedRootSignature(&rootSignatureDesc, featureData.HighestVersion, &signature, &error));
    ThrowIfFailed(mD3DDevice->CreateRootSignature(0, signature->GetBufferPointer(), signature->GetBufferSize(), IID_PPV_ARGS(&mRootSignature)));
}

GraphicsDeviceD3D12::~GraphicsDeviceD3D12()
{
    CoUninitialize();
}

CommandBuffer GraphicsDeviceD3D12::CreateCommandBuffer()
{
    return CommandBuffer(new D3DInterop(this));
}

// Flip the backbuffer and wait until a frame is available to be rendered
void GraphicsDeviceD3D12::Present()
{
    mSwapChain->Present(1, 0);
    WaitForFrame();
}

// Wait for the earliest submitted frame to be finished and ready to be rendered into
void GraphicsDeviceD3D12::WaitForFrame()
{
    // Schedule a Signal command in the queue.
    const UINT64 currentFenceValue = mFenceValues[mFrameId];
    ThrowIfFailed(mCmdQueue->Signal(mFence.Get(), currentFenceValue));

    // Update the frame index.
    mFrameId = mSwapChain->GetCurrentBackBufferIndex();

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
}
// Wait for all GPU operations? Taken from the samples
void GraphicsDeviceD3D12::WaitForGPU()
{
    // Schedule a Signal command in the queue.
    ThrowIfFailed(mCmdQueue->Signal(mFence.Get(), mFenceValues[mFrameId]));

    // Wait until the fence has been processed.
    ThrowIfFailed(mFence->SetEventOnCompletion(mFenceValues[mFrameId], mFenceEvent));
    WaitForSingleObjectEx(mFenceEvent, INFINITE, FALSE);

    // Increment the fence value for the current frame.
    mFenceValues[mFrameId]++;
}
