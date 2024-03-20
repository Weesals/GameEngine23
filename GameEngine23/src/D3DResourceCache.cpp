// vblank
//#define NOMINMAX
//#define WIN32_LEAN_AND_MEAN
//#include <Windows.h>
//#include <bcrypt.h>
//#include <D3dkmthk.h>

#include "D3DResourceCache.h"
#include "D3DUtility.h"

#include <d3dx12.h>
#include <cassert>
#include <fstream>



void D3DResourceCache::CreateBuffer(ComPtr<ID3D12Resource>& buffer, int size, int lockBits) {
    // Buffer already valid, register buffer to be destroyed in the future
    if (buffer != nullptr) {
        mDelayedRelease.InsertItem(buffer, 0, lockBits);
        buffer = nullptr;
    }

    auto resDesc = CD3DX12_RESOURCE_DESC::Buffer(size);
    ThrowIfFailed(mD3D12.GetD3DDevice()->CreateCommittedResource(
        &D3D::DefaultHeap,
        D3D12_HEAP_FLAG_NONE,
        &resDesc,
        D3D12_RESOURCE_STATE_COMMON,
        nullptr,
        IID_PPV_ARGS(&buffer)));
    buffer->SetName(L"MeshBuffer");
    mStatistics.mBufferCreates++;
};
bool D3DResourceCache::RequireBuffer(const BufferLayout& binding, D3DBinding& d3dBin, int lockBits) {
    if (d3dBin.mBuffer != nullptr && d3dBin.mSize >= binding.mSize) return false;
    d3dBin.mSize = (binding.mSize + BufferAlignment) & ~BufferAlignment;
    CreateBuffer(d3dBin.mBuffer, d3dBin.mSize, lockBits);
    d3dBin.mBuffer->SetName(
        binding.mUsage == BufferLayout::Usage::Vertex ? L"VertexBuffer" :
        binding.mUsage == BufferLayout::Usage::Index ? L"IndexBuffer" :
        binding.mUsage == BufferLayout::Usage::Instance ? L"InstanceBuffer" :
        binding.mUsage == BufferLayout::Usage::Uniform ? L"UniformBuffer" :
        L"UnknownBuffer"
    );
    d3dBin.mGPUMemory = d3dBin.mBuffer->GetGPUVirtualAddress();
    d3dBin.mSRVOffset = -1;     // TODO: Pool these
    return true;
}
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
            int offset = PostIncrement(itemSize, (uint32_t)elItemSize);
            OnElement(binding, element, offset, classification);
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
    mRootSignature.mNumResources = 8;
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
        CD3DX12_STATIC_SAMPLER_DESC(3, D3D12_FILTER_COMPARISON_MIN_MAG_LINEAR_MIP_POINT, D3D12_TEXTURE_ADDRESS_MODE_BORDER, D3D12_TEXTURE_ADDRESS_MODE_BORDER, D3D12_TEXTURE_ADDRESS_MODE_BORDER, 0, 16, D3D12_COMPARISON_FUNC_LESS_EQUAL),
        CD3DX12_STATIC_SAMPLER_DESC(4, D3D12_FILTER_MINIMUM_MIN_MAG_LINEAR_MIP_POINT),
        CD3DX12_STATIC_SAMPLER_DESC(5, D3D12_FILTER_MAXIMUM_MIN_MAG_LINEAR_MIP_POINT),
        CD3DX12_STATIC_SAMPLER_DESC(6, D3D12_FILTER_MIN_MAG_MIP_LINEAR, D3D12_TEXTURE_ADDRESS_MODE_CLAMP, D3D12_TEXTURE_ADDRESS_MODE_CLAMP, D3D12_TEXTURE_ADDRESS_MODE_CLAMP),
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
D3DResourceCache::D3DPipelineState* D3DResourceCache::GetOrCreatePipelineState(size_t hash) {
    return GetOrCreate(pipelineMapping, hash);
}
D3DResourceCache::D3DRenderSurface* D3DResourceCache::RequireD3DRT(const RenderTarget2D* rt) {
    bool wasCreated;
    auto* d3dTex = GetOrCreate(rtMapping, rt, wasCreated);
    if (wasCreated) d3dTex->mHandle = mResourceCount++;
    return d3dTex;
}
void D3DResourceCache::SetRenderTargetMapping(const RenderTarget2D* rt, const D3DResourceCache::D3DRenderSurface& surface) {
    auto* slot = RequireD3DRT(rt);
    auto handle = slot->mHandle;
    *slot = surface;
    slot->mHandle = handle;
}
// Allocate or retrieve a container for GPU buffers for this item
D3DResourceCache::D3DTexture* D3DResourceCache::RequireD3DTexture(const Texture& tex) {
    return GetOrCreate(textureMapping, &tex);
}
// Retrieve a buffer capable of upload/copy that will be vaild until
// the frame completes rendering
ID3D12Resource* D3DResourceCache::AllocateUploadBuffer(size_t uploadSize, int lockBits) {
    uploadSize = (uploadSize + BufferAlignment) & (~BufferAlignment);
    auto& uploadBufferItem = mUploadBufferCache.RequireItem(uploadSize, lockBits,
        [&](auto& item) { // Allocate a new item
            auto uploadBufferDesc = CD3DX12_RESOURCE_DESC::Buffer(item.mLayoutHash);
            ThrowIfFailed(mD3D12.GetD3DDevice()->CreateCommittedResource(
                &D3D::UploadHeap,
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
D3DResourceCache::D3DBinding& D3DResourceCache::RequireBinding(const BufferLayout& binding) {
    return ::RequireBinding(binding, mBindings);
}
void D3DResourceCache::UpdateBufferData(ID3D12GraphicsCommandList* cmdList, int lockBits, const BufferLayout& binding, std::span<const RangeInt> ranges) {
    auto& d3dBin = RequireBinding(binding);
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
                D3D::WriteBufferData(mappedData + it, binding, itemSize, range.start, range.length);
                it += range.length;
            }
            uploadBuffer->Unmap(0, nullptr);
            auto BufferState = binding.mUsage == BufferLayout::Usage::Uniform
                ? D3D12_RESOURCE_STATE_ALL_SHADER_RESOURCE | D3D12_RESOURCE_STATE_VERTEX_AND_CONSTANT_BUFFER
                : D3D12_RESOURCE_STATE_COMMON;
            auto beginWrite = { CD3DX12_RESOURCE_BARRIER::Transition(d3dBin.mBuffer.Get(), BufferState, D3D12_RESOURCE_STATE_COPY_DEST), };
            cmdList->ResourceBarrier((UINT)beginWrite.size(), beginWrite.begin());

            it = 0;
            for (auto& range : ranges) {
                cmdList->CopyBufferRegion(d3dBin.mBuffer.Get(), range.start,
                    uploadBuffer, it, range.length);
                it += range.length;
                mStatistics.BufferWrite(ranges.size());
            }
            auto endWrite = { CD3DX12_RESOURCE_BARRIER::Transition(d3dBin.mBuffer.Get(), D3D12_RESOURCE_STATE_COPY_DEST, BufferState), };
            cmdList->ResourceBarrier((UINT)endWrite.size(), endWrite.begin());
            d3dBin.mRevision = binding.mRevision;
        },
        [&](const BufferLayout& binding, D3DBinding& d3dBin, int itemSize) {},
        [&](const BufferLayout& binding, const BufferLayout::Element& element, UINT offset, D3D12_INPUT_CLASSIFICATION classification) {},
        [&](const BufferLayout& binding, D3DBinding& d3dBin, int itemSize) {}
    );
}

D3D12_RESOURCE_DESC D3DResourceCache::GetTextureDesc(const Texture& tex) {
    auto size = tex.GetSize();
    auto fmt = tex.GetBufferFormat();
    auto mipCount = tex.GetMipCount(), arrCount = tex.GetArrayCount();
    auto bitsPerPixel = BufferFormatType::GetBitSize(fmt);
    //assert(size.z == 1);
    // Create the texture resource
    auto textureDesc =
        size.z > 1 ? CD3DX12_RESOURCE_DESC::Tex3D((DXGI_FORMAT)fmt, size.x, size.y, size.z, mipCount)
        : CD3DX12_RESOURCE_DESC::Tex2D((DXGI_FORMAT)fmt, size.x, size.y, arrCount, mipCount);
    if (textureDesc.Width * textureDesc.Height * textureDesc.DepthOrArraySize * bitsPerPixel / 8 <= 0x10000) {
        textureDesc.Alignment = D3D12_SMALL_RESOURCE_PLACEMENT_ALIGNMENT;
    }
    return textureDesc;
}
int D3DResourceCache::GetTextureSRV(ID3D12Resource* buffer,
    DXGI_FORMAT fmt, bool is3D, int arrayCount,
    int lockBits, int mipB, int mipC) {
    size_t hash = (size_t)buffer;
    hash += mipB * 12341237 + mipC * 123412343;
    const auto& result = mResourceViewCache.RequireItem(hash, 1, lockBits,
        [&](auto& item) {
            item.mData.mRTVOffset = mCBOffset;
            mCBOffset += mD3D12.GetDescriptorHandleSizeSRV();
        },
        [&](auto& item) {
            auto device = mD3D12.GetD3DDevice();
            // Create a shader resource view (SRV) for the texture
            //auto size = tex.GetSize();
            D3D12_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
            srvDesc.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
            srvDesc.Format = fmt;
            if (is3D) {
                srvDesc.ViewDimension = D3D12_SRV_DIMENSION_TEXTURE3D;
                srvDesc.Texture3D.MostDetailedMip = mipB;
                srvDesc.Texture3D.MipLevels = mipC;
            }
            else if (arrayCount > 1) {
                srvDesc.ViewDimension = D3D12_SRV_DIMENSION_TEXTURE2DARRAY;
                srvDesc.Texture2DArray.MipLevels = mipC;
                srvDesc.Texture2DArray.MostDetailedMip = mipB;
                srvDesc.Texture2DArray.ArraySize = arrayCount;
            }
            else {
                srvDesc.ViewDimension = D3D12_SRV_DIMENSION_TEXTURE2D;
                srvDesc.Texture2D.MostDetailedMip = mipB;
                srvDesc.Texture2D.MipLevels = mipC;
            }
            // Get the CPU handle to the descriptor in the heap
            CD3DX12_CPU_DESCRIPTOR_HANDLE srvHandle(mD3D12.GetSRVHeap()->GetCPUDescriptorHandleForHeapStart(), item.mData.mRTVOffset);
            device->CreateShaderResourceView(buffer, &srvDesc, srvHandle);
        }, [&](auto& item) {});
    return result.mData.mRTVOffset;
}
void D3DResourceCache::UpdateTextureData(D3DTexture* d3dTex, const Texture& tex, ID3D12GraphicsCommandList* cmdList, int lockBits) {
    auto device = mD3D12.GetD3DDevice();
    auto size = tex.GetSize();

    auto bitsPerPixel = BufferFormatType::GetBitSize(tex.GetBufferFormat());
    assert(bitsPerPixel > 0);

    // Get d3d cache instance
    if (d3dTex->mBuffer == nullptr) {
        auto textureDesc = GetTextureDesc(tex);
        //textureDesc.Flags |= D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS;
        ThrowIfFailed(device->CreateCommittedResource(
            &D3D::DefaultHeap,
            D3D12_HEAP_FLAG_NONE,
            &textureDesc,
            D3D12_RESOURCE_STATE_COMMON,
            nullptr,
            IID_PPV_ARGS(&d3dTex->mBuffer)
        ));
        d3dTex->mBuffer->SetName(tex.GetName().c_str());
        d3dTex->mFormat = textureDesc.Format;
        d3dTex->mSRVOffset = GetTextureSRV(d3dTex->mBuffer.Get(),
            textureDesc.Format, textureDesc.Dimension == D3D12_RESOURCE_DIMENSION_TEXTURE3D,
            textureDesc.DepthOrArraySize, 0xffffffff);
    }

    auto uploadSize = (GetRequiredIntermediateSize(d3dTex->mBuffer.Get(), 0, 1) + D3D12_DEFAULT_RESOURCE_PLACEMENT_ALIGNMENT - 1) & ~(D3D12_DEFAULT_RESOURCE_PLACEMENT_ALIGNMENT - 1);

    // Put the texture in write mode
    auto beginWrite = CD3DX12_RESOURCE_BARRIER::Transition(d3dTex->mBuffer.Get(), D3D12_RESOURCE_STATE_COMMON, D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES);
    cmdList->ResourceBarrier(1, &beginWrite);

    auto blockSize = BufferFormatType::GetCompressedBlockSize(tex.GetBufferFormat());
    if (blockSize < 0) blockSize = 1;
    auto blockBytes = bitsPerPixel * blockSize * blockSize / 8;
    auto blockRes = (size + blockSize - 1) / blockSize;

#if 0
    auto desc = d3dTex->mBuffer->GetDesc();
    D3D12_PLACED_SUBRESOURCE_FOOTPRINT footprints[16];
    UINT numRows[16];
    UINT64 rowSizes[16];
    UINT64 RequiredSize = 0;
    device->GetCopyableFootprints(&desc, 0, tex.GetArrayCount(), 0, footprints, numRows, rowSizes, &RequiredSize);

    // Update the texture data
    auto* uploadBuffer = AllocateUploadBuffer(RequiredSize, lockBits);
    uint8_t* mappedData;
    CD3DX12_RANGE readRange(0, 0);
    ThrowIfFailed(uploadBuffer->Map(0, &readRange, (void**)&mappedData));
    for (int i = 0; i < tex.GetArrayCount(); ++i) {
        for (int m = 0; m < tex.GetMipCount(); ++m) {
            auto srcData = tex.GetData(m, i);
            memcpy(mappedData, srcData.data(), srcData.size());
        }
    }
    uploadBuffer->Unmap(0, nullptr);
    //cmdList->CopyBufferRegion(d3dTex->mBuffer.Get(), 0, uploadBuffer, 0, RequiredSize);
    mStatistics.BufferWrite(RequiredSize);

    // Copy data from the upload buffer to the texture
    D3D12_TEXTURE_COPY_LOCATION srcLocation = {};
    srcLocation.pResource = uploadBuffer;
    srcLocation.Type = D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT;
    srcLocation.PlacedFootprint.Offset = 0;
    srcLocation.PlacedFootprint.Footprint.Format = desc.Format;
    srcLocation.PlacedFootprint.Footprint.Width = blockRes.x;
    srcLocation.PlacedFootprint.Footprint.Height = blockRes.y;
    srcLocation.PlacedFootprint.Footprint.Depth = blockRes.z;// std::max(blockRes.z, tex.GetArrayCount());
    srcLocation.PlacedFootprint.Footprint.RowPitch = blockBytes * blockRes.x;
    srcLocation.PlacedFootprint.Footprint.RowPitch =
        (srcLocation.PlacedFootprint.Footprint.RowPitch + 255) & ~255;

    D3D12_TEXTURE_COPY_LOCATION dstLocation = {};
    dstLocation.pResource = d3dTex->mBuffer.Get();
    dstLocation.Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
    dstLocation.SubresourceIndex = 0;

    D3D12_BOX srcBox = {};
    srcBox.right = srcLocation.PlacedFootprint.Footprint.Width;
    srcBox.bottom = srcLocation.PlacedFootprint.Footprint.Height;
    srcBox.back = srcLocation.PlacedFootprint.Footprint.Depth;

    cmdList->CopyTextureRegion(&dstLocation, 0, 0, 0, &srcLocation, &srcBox);

#else
    for (int i = 0; i < tex.GetArrayCount(); ++i) {
        for (int m = 0; m < tex.GetMipCount(); ++m) {
            auto res = tex.GetMipResolution(size, tex.GetBufferFormat(), m);
            auto srcData = tex.GetData(m, i);
            D3D12_SUBRESOURCE_DATA textureData = {};
            textureData.pData = reinterpret_cast<const UINT8*>(srcData.data());
            textureData.RowPitch = blockBytes * res.x / blockSize;
            textureData.SlicePitch = textureData.RowPitch * res.y / blockSize;
            auto uploadBuffer = AllocateUploadBuffer(uploadSize, lockBits);
            UpdateSubresources<1>(cmdList, d3dTex->mBuffer.Get(), uploadBuffer, 0,
                D3D12CalcSubresource(m, i, 0, tex.GetMipCount(), tex.GetArrayCount()), 1,
                &textureData);
            mStatistics.BufferWrite(uploadSize);
        }
    }
#endif

    // Put the texture back in normal mode
    auto endWrite = CD3DX12_RESOURCE_BARRIER::Transition(d3dTex->mBuffer.Get(), D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_COMMON, D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES);
    cmdList->ResourceBarrier(1, &endWrite);

    d3dTex->mRevision = tex.GetRevision();
}
D3DResourceCache::D3DTexture* D3DResourceCache::RequireDefaultTexture(ID3D12GraphicsCommandList* cmdList, int lockBits)
{
    if (mDefaultTexture == nullptr) {
        mDefaultTexture = std::make_shared<Texture>();
        mDefaultTexture->SetSize(4);
        auto data = mDefaultTexture->GetRawData();
        std::fill((uint32_t*)&*data.begin(), (uint32_t*)(&*data.begin() + data.size()), 0xffe0e0e0);
        mDefaultTexture->MarkChanged();
    }
    auto texture = mDefaultTexture.get();
    auto d3dTex = RequireD3DTexture(*texture);
    if (d3dTex->mRevision != texture->GetRevision())
        UpdateTextureData(d3dTex, *texture, cmdList, lockBits);
    return d3dTex;
}
D3DResourceCache::D3DTexture* D3DResourceCache::RequireCurrentTexture(const Texture* texture, ID3D12GraphicsCommandList* cmdList, int lockBits)
{
    if (texture == nullptr || texture->GetSize().x <= 0) {
        return RequireDefaultTexture(cmdList, lockBits);
    }
    auto d3dTex = RequireD3DTexture(*texture);
    if (d3dTex->mRevision != texture->GetRevision())
        UpdateTextureData(d3dTex, *texture, cmdList, lockBits);
    return d3dTex;
}
void D3DResourceCache::CopyBufferData(ID3D12GraphicsCommandList* cmdList, int lockBits, const BufferLayout& binding, D3DBinding& d3dBin, int itemSize, int byteOffset, int byteSize) {
    auto state = binding.mUsage == BufferLayout::Usage::Index ? D3D12_RESOURCE_STATE_INDEX_BUFFER : D3D12_RESOURCE_STATE_VERTEX_AND_CONSTANT_BUFFER;
    state |= D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE | D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
    auto beginWrite = { CD3DX12_RESOURCE_BARRIER::Transition(d3dBin.mBuffer.Get(), state, D3D12_RESOURCE_STATE_COPY_DEST), };
    cmdList->ResourceBarrier((UINT)beginWrite.size(), beginWrite.begin());
    int size = (byteSize + BufferAlignment) & ~BufferAlignment;

    // Map and fill the buffer data (via temporary upload buffer)
    ID3D12Resource* uploadBuffer = AllocateUploadBuffer(size, lockBits);
    D3D::FillBuffer(uploadBuffer, [&](uint8_t* data) { D3D::WriteBufferData(data, binding, itemSize, byteOffset, byteSize); });
    cmdList->CopyBufferRegion(d3dBin.mBuffer.Get(), byteOffset, uploadBuffer, 0, size);
    mStatistics.BufferWrite(size);

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
        [&](const BufferLayout& binding, const BufferLayout::Element& element, UINT offset, D3D12_INPUT_CLASSIFICATION classification) {
            inputElements.push_back(D3D12_INPUT_ELEMENT_DESC{
                element.mBindName.GetName().c_str(), 0, (DXGI_FORMAT)element.mFormat, 0,
                offset, classification,
                binding.mUsage == BufferLayout::Usage::Instance ? 1u : 0u
            });
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
        [&](const BufferLayout& binding, const BufferLayout::Element& element, UINT offset, D3D12_INPUT_CLASSIFICATION classification) {},
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
    assert(mFrameBitPool.size() <= 64);
    return (int)mFrameBitPool.size() - 1;
}
void D3DResourceCache::AddInFlightSurface(const std::shared_ptr<D3DGraphicsSurface>& surface) {
    if (std::find(mInflightSurfaces.begin(), mInflightSurfaces.end(), surface) != mInflightSurfaces.end()) return;
    mInflightSurfaces.push_back(surface);
}
D3DResourceCache::CommandAllocator* D3DResourceCache::RequireAllocator() {
    for(int i = 0; i < (int)mInflightSurfaces.size(); ++i) {
        auto& surface = mInflightSurfaces[i];
        auto lockFrame = surface->GetLockFrame();
        auto consumeFrame = surface->ConsumeFrame(lockFrame);
        for (; lockFrame != consumeFrame; ++consumeFrame) UnlockFrame((size_t)surface.get() + (consumeFrame & 31));
        if (surface->GetHeadFrame() == lockFrame) mInflightSurfaces.erase(mInflightSurfaces.begin() + (i--));
    }
    for (auto& allocator : mCommandAllocators) {
        if (allocator.mFrameLocks == 0) return &allocator;
    }
    CommandAllocator allocator;
    ThrowIfFailed(mD3D12.GetD3DDevice()->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&allocator.mCmdAllocator)));
    mCommandAllocators.push_back(allocator);
    return &mCommandAllocators.back();
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
    for (auto& allocator : mCommandAllocators) {
        if (allocator.mFrameLocks == 0) continue;
        allocator.mFrameLocks &= ~(1ull << frameHandle);
        if (allocator.mFrameLocks == 0) allocator.mCmdAllocator->Reset();
    }
}
void D3DResourceCache::ClearDelayedData() {
    mResourceViewCache.Clear();
    mUploadBufferCache.Clear();
    mDelayedRelease.Clear();
}
// Ensure a material is ready to be rendererd by the GPU (with the specified vertex layout)
D3DResourceCache::D3DPipelineState* D3DResourceCache::RequirePipelineState(
    const CompiledShader& vertexShader, const CompiledShader& pixelShader,
    const MaterialState& materialState, std::span<const BufferLayout*> bindings,
    std::span<DXGI_FORMAT> frameBufferFormats, DXGI_FORMAT depthBufferFormat
)
{
    // Find (or create) a pipeline that matches these requirements
    size_t hash = GenericHash({
        GenericHash(materialState),
        ArrayHash(frameBufferFormats),
        GenericHash(depthBufferFormat),
    });
    for (auto* binding : bindings) {
        for (auto& el : binding->GetElements()) {
            hash = AppendHash(el.mBindName.mId + ((int)el.mBufferStride << 16) + ((int)el.mFormat << 8), hash);
        }
    }
    hash = AppendHash(std::make_pair(vertexShader.GetBinaryHash(), pixelShader.GetBinaryHash()), hash);

    auto pipelineState = GetOrCreatePipelineState(hash);
    while (pipelineState->mHash != hash) {
        assert(pipelineState->mHash == 0);

        pipelineState->mHash = hash;
        pipelineState->mRootSignature = &mRootSignature;

        ComPtr<ID3DBlob> vertBlob, pixBlob;
        D3DCreateBlob(vertexShader.GetBinary().size(), &vertBlob);
        D3DCreateBlob(pixelShader.GetBinary().size(), &pixBlob);
        memcpy(vertBlob->GetBufferPointer(), vertexShader.GetBinary().data(), vertexShader.GetBinary().size());
        memcpy(pixBlob->GetBufferPointer(), pixelShader.GetBinary().data(), pixelShader.GetBinary().size());

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
        auto ToD3DStencilDesc = [](const DepthMode::StencilDesc& desc)
            {
                return D3D12_DEPTH_STENCILOP_DESC{
                    .StencilFailOp = (D3D12_STENCIL_OP)desc.StecilFailOp,
                    .StencilDepthFailOp = (D3D12_STENCIL_OP)desc.DepthFailOp,
                    .StencilPassOp = (D3D12_STENCIL_OP)desc.PassOp,
                    .StencilFunc = (D3D12_COMPARISON_FUNC)desc.Function,
                };
            };

        ComputeElementLayout(bindings, pipelineState->mInputElements);

        // Create the D3D pipeline
        D3D12_GRAPHICS_PIPELINE_STATE_DESC psoDesc = {};
        psoDesc.InputLayout = { pipelineState->mInputElements.data(), (unsigned int)pipelineState->mInputElements.size() };
        psoDesc.pRootSignature = pipelineState->mRootSignature->mRootSignature.Get();
        psoDesc.VS = CD3DX12_SHADER_BYTECODE(vertBlob.Get());
        psoDesc.PS = CD3DX12_SHADER_BYTECODE(pixBlob.Get());
        psoDesc.RasterizerState = CD3DX12_RASTERIZER_DESC(D3D12_DEFAULT);
        psoDesc.RasterizerState.CullMode = (D3D12_CULL_MODE)materialState.mRasterMode.mCullMode;
        psoDesc.BlendState = CD3DX12_BLEND_DESC(D3D12_DEFAULT);
        psoDesc.BlendState.RenderTarget[0].BlendEnable = materialState.mBlendMode.GetIsOpaque() ? FALSE : TRUE;
        psoDesc.BlendState.RenderTarget[0].SrcBlend = ToD3DBArg(materialState.mBlendMode.mSrcColorBlend);
        psoDesc.BlendState.RenderTarget[0].DestBlend = ToD3DBArg(materialState.mBlendMode.mDestColorBlend);
        psoDesc.BlendState.RenderTarget[0].SrcBlendAlpha = ToD3DBArg(materialState.mBlendMode.mSrcAlphaBlend);
        psoDesc.BlendState.RenderTarget[0].DestBlendAlpha = ToD3DBArg(materialState.mBlendMode.mDestAlphaBlend);
        psoDesc.BlendState.RenderTarget[0].BlendOp = ToD3DBOp(materialState.mBlendMode.mBlendColorOp);
        psoDesc.BlendState.RenderTarget[0].BlendOpAlpha = ToD3DBOp(materialState.mBlendMode.mBlendAlphaOp);
        psoDesc.DepthStencilState = CD3DX12_DEPTH_STENCIL_DESC1(D3D12_DEFAULT);
        psoDesc.DepthStencilState.DepthEnable = materialState.mDepthMode.GetDepthClip() || materialState.mDepthMode.GetDepthWrite();
        psoDesc.DepthStencilState.DepthFunc = (D3D12_COMPARISON_FUNC)materialState.mDepthMode.mComparison;
        psoDesc.DepthStencilState.DepthWriteMask = materialState.mDepthMode.GetDepthWrite() ? D3D12_DEPTH_WRITE_MASK_ALL : D3D12_DEPTH_WRITE_MASK_ZERO;
        psoDesc.DepthStencilState.StencilEnable = materialState.mDepthMode.GetStencilEnable();
        if (psoDesc.DepthStencilState.StencilEnable) {
            psoDesc.DepthStencilState.StencilReadMask = materialState.mDepthMode.mStencilReadMask;
            psoDesc.DepthStencilState.StencilWriteMask = materialState.mDepthMode.mStencilWriteMask;
            psoDesc.DepthStencilState.FrontFace = ToD3DStencilDesc(materialState.mDepthMode.mStencilFront);
            psoDesc.DepthStencilState.BackFace = ToD3DStencilDesc(materialState.mDepthMode.mStencilBack);
        }
        psoDesc.SampleMask = UINT_MAX;
        psoDesc.PrimitiveTopologyType = D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE;
        psoDesc.NumRenderTargets = (uint32_t)frameBufferFormats.size();
        for (int f = 0; f < frameBufferFormats.size(); ++f)
            psoDesc.RTVFormats[f] = frameBufferFormats[f];
        psoDesc.DSVFormat = depthBufferFormat;
        psoDesc.SampleDesc.Count = 1;
        auto device = mD3D12.GetD3DDevice();
        ThrowIfFailed(device->CreateGraphicsPipelineState(&psoDesc, IID_PPV_ARGS(&pipelineState->mPipelineState)));
        pipelineState->mPipelineState->SetName(pixelShader.GetName().GetWName().c_str());

        // Collect constant buffers required by the shaders
        // TODO: Throw an error if different constant buffers
        // are required in the same bind point
        for (auto l : { &vertexShader, &pixelShader }) {
            uint64_t cbMask = 0;
            uint64_t rbMask = 0;
            for (auto& cb : l->GetReflection().mConstantBuffers) {
                if (std::any_of(pipelineState->mConstantBuffers.begin(), pipelineState->mConstantBuffers.end(),
                    [&](auto* o) { return *o == cb; })) continue;
                uint64_t mask = 1ull << cb.mBindPoint;
                if (cbMask & mask) throw "Two CBs occupy the same bind point";
                cbMask |= mask;
                pipelineState->mConstantBuffers.push_back(&cb);
            }
            for (auto& rb : l->GetReflection().mResourceBindings) {
                if (std::any_of(pipelineState->mResourceBindings.begin(), pipelineState->mResourceBindings.end(),
                    [&](auto* o) { return *o == rb; })) continue;
                uint64_t mask = 1ull << rb.mBindPoint;
                if (rbMask & mask) throw "Two CBs occupy the same bind point";
                rbMask |= mask;
                pipelineState->mResourceBindings.push_back(&rb);
            }
        }
        break;
    }
    return pipelineState;
}
// Find or allocate a constant buffer for the specified material and CB layout
D3DConstantBuffer* D3DResourceCache::RequireConstantBuffer(ID3D12GraphicsCommandList* cmdList, int lockBits, std::span<const uint8_t> tData, size_t dataHash) {
    // CB should be padded to multiples of 256
    auto allocSize = (int)(tData.size() + 255) & ~255;
    if (dataHash == 0) dataHash = allocSize + GenericHash(tData.data(), tData.size());

    auto CBState = D3D12_RESOURCE_STATE_ALL_SHADER_RESOURCE | D3D12_RESOURCE_STATE_VERTEX_AND_CONSTANT_BUFFER;

    auto& resultItem = mConstantBufferCache.RequireItem(dataHash, allocSize, lockBits,
        [&](auto& item) { // Allocate a new item
            auto device = mD3D12.GetD3DDevice();
            assert(item.mData.mConstantBuffer == nullptr);
            // We got a fresh item, need to create the relevant buffers
            CD3DX12_RESOURCE_DESC resourceDesc = CD3DX12_RESOURCE_DESC::Buffer(allocSize);
            auto hr = device->CreateCommittedResource(
                &D3D::DefaultHeap,
                D3D12_HEAP_FLAG_NONE,
                &resourceDesc,
                CBState,
                nullptr,
                IID_PPV_ARGS(&item.mData.mConstantBuffer)
            );
            assert(item.mData.mConstantBuffer != nullptr);
            if (FAILED(hr)) throw "[D3D] Failed to create constant buffer";
            mStatistics.mBufferCreates++;
        },
        [&](auto& item) { // Fill an item with data
            // Copy data into this new one
            /*assert(item.mData.mConstantBuffer != nullptr);
            UINT8* cbDataBegin;
            if (SUCCEEDED(item.mData.mConstantBuffer->Map(0, nullptr, reinterpret_cast<void**>(&cbDataBegin)))) {
                std::memcpy(cbDataBegin, tData.data(), tData.size());
                item.mData.mConstantBuffer->Unmap(0, nullptr);
            }
            mStatistics.BufferWrite(tData.size());*/
            int copySize = (tData.size() + 15) & ~15;
            auto beginWrite = { CD3DX12_RESOURCE_BARRIER::Transition(item.mData.mConstantBuffer.Get(), CBState, D3D12_RESOURCE_STATE_COPY_DEST), };
            cmdList->ResourceBarrier((UINT)beginWrite.size(), beginWrite.begin());
            ID3D12Resource* uploadBuffer = AllocateUploadBuffer(copySize, lockBits);
            D3D::FillBuffer(uploadBuffer, [&](uint8_t* data) { std::memcpy(data, tData.data(), tData.size()); });
            cmdList->CopyBufferRegion(item.mData.mConstantBuffer.Get(), 0, uploadBuffer, 0, copySize);
            mStatistics.BufferWrite(tData.size());
            auto endWrite = { CD3DX12_RESOURCE_BARRIER::Transition(item.mData.mConstantBuffer.Get(), D3D12_RESOURCE_STATE_COPY_DEST, CBState), };
            cmdList->ResourceBarrier((UINT)endWrite.size(), endWrite.begin());
        },
        [&](auto& item) { } // An existing item was found to match the data
    );
    assert(resultItem.mLayoutHash == allocSize);
    return &resultItem.mData;
}
D3DResourceCache::D3DRenderSurface::SubresourceData& D3DResourceCache::RequireTextureRTV(D3DResourceCache::D3DRenderSurfaceView& buffer, int lockBits) {
    int subresourceId = D3D12CalcSubresource(buffer.mMip, buffer.mSlice, 0, buffer.mSurface->mDesc.mMips, buffer.mSurface->mDesc.mSlices);
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
    , mLockFrame(0)
{
    // Check the window for how large the backbuffer should be
    RECT rect;
    GetClientRect(hWnd, &rect);
    mResolution = Int2(rect.right - rect.left, rect.bottom - rect.top);
    mRenderTarget = std::make_shared<RenderTarget2D>(std::wstring_view(L"BackBuffer"));
    mRenderTarget->SetFormat(BufferFormat::FORMAT_R8G8B8A8_UNORM_SRGB);

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

    // This grabs references for the surface frame bufers
    SetResolution(GetResolution());
}
D3DGraphicsSurface::~D3DGraphicsSurface() {
    WaitForGPU();
}
void D3DGraphicsSurface::SetResolution(Int2 resolution) {
    auto* mD3DDevice = mDevice.GetD3DDevice();
    if (mResolution != resolution) {
        WaitForGPU();
        for (UINT n = 0; n < FrameCount; n++) {
            mFrameBuffers[n] = { };
            //if (mCmdAllocator[n] != nullptr) mCmdAllocator[n]->Reset();
        }
        mResolution = resolution;
        ResizeSwapBuffers();
        const UINT64 currentFenceValue = mFenceValues[mBackBufferIndex];
        mBackBufferIndex = mSwapChain->GetCurrentBackBufferIndex();
        mFenceValues[mBackBufferIndex] = currentFenceValue;
        mRenderTarget->SetResolution(resolution);
    }
    // Create a RTV for each frame.
    for (UINT n = 0; n < FrameCount; n++) {
        auto& frameBuffer = mFrameBuffers[n];
        frameBuffer.mDesc.mWidth = (uint16_t)mResolution.x;
        frameBuffer.mDesc.mHeight = (uint16_t)mResolution.y;
        frameBuffer.mDesc.mMips = 1;
        frameBuffer.mDesc.mSlices = 1;
        frameBuffer.mFormat = DXGI_FORMAT_R8G8B8A8_UNORM;
        //if (mCmdAllocator[n] == nullptr) {
            //ThrowIfFailed(mD3DDevice->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&mCmdAllocator[n])));
        //}
        if (frameBuffer.mBuffer == nullptr) {
            ThrowIfFailed(mSwapChain->GetBuffer(n, IID_PPV_ARGS(&frameBuffer.mBuffer)));
            wchar_t name[] = L"Frame Buffer 0";
            name[_countof(name) - 2] = '0' + n;
            frameBuffer.mBuffer->SetName(name);
        }
    }
}
void D3DGraphicsSurface::ResizeSwapBuffers() {
    //mSwapChain->Present(1, DXGI_PRESENT_RESTART);
    auto hr = mSwapChain->ResizeBuffers(0, (UINT)mResolution.x, (UINT)mResolution.y, DXGI_FORMAT_UNKNOWN, 0);
    ThrowIfFailed(hr);
}

const std::shared_ptr<RenderTarget2D>& D3DGraphicsSurface::GetBackBuffer() const {
    return mRenderTarget;
}

int D3DGraphicsSurface::GetBackFrameIndex() const {
    return (int)mFenceValues[mBackBufferIndex];
}

bool D3DGraphicsSurface::GetIsOccluded() const { return mIsOccluded; }
void D3DGraphicsSurface::RegisterDenyPresent(int delta) {
    mDenyPresentRef += delta;
}


//extern "C" NTSTATUS __cdecl D3DKMTWaitForVerticalBlankEvent(const D3DKMT_WAITFORVERTICALBLANKEVENT*);
//extern "C" NTSTATUS __cdecl D3DKMTOpenAdapterFromHdc(D3DKMT_OPENADAPTERFROMHDC * lpParams);
/*D3DKMT_WAITFORVERTICALBLANKEVENT getVBlankHandle() {
    //https://docs.microsoft.com/en-us/windows/desktop/gdi/getting-information-on-a-display-monitor
    DISPLAY_DEVICE dd;
    dd.cb = sizeof(DISPLAY_DEVICE);

    DWORD deviceNum = 0;
    while (EnumDisplayDevices(NULL, deviceNum, &dd, 0)) {
        if (dd.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE) break;
        deviceNum++;
    }

    HDC hdc = CreateDC(NULL, dd.DeviceName, NULL, NULL);
    if (hdc == NULL) { }

    D3DKMT_OPENADAPTERFROMHDC OpenAdapterData;
    OpenAdapterData.hDc = hdc;
    D3DKMTOpenAdapterFromHdc(&OpenAdapterData);
    DeleteDC(hdc);
    D3DKMT_WAITFORVERTICALBLANKEVENT we;
    we.hAdapter = OpenAdapterData.hAdapter;
    we.hDevice = 0; //optional. maybe OpenDeviceHandle will give it to us, https://docs.microsoft.com/en-us/windows/desktop/api/dxva2api/nf-dxva2api-idirect3ddevicemanager9-opendevicehandle
    we.VidPnSourceId = OpenAdapterData.VidPnSourceId;

    return we;
}*/

// Flip the backbuffer and wait until a frame is available to be rendered
int D3DGraphicsSurface::Present() {
    if (mDenyPresentRef > 0) {
        //static auto VBlankHandle = getVBlankHandle();
        //D3DKMTWaitForVerticalBlankEvent(&VBlankHandle);
        return WaitForFrame();
    }
    RECT rects = { 0, 0, 10, 10 };
    DXGI_PRESENT_PARAMETERS params = { };
    params.DirtyRectsCount = mDenyPresentRef > 0 ? 1 : 0;
    params.pDirtyRects = &rects;
    params.pScrollOffset = nullptr;
    params.pScrollRect = nullptr;
    //mDenyPresentRef > 0 ? DXGI_PRESENT_DO_NOT_SEQUENCE | DXGI_PRESENT_TEST : 
    auto hr = mSwapChain->Present(1, mDenyPresentRef > 0 ? DXGI_PRESENT_DO_NOT_SEQUENCE : 0);

    if ((hr == DXGI_STATUS_OCCLUDED) != mIsOccluded) {
        mIsOccluded = hr == DXGI_STATUS_OCCLUDED;
        mDenyPresentRef += mIsOccluded ? 1 : -1;
    }
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

UINT64 D3DGraphicsSurface::GetHeadFrame() const {
    return mFenceValues[mBackBufferIndex];
}
UINT64 D3DGraphicsSurface::GetLockFrame() const {
    return mFence->GetCompletedValue() + 1;
}
UINT64 D3DGraphicsSurface::ConsumeFrame(UINT64 untilFrame) {
    auto id = mLockFrame;
    mLockFrame = untilFrame;
    return id;
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
    //mCmdAllocator[mBackBufferIndex]->Reset();
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
