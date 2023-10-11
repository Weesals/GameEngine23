#include "D3DResourceCache.h"

#include <d3dx12.h>
#include <cassert>
#include <fstream>

// From DirectXTK wiki
inline void ThrowIfFailed(HRESULT hr)
{
    if (FAILED(hr))
    {
        throw std::exception();
    }
}



void D3DResourceCache::CreateBuffer(ComPtr<ID3D12Resource>& buffer, int size) {
    auto heapProps = CD3DX12_HEAP_PROPERTIES(D3D12_HEAP_TYPE_DEFAULT);
    // Buffer already valid
    // Register buffer to be destroyed in the future
    if (buffer != nullptr)
    {
        mDelayedRelease.InsertItem(buffer);
        buffer = nullptr;
    }
    auto resDesc = CD3DX12_RESOURCE_DESC::Buffer(size);
    ThrowIfFailed(mD3D12.GetD3DDevice()->CreateCommittedResource(
        &heapProps,
        D3D12_HEAP_FLAG_NONE,
        &resDesc,
        D3D12_RESOURCE_STATE_COMMON,
        nullptr,
        IID_PPV_ARGS(&buffer)));
    buffer->SetName(L"MeshBuffer");
    mStatistics.mBufferCreates++;
};
template<class F2>
void WriteBuffer(ID3D12GraphicsCommandList* cmdList, D3DResourceCache& cache, ID3D12Resource* buffer, int size, const F2& fillBuffer,
    int dstOffset = 0) {
    // Map and fill the buffer data (via temporary upload buffer)
    ID3D12Resource* uploadBuffer = cache.AllocateUploadBuffer(size);
    uint8_t* mappedData;
    CD3DX12_RANGE readRange(0, 0);
    ThrowIfFailed(uploadBuffer->Map(0, &readRange, (void**)&mappedData));
    fillBuffer(mappedData);
    uploadBuffer->Unmap(0, nullptr);
    cmdList->CopyBufferRegion(buffer, dstOffset, uploadBuffer, 0, size);
    cache.mStatistics.BufferWrite(size);
};

