#include "GraphicsDeviceD3D12.h"
#include "D3DConstantBufferCache.h"
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

D3DResourceCache::D3DResourceCache(D3DGraphicsDevice& d3d12)
    : mD3D12(d3d12)
    , mCBOffset(0)
{
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
    auto key = hash ^ std::hash<int>()(sourceVSId) ^ (0x5123 + std::hash<int>()(sourcePSId));
    return GetOrCreate(pipelineMapping, key);
}

// Generate the GPU resources required for rendering a mesh
void D3DResourceCache::UpdateMeshData(D3DMesh* d3dMesh, const Mesh& mesh, ID3D12GraphicsCommandList* cmdList)
{
    auto device = mD3D12.GetD3DDevice();

    // Get vertex attributes
    d3dMesh->mVertElements.clear();
    int vertexStride = GenerateElementDesc(mesh, d3dMesh->mVertElements);

    // Compute buffer sizes
    int vbufferByteSize = vertexStride * mesh.GetVertexCount();
    int ibufferByteSize = sizeof(int) * mesh.GetIndexCount();

    // Allocate vertex and index buffers
    auto heapProps = CD3DX12_HEAP_PROPERTIES(D3D12_HEAP_TYPE_DEFAULT);
    if (d3dMesh->mVertexBufferView.SizeInBytes != vbufferByteSize || d3dMesh->mVertexBuffer == nullptr)
    {
        if (d3dMesh->mVertexBuffer != nullptr)
        {
            mUploadBufferCache.InsertItem(d3dMesh->mVertexBuffer);
            d3dMesh->mVertexBuffer = nullptr;
        }
        auto resDesc = CD3DX12_RESOURCE_DESC::Buffer(vbufferByteSize);
        ThrowIfFailed(device->CreateCommittedResource(
            &heapProps,
            D3D12_HEAP_FLAG_NONE,
            &resDesc,
            D3D12_RESOURCE_STATE_COPY_DEST,
            nullptr,
            IID_PPV_ARGS(&d3dMesh->mVertexBuffer)));
        d3dMesh->mVertexBuffer->SetName(L"VertexBuffer");
    }
    if (d3dMesh->mIndexBufferView.SizeInBytes != ibufferByteSize || d3dMesh->mIndexBuffer == nullptr)
    {
        if (d3dMesh->mIndexBuffer != nullptr)
        {
            mUploadBufferCache.InsertItem(d3dMesh->mIndexBuffer);
            d3dMesh->mIndexBuffer = nullptr;
        }
        auto resDesc = CD3DX12_RESOURCE_DESC::Buffer(ibufferByteSize);
        ThrowIfFailed(device->CreateCommittedResource(
            &heapProps,
            D3D12_HEAP_FLAG_NONE,
            &resDesc,
            D3D12_RESOURCE_STATE_COPY_DEST,
            nullptr,
            IID_PPV_ARGS(&d3dMesh->mIndexBuffer)));
        d3dMesh->mIndexBuffer->SetName(L"IndexBuffer");
    }

    auto vUploadBuffer = AllocateUploadBuffer(vbufferByteSize);
    // Copy data into buffer
    UINT8* mappedData;
    CD3DX12_RANGE readRange(0, 0);
    ThrowIfFailed(vUploadBuffer->Map(0, &readRange, reinterpret_cast<void**>(&mappedData)));
    CopyVertexData(mesh, mappedData, vertexStride);
    vUploadBuffer->Unmap(0, nullptr);
    cmdList->CopyBufferRegion(d3dMesh->mVertexBuffer.Get(), 0, vUploadBuffer, 0, vbufferByteSize);

    auto iUploadBuffer = AllocateUploadBuffer(vbufferByteSize);
    ThrowIfFailed(iUploadBuffer->Map(0, &readRange, reinterpret_cast<void**>(&mappedData)));
    auto inds = mesh.GetIndices();
    std::transform(inds.begin(), inds.end(), (int*)mappedData, [](auto i) { return i; });
    iUploadBuffer->Unmap(0, nullptr);
    cmdList->CopyBufferRegion(d3dMesh->mIndexBuffer.Get(), 0, iUploadBuffer, 0, ibufferByteSize);

    auto endWrite = {
        CD3DX12_RESOURCE_BARRIER::Transition(d3dMesh->mVertexBuffer.Get(), D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_COMMON),
        CD3DX12_RESOURCE_BARRIER::Transition(d3dMesh->mIndexBuffer.Get(), D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_COMMON),
    };
    cmdList->ResourceBarrier((UINT)endWrite.size(), endWrite.begin());

    // Create views into buffers
    d3dMesh->mVertexBufferView = { d3dMesh->mVertexBuffer->GetGPUVirtualAddress(), (UINT)vbufferByteSize, (UINT)vertexStride };
    d3dMesh->mIndexBufferView = { d3dMesh->mIndexBuffer->GetGPUVirtualAddress(), (UINT)ibufferByteSize, DXGI_FORMAT_R32_UINT };
    // Track that the mesh is nwo up to date
    d3dMesh->mRevision = mesh.GetRevision();
}
// Generate a descriptor of the required vertex attributes for this mesh
int D3DResourceCache::GenerateElementDesc(const Mesh& mesh, std::vector<D3D12_INPUT_ELEMENT_DESC>& vertDesc)
{
    uint32_t offset = 0;
    if (!mesh.GetPositions().empty())
        vertDesc.push_back({ "POSITION", 0, DXGI_FORMAT_R32G32B32_FLOAT, 0, PostIncrement(offset, 12u), D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA, 0 });
    if (!mesh.GetNormals().empty())
        vertDesc.push_back({ "NORMAL",   0, DXGI_FORMAT_R32G32B32_FLOAT, 0, PostIncrement(offset, 12u), D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA, 0 });
    if (!mesh.GetUVs().empty())
        vertDesc.push_back({ "TEXCOORD", 0, DXGI_FORMAT_R32G32_FLOAT,    0, PostIncrement(offset, 8u),  D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA, 0 });
    if (!mesh.GetColors().empty())
        vertDesc.push_back({ "COLOR", 0, DXGI_FORMAT_R32G32B32A32_FLOAT, 0, PostIncrement(offset, 16u),  D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA, 0 });
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
// Retrieve a buffer capable of upload/copy that will be vaild until
// the frame completes rendering
ID3D12Resource* D3DResourceCache::AllocateUploadBuffer(int uploadSize)
{
    auto& uploadBufferItem = mUploadBufferCache.RequireItem(uploadSize,
        [&](auto& item) // Allocate a new item
        {
            auto uploadHeapType = CD3DX12_HEAP_PROPERTIES(D3D12_HEAP_TYPE_UPLOAD);
            auto uploadBufferDesc = CD3DX12_RESOURCE_DESC::Buffer(uploadSize);
            ThrowIfFailed(mD3D12.GetD3DDevice()->CreateCommittedResource(
                &uploadHeapType,
                D3D12_HEAP_FLAG_NONE,
                &uploadBufferDesc,
                D3D12_RESOURCE_STATE_GENERIC_READ,
                nullptr,
                IID_PPV_ARGS(&item.mData)
            ));
            item.mData->SetName(L"UploadBuffer");
        },
        [&](auto& item) {}
    );
    return uploadBufferItem.mData.Get();
}
void D3DResourceCache::UpdateTextureData(D3DTexture* d3dTex, const Texture& tex, ID3D12GraphicsCommandList* cmdList)
{
    auto device = mD3D12.GetD3DDevice();
    auto size = tex.GetSize();
    auto textureDesc = CD3DX12_RESOURCE_DESC::Tex2D(DXGI_FORMAT_R8G8B8A8_UNORM, size.x, size.y, 1, 1);

    // Get d3d cache instance
    if (d3dTex->mTexture == nullptr)
    {
        // Create the texture resource
        auto texHeapType = CD3DX12_HEAP_PROPERTIES(D3D12_HEAP_TYPE_DEFAULT);
        textureDesc.Flags |= D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS;
        ThrowIfFailed(device->CreateCommittedResource(
            &texHeapType,
            D3D12_HEAP_FLAG_NONE,
            &textureDesc,
            D3D12_RESOURCE_STATE_COPY_DEST,
            nullptr,
            IID_PPV_ARGS(&d3dTex->mTexture)
        ));
        d3dTex->mTexture->SetName(L"UserTexture");

        // Create a shader resource view (SRV) for the texture
        D3D12_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
        srvDesc.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
        srvDesc.Format = textureDesc.Format;
        srvDesc.ViewDimension = D3D12_SRV_DIMENSION_TEXTURE2D;
        srvDesc.Texture2D.MipLevels = textureDesc.MipLevels;

        // Get the CPU handle to the descriptor in the heap
        auto descriptorSize = mD3D12.GetDescriptorHandleSize();
        CD3DX12_CPU_DESCRIPTOR_HANDLE srvHandle(mD3D12.GetSRVHeap()->GetCPUDescriptorHandleForHeapStart(), mCBOffset);
        device->CreateShaderResourceView(d3dTex->mTexture.Get(), &srvDesc, srvHandle);
        d3dTex->mSRVOffset = mCBOffset;
        mCBOffset += descriptorSize;
    }

    auto uploadSize = (GetRequiredIntermediateSize(d3dTex->mTexture.Get(), 0, 1) + D3D12_DEFAULT_RESOURCE_PLACEMENT_ALIGNMENT - 1) & ~(D3D12_DEFAULT_RESOURCE_PLACEMENT_ALIGNMENT - 1);

    // Update the texture data
    auto srcData = tex.GetData();
    D3D12_SUBRESOURCE_DATA textureData = {};
    textureData.pData = reinterpret_cast<UINT8*>(srcData.data());
    textureData.RowPitch = 4 * size.x;
    textureData.SlicePitch = textureData.RowPitch * size.y;
    auto uploadBuffer = AllocateUploadBuffer((int)uploadSize);
    UpdateSubresources<1>(cmdList, d3dTex->mTexture.Get(), uploadBuffer, 0, 0, 1, &textureData);

    // Put the texture back in normal mode
    auto endWrite = CD3DX12_RESOURCE_BARRIER::Transition(d3dTex->mTexture.Get(), D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_COMMON);
    cmdList->ResourceBarrier(1, &endWrite);

    d3dTex->mRevision = tex.GetRevision();
}

void D3DResourceCache::SetResourceLockIds(UINT64 lockFrameId, UINT64 writeFrameId)
{
    mConstantBufferCache.SetResourceLockIds(lockFrameId, writeFrameId);
    mUploadBufferCache.SetResourceLockIds(lockFrameId, writeFrameId);
}
// Allocate or retrieve a container for GPU buffers for this item
D3DResourceCache::D3DMesh* D3DResourceCache::RequireD3DMesh(const Mesh& mesh)
{
    return GetOrCreate(meshMapping, &mesh);
}
// Allocate or retrieve a container for GPU buffers for this item
D3DResourceCache::D3DTexture* D3DResourceCache::RequireD3DTexture(const Texture& tex)
{
    return GetOrCreate(textureMapping, &tex);
}
// Ensure a material is ready to be rendererd by the GPU (with the specified vertex layout)
D3DResourceCache::D3DPipelineState* D3DResourceCache::RequirePipelineState(const Material& material, std::span<D3D12_INPUT_ELEMENT_DESC> vertElements)
{
    // Get the relevant shaders
    const auto& sourceVS = *material.GetVertexShader();
    const auto& sourcePS = *material.GetPixelShader();
    auto& blendMode = material.GetBlendMode();
    auto& rasterMode = material.GetRasterMode();
    auto& depthMode = material.GetDepthMode();

    // Find (or create) a pipeline that matches these requirements
    size_t hash = GenericHash(blendMode) ^ GenericHash(rasterMode) ^ GenericHash(depthMode);
    for (auto& el : vertElements) hash ^= std::hash<void*>()((void*)el.SemanticName);
    auto pipelineState = RequirePipelineState(sourceVS, sourcePS, hash);
    if (pipelineState->mPipelineState == nullptr)
    {
        auto device = mD3D12.GetD3DDevice();

        // Make sure shaders are compiled
        auto vShader = RequireShader(sourceVS, StrVSEntryPoint);
        auto pShader = RequireShader(sourcePS, StrPSEntryPoint);
        if (vShader->mShader == nullptr) {
            vShader->CompileFromFile(sourceVS.GetPath(), StrVSEntryPoint, "vs_5_0");
        }
        if (pShader->mShader == nullptr) {
            pShader->CompileFromFile(sourceVS.GetPath(), StrPSEntryPoint, "ps_5_0");
        }

        auto ToD3DBArg = [](BlendMode::BlendArg arg)
        {
            D3D12_BLEND mapping[] = {
                D3D12_BLEND_ZERO, D3D12_BLEND_ONE,
                D3D12_BLEND_SRC_COLOR, D3D12_BLEND_INV_SRC_COLOR, D3D12_BLEND_SRC_ALPHA, D3D12_BLEND_INV_SRC_ALPHA,
                D3D12_BLEND_DEST_COLOR, D3D12_BLEND_INV_DEST_COLOR, D3D12_BLEND_DEST_ALPHA, D3D12_BLEND_INV_DEST_ALPHA,
            };
            return mapping[(int)arg];
        };
        auto ToD3DBOp = [](BlendMode::BlendOp op)
        {
            D3D12_BLEND_OP mapping[] = {
                D3D12_BLEND_OP_ADD, D3D12_BLEND_OP_SUBTRACT, D3D12_BLEND_OP_REV_SUBTRACT,
                D3D12_BLEND_OP_MIN, D3D12_BLEND_OP_MAX,
            };
            return mapping[(int)op];
        };

        // Create the D3D pipeline
        D3D12_GRAPHICS_PIPELINE_STATE_DESC psoDesc = {};
        psoDesc.InputLayout = { vertElements.data(), (unsigned int)vertElements.size() };
        psoDesc.pRootSignature = mD3D12.GetRootSignature();
        psoDesc.VS = CD3DX12_SHADER_BYTECODE(vShader->mShader.Get());
        psoDesc.PS = CD3DX12_SHADER_BYTECODE(pShader->mShader.Get());
        psoDesc.RasterizerState = CD3DX12_RASTERIZER_DESC(D3D12_DEFAULT);
        psoDesc.RasterizerState.CullMode = (D3D12_CULL_MODE)rasterMode.mCullMode;
        psoDesc.BlendState = CD3DX12_BLEND_DESC(D3D12_DEFAULT);
        psoDesc.BlendState.RenderTarget[0].BlendEnable = TRUE;
        psoDesc.BlendState.RenderTarget[0].SrcBlend = ToD3DBArg(blendMode.mSrcColorBlend);
        psoDesc.BlendState.RenderTarget[0].DestBlend = ToD3DBArg(blendMode.mDestColorBlend);
        psoDesc.BlendState.RenderTarget[0].SrcBlendAlpha = ToD3DBArg(blendMode.mSrcAlphaBlend);
        psoDesc.BlendState.RenderTarget[0].DestBlendAlpha = ToD3DBArg(blendMode.mDestAlphaBlend);
        psoDesc.BlendState.RenderTarget[0].BlendOp = ToD3DBOp(blendMode.mBlendColorOp);
        psoDesc.BlendState.RenderTarget[0].BlendOpAlpha = ToD3DBOp(blendMode.mBlendAlphaOp);
        psoDesc.DepthStencilState = CD3DX12_DEPTH_STENCIL_DESC1(D3D12_DEFAULT);
        psoDesc.DepthStencilState.DepthFunc = (D3D12_COMPARISON_FUNC)depthMode.mComparison;
        psoDesc.DepthStencilState.DepthWriteMask = depthMode.mWriteEnable ? D3D12_DEPTH_WRITE_MASK_ALL : D3D12_DEPTH_WRITE_MASK_ZERO;
        psoDesc.SampleMask = UINT_MAX;
        psoDesc.PrimitiveTopologyType = D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE;
        psoDesc.NumRenderTargets = 1;
        psoDesc.RTVFormats[0] = DXGI_FORMAT_R8G8B8A8_UNORM;
        psoDesc.DSVFormat = DXGI_FORMAT_D32_FLOAT;
        psoDesc.SampleDesc.Count = 1;
        ThrowIfFailed(device->CreateGraphicsPipelineState(&psoDesc, IID_PPV_ARGS(&pipelineState->mPipelineState)));
        pipelineState->mPipelineState->SetName(material.GetPixelShader()->GetPath().c_str());

        // Collect constant buffers required by the shaders
        // TODO: Throw an error if different constant buffers
        // are required in the same bind point
        for (auto l : { vShader, pShader })
        {
            for (auto& cb : l->mConstantBuffers)
            {
                if (pipelineState->mConstantBuffers.size() <= cb.mBindPoint)
                    pipelineState->mConstantBuffers.resize(cb.mBindPoint + 1);
                pipelineState->mConstantBuffers[cb.mBindPoint] = &cb;
            }
            for (auto& rb : l->mResourceBindings)
            {
                if (pipelineState->mResourceBindings.size() <= rb.mBindPoint)
                    pipelineState->mResourceBindings.resize(rb.mBindPoint + 1);
                pipelineState->mResourceBindings[rb.mBindPoint] = &rb;
            }
        }
    }
    return pipelineState;
}
D3DConstantBuffer* D3DResourceCache::RequireConstantBuffer(const D3DShader::ConstantBuffer& cb, const Material& material)
{
    return mConstantBufferCache.RequireConstantBuffer(material, cb, mD3D12);
}

// Handles receiving rendering events from the user application
// and issuing relevant draw commands
class D3DCommandBuffer : public CommandBufferInteropBase {
    GraphicsDeviceD3D12* mDevice;
    ComPtr<ID3D12GraphicsCommandList> mCmdList;
    const D3DResourceCache::D3DPipelineState* mLastPipeline;
    const D3DResourceCache::D3DMesh* mLastMesh;
    const D3DConstantBuffer* mLastCBs[10];
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
        CD3DX12_CPU_DESCRIPTOR_HANDLE descriptor(mDevice->GetRTVHeap()->GetCPUDescriptorHandleForHeapStart(),
            mDevice->GetBackBufferIndex(), mDevice->GetDescriptorHandleSize());
        CD3DX12_CPU_DESCRIPTOR_HANDLE depth(mDevice->GetDSVHeap()->GetCPUDescriptorHandleForHeapStart());
        mCmdList->OMSetRenderTargets(1, &descriptor, FALSE, &depth);
        mLastPipeline = nullptr;
        mLastMesh = nullptr;
        std::fill(mLastCBs, mLastCBs + _countof(mLastCBs), nullptr);
    }
    // Clear the screen
    void ClearRenderTarget(const ClearConfig& clear) override
    {
        if (clear.HasClearColor())
        {
            CD3DX12_CPU_DESCRIPTOR_HANDLE descriptor(mDevice->GetRTVHeap()->GetCPUDescriptorHandleForHeapStart(),
                mDevice->GetBackBufferIndex(), mDevice->GetDescriptorHandleSize());
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

    // Draw a mesh with the specified material
    void DrawMesh(const std::shared_ptr<Mesh>& mesh, const std::shared_ptr<Material>& material, const DrawConfig& config) override
    {
        auto& cache = mDevice->GetResourceCache();
        // Get an up to date mesh
        auto d3dMesh = cache.RequireD3DMesh(*mesh.get());
        if (d3dMesh->mRevision != mesh->GetRevision())
            cache.UpdateMeshData(d3dMesh, *mesh, mCmdList.Get());

        auto pipelineState = cache.RequirePipelineState(*material, d3dMesh->mVertElements);

        // Require and bind a pipeline matching the material config and mesh attributes
        if (mLastPipeline != pipelineState)
        {
            mLastPipeline = pipelineState;
            mCmdList->SetPipelineState(pipelineState->mPipelineState.Get());

            auto srvHeap = mDevice->GetSRVHeap();
            mCmdList->SetDescriptorHeaps(1, &srvHeap);

            mCmdList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        }

        // Require and bind constant buffers
        for(int i = 0; i < pipelineState->mConstantBuffers.size(); ++i)
        {
            auto cb = pipelineState->mConstantBuffers[i];
            if (cb == nullptr) continue;
            auto d3dCB = cache.RequireConstantBuffer(*cb, *material);
            if (mLastCBs[i] == d3dCB) continue;
            mLastCBs[i] = d3dCB;
            mCmdList->SetGraphicsRootConstantBufferView(i, d3dCB->mConstantBuffer->GetGPUVirtualAddress());
        }
        // Require and bind other resources (textures)
        for (int i = 0; i < pipelineState->mResourceBindings.size(); ++i)
        {
            auto rb = pipelineState->mResourceBindings[i];
            if (rb == nullptr) continue;
            auto* texture = &material->GetUniformTexture(rb->mNameId);
            if (texture == nullptr || *texture == nullptr || (*texture)->GetSize().x <= 0)
            {
                if (cache.mDefaultTexture == nullptr)
                {
                    cache.mDefaultTexture = std::make_shared<Texture>();
                    cache.mDefaultTexture->SetSize(4);
                    auto data = cache.mDefaultTexture->GetData();
                    std::fill(data.begin(), data.end(), 0xff808080);
                    cache.mDefaultTexture->MarkChanged();
                }
                texture = &cache.mDefaultTexture;
            }
            auto d3dTex = cache.RequireD3DTexture(**texture);
            if (d3dTex->mRevision != (*texture)->GetRevision())
                cache.UpdateTextureData(d3dTex, **texture, mCmdList.Get());
            auto handle = mDevice->GetSRVHeap()->GetGPUDescriptorHandleForHeapStart();
            handle.ptr += d3dTex->mSRVOffset;
            mCmdList->SetGraphicsRootDescriptorTable(2 + rb->mBindPoint, handle);
            //mCmdList->SetGraphicsRootShaderResourceView(2, d3dTex->mTexture->GetGPUVirtualAddress());
        }

        // Bind the mesh buffers
        if (mLastMesh != d3dMesh) {
            mLastMesh = d3dMesh;
            mCmdList->IASetVertexBuffers(0, 1, &d3dMesh->mVertexBufferView);
            mCmdList->IASetIndexBuffer(&d3dMesh->mIndexBufferView);
        }

        // Issue the draw calls
        int indexCount = mesh->GetIndexCount();
        if (config.mIndexCount >= 0) indexCount = config.mIndexCount;
        mCmdList->DrawIndexedInstanced(indexCount, std::max(1, material->GetInstanceCount()), config.mIndexBase, 0, 0);
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

GraphicsDeviceD3D12::GraphicsDeviceD3D12(std::shared_ptr<WindowWin32>& window)
    : mWindow(window)
    , mDevice(*window)
    , mCache(mDevice)
{
    auto mD3DDevice = mDevice.GetD3DDevice();
    auto mSwapChain = mDevice.GetSwapChain();
    // Create fence for frame synchronisation
    mBackBufferIndex = mSwapChain->GetCurrentBackBufferIndex();
    for (int i = 0; i < FrameCount; ++i) mFenceValues[i] = 0;
    ThrowIfFailed(mD3DDevice->CreateFence(mFenceValues[mBackBufferIndex], D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&mFence)));
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
    WaitForGPU();
}


CommandBuffer GraphicsDeviceD3D12::CreateCommandBuffer()
{
    return CommandBuffer(new D3DCommandBuffer(this));
}

// Flip the backbuffer and wait until a frame is available to be rendered
void GraphicsDeviceD3D12::Present()
{
    auto hr = mDevice.GetSwapChain()->Present(1, 0);

    if (hr == DXGI_ERROR_DEVICE_REMOVED || hr == DXGI_ERROR_DEVICE_RESET)
    {
        WCHAR* errorString = nullptr;
        auto reason = mDevice.GetD3DDevice()->GetDeviceRemovedReason();
        FormatMessage(FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_IGNORE_INSERTS,
            nullptr, reason, MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT),
            (LPWSTR)&errorString, 0, nullptr);
        OutputDebugStringW(errorString);
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
