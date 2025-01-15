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
#include <sstream>

extern void* SimpleProfilerMarker(const char* name);
extern void SimpleProfilerMarkerEnd(void* zone);

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
}
D3D12_DEPTH_STENCILOP_DESC ToD3DStencilDesc(const DepthMode::StencilDesc& desc) {
    return D3D12_DEPTH_STENCILOP_DESC{
        .StencilFailOp = (D3D12_STENCIL_OP)desc.StecilFailOp,
        .StencilDepthFailOp = (D3D12_STENCIL_OP)desc.DepthFailOp,
        .StencilPassOp = (D3D12_STENCIL_OP)desc.PassOp,
        .StencilFunc = (D3D12_COMPARISON_FUNC)desc.Function,
    };
}
CD3DX12_DEPTH_STENCIL_DESC1 ToD3DDepthStencil(MaterialState materialState) {
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
    return depthStencilState;
}
void CreateShaderBlob(const CompiledShader* shader, ComPtr<ID3DBlob>& blob) {
    D3DCreateBlob(shader->GetBinary().size(), &blob);
    memcpy(blob->GetBufferPointer(), shader->GetBinary().data(), shader->GetBinary().size());
}


D3DResourceCache::D3DBinding& RequireBinding(const BufferLayout& binding, std::unordered_map<size_t, std::unique_ptr<D3DResourceCache::D3DBinding>>& bindingMap) {
    auto d3dBinIt = bindingMap.find(binding.mIdentifier);
    if (d3dBinIt == bindingMap.end()) {
        d3dBinIt = bindingMap.emplace(std::make_pair(binding.mIdentifier, std::make_unique<D3DResourceCache::D3DBinding>())).first;
    }
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
    if (d3dBin.mCount != binding.mCount || d3dBin.mStride != itemSize) {
        d3dBin.mCount = binding.mCount;
        d3dBin.mStride = itemSize;
        d3dBin.mSRVOffset |= 0x80000000;    // Mark invalid, to be removed later
    }
}
template<class Fn1, class Fn2, class Fn3, class Fn4>
void ProcessBindings(std::span<const BufferLayout*> bindings, std::unordered_map<size_t, std::unique_ptr<D3DResourceCache::D3DBinding>>& bindingMap,
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

    D3D12_FEATURE_DATA_ROOT_SIGNATURE featureData = { .HighestVersion = D3D_ROOT_SIGNATURE_VERSION_1_1 };
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
        CD3DX12_STATIC_SAMPLER_DESC(7, D3D12_FILTER_MIN_MAG_MIP_POINT, D3D12_TEXTURE_ADDRESS_MODE_CLAMP, D3D12_TEXTURE_ADDRESS_MODE_CLAMP, D3D12_TEXTURE_ADDRESS_MODE_CLAMP),
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
        mComputeRootSignature.mSRVCount = 5;
        mComputeRootSignature.mUAVCount = 5;
        mComputeRootSignature.mNumResources
            = mComputeRootSignature.mSRVCount + mComputeRootSignature.mUAVCount;

        CD3DX12_ROOT_PARAMETER1 rootParameters[14];
        CD3DX12_DESCRIPTOR_RANGE1 srvR[10];
        int rootParamId = 0;
        for (int i = 0; i < mComputeRootSignature.mNumConstantBuffers; ++i)
            rootParameters[rootParamId++].InitAsConstantBufferView(i);
        int descId = 0;
        for (int i = 0; i < mComputeRootSignature.mSRVCount; ++i, ++descId) {
            srvR[descId] = CD3DX12_DESCRIPTOR_RANGE1(D3D12_DESCRIPTOR_RANGE_TYPE_SRV, 1, i);
            rootParameters[rootParamId++].InitAsDescriptorTable(1, &srvR[descId]);
        }
        for (int i = 0; i < mComputeRootSignature.mUAVCount; ++i, ++descId) {
            srvR[descId] = CD3DX12_DESCRIPTOR_RANGE1(D3D12_DESCRIPTOR_RANGE_TYPE_UAV, 1, i);
            rootParameters[rootParamId++].InitAsDescriptorTable(1, &srvR[descId]);
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
    return pipelineMapping.GetOrCreate(hash);
}
D3DResourceCache::D3DRenderSurface* D3DResourceCache::RequireD3DRT(const RenderTarget2D* rt) {
    bool wasCreated;
    auto* d3dTex = rtMapping.GetOrCreate(rt, wasCreated);
    if (wasCreated) RequireBarrierHandle(d3dTex);
    return d3dTex;
}
void D3DResourceCache::DestroyD3DRT(const RenderTarget2D* rt, LockMask lockBits) {
    auto* d3drt = rtMapping.mMap.find(rt)->second.get();
    auto* d3dBuffer = d3drt->mBuffer.Get();
    LockMask inflightFrames = lockBits;
    inflightFrames |= CheckInflightFrames();
    mResourceViewCache.RemoveIf([=](auto& item) { return item->mResource == d3dBuffer; }, inflightFrames);
    mTargetViewCache.RemoveIf([=](auto& item) { return item->mResource == d3dBuffer; }, inflightFrames);
    d3drt->mSRVOffset = -1;
    if (inflightFrames != 0)
        DelayResourceDispose(d3drt->mBuffer, inflightFrames);
    d3drt->mBuffer = nullptr;
    d3drt->mFormat = (DXGI_FORMAT)(-1);
    rtMapping.Delete(rt);
}
void D3DResourceCache::PurgeSRVs(LockMask lockBits) {
    {
        std::scoped_lock lock(mResourceMutex);
        mResourceViewCache.DetachAll();
        mTargetViewCache.DetachAll();
    }
    std::scoped_lock lock(rtMapping.mMutex);
    LockMask inflightFrames = lockBits;
    inflightFrames |= CheckInflightFrames();
    mResourceViewCache.Substitute(0x80000000, inflightFrames);
    mTargetViewCache.Substitute(0x80000000, inflightFrames);
    //mResourceViewCache.ForAll([](auto& item) { item.mData.mResource = nullptr; });
    //mTargetViewCache.ForAll([](auto& item) { item.mData.mResource = nullptr; });
    // Must clear SRVs because all RTVs are detached and have persistent lock cleared
    // (will be reused once other locks clear)
    for (auto& buffer : textureMapping.mMap) buffer.second->mSRVOffset = -1;
    for (auto& buffer : rtMapping.mMap) buffer.second->mSRVOffset = -1;
    for (auto& buffer : mBindings) buffer.second->mSRVOffset = -1;
}
void D3DResourceCache::SetRenderTargetMapping(const RenderTarget2D* rt, const D3DResourceCache::D3DRenderSurface& surface) {
    assert(surface.mSRVOffset == -1);
    auto* slot = RequireD3DRT(rt);
    slot->mBuffer = surface.mBuffer;
    slot->mRevision = surface.mRevision;
    slot->mSRVOffset = surface.mSRVOffset;
    slot->mFormat = surface.mFormat;
    slot->mDesc = surface.mDesc;
}
bool D3DResourceCache::RequireBuffer(const BufferLayout& binding, D3DBinding& d3dBin, LockMask lockBits) {
    if (d3dBin.mBuffer != nullptr && d3dBin.mSize >= binding.mSize) return false;
    // Buffer already valid, register buffer to be destroyed in the future
    if (d3dBin.mBuffer != nullptr) {
        // TODO: Remove 0x8000...0 lock from this
        if (d3dBin.mSRVOffset >= 0) d3dBin.mSRVOffset |= 0x80000000;
        if (d3dBin.mSRVOffset < -1) ClearBufferSRV(d3dBin, lockBits);
        // TODO: Should use lockbits from previous used frames
        mDelayedRelease.InsertItem(d3dBin.mBuffer, 0, lockBits);
        d3dBin.mBuffer = nullptr;
    }
    d3dBin.mSize = (binding.mSize + BufferAlignment) & ~BufferAlignment;
    assert(d3dBin.mSize > 0);
    assert(d3dBin.mSRVOffset == -1);

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
    mStatistics.mBufferCreates++;
    return true;
}
// Retrieve a buffer capable of upload/copy that will be vaild until
// the frame completes rendering
ID3D12Resource* D3DResourceCache::AllocateUploadBuffer(size_t size, LockMask lockBits) {
    int index;
    return AllocateUploadBuffer(size, lockBits, index);
}
ID3D12Resource* D3DResourceCache::AllocateUploadBuffer(size_t size, LockMask lockBits, int& itemIndex) {
    size = (size + BufferAlignment) & (~BufferAlignment);
    auto& resultItem = mUploadBufferCache.RequireItem(size, lockBits,
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
        [&](auto& item) {
            assert(item.mData.Get() != nullptr);
        },
        [&](int index) { itemIndex = index; }
    );
    return resultItem.mData.Get();
}
ID3D12Resource* D3DResourceCache::AllocateReadbackBuffer(size_t size, LockMask lockBits) {
    size = (size + BufferAlignment) & (~BufferAlignment);
    auto& resultItem = mReadbackBufferCache.RequireItem(size, lockBits,
        [&](auto& item) { // Allocate a new item
            auto readbackBufferDesc = CD3DX12_RESOURCE_DESC::Buffer(item.mLayoutHash);
            ThrowIfFailed(mD3D12.GetD3DDevice()->CreateCommittedResource(
                &D3D::ReadbackHeap,
                D3D12_HEAP_FLAG_NONE,
                &readbackBufferDesc,
                D3D12_RESOURCE_STATE_COPY_DEST,
                nullptr,
                IID_PPV_ARGS(&item.mData.mResource)
            ));
            item.mData.mResource->SetName(L"ReadbackBuffer");
        },
        [&](auto& item) {}
    );
    assert(resultItem.mData.mResource.Get() != nullptr);
    return resultItem.mData.mResource.Get();
}
D3DResourceCache::D3DBinding* D3DResourceCache::GetBinding(uint64_t bindingIdentifier) {
    auto d3dBinIt = mBindings.find(bindingIdentifier);
    if (d3dBinIt == mBindings.end()) return nullptr;
    return d3dBinIt->second.get();
}
D3DResourceCache::D3DBinding& D3DResourceCache::RequireBinding(const BufferLayout& binding) {
    return ::RequireBinding(binding, mBindings);
}
void D3DResourceCache::UpdateBufferData(D3DCommandContext& cmdList, const BufferLayout& binding, std::span<const RangeInt> ranges) {
    auto& d3dBin = RequireBinding(binding);
    bool fullRefresh = false;
    if (RequireBuffer(binding, d3dBin, cmdList.mLockBits) && binding.mRevision != -1) {
        fullRefresh = true;
    }
    // Special case - update full buffer if revision mismatch
    if (ranges.size() == 1 && ranges[0].start == -1) {
        if (d3dBin.mRevision == binding.mRevision) return;
        fullRefresh = true;
    }
    d3dBin.mRevision = binding.mRevision;
    if (fullRefresh) {
        ProcessBindings(binding, d3dBin,
            [&](const BufferLayout& binding, D3DBinding& d3dBin, int itemSize) {
                // Special case - if mData is null dont copy
                // TODO: Support multiple null elements?
                if (binding.mElementCount == 1 && binding.mElements->mData == nullptr)
                    return;
                CopyBufferData(cmdList, binding, d3dBin, itemSize, 0, binding.mSize);
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
            ID3D12Resource* uploadBuffer = AllocateUploadBuffer(totalCount, cmdList.mLockBits);
            UINT8* mappedData;
            CD3DX12_RANGE readRange(0, 0);
            ThrowIfFailed(uploadBuffer->Map(0, &readRange, (void**)&mappedData));
            int it = 0;
            for (auto& range : ranges) {
                D3D::WriteBufferData(mappedData + it, binding, itemSize, range.start, range.length);
                it += range.length;
            }
            uploadBuffer->Unmap(0, nullptr);
            RequireState(cmdList, d3dBin, binding, D3D12_RESOURCE_STATE_COPY_DEST);
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
}
void D3DResourceCache::CopyBufferData(D3DCommandContext& cmdList, const BufferLayout& source, const BufferLayout& dest, int srcOffset, int dstOffset, int length) {
    auto& srcBinding = RequireBinding(source);
    auto& dstBinding = RequireBinding(dest);
    RequireState(cmdList, srcBinding, source, D3D12_RESOURCE_STATE_COPY_SOURCE);
    RequireState(cmdList, dstBinding, dest, D3D12_RESOURCE_STATE_COPY_DEST);
    FlushBarriers(cmdList);
    cmdList->CopyBufferRegion(dstBinding.mBuffer.Get(), dstOffset, srcBinding.mBuffer.Get(), srcOffset, length);
}

ID3D12Resource* D3DResourceCache::CreateReadback(D3DCommandContext& cmdList, const D3DRenderSurface& surface) {
    auto* renderTargetResource = surface.mBuffer.Get();

    auto desc = surface.mBuffer->GetDesc();
    D3D12_PLACED_SUBRESOURCE_FOOTPRINT footprint;
    UINT numRows[1];
    UINT64 rowSizes[1];
    UINT64 requiredSize = 0;
    mD3D12.GetD3DDevice()->GetCopyableFootprints(&desc, 0, 1, 0,
        &footprint, numRows, rowSizes, &requiredSize);

    D3D12_TEXTURE_COPY_LOCATION dest = CD3DX12_TEXTURE_COPY_LOCATION(
        AllocateReadbackBuffer(footprint.Footprint.RowPitch * footprint.Footprint.Height, cmdList.mLockBits),
        footprint
    );

    D3D12_TEXTURE_COPY_LOCATION src = CD3DX12_TEXTURE_COPY_LOCATION(renderTargetResource, 0);

    cmdList.mBarrierStateManager->SetResourceState(
        surface.mBuffer.Get(), surface.mBarrierHandle,
        -1, D3D12_RESOURCE_STATE_COPY_SOURCE, surface.mDesc);
    FlushBarriers(cmdList);

    cmdList->CopyTextureRegion(&dest, 0, 0, 0, &src, nullptr);
    return dest.pResource;
}
D3DResourceCache::D3DReadback* D3DResourceCache::GetReadback(ID3D12Resource* resource, LockMask& outLockHandle) {
    auto all = mReadbackBufferCache.GetAllActive();
    for (auto it = all.begin(); it != all.end(); ++it) {
        if (it->mResource.Get() == resource) {
            outLockHandle = it.GetLockHandle();
            return &*it;
        }
    }
    return nullptr;
 }
int D3DResourceCache::GetReadbackState(ID3D12Resource* resource) {
    LockMask lockHandle;
    auto item = GetReadback(resource, lockHandle);
    if (lockHandle != (1ull << 63)) return -1;
    auto desc = item->mResource->GetDesc();
    return (int)desc.Width;
}
int D3DResourceCache::CopyAndDisposeReadback(ID3D12Resource* resource, std::span<uint8_t> dest) {
    LockMask lockHandle;
    auto item = GetReadback(resource, lockHandle);
    auto desc = resource->GetDesc();

    void* pData;
    D3D12_RANGE readRange = { 0, (SIZE_T)desc.Width };
    ThrowIfFailed(resource->Map(0, &readRange, &pData));
    std::memcpy(dest.data(), pData, std::min(dest.size(), desc.Width));
    resource->Unmap(0, nullptr);

    auto all = mReadbackBufferCache.GetAllActive();
    auto match = std::find_if(all.begin(), all.end(), [&](auto& item) { return item.mResource.Get() == resource; });
    if (match != all.end()) match.Delete();
    return 1;
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
    LockMask lockBits, int mipB, int mipC) {
    assert(buffer != nullptr);
    size_t hash = (size_t)buffer + mipB * 12341237 + mipC * 123412343;
    const auto& result = mResourceViewCache.RequireItem(hash, 1, lockBits,
        [&](auto& item) {
            item.mData.mSRVOffset = mCBOffset.fetch_add(mD3D12.GetDescriptorHandleSizeSRV());
        },
        [&](auto& item) {
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
            CD3DX12_CPU_DESCRIPTOR_HANDLE srvHandle(mD3D12.GetSRVHeap()->GetCPUDescriptorHandleForHeapStart(), item.mData.mSRVOffset);
            mD3D12.GetD3DDevice()->CreateShaderResourceView(buffer, &srvDesc, srvHandle);
            item.mData.mResource = buffer;
            item.mData.mLastUse = srvDesc;
        }, [&](auto& item) {
            assert(item.mData.mResource == buffer);
        });
    return result.mData.mSRVOffset;
}
int D3DResourceCache::GetBufferSRV(D3DBinding& d3dBin, int offset, int count, int stride, LockMask lockBits) {
    bool isFullRange = offset == 0 && count == d3dBin.mCount;
    if (isFullRange) {
        if (d3dBin.mSRVOffset >= 0) return d3dBin.mSRVOffset;
        if (d3dBin.mSRVOffset < -1) ClearBufferSRV(d3dBin, lockBits);
        lockBits |= 0x80000000;
    }
    auto buffer = d3dBin.mBuffer.Get();
    size_t hash = (size_t)buffer + offset * 123412343 + count * 12341237 + stride * 12345;
    const auto& result = mResourceViewCache.RequireItem(hash, 2, lockBits,
        [&](auto& item) {
            item.mData.mSRVOffset = mCBOffset.fetch_add(mD3D12.GetDescriptorHandleSizeSRV());
        },
        [&](auto& item) {
            assert(count < 10000000);
            // Create a shader resource view (SRV) for the texture
            D3D12_SHADER_RESOURCE_VIEW_DESC srvDesc = {
                .Format = DXGI_FORMAT_UNKNOWN, .ViewDimension = D3D12_SRV_DIMENSION_BUFFER,
                .Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING,
                .Buffer = {
                    .FirstElement = (UINT)offset, .NumElements = (UINT)count,
                    .StructureByteStride = (UINT)stride, .Flags = D3D12_BUFFER_SRV_FLAG_NONE,
                },
            };

            // Get the CPU handle to the descriptor in the heap
            CD3DX12_CPU_DESCRIPTOR_HANDLE srvHandle(mD3D12.GetSRVHeap()->GetCPUDescriptorHandleForHeapStart(), item.mData.mSRVOffset);
            mD3D12.GetD3DDevice()->CreateShaderResourceView(buffer, &srvDesc, srvHandle);
            item.mData.mResource = buffer;
            item.mData.mLastUse = srvDesc;
        }, [&](auto& item) {
            assert(item.mData.mResource == buffer);
        });
    if (isFullRange) d3dBin.mSRVOffset = result.mData.mSRVOffset;
    return result.mData.mSRVOffset;
}
int D3DResourceCache::GetUAV(ID3D12Resource* buffer,
    DXGI_FORMAT fmt, bool is3D, int arrayCount,
    LockMask lockBits, int mipB, int mipC) {
    size_t hash = (size_t)buffer + mipB * 12341237 + mipC * 123412343;
    const auto& result = mResourceViewCache.RequireItem(hash, 3, lockBits,
        [&](auto& item) {
            item.mData.mSRVOffset = mCBOffset.fetch_add(mD3D12.GetDescriptorHandleSizeSRV());
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
            }
            // Get the CPU handle to the descriptor in the heap
            CD3DX12_CPU_DESCRIPTOR_HANDLE srvHandle(mD3D12.GetSRVHeap()->GetCPUDescriptorHandleForHeapStart(), item.mData.mSRVOffset);
            device->CreateUnorderedAccessView(buffer, nullptr, &uavDesc, srvHandle);
            item.mData.mResource = buffer;
            item.mData.mLastUse = {};
        }, [&](auto& item) {
            assert(item.mData.mResource == buffer);
        });
    return result.mData.mSRVOffset;
}
int D3DResourceCache::GetBufferUAV(ID3D12Resource* buffer, int arrayCount, int stride, D3D12_BUFFER_UAV_FLAGS flags, LockMask lockBits) {
    size_t hash = (size_t)buffer + stride * 12341237 + arrayCount * 123412343 + 18767;
    const auto& result = mResourceViewCache.RequireItem(hash, 4, lockBits,
        [&](auto& item) {
            item.mData.mSRVOffset = mCBOffset.fetch_add(mD3D12.GetDescriptorHandleSizeSRV());
        },
        [&](auto& item) {
            auto device = mD3D12.GetD3DDevice();
            D3D12_UNORDERED_ACCESS_VIEW_DESC uavDesc = {
                .Format = DXGI_FORMAT_UNKNOWN, .ViewDimension = D3D12_UAV_DIMENSION_BUFFER,
                .Buffer = {
                    .FirstElement = 1, .NumElements = (UINT)(arrayCount - 1),
                    .StructureByteStride = (UINT)stride, .CounterOffsetInBytes = 0, .Flags = flags
                },
            };
            CD3DX12_CPU_DESCRIPTOR_HANDLE srvHandle(mD3D12.GetSRVHeap()->GetCPUDescriptorHandleForHeapStart(), item.mData.mSRVOffset);
            device->CreateUnorderedAccessView(buffer, buffer, &uavDesc, srvHandle);
            item.mData.mResource = buffer;
            item.mData.mLastUse = {};
        }, [&](auto& item) {
            assert(item.mData.mResource == buffer);
        });
    return result.mData.mSRVOffset;
}
void D3DResourceCache::RequireBarrierHandle(D3DTexture* d3dTex) {
    if (d3dTex->mBarrierHandle == D3D::BarrierHandle::Invalid) {
        d3dTex->mBarrierHandle = mLastBarrierId++;
    }
}
void D3DResourceCache::UpdateTextureData(D3DTexture* d3dTex, const Texture& tex, D3DCommandContext& cmdList) {
    auto updateTextureZone = SimpleProfilerMarker("Update Texture");
    auto device = mD3D12.GetD3DDevice();
    auto size = tex.GetSize();

    auto bitsPerPixel = BufferFormatType::GetBitSize(tex.GetBufferFormat());
    assert(bitsPerPixel > 0);
    if (d3dTex->mBarrierHandle != D3D::BarrierHandle::Invalid) throw "Not implemented!";

    // Get d3d cache instance
    if (d3dTex->mBuffer == nullptr) {
        static std::mutex texMutex;
        std::scoped_lock lock(texMutex);
        if (d3dTex->mBuffer != nullptr) return;
        assert(d3dTex->mSRVOffset == -1);
        auto textureDesc = GetTextureDesc(tex);
        if (tex.GetAllowUnorderedAccess())
            textureDesc.Flags |= D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS;
        ComPtr<ID3D12Resource> buffer;
        ThrowIfFailed(device->CreateCommittedResource(
            &D3D::DefaultHeap,
            D3D12_HEAP_FLAG_NONE,
            &textureDesc,
            D3D12_RESOURCE_STATE_COPY_DEST,
            nullptr,
            IID_PPV_ARGS(&buffer)
        ));
        buffer->SetName(tex.GetName().c_str());
        d3dTex->mFormat = textureDesc.Format;
        assert(d3dTex->mBuffer == nullptr);
        d3dTex->mBuffer = buffer;
        assert(d3dTex->mSRVOffset == -1);
    } else {
        // Put the texture in write mode
        auto beginWrite = CD3DX12_RESOURCE_BARRIER::Transition(d3dTex->mBuffer.Get(), D3D12_RESOURCE_STATE_COMMON, D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES);
        cmdList->ResourceBarrier(1, &beginWrite);
    }

    auto uploadSize = (GetRequiredIntermediateSize(d3dTex->mBuffer.Get(), 0, 1) + D3D12_DEFAULT_RESOURCE_PLACEMENT_ALIGNMENT - 1) & ~(D3D12_DEFAULT_RESOURCE_PLACEMENT_ALIGNMENT - 1);
    auto blockSize = BufferFormatType::GetCompressedBlockSize(tex.GetBufferFormat());
    if (blockSize < 0) blockSize = 1;
    auto blockBytes = bitsPerPixel * blockSize * blockSize / 8;

    for (int i = 0; i < tex.GetArrayCount(); ++i) {
        for (int m = 0; m < tex.GetMipCount(); ++m) {
            auto res = tex.GetMipResolution(size, tex.GetBufferFormat(), m);
            auto srcData = tex.GetData(m, i);
            D3D12_SUBRESOURCE_DATA textureData = {};
            textureData.pData = reinterpret_cast<const UINT8*>(srcData.data());
            textureData.RowPitch = blockBytes * res.x / blockSize;
            textureData.SlicePitch = textureData.RowPitch * res.y / blockSize;
            auto uploadBuffer = AllocateUploadBuffer(uploadSize, cmdList.mLockBits);
            UpdateSubresources<1>(cmdList, d3dTex->mBuffer.Get(), uploadBuffer, 0,
                D3D12CalcSubresource(m, i, 0, tex.GetMipCount(), tex.GetArrayCount()), 1,
                &textureData);
            mStatistics.BufferWrite(uploadSize);
        }
    }

    cmdList.mBarrierStateManager->mDelayedBarriers.push_back(
        CD3DX12_RESOURCE_BARRIER::Transition(d3dTex->mBuffer.Get(), D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_COMMON, D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES)
    );

    d3dTex->mRevision = tex.GetRevision();
    SimpleProfilerMarkerEnd(updateTextureZone);
}
Texture* D3DResourceCache::RequireDefaultTexture() {
    if (mDefaultTexture == nullptr) {
        std::scoped_lock lock(mResourceMutex);
        if (mDefaultTexture == nullptr) {
            mDefaultTexture = std::make_shared<Texture>(Int3(4, 4, 1));
            auto data = mDefaultTexture->GetRawData();
            std::fill((uint32_t*)&*data.begin(), (uint32_t*)(&*data.begin() + data.size()), 0xffe0e0e0);
            mDefaultTexture->MarkChanged();
        }
    }
    return mDefaultTexture.get();;
}
D3DResourceCache::D3DTexture* D3DResourceCache::RequireTexture(const Texture* texture, D3DCommandContext& cmdList) {
    return textureMapping.GetOrCreate(texture);
}
D3DResourceCache::D3DTexture* D3DResourceCache::RequireCurrentTexture(const Texture* texture, D3DCommandContext& cmdList) {
    auto d3dTex = RequireTexture(texture, cmdList);
    if (d3dTex->mRevision != texture->GetRevision())
        UpdateTextureData(d3dTex, *texture, cmdList);
    return d3dTex;
}
void D3DResourceCache::RequireState(D3DCommandContext& cmdList, D3DBinding& buffer, const BufferLayout& binding, D3D12_RESOURCE_STATES state) {
    if (buffer.mState == state) return;
    cmdList.mBarrierStateManager->mDelayedBarriers.push_back({ CD3DX12_RESOURCE_BARRIER::Transition(buffer.mBuffer.Get(), buffer.mState, state), });
    buffer.mState = state;
}
void D3DResourceCache::FlushBarriers(D3DCommandContext& cmdList) {
    auto& delayedBarriers = cmdList.mBarrierStateManager->mDelayedBarriers;
    if (delayedBarriers.empty()) return;
    cmdList->ResourceBarrier((UINT)delayedBarriers.size(), delayedBarriers.data());
    delayedBarriers.clear();
}
void D3DResourceCache::DelayResourceDispose(const ComPtr<ID3D12Resource>& resource, LockMask lockBits) {
    assert(lockBits != 0);  // Cant delay if no references to wait for
    mDelayedRelease.InsertItem(resource, 0, lockBits);
}
void D3DResourceCache::CopyBufferData(D3DCommandContext& cmdList, const BufferLayout& binding, D3DBinding& d3dBin, int itemSize, int byteOffset, int byteSize) {
    RequireState(cmdList, d3dBin, binding, D3D12_RESOURCE_STATE_COPY_DEST);
    FlushBarriers(cmdList);

    int size = (byteSize + BufferAlignment) & ~BufferAlignment;
    // Map and fill the buffer data (via temporary upload buffer)
    ID3D12Resource* uploadBuffer = AllocateUploadBuffer(size, cmdList.mLockBits);
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
    D3DCommandContext& cmdList,
    std::vector<D3D12_VERTEX_BUFFER_VIEW>& inputViews,
    D3D12_INDEX_BUFFER_VIEW& indexView, int& indexCount)
{
    indexCount = -1;
    ProcessBindings(bindings, mBindings,
        [&](const BufferLayout& binding, D3DBinding& d3dBin, int itemSize) {
            RequireBuffer(binding, d3dBin, cmdList.mLockBits);
            if (d3dBin.mRevision != binding.mRevision) {
                CopyBufferData(cmdList, binding, d3dBin, itemSize, 0, binding.mSize);
            }
            RequireState(cmdList, d3dBin, binding,
                binding.mUsage == BufferLayout::Usage::Index ? D3D12_RESOURCE_STATE_INDEX_BUFFER : D3D12_RESOURCE_STATE_VERTEX_AND_CONSTANT_BUFFER);
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
void D3DResourceCache::PushAllocator(D3DAllocatorHandle& handle) {
    auto& cmdAllocator = *mCommandAllocators[handle.mAllocatorId];
    std::atomic_ref<UINT64> fenceValue(cmdAllocator.mFenceValue);
    handle.mFenceValue = ++fenceValue;
    ThrowIfFailed(mD3D12.GetCmdQueue()->Signal(cmdAllocator.mFence.Get(), handle.mFenceValue));
}
int D3DResourceCache::AwaitAllocator(D3DAllocatorHandle handle) {
    if (handle.mAllocatorId < 0) return -1;
    auto& cmdAllocator = *mCommandAllocators[handle.mAllocatorId];
    // If the next frame is not ready to be rendered yet, wait until it is ready.
    while (true) {
        //if (cmdAllocator.mLockFrame >= handle.mFenceValue) break;
        auto fenceVal = cmdAllocator.GetLockFrame();
        if (fenceVal >= handle.mFenceValue) break;
        mD3D12.CheckDeviceState();
        ThrowIfFailed(cmdAllocator.mFence->SetEventOnCompletion(handle.mFenceValue, cmdAllocator.mFenceEvent));
        DWORD waitResult = WaitForSingleObjectEx(cmdAllocator.mFenceEvent, 1000, FALSE);
        if (waitResult != WAIT_TIMEOUT) break;
        OutputDebugStringA("Frame did not complete in time\n");
    }
    return 0;
}
void D3DResourceCache::ClearAllocator(D3DAllocatorHandle handle) {
    if (handle.mAllocatorId < 0) return;
    auto& cmdAllocator = *mCommandAllocators[handle.mAllocatorId];
    if (cmdAllocator.GetHeadFrame() != handle.mFenceValue) return;
    cmdAllocator.mCmdAllocator->Reset();
}
D3DAllocatorHandle D3DResourceCache::GetFirstBusyAllocator() {
    if (CheckInflightFrames()) {
        for (auto& allocator : mCommandAllocators) {
            if (allocator->HasLockedFrames()) return allocator->CreateWaitHandle();
        }
    }
    return D3DAllocatorHandle();
}
D3DResourceCache::CommandAllocator* D3DResourceCache::RequireAllocator() {
    CheckInflightFrames();
    for (auto& allocator : mCommandAllocators) {
        auto* allocatorPtr = allocator.get();
        if (allocator->HasLockedFrames()) continue;
        auto oldFenceValue = allocatorPtr->mLockFrame;
        std::atomic_ref<UINT64> fenceValue(allocatorPtr->mFenceValue);
        if (!fenceValue.compare_exchange_strong(oldFenceValue, oldFenceValue + 1)) continue;
        return allocatorPtr;
    }
    if (mCommandAllocators.size() >= 64) {
        throw "Too many allocators!";
    }
    std::shared_ptr<CommandAllocator> allocator = std::make_shared<CommandAllocator>();
    ThrowIfFailed(mD3D12.GetD3DDevice()->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&allocator->mCmdAllocator)));
    char name[32]; sprintf_s(name, "CmdAl %d", (int)allocator->mId);
    allocator->mCmdAllocator->SetPrivateData(WKPDID_D3DDebugObjectName, (UINT)strlen(name), name);
    allocator->mFenceValue = 1;
    allocator->mLockFrame = 0;
    // Create fence for frame synchronisation
    ThrowIfFailed(mD3D12.GetD3DDevice()->CreateFence(0, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&allocator->mFence)));
    allocator->mFenceEvent = CreateEvent(nullptr, FALSE, FALSE, nullptr);
    if (allocator->mFenceEvent == nullptr) ThrowIfFailed(HRESULT_FROM_WIN32(GetLastError()));

    char msg[32]; sprintf_s(msg, "Creating %s\n", name);
    OutputDebugStringA(msg);

    static std::mutex cmdAllocMutex;
    std::scoped_lock lock(cmdAllocMutex);
    allocator->mId = (int)mCommandAllocators.size();
    mCommandAllocators.push_back(allocator);
    return allocator.get();
}
LockMask lastInflightFrames = 0;
LockMask D3DResourceCache::CheckInflightFrames() {
    LockMask completeFrames = 0, inflightFrames = 0;
    for (int i = 0; i < (int)mCommandAllocators.size(); ++i) {
        auto& cmdAllocator = *mCommandAllocators[i];
        if (!cmdAllocator.HasLockedFrames()) continue;
        auto lockFrame = cmdAllocator.GetLockFrame();
        if (lockFrame == cmdAllocator.mLockFrame) {
            inflightFrames |= 1ull << cmdAllocator.mId;
        }
        else if (cmdAllocator.ConsumeFrame(lockFrame)) {
            completeFrames |= 1ull << cmdAllocator.mId;
            cmdAllocator.mCmdAllocator->Reset();
        }
    }
    if (completeFrames != 0) {
        UnlockFrame(completeFrames);
        mDelayedRelease.PurgeUnlocked();
    }
    lastInflightFrames = inflightFrames;
    return inflightFrames;
}
void D3DResourceCache::UnlockFrame(size_t frameHandles) {
    mConstantBufferCache.Unlock(frameHandles);
    mConstantBufferPool.Unlock(frameHandles);
    mResourceViewCache.Unlock(frameHandles);
    mTargetViewCache.Unlock(frameHandles);
    mUploadBufferCache.Unlock(frameHandles);
    auto readbackMask = mReadbackBufferCache.Unlock(frameHandles);
    for (auto& item : mReadbackBufferCache.GetMaskItemIterator(readbackMask)) {
        // TODO: notify?
    }
    mDelayedRelease.Unlock(frameHandles);
}
struct CBRBAppender {
    std::string errors;
    Identifier cbBinds[32] = { 0 };
    Identifier rbBinds[32] = { 0 };
    Identifier uaBinds[32] = { 0 };
    PipelineLayout& layout;
    CBRBAppender(PipelineLayout& layout) 
        : layout(layout) { }
    ~CBRBAppender() {
        if (!errors.empty()) {
            errors = "Compiling " + layout.mName.GetName() + ":\n" + errors;
            MessageBoxA(0, errors.c_str(), "Binding collision", 0);
        }
    }
    void AppendCBRBs(const ShaderBase::ShaderReflection& reflection) {
        for (auto& cb : reflection.mConstantBuffers) {
            if (std::any_of(layout.mConstantBuffers.begin(), layout.mConstantBuffers.end(),
                [&](auto* o) { return *o == cb; })) continue;
            if (cbBinds[cb.mBindPoint].IsValid() && cbBinds[cb.mBindPoint] != cb.mName)
                errors += "CB Collision " + cbBinds[cb.mBindPoint].GetName() + " and " + cb.mName.GetName() + "\n";
            cbBinds[cb.mBindPoint] = cb.mName;
            layout.mConstantBuffers.push_back(&cb);
        }
        for (auto& rb : reflection.mResourceBindings) {
            if (std::any_of(layout.mResources.begin(), layout.mResources.end(),
                [&](auto* o) { return *o == rb; })) continue;
            auto& binds = rb.mType <= ShaderBase::R_SBuffer ? rbBinds : uaBinds;
            if (binds[rb.mBindPoint].IsValid() && binds[rb.mBindPoint] != rb.mName)
                errors += "RB Collision " + binds[rb.mBindPoint].GetName() + " and " + rb.mName.GetName() + "\n";
            binds[rb.mBindPoint] = rb.mName;
            layout.mResources.push_back(&rb);
        }
    }
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
    //static Identifier indirectCountName("INDIRECTINSTANCES");
    auto useBindings = bindings;
    if (useBindings[0]->mElements[0].mBindName == indirectArgsName) useBindings = useBindings.subspan(1);
    // TODO: This isnt required? Its bound as a uniform buffer. Could skip any uniform buffers instead?
    //if (useBindings[0]->mElements[0].mBindName == indirectCountName) useBindings = useBindings.subspan(1);
    for (auto* binding : useBindings) {
        for (auto& el : binding->GetElements()) {
            hash = AppendHash(el.mBindName.mId + ((int)el.mBufferStride << 16) + ((int)el.mFormat << 8), hash);
        }
    }
    if (shaders.mMeshShader != nullptr) {
        hash = AppendHash(shaders.mMeshShader->GetBinaryHash(), hash);
        if (shaders.mAmplificationShader != nullptr)
            hash = AppendHash(shaders.mAmplificationShader->GetBinaryHash(), hash);
    }
    else if (shaders.mVertexShader != nullptr) {
        hash = AppendHash(shaders.mVertexShader->GetBinaryHash(), hash);
    }
    hash = AppendHash(shaders.mPixelShader->GetBinaryHash(), hash);

    auto pipelineState = GetOrCreatePipelineState(hash);
    while (pipelineState->mHash != hash) {
        auto createPipelineZone = SimpleProfilerMarker("Create Pipeline");
        assert(pipelineState->mHash == 0);

        pipelineState->mHash = hash;
        pipelineState->mRootSignature = &mRootSignature;
        pipelineState->mMaterialState = materialState;

#if _DEBUG
        if (shaders.mVertexShader != nullptr) {
            auto& reflection = shaders.mVertexShader->GetReflection();
            for (auto& input : reflection.mInputParameters) {
                if (input.mSemantic.GetName().starts_with("SV_")) continue;
                bool found = false;
                for (auto& binding : useBindings) {
                    found |= std::any_of(binding->GetElements().begin(), binding->GetElements().end(),
                        [&](auto& element) { return element.mBindName == input.mSemantic; });
                }
                if (!found) {
                    std::ostringstream str;
                    str << "ERROR: Shader expects " << input.mSemantic.GetName() << " but was not found in bindings" << std::endl;
                    OutputDebugStringA(str.str().c_str());
                    pipelineState->mType = -1;
                    return pipelineState;
                }
            }
        }
#endif

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

        auto rasterizerState = CD3DX12_RASTERIZER_DESC(D3D12_DEFAULT);
        rasterizerState.CullMode = (D3D12_CULL_MODE)materialState.mRasterMode.mCullMode;
        auto blendState = CD3DX12_BLEND_DESC(D3D12_DEFAULT);
        blendState.RenderTarget[0] = ToD3DBlend(materialState.mBlendMode);
        auto depthStencilState = ToD3DDepthStencil(materialState);

        auto ApplyCommonFields = [&](auto& psoDesc, int psoType) {
            pipelineState->mType = psoType;
            psoDesc.pRootSignature = pipelineState->mRootSignature->mRootSignature.Get();
            psoDesc.RasterizerState = rasterizerState;
            psoDesc.BlendState = blendState;
            psoDesc.DepthStencilState = depthStencilState;
            psoDesc.NumRenderTargets = (uint32_t)frameBufferFormats.size();
            std::copy(frameBufferFormats.begin(), frameBufferFormats.end(), psoDesc.RTVFormats);
            psoDesc.DSVFormat = depthBufferFormat;
            psoDesc.SampleMask = DefaultSampleMask();
            psoDesc.SampleDesc = DefaultSampleDesc();
        };

        // Create the D3D pipeline
        auto device = mD3D12.GetD3DDevice();
        HRESULT hr = 0;
        if (meshBlob != nullptr) {
            D3DX12_MESH_SHADER_PIPELINE_STATE_DESC psoDesc = {};
            ApplyCommonFields(psoDesc, 1);
            if (ampBlob != nullptr) psoDesc.AS = CD3DX12_SHADER_BYTECODE(ampBlob.Get());
            psoDesc.MS = CD3DX12_SHADER_BYTECODE(meshBlob.Get());
            psoDesc.PS = CD3DX12_SHADER_BYTECODE(pixBlob.Get());
            auto meshStreamDesc = CD3DX12_PIPELINE_MESH_STATE_STREAM(psoDesc);
            D3D12_PIPELINE_STATE_STREAM_DESC streamDesc = {};
            streamDesc.SizeInBytes = sizeof(meshStreamDesc);
            streamDesc.pPipelineStateSubobjectStream = &meshStreamDesc;
            hr = device->CreatePipelineState(&streamDesc, IID_PPV_ARGS(&pipelineState->mPipelineState));
        }
        else {
            D3D12_GRAPHICS_PIPELINE_STATE_DESC psoDesc = {};
            ApplyCommonFields(psoDesc, 0);
            ComputeElementLayout(useBindings, pipelineState->mInputElements);
            psoDesc.InputLayout = { pipelineState->mInputElements.data(), (unsigned int)pipelineState->mInputElements.size() };
            psoDesc.VS = CD3DX12_SHADER_BYTECODE(vertBlob.Get());
            psoDesc.PS = CD3DX12_SHADER_BYTECODE(pixBlob.Get());
            psoDesc.PrimitiveTopologyType = D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE;
            hr = device->CreateGraphicsPipelineState(&psoDesc, IID_PPV_ARGS(&pipelineState->mPipelineState));
        }
        SimpleProfilerMarkerEnd(createPipelineZone);
        if (FAILED(hr)) {
            OutputDebugStringA("ERROR: Failed to create pipeline for ");
            OutputDebugStringA(shaders.mPixelShader->GetName().GetName().c_str());
            OutputDebugStringA("\n");
            ThrowIfFailed(hr);
            pipelineState->mType = -1;
            return pipelineState;
        }
        pipelineState->mPipelineState->SetName(shaders.mPixelShader->GetName().GetWName().c_str());

        // Collect constant buffers required by the shaders
        // TODO: Throw an error if different constant buffers
        // are required in the same bind point
        pipelineState->mLayout = std::make_unique<PipelineLayout>();
        pipelineState->mLayout->mName = shaders.mPixelShader->GetName();
        pipelineState->mLayout->mRootHash = (size_t)pipelineState->mRootSignature;
        pipelineState->mLayout->mPipelineHash = (size_t)pipelineState;
        for (auto& b : bindings) pipelineState->mLayout->mBindings.push_back(b);
        pipelineState->mLayout->mMaterialState = materialState;

        CBRBAppender appender(*pipelineState->mLayout);
        for (auto l : { shaders.mAmplificationShader, shaders.mMeshShader, shaders.mVertexShader, shaders.mPixelShader }) {
            if (l != nullptr) appender.AppendCBRBs(l->GetReflection());
        }
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
        mD3D12.GetD3DDevice()
            ->CreateComputePipelineState(&psoDesc, IID_PPV_ARGS(&pipelineState->mPipelineState));
        pipelineState->mType = 3;

        pipelineState->mLayout = std::make_unique<PipelineLayout>();
        pipelineState->mLayout->mName = shader.GetName();
        pipelineState->mLayout->mRootHash = (size_t)pipelineState->mRootSignature;
        pipelineState->mLayout->mPipelineHash = pipelineState->mPipelineState != nullptr ? (size_t)pipelineState : 0;
        
        CBRBAppender appender(*pipelineState->mLayout);
        appender.AppendCBRBs(shader.GetReflection());
    }
    return pipelineState;
}
// Find or allocate a constant buffer for the specified material and CB layout
D3DConstantBuffer* D3DResourceCache::RequireConstantBuffer(D3DCommandContext& cmdList, std::span<const uint8_t> tData, size_t dataHash, D3DResourceCache::CBBumpAllocator& bumpAllocator) {
    // CB should be padded to multiples of 256
    auto allocSize = (int)(tData.size() + 255) & ~255;
    if (dataHash == 0) dataHash = allocSize + GenericHash(tData.data(), tData.size());

    auto FillItem = [&](PerFrameItemStore<D3DConstantBuffer>::Item& item) {
        const int MinAllocationSize = 4 * 1024;
        int bumpBuffer = bumpAllocator.mBumpConstantBuffer;
        int bumpConsume = bumpAllocator.mBumpConstantConsume;
        if (bumpBuffer == -1 || bumpConsume + allocSize > MinAllocationSize) {
            auto bufferSize = std::max(allocSize, MinAllocationSize);
            mConstantBufferPool.RequireItem(
                bufferSize,
                cmdList.mLockBits,
                [&](auto& item) { // Allocate a new item
                    // We got a fresh item, need to create the relevant buffers
                    CD3DX12_RESOURCE_DESC resourceDesc = CD3DX12_RESOURCE_DESC::Buffer(bufferSize);
                    auto device = mD3D12.GetD3DDevice();
                    auto hr = device->CreateCommittedResource(
                        &D3D::DefaultHeap,
                        D3D12_HEAP_FLAG_NONE,
                        &resourceDesc,
                        D3D12_RESOURCE_STATE_COMMON,
                        nullptr,
                        IID_PPV_ARGS(&item.mData.mConstantBuffer)
                    );
                    if (FAILED(hr)) throw "[D3D] Failed to create constant buffer";
                    item.mData.mConstantBuffer->SetName(L"ConstantBuffer");
                    item.mData.mRevision = 1;
                    mStatistics.mBufferCreates++;
                },
                [&](auto& item) {
                    item.mData.mRevision++;
                },
                [&](int itemIndex) {
                    bumpBuffer = itemIndex;
                    bumpConsume = 0;
                });
        }
        assert(bumpBuffer >= 0);
        item.mData.mConstantBufferIndex = bumpBuffer;
        item.mData.mConstantBufferRevision = mConstantBufferPool.GetItem(bumpBuffer).mData.mRevision;
        item.mData.mOffset = bumpConsume;
        bumpAllocator.mBumpConstantBuffer = bumpBuffer;
        bumpAllocator.mBumpConstantConsume = bumpConsume + allocSize;

        int copySize = (tData.size() + 15) & ~15;
        auto* constantBuffer = mConstantBufferPool.GetItem(item.mData.mConstantBufferIndex).mData.mConstantBuffer.Get();
        bool canSkipTransition = false;
        if (!cmdList.mBarrierStateManager->mDelayedBarriers.empty()) {
            auto lastBarrier = cmdList.mBarrierStateManager->mDelayedBarriers.back();
            if (lastBarrier.Transition.pResource == constantBuffer && lastBarrier.Transition.StateBefore == D3D12_RESOURCE_STATE_COPY_DEST) {
                cmdList.mBarrierStateManager->mDelayedBarriers.pop_back();
                canSkipTransition = true;
            }
        }
        if (!canSkipTransition) {
            cmdList.mBarrierStateManager->mDelayedBarriers.push_back(CD3DX12_RESOURCE_BARRIER::Transition(constantBuffer, D3D12_RESOURCE_STATE_COMMON, D3D12_RESOURCE_STATE_COPY_DEST));
        }
        ID3D12Resource* uploadBuffer = AllocateUploadBuffer(copySize, cmdList.mLockBits);
        D3D::FillBuffer(uploadBuffer, [&](uint8_t* data) { std::memcpy(data, tData.data(), tData.size()); });
        FlushBarriers(cmdList);
        cmdList->CopyBufferRegion(constantBuffer, item.mData.mOffset, uploadBuffer, 0, copySize);
        mStatistics.BufferWrite(tData.size());
        cmdList.mBarrierStateManager->mDelayedBarriers.push_back(CD3DX12_RESOURCE_BARRIER::Transition(constantBuffer, D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_COMMON));
    };
    auto& resultItem = mConstantBufferCache.RequireItem(dataHash, allocSize, cmdList.mLockBits,
        [&](auto& item) { // Allocate a new item
            item.mData.mConstantBufferIndex = -1;
            item.mData.mConstantBufferRevision = 0;
            item.mData.mOffset = 0;
        },
        [&](auto& item) { // Fill an item with data
            FillItem(item);
        },
        [&](auto& item) {
            auto& cbItem = mConstantBufferPool.GetItem(item.mData.mConstantBufferIndex);
            if (cbItem.mData.mRevision == item.mData.mConstantBufferRevision) {
                mConstantBufferPool.RequireItemLock(cbItem, cmdList.mLockBits);
            }
            else {
                FillItem(item);
            }
        } // An existing item was found to match the data
    );
    auto& cbItem = mConstantBufferPool.GetItem(resultItem.mData.mConstantBufferIndex);
    assert(cbItem.mData.mRevision == resultItem.mData.mConstantBufferRevision);
    assert(resultItem.mLayoutHash == allocSize);
    return &resultItem.mData;
}
ComPtr<ID3D12Resource>& D3DResourceCache::GetConstantBuffer(int index) {
    return mConstantBufferPool.GetItem(index).mData.mConstantBuffer;
}
D3DResourceCache::RenderTargetView& D3DResourceCache::RequireTextureRTV(
    D3DResourceCache::D3DRenderSurfaceView& bufferView, LockMask lockBits
) {
    int subresourceId = D3D12CalcSubresource(bufferView.mMip, bufferView.mSlice, 0,
        bufferView.mSurface->mDesc.mMips, bufferView.mSurface->mDesc.mSlices);
    auto* surface = bufferView.mSurface;
    auto* buffer = surface->mBuffer.Get();
    auto isDepth = BufferFormatType::GetIsDepthBuffer((BufferFormat)surface->mFormat);
    size_t dataHash = (size_t)buffer + subresourceId;
    auto& item = mTargetViewCache.RequireItem(dataHash, isDepth ? 1 : 0, lockBits,
        [&](auto& item) {
            item.mData.mRTVOffset = isDepth
                ? mDSOffset.fetch_add(mD3D12.GetDescriptorHandleSizeDSV())
                : mRTOffset.fetch_add(mD3D12.GetDescriptorHandleSizeRTV());
        }, [&](auto& item) {
            if (isDepth) {
                D3D12_DEPTH_STENCIL_VIEW_DESC dsViewDesc = { .Format = surface->mFormat, .ViewDimension = D3D12_DSV_DIMENSION_TEXTURE2D };
                dsViewDesc.Texture2D = { .MipSlice = (UINT)bufferView.mMip };
                mD3D12.GetD3DDevice()->CreateDepthStencilView(buffer, &dsViewDesc,
                    CD3DX12_CPU_DESCRIPTOR_HANDLE(mD3D12.GetDSVHeap()->GetCPUDescriptorHandleForHeapStart(), item.mData.mRTVOffset));
            }
            else {
                D3D12_RENDER_TARGET_VIEW_DESC rtvViewDesc = { .Format = surface->mFormat, };
                if (bufferView.mSlice > 0) {
                    rtvViewDesc.ViewDimension = D3D12_RTV_DIMENSION_TEXTURE2DARRAY;
                    rtvViewDesc.Texture2DArray = { .MipSlice = (UINT)bufferView.mMip, .ArraySize = (UINT)bufferView.mSlice };
                }
                else {
                    rtvViewDesc.ViewDimension = D3D12_RTV_DIMENSION_TEXTURE2D;
                    rtvViewDesc.Texture2D = { .MipSlice = (UINT)bufferView.mMip };
                }
                mD3D12.GetD3DDevice()->CreateRenderTargetView(buffer, &rtvViewDesc,
                    CD3DX12_CPU_DESCRIPTOR_HANDLE(mD3D12.GetRTVHeap()->GetCPUDescriptorHandleForHeapStart(), item.mData.mRTVOffset));
            }
            item.mData.mResource = buffer;
        }, [&](auto& item) {
            assert(item.mDataHash == dataHash);
            assert(item.mData.mResource == buffer);
        });
    return item.mData;
}
int D3DResourceCache::RequireTextureSRV(D3DResourceCache::D3DTexture& texture, LockMask lockBits) {
    if (texture.mSRVOffset < 0) {
        if (texture.mSRVOffset < -1) ClearBufferSRV(texture, lockBits);
        auto textureDesc = texture.mBuffer->GetDesc();
        texture.mSRVOffset = GetTextureSRV(texture.mBuffer.Get(),
            textureDesc.Format, textureDesc.Dimension == D3D12_RESOURCE_DIMENSION_TEXTURE3D,
            textureDesc.DepthOrArraySize, 0x80000000);
    }
    return texture.mSRVOffset;
}
void D3DResourceCache::InvalidateBufferSRV(D3DResourceCache::D3DBuffer& buffer) {
    if (buffer.mSRVOffset >= 0) buffer.mSRVOffset |= 0x80000000;
}
void D3DResourceCache::ClearBufferSRV(D3DResourceCache::D3DBuffer& buffer, LockMask lockBits) {
    assert(buffer.mSRVOffset < -1);
    buffer.mSRVOffset &= ~0x80000000;
    auto itemId = mResourceViewCache.Find([&](auto& item) {
        return item.mData.mSRVOffset == buffer.mSRVOffset;
    });
    mResourceViewCache.Substitute(mResourceViewCache.GetItem(itemId), 0x80000000, CheckInflightFrames() | lockBits);
    buffer.mSRVOffset = -1;
}
