#include "D3DConstantBufferCache.h"

#include <d3dx12.h>

// Find or allocate a constant buffer for the specified material and CB layout
D3DConstantBuffer* D3DConstantBufferCache::RequireConstantBuffer(const Material& material
    , const ShaderBase::ConstantBuffer& cBuffer
    , D3DGraphicsDevice& d3d12)
{
    tData.resize(cBuffer.mSize);

    // Copy data into the constant buffer
    // TODO: Generate a hash WITHOUT copying data?
    //  => Might be more expensive to evaluate props twice
    std::memset(tData.data(), 0, sizeof(tData[0]) * tData.size());
    for (auto& var : cBuffer.mValues)
    {
        auto varData = material.GetUniformBinaryData(var.mNameId);
        std::memcpy(tData.data() + var.mOffset, varData.data(), varData.size());
    }
    return RequireConstantBuffer(tData, d3d12);
}

// Find or allocate a constant buffer for the specified material and CB layout
D3DConstantBuffer* D3DConstantBufferCache::RequireConstantBuffer(std::span<const uint8_t> tData
    , D3DGraphicsDevice & d3d12)
{
    // CB should be padded to multiples of 256
    auto allocSize = (int)(tData.size() + 255) & ~255;
    auto dataHash = GenericHash(tData.data(), tData.size());

    auto& resultItem = RequireItem(dataHash, allocSize,
        [&](auto& item) // Allocate a new item
        {
            auto device = d3d12.GetD3DDevice();
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
            if (FAILED(hr))
            {
                throw "[D3D] Failed to create constant buffer";
            }
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
        },
        [&](auto& item)  // An existing item was found to match the data
        {
        }
    );
    assert(resultItem.mLayoutHash == allocSize);

    return &resultItem.mData;
}