D3DResourceCache::D3DResourceCache(D3DGraphicsDevice& d3d12, RenderStatistics& statistics)
    : mD3D12(d3d12)
    , mStatistics(statistics)
    , mCBOffset(0)
    , mRTOffset(0)
    , mDSOffset(0)
{
    auto mD3DDevice = mD3D12.GetD3DDevice();

    mRootSignature.mNumConstantBuffers = 4;
    mRootSignature.mNumResources = 4;
    D3D12_FEATURE_DATA_ROOT_SIGNATURE featureData = {};
    // This is the highest version the sample supports. If CheckFeatureSupport succeeds, the HighestVersion returned will not be greater than this.
    featureData.HighestVersion = D3D_ROOT_SIGNATURE_VERSION_1_1;
    if (FAILED(mD3DDevice->CheckFeatureSupport(D3D12_FEATURE_ROOT_SIGNATURE, &featureData, sizeof(featureData))))
        featureData.HighestVersion = D3D_ROOT_SIGNATURE_VERSION_1_0;

    CD3DX12_ROOT_PARAMETER1 rootParameters[16];
    CD3DX12_DESCRIPTOR_RANGE1 srvR[8];
    int rootParamId = 0;
    for (int i = 0; i < mRootSignature.mNumConstantBuffers; ++i)
        rootParameters[rootParamId++].InitAsConstantBufferView(i);
    for (int i = 0; i < mRootSignature.mNumResources; ++i) {
        srvR[i] = CD3DX12_DESCRIPTOR_RANGE1(D3D12_DESCRIPTOR_RANGE_TYPE_SRV, 1, i);
        rootParameters[rootParamId++].InitAsDescriptorTable(1, &srvR[i]);
    }

    CD3DX12_VERSIONED_ROOT_SIGNATURE_DESC rootSignatureDesc = { };
    CD3DX12_STATIC_SAMPLER_DESC samplerDesc[] = {
        CD3DX12_STATIC_SAMPLER_DESC(0, D3D12_FILTER_MIN_MAG_MIP_LINEAR),
        CD3DX12_STATIC_SAMPLER_DESC(1, D3D12_FILTER_MIN_MAG_MIP_LINEAR),
    };
    rootSignatureDesc.Init_1_1(rootParamId, rootParameters, _countof(samplerDesc), samplerDesc,
        D3D12_ROOT_SIGNATURE_FLAG_ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT |
        D3D12_ROOT_SIGNATURE_FLAG_DENY_HULL_SHADER_ROOT_ACCESS |
        D3D12_ROOT_SIGNATURE_FLAG_DENY_DOMAIN_SHADER_ROOT_ACCESS |
        D3D12_ROOT_SIGNATURE_FLAG_DENY_GEOMETRY_SHADER_ROOT_ACCESS
    );

    ComPtr<ID3DBlob> signature;
    ComPtr<ID3DBlob> error;
    auto hr = D3DX12SerializeVersionedRootSignature(&rootSignatureDesc, featureData.HighestVersion, &signature, &error);
    if (FAILED(hr))
    {
        OutputDebugStringA((char*)error->GetBufferPointer());
    }
    ThrowIfFailed(mD3DDevice->CreateRootSignature(0, signature->GetBufferPointer(), signature->GetBufferSize(), IID_PPV_ARGS(&mRootSignature.mRootSignature)));
}
D3DShader* D3DResourceCache::RequireShader(const Shader& shader, const std::string& profile, const IdentifierWithName& renderPass)
{
    auto pathId = shader.GetIdentifier();
    auto entryPointId = Identifier::RequireStringId(shader.GetEntryPoint()) + ((int)renderPass * 1234);
    bool wasCreated = false;
    auto* d3dshader = GetOrCreate(shaderMapping, ShaderKey{ pathId, entryPointId }, wasCreated);
    if (wasCreated) {
        assert(d3dshader->mShader == nullptr);
        std::string entryFn = shader.GetEntryPoint();
        if (renderPass.IsValid()) {
            entryFn = renderPass.GetName() + "_" + entryFn;
            std::ifstream shaderFile(shader.GetPath());
            bool valid = false;
            while (!shaderFile.eof()) {
                char buffer[4096];
                shaderFile.read(buffer, _countof(buffer));
                std::string_view shaderCode(buffer, _countof(buffer));
                if (shaderCode.find(renderPass.GetName()) != -1) { valid = true; break; }
            }
            if (!valid) return nullptr;
        }
        d3dshader->CompileFromFile(shader.GetPath(), entryFn, profile.c_str());
    }
    return d3dshader;
}
D3DResourceCache::D3DPipelineState* D3DResourceCache::GetOrCreatePipelineState(const Shader& vs, const Shader& ps, size_t hash)
{
    return GetOrCreate(pipelineMapping, hash);
}
D3DResourceCache::D3DRT* D3DResourceCache::RequireD3DRT(const RenderTarget2D* rt) {
    return GetOrCreate(rtMapping, rt);
}
// Allocate or retrieve a container for GPU buffers for this item
D3DResourceCache::D3DMesh* D3DResourceCache::RequireD3DMesh(const Mesh& mesh)
{
    return GetOrCreate(meshMapping, &mesh);
}
// Allocate or retrieve a container for GPU buffers for this item
D3DResourceCache::D3DBufferWithSRV* D3DResourceCache::RequireD3DBuffer(const Texture& tex)
{
    return GetOrCreate(textureMapping, &tex);
}
void D3DResourceCache::RequireD3DBuffer(D3DBufferWithSRV* d3dBuf, const GraphicsBufferBase& buffer, ID3D12GraphicsCommandList* cmdList)
{
    int stride = buffer.GetStride() * 10;
    int count = buffer.GetSize() / stride;
    int size = stride * count;

    // Get d3d cache instance
    if (d3dBuf->mBuffer == nullptr)
    {
        CreateBuffer(d3dBuf->mBuffer, size);

        // Create a shader resource view (SRV) for the texture
        D3D12_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
        srvDesc.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
        srvDesc.Format = DXGI_FORMAT_UNKNOWN;
        srvDesc.ViewDimension = D3D12_SRV_DIMENSION_BUFFER;
        srvDesc.Buffer.NumElements = count;
        srvDesc.Buffer.StructureByteStride = stride;
        srvDesc.Buffer.Flags = D3D12_BUFFER_SRV_FLAG_NONE;

        // Get the CPU handle to the descriptor in the heap
        auto descriptorSize = mD3D12.GetDescriptorHandleSizeSRV();
        CD3DX12_CPU_DESCRIPTOR_HANDLE srvHandle(mD3D12.GetSRVHeap()->GetCPUDescriptorHandleForHeapStart(), mCBOffset);
        auto device = mD3D12.GetD3DDevice();
        device->CreateShaderResourceView(d3dBuf->mBuffer.Get(), &srvDesc, srvHandle);
        d3dBuf->mSRVOffset = mCBOffset;
        mCBOffset += descriptorSize;
    }
}
// Retrieve a buffer capable of upload/copy that will be vaild until
// the frame completes rendering
ID3D12Resource* D3DResourceCache::AllocateUploadBuffer(int uploadSize)
{
    uploadSize = (uploadSize + 255) & (~255);
    auto& uploadBufferItem = mUploadBufferCache.RequireItem(uploadSize,
        [&](auto& item) // Allocate a new item
        {
            auto uploadHeapType = CD3DX12_HEAP_PROPERTIES(D3D12_HEAP_TYPE_UPLOAD);
            auto uploadBufferDesc = CD3DX12_RESOURCE_DESC::Buffer(item.mLayoutHash);
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
void D3DResourceCache::UpdateBufferData(D3DBufferWithSRV* d3dBuf, const GraphicsBufferBase& buffer, ID3D12GraphicsCommandList* cmdList)
{
    int stride = buffer.GetStride() * 10;
    int count = buffer.GetSize() / stride;
    int size = stride * count;
    RequireD3DBuffer(d3dBuf, buffer, cmdList);

    // Put the texture back in normal mode
    auto beginWrite = CD3DX12_RESOURCE_BARRIER::Transition(d3dBuf->mBuffer.Get(), D3D12_RESOURCE_STATE_COMMON, D3D12_RESOURCE_STATE_COPY_DEST);
    cmdList->ResourceBarrier(1, &beginWrite);

    //int isize = (int)GetRequiredIntermediateSize(d3dBuf->mBuffer.Get(), 0, 1);
    WriteBuffer(cmdList, *this, d3dBuf->mBuffer.Get(), size, [&](auto* mappedData) {
        std::memcpy(mappedData, buffer.GetRawData(), size);
    });

    // Put the texture back in normal mode
    auto endWrite = CD3DX12_RESOURCE_BARRIER::Transition(d3dBuf->mBuffer.Get(), D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_COMMON);
    cmdList->ResourceBarrier(1, &endWrite);

    d3dBuf->mRevision = buffer.GetRevision();
}
void D3DResourceCache::UpdateBufferData(ID3D12GraphicsCommandList* cmdList, GraphicsBufferBase* buffer, const std::span<RangeInt>& ranges)
{
    auto d3dBuf = RequireD3DBuffer(*(Texture*)buffer);
    RequireD3DBuffer(d3dBuf, *buffer, cmdList);

    int totalCount = std::accumulate(ranges.begin(), ranges.end(), 0, [](int counter, RangeInt range) { return counter + range.length; });
    int stride = buffer->GetStride();

    // Map and fill the buffer data (via temporary upload buffer)
    ID3D12Resource* uploadBuffer = AllocateUploadBuffer(totalCount * stride);
    UINT8* mappedData;
    CD3DX12_RANGE readRange(0, 0);
    ThrowIfFailed(uploadBuffer->Map(0, &readRange, (void**)&mappedData));
    int it = 0;
    for (auto& range : ranges) {
        std::memcpy(mappedData + it, buffer->GetRawData() + range.start * stride, range.length * stride);
        it += range.length * stride;
    }
    uploadBuffer->Unmap(0, nullptr);

    auto beginWrite = {
        CD3DX12_RESOURCE_BARRIER::Transition(d3dBuf->mBuffer.Get(), D3D12_RESOURCE_STATE_COMMON, D3D12_RESOURCE_STATE_COPY_DEST),
    };
    cmdList->ResourceBarrier((UINT)beginWrite.size(), beginWrite.begin());

    it = 0;
    for (auto& range : ranges) {
        cmdList->CopyBufferRegion(d3dBuf->mBuffer.Get(), range.start * stride,
            uploadBuffer, it, range.length * stride);
        it += range.length * stride;
        mStatistics.BufferWrite(ranges.size());
    }
    auto endWrite = {
        CD3DX12_RESOURCE_BARRIER::Transition(d3dBuf->mBuffer.Get(), D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_COMMON),
    };
    cmdList->ResourceBarrier((UINT)endWrite.size(), endWrite.begin());
    d3dBuf->mRevision = buffer->GetRevision();
}
void D3DResourceCache::UpdateTextureData(D3DBufferWithSRV* d3dTex, const Texture& tex, ID3D12GraphicsCommandList* cmdList)
{
    auto device = mD3D12.GetD3DDevice();
    auto size = tex.GetSize();

    // Get d3d cache instance
    if (d3dTex->mBuffer == nullptr)
    {
        // Create the texture resource
        auto texHeapType = CD3DX12_HEAP_PROPERTIES(D3D12_HEAP_TYPE_DEFAULT);
        auto textureDesc = CD3DX12_RESOURCE_DESC::Tex2D(DXGI_FORMAT_R8G8B8A8_UNORM, size.x, size.y, 1, 1);
        textureDesc.Flags |= D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS;
        ThrowIfFailed(device->CreateCommittedResource(
            &texHeapType,
            D3D12_HEAP_FLAG_NONE,
            &textureDesc,
            D3D12_RESOURCE_STATE_COPY_DEST,
            nullptr,
            IID_PPV_ARGS(&d3dTex->mBuffer)
        ));
        d3dTex->mBuffer->SetName(L"UserTexture");

        // Create a shader resource view (SRV) for the texture
        D3D12_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
        srvDesc.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
        srvDesc.Format = textureDesc.Format;
        srvDesc.ViewDimension = D3D12_SRV_DIMENSION_TEXTURE2D;
        srvDesc.Texture2D.MipLevels = textureDesc.MipLevels;

        // Get the CPU handle to the descriptor in the heap
        auto descriptorSize = mD3D12.GetDescriptorHandleSizeSRV();
        CD3DX12_CPU_DESCRIPTOR_HANDLE srvHandle(mD3D12.GetSRVHeap()->GetCPUDescriptorHandleForHeapStart(), mCBOffset);
        device->CreateShaderResourceView(d3dTex->mBuffer.Get(), &srvDesc, srvHandle);
        d3dTex->mSRVOffset = mCBOffset;
        mCBOffset += descriptorSize;
    }

    auto uploadSize = (GetRequiredIntermediateSize(d3dTex->mBuffer.Get(), 0, 1) + D3D12_DEFAULT_RESOURCE_PLACEMENT_ALIGNMENT - 1) & ~(D3D12_DEFAULT_RESOURCE_PLACEMENT_ALIGNMENT - 1);

    // Update the texture data
    auto srcData = tex.GetData();
    D3D12_SUBRESOURCE_DATA textureData = {};
    textureData.pData = reinterpret_cast<UINT8*>(srcData.data());
    textureData.RowPitch = 4 * size.x;
    textureData.SlicePitch = textureData.RowPitch * size.y;
    auto uploadBuffer = AllocateUploadBuffer((int)uploadSize);
    UpdateSubresources<1>(cmdList, d3dTex->mBuffer.Get(), uploadBuffer, 0, 0, 1, &textureData);
    mStatistics.BufferWrite(4 * size.x * size.y);

    // Put the texture back in normal mode
    auto endWrite = CD3DX12_RESOURCE_BARRIER::Transition(d3dTex->mBuffer.Get(), D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_COMMON);
    cmdList->ResourceBarrier(1, &endWrite);

    d3dTex->mRevision = tex.GetRevision();
}
D3DResourceCache::D3DBufferWithSRV* D3DResourceCache::RequireCurrentTexture(const Texture* texture, ID3D12GraphicsCommandList* cmdList)
{
    if (texture == nullptr || texture->GetSize().x <= 0)
    {
        if (mDefaultTexture == nullptr)
        {
            mDefaultTexture = std::make_shared<Texture>();
            mDefaultTexture->SetSize(4);
            auto& data = mDefaultTexture->GetData();
            std::fill((uint32_t*)data.begin()._Ptr, (uint32_t*)data.end()._Ptr, 0xffe0e0e0);
            mDefaultTexture->MarkChanged();
        }
        texture = mDefaultTexture.get();
    }
    auto d3dTex = RequireD3DBuffer(*texture);
    if (d3dTex->mRevision != texture->GetRevision())
        UpdateTextureData(d3dTex, *texture, cmdList);
    return d3dTex;
}
D3DResourceCache::D3DBufferWithSRV* D3DResourceCache::RequireCurrentBuffer(const GraphicsBufferBase* buffer, ID3D12GraphicsCommandList* cmdList)
{
    auto d3dBuf = RequireD3DBuffer(*(Texture*)buffer);
    if (d3dBuf->mRevision != buffer->GetRevision()) {
        UpdateBufferData(d3dBuf, *buffer, cmdList);
    }
    return d3dBuf;
}
template<class Fn1, class Fn2, class Fn3, class Fn4>
void ProcessBindings(std::span<const BufferLayout*> bindings, std::map<size_t, std::unique_ptr<D3DResourceCache::D3DBinding>>& bindingMap,
    const Fn1& OnIndices, const Fn2& OnElement, const Fn3& OnVertices, const Fn4& OnBuffer)
{
    int vbuffCount = 0;
    for (auto* bindingPtr : bindings) {
        auto& binding = *bindingPtr;
        auto d3dBinIt = bindingMap.find(binding.mBuffer.mIdentifier);
        if (d3dBinIt == bindingMap.end()) {
            d3dBinIt = bindingMap.emplace(std::make_pair(binding.mBuffer.mIdentifier, std::make_unique<D3DResourceCache::D3DBinding>())).first;
            d3dBinIt->second->mRevision = -16;
            d3dBinIt->second->mUsage = binding.mUsage;
        }
        assert(d3dBinIt->second->mUsage == binding.mUsage);
        auto* d3dBin = d3dBinIt->second.get();
        uint32_t itemSize = 0;
        if (binding.mUsage == BufferLayout::Usage::Index) {
            assert(binding.GetElements().size() == 1);
            assert(binding.GetElements()[0].mBufferStride == binding.GetElements()[0].mItemSize);
            itemSize = binding.GetElements()[0].mItemSize;
            OnIndices(binding, *d3dBin, itemSize);
        }
        else {
            auto classification =
                binding.mUsage == BufferLayout::Usage::Vertex ? D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA
                : binding.mUsage == BufferLayout::Usage::Instance || binding.mUsage == BufferLayout::Usage::Uniform ? D3D12_INPUT_CLASSIFICATION_PER_INSTANCE_DATA
                : throw "Not implemented";
            for (auto& element : binding.GetElements()) {
                if (element.mItemSize >= 4) itemSize = (itemSize + 3) & (~3);
                OnElement(D3D12_INPUT_ELEMENT_DESC{ element.mBindName, 0, (DXGI_FORMAT)element.mFormat, (uint32_t)vbuffCount,
                    PostIncrement(itemSize, (uint32_t)element.mItemSize), classification,
                    binding.mUsage == BufferLayout::Usage::Instance ? 1u : 0u });
            }
            OnVertices(binding, *d3dBin, itemSize);
            ++vbuffCount;
        }
        OnBuffer(binding, *d3dBin, itemSize);
    }
}
void D3DResourceCache::ComputeElementLayout(std::span<const BufferLayout*> bindings,
    std::vector<D3D12_INPUT_ELEMENT_DESC>& inputElements)
{
    ProcessBindings(bindings, mBindings,
        [&](const BufferLayout& binding, D3DBinding& d3dBin, int itemSize) {
        }, [&](const D3D12_INPUT_ELEMENT_DESC& element) {
            inputElements.push_back(element);
        }, [&](const BufferLayout& binding, D3DBinding& d3dBin, int itemSize) {
        }, [&](const BufferLayout& binding, D3DBinding& d3dBin, int itemSize) {
        });
}
void D3DResourceCache::ComputeElementData(std::span<const BufferLayout*> bindings,
    ID3D12GraphicsCommandList* cmdList,
    std::vector<D3D12_VERTEX_BUFFER_VIEW>& inputViews,
    D3D12_INDEX_BUFFER_VIEW& indexView, int& indexCount)
{
    auto RequireBuffer = [&](const BufferLayout& binding, D3DBinding& d3dBin) {
        if (d3dBin.mBuffer == nullptr || d3dBin.mSize < binding.mBuffer.mSize) {
            d3dBin.mSize = binding.mBuffer.mSize;
            CreateBuffer(d3dBin.mBuffer, d3dBin.mSize);
            d3dBin.mBuffer->SetName(
                binding.mUsage == BufferLayout::Usage::Vertex ? L"VertexBuffer" :
                binding.mUsage == BufferLayout::Usage::Index ? L"IndexBuffer" :
                binding.mUsage == BufferLayout::Usage::Instance ? L"InstanceBuffer" :
                L"ElementBuffer"
            );
            d3dBin.mGPUMemory = d3dBin.mBuffer->GetGPUVirtualAddress();
        }
        };
    indexCount = -1;
    ProcessBindings(bindings, mBindings,
        [&](const BufferLayout& binding, D3DBinding& d3dBin, int itemSize) {
            RequireBuffer(binding, d3dBin);
            indexCount = binding.mCount;
            indexView = {
                d3dBin.mGPUMemory + (UINT)(binding.mOffset * itemSize),
                (UINT)(binding.mCount * itemSize),
                (DXGI_FORMAT)binding.GetElements()[0].mFormat
            };
        }, [&](const D3D12_INPUT_ELEMENT_DESC& element) {
            }, [&](const BufferLayout& binding, D3DBinding& d3dBin, int itemSize) {
                RequireBuffer(binding, d3dBin);
                inputViews.push_back({
                    d3dBin.mGPUMemory + (UINT)(binding.mOffset * itemSize),
                    (UINT)(binding.mCount * itemSize),
                    (UINT)itemSize
                    });
                }, [&](const BufferLayout& binding, D3DBinding& d3dBin, int itemSize) {
                    if (d3dBin.mRevision == binding.mBuffer.mRevision) return;
                    auto state = binding.mUsage == BufferLayout::Usage::Index ? D3D12_RESOURCE_STATE_INDEX_BUFFER : D3D12_RESOURCE_STATE_VERTEX_AND_CONSTANT_BUFFER;
                    auto beginWrite = {
                        CD3DX12_RESOURCE_BARRIER::Transition(d3dBin.mBuffer.Get(), state, D3D12_RESOURCE_STATE_COPY_DEST),
                    };
                    cmdList->ResourceBarrier((UINT)beginWrite.size(), beginWrite.begin());
                    WriteBuffer(cmdList, *this, d3dBin.mBuffer.Get(), binding.mBuffer.mSize, //binding.mCount * itemSize,
                        [&](auto* data) {
                            // Fast path
                            if (binding.GetElements().size() == 1 && binding.GetElements()[0].mBufferStride == itemSize) {
                                memcpy(data, (uint8_t*)binding.GetElements()[0].mData, binding.mBuffer.mSize);
                                return;
                            }
                            int count = binding.mBuffer.mSize / itemSize;
                            int toffset = 0;
                            for (auto& element : binding.GetElements()) {
                                for (int s = 0; s < count; ++s) {   //binding.mCount
                                    memcpy(data + s * itemSize + toffset, (uint8_t*)element.mData + element.mBufferStride * s, element.mItemSize);
                                }
                                toffset += element.mItemSize;
                            }
                        },
                        0//binding.mOffset * itemSize
                    );
                    d3dBin.mRevision = binding.mBuffer.mRevision;
                    auto endWrite = {
                        CD3DX12_RESOURCE_BARRIER::Transition(d3dBin.mBuffer.Get(), D3D12_RESOURCE_STATE_COPY_DEST, state),
                    };
                    cmdList->ResourceBarrier((UINT)endWrite.size(), endWrite.begin());
                }
            );
}
void D3DResourceCache::SetResourceLockIds(UINT64 lockFrameId, UINT64 writeFrameId)
{
    mConstantBufferCache.SetResourceLockIds(lockFrameId, writeFrameId);
    mUploadBufferCache.SetResourceLockIds(lockFrameId, writeFrameId);
    mDelayedRelease.SetResourceLockIds(lockFrameId, writeFrameId);
}
struct Getter {
    std::span<const Material*> materials;
    template<class T, typename Fn>
    T MaterialGet(const Fn& fn) {
        T out = T();
        for (auto& mat : materials) {
            if (fn(mat, out)) return out;
        }
        return out;
    };
};
// Ensure a material is ready to be rendererd by the GPU (with the specified vertex layout)
D3DResourceCache::D3DPipelineState* D3DResourceCache::RequirePipelineState(std::span<const Material*> materials, std::span<const BufferLayout*> bindings, const IdentifierWithName& renderPass)
{
    Getter getter = { .materials = materials };
    // Get the relevant shaders
    const auto& sourceVS = *getter.MaterialGet<Shader*>([](const Material* mat, Shader*& out) { out = mat->GetVertexShader().get(); return out != nullptr; });
    const auto& sourcePS = *getter.MaterialGet<Shader*>([](const Material* mat, Shader*& out) { out = mat->GetPixelShader().get(); return out != nullptr; });
    const auto& blendMode = materials.back()->GetBlendMode();
    const auto& rasterMode = materials.back()->GetRasterMode();
    const auto& depthMode = materials.back()->GetDepthMode();

    // Find (or create) a pipeline that matches these requirements
    size_t hash = GenericHash({ GenericHash(blendMode), GenericHash(rasterMode), GenericHash(depthMode), GenericHash((Identifier)renderPass), });
    for (auto* binding : bindings) {
        for (auto& el : binding->GetElements()) {
            hash = AppendHash(el.mBufferStride, hash);
        }
        //hash = AppendHash(el, hash);
        // TODO: Append binding layout hash
    }
    hash = AppendHash(std::initializer_list<Identifier>{ sourceVS.GetIdentifier(), sourcePS.GetIdentifier() }, hash);
    auto pipelineState = GetOrCreatePipelineState(sourceVS, sourcePS, hash);
    while (pipelineState->mHash != hash)
    {
        pipelineState->mHash = hash;
        pipelineState->mRootSignature = &mRootSignature;

        auto device = mD3D12.GetD3DDevice();

        // Make sure shaders are compiled
        auto vShader = RequireShader(sourceVS, StrVSProfile, renderPass);
        auto pShader = RequireShader(sourcePS, StrPSProfile, renderPass);
        if (vShader == nullptr || pShader == nullptr) break;
        if (vShader->mShader == nullptr || pShader->mShader == nullptr) break;

        auto ToD3DBArg = [](BlendMode::BlendArg arg)
            {
                static D3D12_BLEND mapping[] = {
                    D3D12_BLEND_ZERO, D3D12_BLEND_ONE,
                    D3D12_BLEND_SRC_COLOR, D3D12_BLEND_INV_SRC_COLOR, D3D12_BLEND_SRC_ALPHA, D3D12_BLEND_INV_SRC_ALPHA,
                    D3D12_BLEND_DEST_COLOR, D3D12_BLEND_INV_DEST_COLOR, D3D12_BLEND_DEST_ALPHA, D3D12_BLEND_INV_DEST_ALPHA,
                };
                return mapping[(int)arg];
            };
        auto ToD3DBOp = [](BlendMode::BlendOp op)
            {
                static D3D12_BLEND_OP mapping[] = {
                    D3D12_BLEND_OP_ADD, D3D12_BLEND_OP_SUBTRACT, D3D12_BLEND_OP_REV_SUBTRACT,
                    D3D12_BLEND_OP_MIN, D3D12_BLEND_OP_MAX,
                };
                return mapping[(int)op];
            };

        ComputeElementLayout(bindings, pipelineState->mInputElements);

        // Create the D3D pipeline
        D3D12_GRAPHICS_PIPELINE_STATE_DESC psoDesc = {};
        psoDesc.InputLayout = { pipelineState->mInputElements.data(), (unsigned int)pipelineState->mInputElements.size() };
        psoDesc.pRootSignature = pipelineState->mRootSignature->mRootSignature.Get();
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
        pipelineState->mPipelineState->SetName(sourcePS.GetPath().c_str());

        // Collect constant buffers required by the shaders
        // TODO: Throw an error if different constant buffers
        // are required in the same bind point
        for (auto l : { vShader, pShader })
        {
            for (auto& cb : l->mReflection.mConstantBuffers)
            {
                if (std::any_of(pipelineState->mConstantBuffers.begin(), pipelineState->mConstantBuffers.end(),
                    [&](auto* o) { return *o == cb; })) continue;
                pipelineState->mConstantBuffers.push_back(&cb);
            }
            for (auto& rb : l->mReflection.mResourceBindings)
            {
                if (std::any_of(pipelineState->mResourceBindings.begin(), pipelineState->mResourceBindings.end(),
                    [&](auto* o) { return *o == rb; })) continue;
                pipelineState->mResourceBindings.push_back(&rb);
            }
        }
        break;
    }
    return pipelineState;
}
// Find or allocate a constant buffer for the specified material and CB layout
D3DConstantBuffer* D3DResourceCache::RequireConstantBuffer(const ShaderBase::ConstantBuffer& cBuffer, const Material& material)
{
    mTempData.resize(cBuffer.mSize);

    // Copy data into the constant buffer
    // TODO: Generate a hash WITHOUT copying data?
    //  => Might be more expensive to evaluate props twice
    std::memset(mTempData.data(), 0, sizeof(mTempData[0]) * mTempData.size());
    for (auto& var : cBuffer.mValues)
    {
        auto varData = material.GetUniformBinaryData(var.mNameId);
        std::memcpy(mTempData.data() + var.mOffset, varData.data(), varData.size());
    }
    return RequireConstantBuffer(mTempData);
}
// Find or allocate a constant buffer for the specified material and CB layout
D3DConstantBuffer* D3DResourceCache::RequireConstantBuffer(std::span<const uint8_t> tData)
{
    // CB should be padded to multiples of 256
    auto allocSize = (int)(tData.size() + 255) & ~255;
    auto dataHash = GenericHash(tData.data(), tData.size());

    auto& resultItem = mConstantBufferCache.RequireItem(dataHash, allocSize,
        [&](auto& item) // Allocate a new item
        {
            auto device = mD3D12.GetD3DDevice();
            assert(item.mData.mConstantBuffer == nullptr);
            // We got a fresh item, need to create the relevant buffers
            CD3DX12_HEAP_PROPERTIES heapProperties(D3D12_HEAP_TYPE_UPLOAD);
            CD3DX12_RESOURCE_DESC resourceDesc = CD3DX12_RESOURCE_DESC::Buffer(allocSize);
            auto hr = device->CreateCommittedResource(
                &heapProperties,
                D3D12_HEAP_FLAG_NONE,
                &resourceDesc,
                D3D12_RESOURCE_STATE_GENERIC_READ,
                nullptr,
                IID_PPV_ARGS(&item.mData.mConstantBuffer)
            );
            if (FAILED(hr)) throw "[D3D] Failed to create constant buffer";
            mStatistics.mBufferCreates++;
        },
        [&](auto& item)  // Fill an item with data
        {
            // Copy data into this new one
            assert(item.mData.mConstantBuffer != nullptr);
            UINT8* cbDataBegin;
            if (SUCCEEDED(item.mData.mConstantBuffer->Map(0, nullptr, reinterpret_cast<void**>(&cbDataBegin)))) {
                std::memcpy(cbDataBegin, tData.data(), tData.size());
                item.mData.mConstantBuffer->Unmap(0, nullptr);
            }
            mStatistics.BufferWrite(tData.size());
        },
        [&](auto& item)  // An existing item was found to match the data
        {
        }
    );
    assert(resultItem.mLayoutHash == allocSize);
    return &resultItem.mData;
}
