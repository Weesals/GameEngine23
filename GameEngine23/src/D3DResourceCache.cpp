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



bool D3DResourceCache::RequireBuffer(const BufferLayout& binding, D3DBinding& d3dBin, int lockBits) {
    if (d3dBin.mBuffer != nullptr && d3dBin.mSize >= binding.mSize) return false;
    d3dBin.mSize = (binding.mSize + BufferAlignment) & ~BufferAlignment;
    assert(d3dBin.mSize > 0);
    // Buffer already valid, register buffer to be destroyed in the future
    if (d3dBin.mBuffer != nullptr) {
        mDelayedRelease.InsertItem(d3dBin.mBuffer, 0, lockBits);
        d3dBin.mBuffer = nullptr;
    }

    auto resDesc = CD3DX12_RESOURCE_DESC::Buffer(d3dBin.mSize);
    if (binding.GetAllowUnorderedAccess())
        resDesc.Flags |= D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS;
    ThrowIfFailed(mD3D12.GetD3DDevice()->CreateCommittedResource(
        &D3D::DefaultHeap,
        D3D12_HEAP_FLAG_NONE,
        &resDesc,
        D3D12_RESOURCE_STATE_COMMON,
        nullptr,
        IID_PPV_ARGS(&d3dBin.mBuffer)));
    std::wstring name = binding.mUsage == BufferLayout::Usage::Vertex ? L"VertexBuffer" :
        binding.mUsage == BufferLayout::Usage::Index ? L"IndexBuffer" :
        binding.mUsage == BufferLayout::Usage::Instance ? L"InstanceBuffer" :
        binding.mUsage == BufferLayout::Usage::Uniform ? L"UniformBuffer" :
        L"UnknownBuffer";
    name += L" <";
    for (auto& el : binding.GetElements()) name += el.mBindName.GetWName() + L",";
    name += L">";
    d3dBin.mBuffer->SetName(name.c_str());
    d3dBin.mGPUMemory = d3dBin.mBuffer->GetGPUVirtualAddress();
    d3dBin.mSRVOffset = -1;     // TODO: Pool these
    mStatistics.mBufferCreates++;
    return true;
}
D3DResourceCache::D3DBinding& RequireBinding(const BufferLayout& binding, std::map<size_t, std::unique_ptr<D3DResourceCache::D3DBinding>>& bindingMap) {
    auto d3dBinIt = bindingMap.find(binding.mIdentifier);
    if (d3dBinIt == bindingMap.end()) {
        d3dBinIt = bindingMap.emplace(std::make_pair(binding.mIdentifier, std::make_unique<D3DResourceCache::D3DBinding>())).first;
        d3dBinIt->second->mRevision = -1;
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
        if (bindingPtr->mElements == nullptr) continue;
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

    D3D12_FEATURE_DATA_ROOT_SIGNATURE featureData = {};
    // This is the highest version the sample supports. If CheckFeatureSupport succeeds, the HighestVersion returned will not be greater than this.
    featureData.HighestVersion = D3D_ROOT_SIGNATURE_VERSION_1_1;
    if (FAILED(mD3DDevice->CheckFeatureSupport(D3D12_FEATURE_ROOT_SIGNATURE, &featureData, sizeof(featureData))))
        featureData.HighestVersion = D3D_ROOT_SIGNATURE_VERSION_1_0;

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

    {
        mRootSignature.mNumConstantBuffers = 4;
        mRootSignature.mNumResources = 8;

        CD3DX12_ROOT_PARAMETER1 rootParameters[14];
        CD3DX12_DESCRIPTOR_RANGE1 srvR[10];
        int rootParamId = 0;
        for (int i = 0; i < mRootSignature.mNumConstantBuffers; ++i)
            rootParameters[rootParamId++].InitAsConstantBufferView(i);
        for (int i = 0; i < mRootSignature.mNumResources; ++i) {
            srvR[i] = CD3DX12_DESCRIPTOR_RANGE1(D3D12_DESCRIPTOR_RANGE_TYPE_SRV, 1, i);
            rootParameters[rootParamId++].InitAsDescriptorTable(1, &srvR[i]);
        }

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
    {
        mComputeRootSignature.mNumConstantBuffers = 4;
        mComputeRootSignature.mNumResources = 10;

        CD3DX12_ROOT_PARAMETER1 rootParameters[14];
        CD3DX12_DESCRIPTOR_RANGE1 srvR[10];
        int rootParamId = 0;
        for (int i = 0; i < mComputeRootSignature.mNumConstantBuffers; ++i)
            rootParameters[rootParamId++].InitAsConstantBufferView(i);
        for (int i = 0; i < 5; ++i) {
            srvR[i] = CD3DX12_DESCRIPTOR_RANGE1(D3D12_DESCRIPTOR_RANGE_TYPE_SRV, 1, i);
            rootParameters[rootParamId++].InitAsDescriptorTable(1, &srvR[i]);
        }
        for (int i = 5; i < mComputeRootSignature.mNumResources; ++i) {
            srvR[i] = CD3DX12_DESCRIPTOR_RANGE1(D3D12_DESCRIPTOR_RANGE_TYPE_UAV, 1, i - 5);
            rootParameters[rootParamId++].InitAsDescriptorTable(1, &srvR[i]);
        }

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
        ThrowIfFailed(mD3DDevice->CreateRootSignature(0, signature->GetBufferPointer(), signature->GetBufferSize(), IID_PPV_ARGS(&mComputeRootSignature.mRootSignature)));
    }
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
        assert(srvDesc.Buffer.NumElements < 100000000);

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
    if (RequireBuffer(binding, d3dBin, lockBits) && binding.mRevision != -1) {
        fullRefresh = true;
    }
    // Special case - update full buffer if revision mismatch
    if (ranges.size() == 1 && ranges[0].start == -1) {
        if (d3dBin.mRevision == binding.mRevision) return;
        fullRefresh = true;
    }
    if (fullRefresh) {
        ProcessBindings(binding, d3dBin,
            [&](const BufferLayout& binding, D3DBinding& d3dBin, int itemSize) {
                // Special case - if mData is null dont copy
                // TODO: Support multiple null elements?
                if (binding.mElementCount == 1 && binding.mElements->mData == nullptr)
                    return;
                CopyBufferData(cmdList, lockBits, binding, d3dBin, itemSize, 0, binding.mSize);
            },
            [&](const BufferLayout& binding, D3DBinding& d3dBin, int itemSize) {},
            [&](const BufferLayout& binding, const BufferLayout::Element& element, UINT offset, D3D12_INPUT_CLASSIFICATION classification) {},
            [&](const BufferLayout& binding, D3DBinding& d3dBin, int itemSize) {}
        );
        return;
    }
    int totalCount = std::accumulate(ranges.begin(), ranges.end(), 0, [](int counter, RangeInt range) { return counter + range.length; });
    if (totalCount == 0) return;
    ProcessBindings(binding, d3dBin,
        [&](const BufferLayout& binding, D3DBinding& d3dBin, int itemSize) {
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
            RequireState(d3dBin, binding, D3D12_RESOURCE_STATE_COPY_DEST);
            FlushBarriers(cmdList);

            it = 0;
            for (auto& range : ranges) {
                cmdList->CopyBufferRegion(d3dBin.mBuffer.Get(), range.start,
                    uploadBuffer, it, range.length);
                it += range.length;
                mStatistics.BufferWrite(ranges.size());
            }
        },
        [&](const BufferLayout& binding, D3DBinding& d3dBin, int itemSize) {},
        [&](const BufferLayout& binding, const BufferLayout::Element& element, UINT offset, D3D12_INPUT_CLASSIFICATION classification) {},
        [&](const BufferLayout& binding, D3DBinding& d3dBin, int itemSize) {}
    );
    d3dBin.mRevision = binding.mRevision;
}
void D3DResourceCache::UpdateBufferData(ID3D12GraphicsCommandList* cmdList, int lockBits, const BufferLayout& source, const BufferLayout& dest, int srcOffset, int dstOffset, int length) {
    auto& srcBinding = RequireBinding(source);
    auto& dstBinding = RequireBinding(dest);
    RequireState(srcBinding, source, D3D12_RESOURCE_STATE_COPY_SOURCE);
    RequireState(dstBinding, dest, D3D12_RESOURCE_STATE_COPY_DEST);
    FlushBarriers(cmdList);
    cmdList->CopyBufferRegion(dstBinding.mBuffer.Get(), dstOffset, srcBinding.mBuffer.Get(), srcOffset, length);
}

D3D12_RESOURCE_DESC D3DResourceCache::GetTextureDesc(const Texture& tex) {
    auto size = tex.GetSize();
    auto fmt = tex.GetBufferFormat();
    auto mipCount = tex.GetMipCount(), arrCount = tex.GetArrayCount();
    auto bitsPerPixel = BufferFormatType::GetBitSize(fmt);
    //assert(size.z == 1);
    // Create the texture resource
    auto texDesc =
        size.z > 1 ? CD3DX12_RESOURCE_DESC::Tex3D((DXGI_FORMAT)fmt, size.x, size.y, size.z, mipCount)
        : CD3DX12_RESOURCE_DESC::Tex2D((DXGI_FORMAT)fmt, size.x, size.y, arrCount, mipCount);
    if (texDesc.Width * texDesc.Height * texDesc.DepthOrArraySize * bitsPerPixel / 8 <= 0x10000)
        texDesc.Alignment = D3D12_SMALL_RESOURCE_PLACEMENT_ALIGNMENT;
    if (tex.GetAllowUnorderedAccess())
        texDesc.Flags |= D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS;
    return texDesc;
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
            item.mData.mResource = buffer;
        }, [&](auto& item) {
            assert(item.mData.mResource == buffer);
        });
    return result.mData.mRTVOffset;
}
int D3DResourceCache::GetBufferSRV(ID3D12Resource* buffer, int offset, int count, int stride, int lockBits) {
    size_t hash = (size_t)buffer;
    hash += offset * 123412343 + count * 12341237 + stride * 12345;
    const auto& result = mResourceViewCache.RequireItem(hash, 1, lockBits,
        [&](auto& item) {
            item.mData.mRTVOffset = mCBOffset;
            mCBOffset += mD3D12.GetDescriptorHandleSizeSRV();
        },
        [&](auto& item) {
            auto device = mD3D12.GetD3DDevice();

            // Create a shader resource view (SRV) for the texture
            D3D12_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
            srvDesc.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
            srvDesc.Format = DXGI_FORMAT_UNKNOWN;
            srvDesc.ViewDimension = D3D12_SRV_DIMENSION_BUFFER;
            srvDesc.Buffer.FirstElement = offset;
            srvDesc.Buffer.NumElements = count;
            srvDesc.Buffer.StructureByteStride = stride;
            srvDesc.Buffer.Flags = D3D12_BUFFER_SRV_FLAG_NONE;
            assert(srvDesc.Buffer.NumElements < 10000000);

            // Get the CPU handle to the descriptor in the heap
            CD3DX12_CPU_DESCRIPTOR_HANDLE srvHandle(mD3D12.GetSRVHeap()->GetCPUDescriptorHandleForHeapStart(), item.mData.mRTVOffset);
            device->CreateShaderResourceView(buffer, &srvDesc, srvHandle);
        }, [&](auto& item) {});
    return result.mData.mRTVOffset;
}
int D3DResourceCache::GetUAV(ID3D12Resource* buffer,
    DXGI_FORMAT fmt, bool is3D, int arrayCount,
    int lockBits, int mipB, int mipC) {
    size_t hash = (size_t)buffer + mipB * 12341237 + mipC * 123412343 + 13567;
    const auto& result = mResourceViewCache.RequireItem(hash, 1, lockBits,
        [&](auto& item) {
            item.mData.mRTVOffset = mCBOffset;
            mCBOffset += mD3D12.GetDescriptorHandleSizeSRV();
        },
        [&](auto& item) {
            auto device = mD3D12.GetD3DDevice();
            D3D12_UNORDERED_ACCESS_VIEW_DESC uavDesc = {};
            uavDesc.Format = fmt;
            if (is3D) {
                uavDesc.ViewDimension = D3D12_UAV_DIMENSION_TEXTURE3D;
                uavDesc.Texture3D.MipSlice = mipB;
            }
            else if (arrayCount > 1) {
                uavDesc.ViewDimension = D3D12_UAV_DIMENSION_TEXTURE2DARRAY;
                uavDesc.Texture2DArray.MipSlice = mipB;
                uavDesc.Texture2DArray.ArraySize = 1;
            }
            else {
                uavDesc.ViewDimension = D3D12_UAV_DIMENSION_TEXTURE2D;
                uavDesc.Texture2D.MipSlice = mipB;
                uavDesc.Texture2D.PlaneSlice = 0;
            }
            // Get the CPU handle to the descriptor in the heap
            CD3DX12_CPU_DESCRIPTOR_HANDLE srvHandle(mD3D12.GetSRVHeap()->GetCPUDescriptorHandleForHeapStart(), item.mData.mRTVOffset);
            device->CreateUnorderedAccessView(buffer, nullptr, &uavDesc, srvHandle);
        }, [&](auto& item) {});
    return result.mData.mRTVOffset;
}
int D3DResourceCache::GetBufferUAV(ID3D12Resource* buffer, int arrayCount, int stride, D3D12_BUFFER_UAV_FLAGS flags, int lockBits) {
    size_t hash = (size_t)buffer + stride * 12341237 + arrayCount * 123412343 + 18767;
    const auto& result = mResourceViewCache.RequireItem(hash, 1, lockBits,
        [&](auto& item) {
            item.mData.mRTVOffset = mCBOffset;
            mCBOffset += mD3D12.GetDescriptorHandleSizeSRV();
        },
        [&](auto& item) {
            auto device = mD3D12.GetD3DDevice();
            D3D12_UNORDERED_ACCESS_VIEW_DESC uavDesc = {};
            uavDesc.Format = DXGI_FORMAT_UNKNOWN;
            uavDesc.ViewDimension = D3D12_UAV_DIMENSION_BUFFER;
            uavDesc.Buffer.NumElements = arrayCount - 1;
            uavDesc.Buffer.Flags = flags;
            uavDesc.Buffer.FirstElement = 1;
            uavDesc.Buffer.StructureByteStride = stride;
            uavDesc.Buffer.CounterOffsetInBytes = 0;// (stride * arrayCount - 4) & ~(D3D12_UAV_COUNTER_PLACEMENT_ALIGNMENT - 1);
            CD3DX12_CPU_DESCRIPTOR_HANDLE srvHandle(mD3D12.GetSRVHeap()->GetCPUDescriptorHandleForHeapStart(), item.mData.mRTVOffset);
            device->CreateUnorderedAccessView(buffer, buffer, &uavDesc, srvHandle);
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
        if (tex.GetAllowUnorderedAccess())
            textureDesc.Flags |= D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS;
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
void D3DResourceCache::RequireState(D3DBinding& buffer, const BufferLayout& binding, D3D12_RESOURCE_STATES state) {
    if (state == D3D12_RESOURCE_STATE_COMMON) {
        state =
            (binding.mUsage == BufferLayout::Usage::Index ? D3D12_RESOURCE_STATE_INDEX_BUFFER : D3D12_RESOURCE_STATE_VERTEX_AND_CONSTANT_BUFFER)
            | D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE | D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
    }
    if (buffer.mState == state) return;
    mBarrierStateManager.mDelayedBarriers.push_back({ CD3DX12_RESOURCE_BARRIER::Transition(buffer.mBuffer.Get(), buffer.mState, state), });
    buffer.mState = state;
}
void D3DResourceCache::FlushBarriers(ID3D12GraphicsCommandList* cmdList) {
    auto& delayedBarriers = mBarrierStateManager.mDelayedBarriers;
    if (delayedBarriers.empty()) return;
    cmdList->ResourceBarrier((UINT)delayedBarriers.size(), delayedBarriers.data());
    delayedBarriers.clear();
}
void D3DResourceCache::CopyBufferData(ID3D12GraphicsCommandList* cmdList, int lockBits, const BufferLayout& binding, D3DBinding& d3dBin, int itemSize, int byteOffset, int byteSize) {
    RequireState(d3dBin, binding, D3D12_RESOURCE_STATE_COPY_DEST);
    FlushBarriers(cmdList);

    int size = (byteSize + BufferAlignment) & ~BufferAlignment;
    // Map and fill the buffer data (via temporary upload buffer)
    ID3D12Resource* uploadBuffer = AllocateUploadBuffer(size, lockBits);
    D3D::FillBuffer(uploadBuffer, [&](uint8_t* data) { D3D::WriteBufferData(data, binding, itemSize, byteOffset, byteSize); });
    cmdList->CopyBufferRegion(d3dBin.mBuffer.Get(), byteOffset, uploadBuffer, 0, size);
    mStatistics.BufferWrite(size);
    d3dBin.mRevision = binding.mRevision;
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
            if (d3dBin.mRevision != binding.mRevision) {
                CopyBufferData(cmdList, lockBits, binding, d3dBin, itemSize, 0, binding.mSize);
            }
            RequireState(d3dBin, binding);
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
    allocator.mCmdAllocator->SetPrivateData(WKPDID_D3DDebugObjectName, 5, "CmdAl");
    mCommandAllocators.push_back(std::move(allocator));
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
D3D12_RENDER_TARGET_BLEND_DESC ToD3DBlend(BlendMode mode) {
    static D3D12_BLEND argMapping[] = {
        D3D12_BLEND_ZERO, D3D12_BLEND_ONE,
        D3D12_BLEND_SRC_COLOR, D3D12_BLEND_INV_SRC_COLOR, D3D12_BLEND_SRC_ALPHA, D3D12_BLEND_INV_SRC_ALPHA,
        D3D12_BLEND_DEST_COLOR, D3D12_BLEND_INV_DEST_COLOR, D3D12_BLEND_DEST_ALPHA, D3D12_BLEND_INV_DEST_ALPHA,
        D3D12_BLEND_SRC1_COLOR, D3D12_BLEND_INV_SRC1_COLOR, D3D12_BLEND_SRC1_ALPHA, D3D12_BLEND_INV_SRC1_ALPHA,
    };
    static D3D12_BLEND_OP opMapping[] = {
        D3D12_BLEND_OP_ADD, D3D12_BLEND_OP_SUBTRACT, D3D12_BLEND_OP_REV_SUBTRACT,
        D3D12_BLEND_OP_MIN, D3D12_BLEND_OP_MAX,
    };
    D3D12_RENDER_TARGET_BLEND_DESC blend = { };
    blend.BlendEnable = mode.GetIsOpaque() ? FALSE : TRUE;
    blend.SrcBlend = argMapping[mode.mSrcColorBlend];
    blend.DestBlend = argMapping[mode.mDestColorBlend];
    blend.SrcBlendAlpha = argMapping[mode.mSrcAlphaBlend];
    blend.DestBlendAlpha = argMapping[mode.mDestAlphaBlend];
    blend.BlendOp = opMapping[mode.mBlendColorOp];
    blend.BlendOpAlpha = opMapping[mode.mBlendAlphaOp];
    blend.RenderTargetWriteMask = 0x0f;
    return blend;
};
void CreateShaderBlob(const CompiledShader* shader, ComPtr<ID3DBlob>& blob) {
    D3DCreateBlob(shader->GetBinary().size(), &blob);
    memcpy(blob->GetBufferPointer(), shader->GetBinary().data(), shader->GetBinary().size());
};

// Ensure a material is ready to be rendererd by the GPU (with the specified vertex layout)
D3DResourceCache::D3DPipelineState* D3DResourceCache::RequirePipelineState(
    const ShaderStages& shaders,
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
    static Identifier indirectArgsName("INDIRECTARGS");
    static Identifier indirectCountName("INDIRECTINSTANCES");
    if (bindings[0]->mElements[0].mBindName == indirectArgsName) bindings = bindings.subspan(1);
    if (bindings[0]->mElements[0].mBindName == indirectCountName) bindings = bindings.subspan(1);
    for (auto* binding : bindings) {
        for (auto& el : binding->GetElements()) {
            hash = AppendHash(el.mBindName.mId + ((int)el.mBufferStride << 16) + ((int)el.mFormat << 8), hash);
        }
    }
    if (shaders.mMeshShader != nullptr) {
        hash = AppendHash(shaders.mMeshShader->GetBinaryHash(), hash);
        if (shaders.mAmplificationShader != nullptr)
            hash = AppendHash(shaders.mAmplificationShader->GetBinaryHash(), hash);
    }
    else {
        hash = AppendHash(shaders.mVertexShader->GetBinaryHash(), hash);
    }
    hash = AppendHash(shaders.mPixelShader->GetBinaryHash(), hash);

    auto pipelineState = GetOrCreatePipelineState(hash);
    while (pipelineState->mHash != hash) {
        assert(pipelineState->mHash == 0);

        pipelineState->mHash = hash;
        pipelineState->mRootSignature = &mRootSignature;

        ComPtr<ID3DBlob> ampBlob, meshBlob, vertBlob, pixBlob;
        if (shaders.mMeshShader != nullptr) {
            CreateShaderBlob(shaders.mMeshShader, meshBlob);
            if (shaders.mAmplificationShader != nullptr) {
                CreateShaderBlob(shaders.mAmplificationShader, ampBlob);
            }
        } else if (shaders.mVertexShader != nullptr) {
            CreateShaderBlob(shaders.mVertexShader, vertBlob);
        }
        CreateShaderBlob(shaders.mPixelShader, pixBlob);

        auto ToD3DStencilDesc = [](const DepthMode::StencilDesc& desc) {
            return D3D12_DEPTH_STENCILOP_DESC{
                .StencilFailOp = (D3D12_STENCIL_OP)desc.StecilFailOp,
                .StencilDepthFailOp = (D3D12_STENCIL_OP)desc.DepthFailOp,
                .StencilPassOp = (D3D12_STENCIL_OP)desc.PassOp,
                .StencilFunc = (D3D12_COMPARISON_FUNC)desc.Function,
            };
        };

        auto rasterizerState = CD3DX12_RASTERIZER_DESC(D3D12_DEFAULT);
        rasterizerState.CullMode = (D3D12_CULL_MODE)materialState.mRasterMode.mCullMode;
        auto blendState = CD3DX12_BLEND_DESC(D3D12_DEFAULT);
        blendState.RenderTarget[0] = ToD3DBlend(materialState.mBlendMode);
        auto depthStencilState = CD3DX12_DEPTH_STENCIL_DESC1(D3D12_DEFAULT);
        depthStencilState.DepthEnable = materialState.mDepthMode.GetDepthClip() || materialState.mDepthMode.GetDepthWrite();
        depthStencilState.DepthFunc = (D3D12_COMPARISON_FUNC)materialState.mDepthMode.mComparison;
        depthStencilState.DepthWriteMask = materialState.mDepthMode.GetDepthWrite() ? D3D12_DEPTH_WRITE_MASK_ALL : D3D12_DEPTH_WRITE_MASK_ZERO;
        depthStencilState.StencilEnable = materialState.mDepthMode.GetStencilEnable();
        if (depthStencilState.StencilEnable) {
            depthStencilState.StencilReadMask = materialState.mDepthMode.mStencilReadMask;
            depthStencilState.StencilWriteMask = materialState.mDepthMode.mStencilWriteMask;
            depthStencilState.FrontFace = ToD3DStencilDesc(materialState.mDepthMode.mStencilFront);
            depthStencilState.BackFace = ToD3DStencilDesc(materialState.mDepthMode.mStencilBack);
        }

        // Create the D3D pipeline
        auto device = mD3D12.GetD3DDevice();
        if (meshBlob != nullptr) {
            D3DX12_MESH_SHADER_PIPELINE_STATE_DESC psoDesc = {};
            psoDesc.pRootSignature = pipelineState->mRootSignature->mRootSignature.Get();
            if (ampBlob != nullptr) psoDesc.AS = CD3DX12_SHADER_BYTECODE(ampBlob.Get());
            psoDesc.MS = CD3DX12_SHADER_BYTECODE(meshBlob.Get());
            psoDesc.PS = CD3DX12_SHADER_BYTECODE(pixBlob.Get());
            psoDesc.RasterizerState = rasterizerState;
            psoDesc.BlendState = blendState;
            psoDesc.DepthStencilState = depthStencilState;
            psoDesc.NumRenderTargets = (uint32_t)frameBufferFormats.size();
            for (int f = 0; f < frameBufferFormats.size(); ++f)
                psoDesc.RTVFormats[f] = frameBufferFormats[f];
            psoDesc.DSVFormat = depthBufferFormat;
            psoDesc.SampleMask = DefaultSampleMask();
            psoDesc.SampleDesc = DefaultSampleDesc();
            auto meshStreamDesc = CD3DX12_PIPELINE_MESH_STATE_STREAM(psoDesc);
            D3D12_PIPELINE_STATE_STREAM_DESC streamDesc = {};
            streamDesc.SizeInBytes = sizeof(meshStreamDesc);
            streamDesc.pPipelineStateSubobjectStream = &meshStreamDesc;
            ThrowIfFailed(device->CreatePipelineState(&streamDesc, IID_PPV_ARGS(&pipelineState->mPipelineState)));
            pipelineState->mType = 1;
        }
        else {
            ComputeElementLayout(bindings, pipelineState->mInputElements);
            D3D12_GRAPHICS_PIPELINE_STATE_DESC psoDesc = {};
            psoDesc.InputLayout = { pipelineState->mInputElements.data(), (unsigned int)pipelineState->mInputElements.size() };
            psoDesc.pRootSignature = pipelineState->mRootSignature->mRootSignature.Get();
            psoDesc.VS = CD3DX12_SHADER_BYTECODE(vertBlob.Get());
            psoDesc.PS = CD3DX12_SHADER_BYTECODE(pixBlob.Get());
            psoDesc.RasterizerState = rasterizerState;
            psoDesc.BlendState = blendState;
            psoDesc.DepthStencilState = depthStencilState;
            psoDesc.PrimitiveTopologyType = D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE;
            psoDesc.NumRenderTargets = (uint32_t)frameBufferFormats.size();
            for (int f = 0; f < frameBufferFormats.size(); ++f)
                psoDesc.RTVFormats[f] = frameBufferFormats[f];
            psoDesc.DSVFormat = depthBufferFormat;
            psoDesc.SampleMask = DefaultSampleMask();
            psoDesc.SampleDesc = DefaultSampleDesc();
            ThrowIfFailed(device->CreateGraphicsPipelineState(&psoDesc, IID_PPV_ARGS(&pipelineState->mPipelineState)));
            pipelineState->mType = 0;
        }
        pipelineState->mPipelineState->SetName(shaders.mPixelShader->GetName().GetWName().c_str());

        // Collect constant buffers required by the shaders
        // TODO: Throw an error if different constant buffers
        // are required in the same bind point
        Identifier cbBinds[32] = { 0 };
        Identifier rbBinds[32] = { 0 };
        std::string errors;
        for (auto l : { shaders.mAmplificationShader, shaders.mMeshShader, shaders.mVertexShader, shaders.mPixelShader }) {
            if (l == nullptr) continue;
            uint64_t cbMask = 0;
            uint64_t rbMask = 0;
            for (auto& cb : l->GetReflection().mConstantBuffers) {
                if (std::any_of(pipelineState->mConstantBuffers.begin(), pipelineState->mConstantBuffers.end(),
                    [&](auto* o) { return *o == cb; })) continue;
                uint64_t mask = 1ull << cb.mBindPoint;
                if (cbMask & mask) throw "Two CBs occupy the same bind point";
                cbMask |= mask;
                if (cbBinds[cb.mBindPoint].IsValid() && cbBinds[cb.mBindPoint] != cb.mName)
                    errors += "CB Collision " + cbBinds[cb.mBindPoint].GetName() + " and " + cb.mName.GetName() + "\n";
                cbBinds[cb.mBindPoint] = cb.mName;
                pipelineState->mConstantBuffers.push_back(&cb);
            }
            for (auto& rb : l->GetReflection().mResourceBindings) {
                if (std::any_of(pipelineState->mResourceBindings.begin(), pipelineState->mResourceBindings.end(),
                    [&](auto* o) { return *o == rb; })) continue;
                uint64_t mask = 1ull << rb.mBindPoint;
                if (rbMask & mask) throw "Two Res occupy the same bind point";
                rbMask |= mask;
                if (rbBinds[rb.mBindPoint].IsValid() && rbBinds[rb.mBindPoint] != rb.mName)
                    errors += "RB Collision " + rbBinds[rb.mBindPoint].GetName() + " and " + rb.mName.GetName() + "\n";
                rbBinds[rb.mBindPoint] = rb.mName;
                pipelineState->mResourceBindings.push_back(&rb);
            }
        }
        if (!errors.empty()) {
            MessageBoxA(0, errors.c_str(), "Binding collision", 0);
        }
        break;
    }
    return pipelineState;
}
D3DResourceCache::D3DPipelineState* D3DResourceCache::RequireComputePSO(const CompiledShader& shader) {
    size_t hash = GenericHash(shader.GetBinaryHash());
    auto pipelineState = GetOrCreatePipelineState(hash);
    while (pipelineState->mHash != hash) {
        assert(pipelineState->mHash == 0);

        pipelineState->mHash = hash;
        pipelineState->mRootSignature = &mComputeRootSignature;

        ComPtr<ID3DBlob> computeBlob;
        CreateShaderBlob(&shader, computeBlob);
        D3D12_COMPUTE_PIPELINE_STATE_DESC psoDesc = {};
        psoDesc.pRootSignature = pipelineState->mRootSignature->mRootSignature.Get();
        psoDesc.CS = CD3DX12_SHADER_BYTECODE(computeBlob.Get());
        auto device = mD3D12.GetD3DDevice();
        device->CreateComputePipelineState(&psoDesc, IID_PPV_ARGS(&pipelineState->mPipelineState));
        pipelineState->mType = 3;

        std::string errors;

        uint64_t cbMask = 0;
        uint64_t rbMask = 0;
        const ShaderBase::ShaderReflection& reflection = shader.GetReflection();
        for (auto& cb : reflection.mConstantBuffers) {
            if (std::any_of(pipelineState->mConstantBuffers.begin(), pipelineState->mConstantBuffers.end(),
                [&](auto* o) { return *o == cb; })) continue;
            uint64_t mask = 1ull << cb.mBindPoint;
            assert((cbMask & mask) == 0);
            cbMask |= mask;
            pipelineState->mConstantBuffers.push_back(&cb);
        }
        for (auto& rb : reflection.mResourceBindings) {
            if (std::any_of(pipelineState->mResourceBindings.begin(), pipelineState->mResourceBindings.end(),
                [&](auto* o) { return *o == rb; })) continue;
            uint64_t mask = 1ull << rb.mBindPoint;
            //assert((rbMask & mask) == 0);
            rbMask |= mask;
            pipelineState->mResourceBindings.push_back(&rb);
        }
        if (!errors.empty()) {
            MessageBoxA(0, errors.c_str(), "Binding collision", 0);
        }
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
    swapChainDesc.Width = mResolution.x;
    swapChainDesc.Height = mResolution.y;
    swapChainDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    swapChainDesc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    swapChainDesc.BufferCount = FrameCount;
    swapChainDesc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;
    swapChainDesc.SampleDesc = DefaultSampleDesc();

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
    auto hr = mSwapChain->Present(0, mDenyPresentRef > 0 ? DXGI_PRESENT_DO_NOT_SEQUENCE : 0);

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
        mDevice.CheckDeviceState();
        ThrowIfFailed(mFence->SetEventOnCompletion(mFenceValues[mBackBufferIndex], mFenceEvent));
        DWORD waitResult = WaitForSingleObjectEx(mFenceEvent, 1000, FALSE);
        if (waitResult == WAIT_TIMEOUT) {
            throw "Frame did not complete in time";
        }
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
