#include "D3DResourceCache.h"

#include <d3dx12.h>
#include <cassert>
#include <fstream>

#define BufferAlignment 15

// From DirectXTK wiki
inline void ThrowIfFailed(HRESULT hr)
{
    if (FAILED(hr))
    {
        throw std::exception();
    }
}



void D3DResourceCache::CreateBuffer(ComPtr<ID3D12Resource>& buffer, int size, int lockBits) {
    auto heapProps = CD3DX12_HEAP_PROPERTIES(D3D12_HEAP_TYPE_DEFAULT);
    // Buffer already valid
    // Register buffer to be destroyed in the future
    if (buffer != nullptr)
    {
        mDelayedRelease.InsertItem(buffer, 0, lockBits);
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
bool D3DResourceCache::RequireBuffer(const BufferLayout& binding, D3DBinding& d3dBin, int lockBits) {
    int size = (binding.mSize + BufferAlignment) & ~BufferAlignment;
    if (d3dBin.mBuffer != nullptr && d3dBin.mSize >= size) return false;
    d3dBin.mSize = size;
    CreateBuffer(d3dBin.mBuffer, d3dBin.mSize, lockBits);
    d3dBin.mBuffer->SetName(
        binding.mUsage == BufferLayout::Usage::Vertex ? L"VertexBuffer" :
        binding.mUsage == BufferLayout::Usage::Index ? L"IndexBuffer" :
        binding.mUsage == BufferLayout::Usage::Instance ? L"InstanceBuffer" :
        L"ElementBuffer"
    );
    d3dBin.mGPUMemory = d3dBin.mBuffer->GetGPUVirtualAddress();
    d3dBin.mSRVOffset = -1;     // TODO: Pool these
    return true;
}
void WriteBufferData(uint8_t* data, const BufferLayout& binding, int itemSize, int byteOffset, int byteSize) {
    // Fast path
    if (binding.GetElements().size() == 1 && binding.GetElements()[0].mBufferStride == itemSize) {
        memcpy(data, (uint8_t*)binding.GetElements()[0].mData + byteOffset, byteSize);
        return;
    }
    int count = byteSize / itemSize;
    int toffset = 0;
    for (auto& element : binding.GetElements()) {
        auto elItemSize = element.GetItemByteSize();
        auto* dstData = data + toffset;
        auto* srcData = (uint8_t*)element.mData + byteOffset;
        for (int s = 0; s < count; ++s) {   //binding.mCount
            memcpy(dstData, srcData, elItemSize);
            dstData += itemSize;
            srcData += element.mBufferStride;
        }
        toffset += elItemSize;
    }
}
template<class F2>
void WriteBuffer(ID3D12GraphicsCommandList* cmdList, int lockBits, D3DResourceCache& cache, ID3D12Resource* buffer, int size, const F2& fillBuffer,
    int dstOffset = 0) {
    // Map and fill the buffer data (via temporary upload buffer)
    ID3D12Resource* uploadBuffer = cache.AllocateUploadBuffer(size, lockBits);
    uint8_t* mappedData;
    CD3DX12_RANGE readRange(0, 0);
    ThrowIfFailed(uploadBuffer->Map(0, &readRange, (void**)&mappedData));
    fillBuffer(mappedData);
    uploadBuffer->Unmap(0, nullptr);
    cmdList->CopyBufferRegion(buffer, dstOffset, uploadBuffer, 0, size);
    cache.mStatistics.BufferWrite(size);
};
D3DResourceCache::D3DBinding& RequireBinding(const BufferLayout& binding, std::map<size_t, std::unique_ptr<D3DResourceCache::D3DBinding>>& bindingMap) {
    auto d3dBinIt = bindingMap.find(binding.mIdentifier);
    if (d3dBinIt == bindingMap.end()) {
        d3dBinIt = bindingMap.emplace(std::make_pair(binding.mIdentifier, std::make_unique<D3DResourceCache::D3DBinding>())).first;
        d3dBinIt->second->mRevision = -16;
        d3dBinIt->second->mUsage = binding.mUsage;
        d3dBinIt->second->mSRVOffset = -1;
    }
    assert(d3dBinIt->second->mUsage == binding.mUsage);
    return *d3dBinIt->second.get();
}
template<class Fn1, class Fn2, class Fn3, class Fn4>
void ProcessBindings(const BufferLayout& binding, D3DResourceCache::D3DBinding& d3dBin,
    const Fn1& OnBuffer, const Fn2& OnIndices, const Fn3& OnElement, const Fn4& OnVertices)
{
    uint32_t itemSize = 0;
    if (binding.mUsage == BufferLayout::Usage::Index) {
        assert(binding.GetElements().size() == 1);
        assert(binding.GetElements()[0].mBufferStride == binding.GetElements()[0].GetItemByteSize());
        itemSize = binding.GetElements()[0].GetItemByteSize();
        OnBuffer(binding, d3dBin, itemSize);
        OnIndices(binding, d3dBin, itemSize);
    }
    else {
        auto classification =
            binding.mUsage == BufferLayout::Usage::Vertex ? D3D12_INPUT_CLASSIFICATION_PER_VERTEX_DATA
            : binding.mUsage == BufferLayout::Usage::Instance || binding.mUsage == BufferLayout::Usage::Uniform ? D3D12_INPUT_CLASSIFICATION_PER_INSTANCE_DATA
            : throw "Not implemented";
        for (auto& element : binding.GetElements()) {
            auto elItemSize = element.GetItemByteSize();
            if (elItemSize >= 4) itemSize = (itemSize + 3) & (~3);
            OnElement(D3D12_INPUT_ELEMENT_DESC{ element.mBindName.GetName().c_str(), 0, (DXGI_FORMAT)element.mFormat, 0,
                PostIncrement(itemSize, (uint32_t)elItemSize), classification,
                binding.mUsage == BufferLayout::Usage::Instance ? 1u : 0u });
        }
        OnBuffer(binding, d3dBin, itemSize);
        OnVertices(binding, d3dBin, itemSize);
    }
    d3dBin.mCount = binding.mCount;
    d3dBin.mStride = itemSize;
}
template<class Fn1, class Fn2, class Fn3, class Fn4>
void ProcessBindings(std::span<const BufferLayout*> bindings, std::map<size_t, std::unique_ptr<D3DResourceCache::D3DBinding>>& bindingMap,
    const Fn1& OnBuffer, const Fn2& OnIndices, const Fn3& OnElement, const Fn4& OnVertices)
{
    for (auto* bindingPtr : bindings) {
        auto& d3dBin = RequireBinding(*bindingPtr, bindingMap);
        ProcessBindings(*bindingPtr, d3dBin, OnBuffer, OnIndices, OnElement, OnVertices);
    }
}
D3DResourceCache::D3DResourceCache(D3DGraphicsDevice& d3d12, RenderStatistics& statistics)
    : mD3D12(d3d12)
    , mStatistics(statistics)
    , mCBOffset(0)
    , mRTOffset(0)
    , mDSOffset(0)
{
    auto mD3DDevice = mD3D12.GetD3DDevice();

    mRootSignature.mNumConstantBuffers = 4;
    mRootSignature.mNumResources = 6;
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
        CD3DX12_STATIC_SAMPLER_DESC(0, D3D12_FILTER_MIN_MAG_MIP_POINT),
        CD3DX12_STATIC_SAMPLER_DESC(1, D3D12_FILTER_MIN_MAG_MIP_LINEAR),
        CD3DX12_STATIC_SAMPLER_DESC(2, D3D12_FILTER_ANISOTROPIC),
        CD3DX12_STATIC_SAMPLER_DESC(3, D3D12_FILTER_COMPARISON_MIN_MAG_LINEAR_MIP_POINT, D3D12_TEXTURE_ADDRESS_MODE_CLAMP, D3D12_TEXTURE_ADDRESS_MODE_CLAMP, D3D12_TEXTURE_ADDRESS_MODE_CLAMP, 0, 16, D3D12_COMPARISON_FUNC_LESS_EQUAL),
        CD3DX12_STATIC_SAMPLER_DESC(4, D3D12_FILTER_MINIMUM_MIN_MAG_LINEAR_MIP_POINT),
        CD3DX12_STATIC_SAMPLER_DESC(5, D3D12_FILTER_MAXIMUM_MIN_MAG_LINEAR_MIP_POINT),
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
    if (FAILED(hr)) {
        OutputDebugStringA((char*)error->GetBufferPointer());
    }
    ThrowIfFailed(mD3DDevice->CreateRootSignature(0, signature->GetBufferPointer(), signature->GetBufferSize(), IID_PPV_ARGS(&mRootSignature.mRootSignature)));
}
D3DShader* D3DResourceCache::RequireShader(const Shader& shader, const std::string& profile, std::span<const MacroValue> macros, const IdentifierWithName& renderPass) {
    auto pathId = shader.GetIdentifier();
    auto entryPointId = shader.GetEntryPoint() + ((int)renderPass * 1234);
    for (auto& macro : macros) entryPointId += (macro.mName.mId << 4) * (macro.mValue + 1234);
    bool wasCreated = false;
    auto* d3dshader = GetOrCreate(shaderMapping, ShaderKey{ pathId, entryPointId }, wasCreated);
    if (wasCreated) {
        assert(d3dshader->mShader == nullptr);
        std::string entryFn = shader.GetEntryPoint().GetName();
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
        D3D_SHADER_MACRO d3dMacros[64];
        auto count = std::min(macros.size(), _countof(d3dMacros) - 1);
        for (int m = 0; m < count; ++m) {
            d3dMacros[m] = D3D_SHADER_MACRO{
                .Name = macros[m].mName.GetName().c_str(),
                .Definition = macros[m].mValue.GetName().c_str(),
            };
        }
        d3dMacros[count] = { };
        d3dshader->CompileFromFile(shader.GetPath(), entryFn, profile.c_str(), d3dMacros);
    }
    return d3dshader;
}
D3DResourceCache::D3DPipelineState* D3DResourceCache::GetOrCreatePipelineState(const Shader& vs, const Shader& ps, size_t hash) {
    return GetOrCreate(pipelineMapping, hash);
}
D3DResourceCache::D3DRenderSurface* D3DResourceCache::RequireD3DRT(const RenderTarget2D* rt) {
    return GetOrCreate(rtMapping, rt);
}
// Allocate or retrieve a container for GPU buffers for this item
D3DResourceCache::D3DMesh* D3DResourceCache::RequireD3DMesh(const Mesh& mesh) {
    return GetOrCreate(meshMapping, &mesh);
}
// Allocate or retrieve a container for GPU buffers for this item
D3DResourceCache::D3DBufferWithSRV* D3DResourceCache::RequireD3DBuffer(const Texture& tex) {
    return GetOrCreate(textureMapping, &tex);
}
// Retrieve a buffer capable of upload/copy that will be vaild until
// the frame completes rendering
ID3D12Resource* D3DResourceCache::AllocateUploadBuffer(int uploadSize, int lockBits) {
    uploadSize = (uploadSize + BufferAlignment) & (~BufferAlignment);
    auto& uploadBufferItem = mUploadBufferCache.RequireItem(uploadSize, lockBits,
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
D3DResourceCache::D3DBinding* D3DResourceCache::GetBinding(uint64_t bindingIdentifier) {
    auto d3dBinIt = mBindings.find(bindingIdentifier);
    if (d3dBinIt == mBindings.end()) return nullptr;

    D3DBinding* binding = d3dBinIt->second.get();
    if (binding->mSRVOffset == -1) {
        // Create a shader resource view (SRV) for the texture
        D3D12_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
        srvDesc.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
        srvDesc.Format = DXGI_FORMAT_UNKNOWN;
        srvDesc.ViewDimension = D3D12_SRV_DIMENSION_BUFFER;
        srvDesc.Buffer.NumElements = binding->mSize / binding->mStride;
        srvDesc.Buffer.StructureByteStride = binding->mStride;
        srvDesc.Buffer.Flags = D3D12_BUFFER_SRV_FLAG_NONE;

        // Get the CPU handle to the descriptor in the heap
        auto descriptorSize = mD3D12.GetDescriptorHandleSizeSRV();
        CD3DX12_CPU_DESCRIPTOR_HANDLE srvHandle(mD3D12.GetSRVHeap()->GetCPUDescriptorHandleForHeapStart(), mCBOffset);
        auto device = mD3D12.GetD3DDevice();
        device->CreateShaderResourceView(binding->mBuffer.Get(), &srvDesc, srvHandle);
        binding->mSRVOffset = mCBOffset;
        mCBOffset += descriptorSize;
    }
    return binding;
}
void D3DResourceCache::UpdateBufferData(ID3D12GraphicsCommandList* cmdList, int lockBits, const BufferLayout& binding, std::span<const RangeInt> ranges) {
    auto& d3dBin = RequireBinding(binding, mBindings);
    bool fullRefresh = false;
    if (RequireBuffer(binding, d3dBin, lockBits)) {
        fullRefresh = true;
    }
    int totalCount = std::accumulate(ranges.begin(), ranges.end(), 0, [](int counter, RangeInt range) { return counter + range.length; });
    if (totalCount == 0) return;
    ProcessBindings(binding, d3dBin,
        [&](const BufferLayout& binding, D3DBinding& d3dBin, int itemSize) {
            if (fullRefresh) {
                CopyBufferData(cmdList, lockBits, binding, d3dBin, itemSize, 0, binding.mSize);
                return;
            }
            // Map and fill the buffer data (via temporary upload buffer)
            ID3D12Resource* uploadBuffer = AllocateUploadBuffer(totalCount, lockBits);
            UINT8* mappedData;
            CD3DX12_RANGE readRange(0, 0);
            ThrowIfFailed(uploadBuffer->Map(0, &readRange, (void**)&mappedData));
            int it = 0;
            for (auto& range : ranges) {
                WriteBufferData(mappedData + it, binding, itemSize, range.start, range.length);
                it += range.length;
            }
            uploadBuffer->Unmap(0, nullptr);
            auto beginWrite = { CD3DX12_RESOURCE_BARRIER::Transition(d3dBin.mBuffer.Get(), D3D12_RESOURCE_STATE_COMMON, D3D12_RESOURCE_STATE_COPY_DEST), };
            cmdList->ResourceBarrier((UINT)beginWrite.size(), beginWrite.begin());

            it = 0;
            for (auto& range : ranges) {
                cmdList->CopyBufferRegion(d3dBin.mBuffer.Get(), range.start,
                    uploadBuffer, it, range.length);
                it += range.length;
                mStatistics.BufferWrite(ranges.size());
            }
            auto endWrite = { CD3DX12_RESOURCE_BARRIER::Transition(d3dBin.mBuffer.Get(), D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_COMMON), };
            cmdList->ResourceBarrier((UINT)endWrite.size(), endWrite.begin());
            d3dBin.mRevision = binding.mRevision;
        },
        [&](const BufferLayout& binding, D3DBinding& d3dBin, int itemSize) {},
        [&](const D3D12_INPUT_ELEMENT_DESC& element) {},
        [&](const BufferLayout& binding, D3DBinding& d3dBin, int itemSize) {}
    );
}

void D3DResourceCache::UpdateTextureData(D3DBufferWithSRV* d3dTex, const Texture& tex, ID3D12GraphicsCommandList* cmdList, int lockBits) {
    auto device = mD3D12.GetD3DDevice();
    auto size = tex.GetSize();

    // Get d3d cache instance
    if (d3dTex->mBuffer == nullptr) {
        // Create the texture resource
        auto texHeapType = CD3DX12_HEAP_PROPERTIES(D3D12_HEAP_TYPE_DEFAULT);
        auto textureDesc = CD3DX12_RESOURCE_DESC::Tex2D((DXGI_FORMAT)tex.GetBufferFormat(), size.x, size.y, tex.GetArrayCount(), tex.GetMipCount());
        //textureDesc.Flags |= D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS;
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
        if (tex.GetArrayCount() > 1) {
            srvDesc.ViewDimension = D3D12_SRV_DIMENSION_TEXTURE2DARRAY;
            srvDesc.Texture2DArray.MipLevels = textureDesc.MipLevels;
            srvDesc.Texture2DArray.ArraySize = tex.GetArrayCount();
        }
        else {
            srvDesc.ViewDimension = D3D12_SRV_DIMENSION_TEXTURE2D;
            srvDesc.Texture2D.MipLevels = textureDesc.MipLevels;
        }

        // Get the CPU handle to the descriptor in the heap
        auto descriptorSize = mD3D12.GetDescriptorHandleSizeSRV();
        CD3DX12_CPU_DESCRIPTOR_HANDLE srvHandle(mD3D12.GetSRVHeap()->GetCPUDescriptorHandleForHeapStart(), mCBOffset);
        device->CreateShaderResourceView(d3dTex->mBuffer.Get(), &srvDesc, srvHandle);
        d3dTex->mSRVOffset = mCBOffset;
        d3dTex->mFormat = textureDesc.Format;
        mCBOffset += descriptorSize;
    }

    auto uploadSize = (GetRequiredIntermediateSize(d3dTex->mBuffer.Get(), 0, 1) + D3D12_DEFAULT_RESOURCE_PLACEMENT_ALIGNMENT - 1) & ~(D3D12_DEFAULT_RESOURCE_PLACEMENT_ALIGNMENT - 1);

    // Update the texture data
    /*WriteBuffer(cmdList, *this, d3dTex->mBuffer.Get(), srcData.size(), [&](uint8_t* data) {
        memcpy(data, srcData.data(), srcData.size());
    });*/
    for (int i = 0; i < tex.GetArrayCount(); ++i) {
        // Put the texture back in normal mode
        auto beginWrite = CD3DX12_RESOURCE_BARRIER::Transition(d3dTex->mBuffer.Get(), D3D12_RESOURCE_STATE_COMMON, D3D12_RESOURCE_STATE_COPY_DEST, i);
        //cmdList->ResourceBarrier(1, &beginWrite);

        for (int m = 0; m < tex.GetMipCount(); ++m) {
            auto res = tex.GetMipResolution(size, tex.GetBufferFormat(), m);
            auto srcData = tex.GetData(m, i);
            D3D12_SUBRESOURCE_DATA textureData = {};
            textureData.pData = reinterpret_cast<const UINT8*>(srcData.data());
            textureData.RowPitch = 4 * res.x;
            textureData.SlicePitch = textureData.RowPitch * res.y;
            auto uploadBuffer = AllocateUploadBuffer((int)uploadSize, lockBits);
            UpdateSubresources<1>(cmdList, d3dTex->mBuffer.Get(), uploadBuffer, 0,
                D3D12CalcSubresource(m, i, 0, tex.GetMipCount(), tex.GetArrayCount()), 1,
                &textureData);
            mStatistics.BufferWrite(4 * res.x * res.y);//*/
        }

        // Put the texture back in normal mode
        auto endWrite = CD3DX12_RESOURCE_BARRIER::Transition(d3dTex->mBuffer.Get(), D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_COMMON, i);
        cmdList->ResourceBarrier(1, &endWrite);
    }

    d3dTex->mRevision = tex.GetRevision();
}
D3DResourceCache::D3DBufferWithSRV* D3DResourceCache::RequireCurrentTexture(const Texture* texture, ID3D12GraphicsCommandList* cmdList, int lockBits)
{
    if (texture == nullptr || texture->GetSize().x <= 0) {
        if (mDefaultTexture == nullptr) {
            mDefaultTexture = std::make_shared<Texture>();
            mDefaultTexture->SetSize(4);
            auto data = mDefaultTexture->GetRawData();
            std::fill((uint32_t*)&*data.begin(), (uint32_t*)(&*data.begin() + data.size()), 0xffe0e0e0);
            mDefaultTexture->MarkChanged();
        }
        texture = mDefaultTexture.get();
    }
    auto d3dTex = RequireD3DBuffer(*texture);
    if (d3dTex->mRevision != texture->GetRevision())
        UpdateTextureData(d3dTex, *texture, cmdList, lockBits);
    return d3dTex;
}
void D3DResourceCache::CopyBufferData(ID3D12GraphicsCommandList* cmdList, int lockBits, const BufferLayout& binding, D3DBinding& d3dBin, int itemSize, int byteOffset, int byteSize) {
    auto state = binding.mUsage == BufferLayout::Usage::Index ? D3D12_RESOURCE_STATE_INDEX_BUFFER : D3D12_RESOURCE_STATE_VERTEX_AND_CONSTANT_BUFFER;
    auto beginWrite = { CD3DX12_RESOURCE_BARRIER::Transition(d3dBin.mBuffer.Get(), state, D3D12_RESOURCE_STATE_COPY_DEST), };
    cmdList->ResourceBarrier((UINT)beginWrite.size(), beginWrite.begin());
    int size = (byteSize + BufferAlignment) & ~BufferAlignment;
    WriteBuffer(cmdList, lockBits, *this, d3dBin.mBuffer.Get(), size,
        [&](uint8_t* data) { WriteBufferData(data, binding, itemSize, byteOffset, byteSize); },
        byteOffset
    );
    d3dBin.mRevision = binding.mRevision;
    auto endWrite = { CD3DX12_RESOURCE_BARRIER::Transition(d3dBin.mBuffer.Get(), D3D12_RESOURCE_STATE_COPY_DEST, state), };
    cmdList->ResourceBarrier((UINT)endWrite.size(), endWrite.begin());
}
void D3DResourceCache::ComputeElementLayout(std::span<const BufferLayout*> bindings,
    std::vector<D3D12_INPUT_ELEMENT_DESC>& inputElements)
{
    int vertexSlot = 0;
    ProcessBindings(bindings, mBindings,
        [&](const BufferLayout& binding, D3DBinding& d3dBin, int itemSize) {},
        [&](const BufferLayout& binding, D3DBinding& d3dBin, int itemSize) {},
        [&](const D3D12_INPUT_ELEMENT_DESC& element) {
            inputElements.push_back(element);
            inputElements.back().InputSlot = vertexSlot;
        },
        [&](const BufferLayout& binding, D3DBinding& d3dBin, int itemSize) { ++vertexSlot; }
    );
}
void D3DResourceCache::ComputeElementData(std::span<const BufferLayout*> bindings,
    ID3D12GraphicsCommandList* cmdList,
    int lockBits,
    std::vector<D3D12_VERTEX_BUFFER_VIEW>& inputViews,
    D3D12_INDEX_BUFFER_VIEW& indexView, int& indexCount)
{
    indexCount = -1;
    ProcessBindings(bindings, mBindings,
        [&](const BufferLayout& binding, D3DBinding& d3dBin, int itemSize) {
            RequireBuffer(binding, d3dBin, lockBits);
            if (d3dBin.mRevision == binding.mRevision) return;
            CopyBufferData(cmdList, lockBits, binding, d3dBin, itemSize, 0, binding.mSize);
        },
        [&](const BufferLayout& binding, D3DBinding& d3dBin, int itemSize) {
            indexCount = binding.mCount;
            indexView = {
                d3dBin.mGPUMemory + (UINT)(binding.mOffset * itemSize),
                (UINT)(binding.mCount * itemSize),
                (DXGI_FORMAT)binding.GetElements()[0].mFormat
            };
        },
        [&](const D3D12_INPUT_ELEMENT_DESC& element) {},
        [&](const BufferLayout& binding, D3DBinding& d3dBin, int itemSize) {
            inputViews.push_back({
                d3dBin.mGPUMemory + (UINT)(binding.mOffset * itemSize),
                (UINT)(binding.mCount * itemSize),
                (UINT)itemSize
            });
        }
    );
}
int D3DResourceCache::RequireFrameHandle(size_t frameHash) {
    for (int i = 0; i < (int)mFrameBitPool.size(); i++) {
        if (mFrameBitPool[i] == frameHash) return i;
    }
    for (int i = 0; i < (int)mFrameBitPool.size(); i++) {
        if (mFrameBitPool[i] == 0) {
            mFrameBitPool[i] = frameHash;
            return i;
        }
    }
    mFrameBitPool.push_back(frameHash);
    return (int)mFrameBitPool.size() - 1;
}
void D3DResourceCache::UnlockFrame(size_t frameHash) {
    int frameHandle = (int)mFrameBitPool.size() - 1;
    for (; frameHandle >= 0; frameHandle--) {
        if (mFrameBitPool[frameHandle] == frameHash) break;
    }
    if (frameHandle < 0) return;
    mFrameBitPool[frameHandle] = 0;
    mConstantBufferCache.Unlock(1ull << frameHandle);
    mResourceViewCache.Unlock(1ull << frameHandle);
    mUploadBufferCache.Unlock(1ull << frameHandle);
    mDelayedRelease.Unlock(1ull << frameHandle);
}
void D3DResourceCache::ClearDelayedData() {
    mResourceViewCache.Clear();
    mUploadBufferCache.Clear();
    mDelayedRelease.Clear();
}
// Ensure a material is ready to be rendererd by the GPU (with the specified vertex layout)
D3DResourceCache::D3DPipelineState* D3DResourceCache::RequirePipelineState(
    const Shader& vertexShader, const Shader& pixelShader,
    const MaterialState& materialState, std::span<const BufferLayout*> bindings,
    std::span<const MacroValue> macros, const IdentifierWithName& renderPass,
    std::span<DXGI_FORMAT> frameBufferFormats, DXGI_FORMAT depthBufferFormat
)
{
    // Find (or create) a pipeline that matches these requirements
    size_t hash = GenericHash({ GenericHash(materialState), GenericHash((Identifier)renderPass), });
    hash = GenericHash({ hash, ArrayHash(frameBufferFormats), GenericHash(depthBufferFormat) });
    for (auto* binding : bindings) {
        for (auto& el : binding->GetElements()) {
            hash = AppendHash(el.mBindName.mId + ((int)el.mBufferStride << 16) + ((int)el.mFormat << 8), hash);
        }
    }
    hash = AppendHash(std::make_pair(vertexShader.GetHash(), pixelShader.GetHash()), hash);
    for (auto macro : macros) hash = AppendHash(macro, hash);
    auto pipelineState = GetOrCreatePipelineState(vertexShader, pixelShader, hash);
    while (pipelineState->mHash != hash) {
        pipelineState->mHash = hash;
        pipelineState->mRootSignature = &mRootSignature;

        auto device = mD3D12.GetD3DDevice();

        // Make sure shaders are compiled
        auto vShader = RequireShader(vertexShader, StrVSProfile, macros, renderPass);
        auto pShader = RequireShader(pixelShader, StrPSProfile, macros, renderPass);
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
        psoDesc.RasterizerState.CullMode = (D3D12_CULL_MODE)materialState.mRasterMode.mCullMode;
        psoDesc.BlendState = CD3DX12_BLEND_DESC(D3D12_DEFAULT);
        psoDesc.BlendState.RenderTarget[0].BlendEnable = TRUE;
        psoDesc.BlendState.RenderTarget[0].SrcBlend = ToD3DBArg(materialState.mBlendMode.mSrcColorBlend);
        psoDesc.BlendState.RenderTarget[0].DestBlend = ToD3DBArg(materialState.mBlendMode.mDestColorBlend);
        psoDesc.BlendState.RenderTarget[0].SrcBlendAlpha = ToD3DBArg(materialState.mBlendMode.mSrcAlphaBlend);
        psoDesc.BlendState.RenderTarget[0].DestBlendAlpha = ToD3DBArg(materialState.mBlendMode.mDestAlphaBlend);
        psoDesc.BlendState.RenderTarget[0].BlendOp = ToD3DBOp(materialState.mBlendMode.mBlendColorOp);
        psoDesc.BlendState.RenderTarget[0].BlendOpAlpha = ToD3DBOp(materialState.mBlendMode.mBlendAlphaOp);
        psoDesc.DepthStencilState = CD3DX12_DEPTH_STENCIL_DESC1(D3D12_DEFAULT);
        psoDesc.DepthStencilState.DepthFunc = (D3D12_COMPARISON_FUNC)materialState.mDepthMode.mComparison;
        psoDesc.DepthStencilState.DepthWriteMask = materialState.mDepthMode.mWriteEnable ? D3D12_DEPTH_WRITE_MASK_ALL : D3D12_DEPTH_WRITE_MASK_ZERO;
        psoDesc.SampleMask = UINT_MAX;
        psoDesc.PrimitiveTopologyType = D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE;
        psoDesc.NumRenderTargets = (uint32_t)frameBufferFormats.size();
        for (int f = 0; f < frameBufferFormats.size(); ++f)
            psoDesc.RTVFormats[f] = frameBufferFormats[f];
        psoDesc.DSVFormat = depthBufferFormat;
        psoDesc.SampleDesc.Count = 1;
        ThrowIfFailed(device->CreateGraphicsPipelineState(&psoDesc, IID_PPV_ARGS(&pipelineState->mPipelineState)));
        pipelineState->mPipelineState->SetName(pixelShader.GetPath().c_str());

        // Collect constant buffers required by the shaders
        // TODO: Throw an error if different constant buffers
        // are required in the same bind point
        for (auto l : { vShader, pShader }) {
            for (auto& cb : l->mReflection.mConstantBuffers) {
                if (std::any_of(pipelineState->mConstantBuffers.begin(), pipelineState->mConstantBuffers.end(),
                    [&](auto* o) { return *o == cb; })) continue;
                pipelineState->mConstantBuffers.push_back(&cb);
            }
            for (auto& rb : l->mReflection.mResourceBindings) {
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
D3DConstantBuffer* D3DResourceCache::RequireConstantBuffer(int lockBits, const ShaderBase::ConstantBuffer& cBuffer, const Material& material) {
    mTempData.resize(cBuffer.mSize);

    // Copy data into the constant buffer
    // TODO: Generate a hash WITHOUT copying data?
    //  => Might be more expensive to evaluate props twice
    std::memset(mTempData.data(), 0, sizeof(mTempData[0]) * mTempData.size());
    for (auto& var : cBuffer.mValues) {
        auto varData = material.GetUniformBinaryData(var.mName);
        std::memcpy(mTempData.data() + var.mOffset, varData.data(), varData.size());
    }
    return RequireConstantBuffer(lockBits, mTempData);
}
// Find or allocate a constant buffer for the specified material and CB layout
D3DConstantBuffer* D3DResourceCache::RequireConstantBuffer(int lockBits, std::span<const uint8_t> tData) {
    // CB should be padded to multiples of 256
    auto allocSize = (int)(tData.size() + 255) & ~255;
    auto dataHash = allocSize + GenericHash(tData.data(), tData.size());

    auto& resultItem = mConstantBufferCache.RequireItem(dataHash, allocSize, lockBits,
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
D3DResourceCache::D3DRenderSurface::SubresourceData& D3DResourceCache::RequireTextureRTV(D3DResourceCache::D3DRenderSurfaceView& buffer, int lockBits) {
    int subresourceId = D3D12CalcSubresource(buffer.mMip, buffer.mSlice, 0, buffer.mSurface->mMips, buffer.mSurface->mSlices);
    auto* subresource = const_cast<D3DResourceCache::D3DRenderSurface::SubresourceData*>(&buffer.mSurface->RequireSubResource(subresourceId));
    if (subresource->mRTVOffset < 0) {
        auto* surface = buffer.mSurface;
        auto isDepth = BufferFormatType::GetIsDepthBuffer((BufferFormat)surface->mFormat);
        if (isDepth) {
            if (subresource->mRTVOffset < 0) {
                subresource->mRTVOffset = mDSOffset;
                mDSOffset += mD3D12.GetDescriptorHandleSizeDSV();
            }
            D3D12_DEPTH_STENCIL_VIEW_DESC dsViewDesc = { .Format = surface->mFormat, .ViewDimension = D3D12_DSV_DIMENSION_TEXTURE2D };
            dsViewDesc.Texture2D.MipSlice = buffer.mMip;
            mD3D12.GetD3DDevice()->CreateDepthStencilView(surface->mBuffer.Get(), &dsViewDesc,
                CD3DX12_CPU_DESCRIPTOR_HANDLE(mD3D12.GetDSVHeap()->GetCPUDescriptorHandleForHeapStart(), subresource->mRTVOffset));
        }
        else {
            if (subresource->mRTVOffset < 0) {
                subresource->mRTVOffset = mRTOffset;
                mRTOffset += mD3D12.GetDescriptorHandleSizeRTV();
            }
            D3D12_RENDER_TARGET_VIEW_DESC rtvViewDesc = { .Format = surface->mFormat, };
            if (buffer.mSlice > 0) {
                rtvViewDesc.ViewDimension = D3D12_RTV_DIMENSION_TEXTURE2DARRAY;
                rtvViewDesc.Texture2DArray.MipSlice = buffer.mMip;
                rtvViewDesc.Texture2DArray.ArraySize = buffer.mSlice;
            } else {
                rtvViewDesc.ViewDimension = D3D12_RTV_DIMENSION_TEXTURE2D;
                rtvViewDesc.Texture2D.MipSlice = buffer.mMip;
            }
            mD3D12.GetD3DDevice()->CreateRenderTargetView(surface->mBuffer.Get(), &rtvViewDesc,
                CD3DX12_CPU_DESCRIPTOR_HANDLE(mD3D12.GetRTVHeap()->GetCPUDescriptorHandleForHeapStart(), subresource->mRTVOffset));
        }
    }
    return *subresource;
}




D3DGraphicsSurface::D3DGraphicsSurface(D3DGraphicsDevice& device, HWND hWnd)
    : mDevice(device)
{
    // Check the window for how large the backbuffer should be
    RECT rect;
    GetClientRect(hWnd, &rect);
    mResolution = Int2(rect.right - rect.left, rect.bottom - rect.top);

    // Create the swap chain
    DXGI_SWAP_CHAIN_DESC1 swapChainDesc = {};
    swapChainDesc.BufferCount = FrameCount;
    swapChainDesc.Width = mResolution.x;
    swapChainDesc.Height = mResolution.y;
    swapChainDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    swapChainDesc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    swapChainDesc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;
    swapChainDesc.SampleDesc.Count = 1;

    ComPtr<IDXGISwapChain1> swapChain;
    auto* d3dFactory = device.GetFactory();
    auto* cmdQueue = device.GetCmdQueue();
    ThrowIfFailed(d3dFactory->CreateSwapChainForHwnd(cmdQueue, hWnd, &swapChainDesc, nullptr, nullptr, &swapChain));
    ThrowIfFailed(swapChain.As(&mSwapChain));
    mSwapChain->SetColorSpace1(DXGI_COLOR_SPACE_RGB_FULL_G22_NONE_P709);

    // Create fence for frame synchronisation
    mBackBufferIndex = mSwapChain->GetCurrentBackBufferIndex();
    for (int i = 0; i < FrameCount; ++i) mFenceValues[i] = 0;
    ThrowIfFailed(mDevice.GetD3DDevice()->CreateFence(mFenceValues[mBackBufferIndex], D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&mFence)));
    ++mFenceValues[mBackBufferIndex];
    mFenceEvent = CreateEvent(nullptr, FALSE, FALSE, nullptr);
    if (mFenceEvent == nullptr) ThrowIfFailed(HRESULT_FROM_WIN32(GetLastError()));
}
void D3DGraphicsSurface::SetResolution(Int2 resolution) {
    auto* mD3DDevice = mDevice.GetD3DDevice();
    if (mResolution != resolution) {
        for (UINT n = 0; n < FrameCount; n++) {
            mFrameBuffers[n] = { };
            if (mCmdAllocator[n] != nullptr)
                mCmdAllocator[n]->Reset();
        }
        mResolution = resolution;
        ResizeSwapBuffers();
        const UINT64 currentFenceValue = mFenceValues[mBackBufferIndex];
        mBackBufferIndex = mSwapChain->GetCurrentBackBufferIndex();
        mFenceValues[mBackBufferIndex] = currentFenceValue;
    }
    // Create a RTV for each frame.
    for (UINT n = 0; n < FrameCount; n++) {
        auto& frameBuffer = mFrameBuffers[n];
        frameBuffer.mWidth = (uint16_t)mResolution.x;
        frameBuffer.mHeight = (uint16_t)mResolution.y;
        frameBuffer.mMips = 1;
        frameBuffer.mSlices = 1;
        frameBuffer.mFormat = DXGI_FORMAT_R8G8B8A8_UNORM;
        if (mCmdAllocator[n] == nullptr) {
            ThrowIfFailed(mD3DDevice->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&mCmdAllocator[n])));
        }
        if (frameBuffer.mBuffer == nullptr) {
            ThrowIfFailed(mSwapChain->GetBuffer(n, IID_PPV_ARGS(&frameBuffer.mBuffer)));
            wchar_t name[] = L"Frame Buffer 0";
            name[_countof(name) - 1] = n;
            frameBuffer.mBuffer->SetName(name);
        }
    }
}
void D3DGraphicsSurface::ResizeSwapBuffers() {
    auto hr = mSwapChain->ResizeBuffers(0, (UINT)mResolution.x, (UINT)mResolution.y, DXGI_FORMAT_UNKNOWN, 0);
    ThrowIfFailed(hr);
}

int D3DGraphicsSurface::GetBackFrameIndex() const {
    return (int)mFenceValues[mBackBufferIndex];
}

// Flip the backbuffer and wait until a frame is available to be rendered
int D3DGraphicsSurface::Present() {
    auto hr = mSwapChain->Present(1, 0);

    if (hr == DXGI_ERROR_DEVICE_REMOVED || hr == DXGI_ERROR_DEVICE_RESET) {
        mDevice.CheckDeviceState();
        return -1;

        // Reset all cached resources
        //mCache = D3DResourceCache(mDevice);
        // Reset the entire d3d device
        //mDevice = D3DGraphicsDevice(*mWindow);
    }
    else {
        ThrowIfFailed(hr);
    }
    return WaitForFrame();
}

// Wait for the earliest submitted frame to be finished and ready to be rendered into
int D3DGraphicsSurface::WaitForFrame() {
    // Schedule a Signal command in the queue.
    const UINT64 currentFenceValue = mFenceValues[mBackBufferIndex];
    ThrowIfFailed(mDevice.GetCmdQueue()->Signal(mFence.Get(), currentFenceValue));

    // Update the frame index.
    mBackBufferIndex = mSwapChain->GetCurrentBackBufferIndex();
    UINT64 previousId = mFenceValues[mBackBufferIndex];

    // If the next frame is not ready to be rendered yet, wait until it is ready.
    auto fenceVal = mFence->GetCompletedValue();
    if (fenceVal < mFenceValues[mBackBufferIndex]) {
        ThrowIfFailed(mFence->SetEventOnCompletion(mFenceValues[mBackBufferIndex], mFenceEvent));
        WaitForSingleObjectEx(mFenceEvent, INFINITE, FALSE);
    }

    // Set the fence value for the next frame.
    mFenceValues[mBackBufferIndex] = currentFenceValue + 1;
    mCmdAllocator[mBackBufferIndex]->Reset();
    //mCache.SetResourceLockIds(fenceVal, currentFenceValue);
    return (int)previousId;
}
// Wait for all GPU operations? Taken from the samples
void D3DGraphicsSurface::WaitForGPU() {
    // Schedule a Signal command in the queue.
    ThrowIfFailed(mDevice.GetCmdQueue()->Signal(mFence.Get(), mFenceValues[mBackBufferIndex]));

    // Wait until the fence has been processed.
    ThrowIfFailed(mFence->SetEventOnCompletion(mFenceValues[mBackBufferIndex], mFenceEvent));
    WaitForSingleObjectEx(mFenceEvent, INFINITE, FALSE);

    // Increment the fence value for the current frame.
    mFenceValues[mBackBufferIndex]++;
}
