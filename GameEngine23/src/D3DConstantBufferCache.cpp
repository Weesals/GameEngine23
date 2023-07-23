#include "D3DConstantBufferCache.h"

#include <d3dx12.h>

D3DConstantBufferCache::D3DConstantBufferCache()
    : mCBOffset(0) { }

// Find or allocate a constant buffer for the specified material and CB layout
D3DConstantBuffer* D3DConstantBufferCache::RequireConstantBuffer(const Material& material
    , const D3DShader::ConstantBuffer& cBuffer
    , D3DGraphicsDevice& d3d12)
{
    // CB should be padded to multiples of 256
    auto allocSize = (cBuffer.mSize + 255) & ~255;
    data.resize(cBuffer.mSize);

    // Copy data into the constant buffer
    // TODO: Generate a hash WITHOUT copying data?
    //  => Might be more expensive to evaluate props twice
    std::memset(data.data(), 0, sizeof(data[0]) * data.size());
    for (auto& var : cBuffer.mValues)
    {
        auto varData = material.GetUniformBinaryData(var.mNameId);
        std::memcpy(data.data() + var.mOffset, varData.data(), varData.size());
    }

    auto dataHash = ComputeHash(data);

    auto& resultItem = RequireItem(dataHash, allocSize,
        [&](auto& item) // Allocate a new item
        {
            auto device = d3d12.GetD3DDevice();
            assert(item.mData.mConstantBuffer == nullptr);
            // We got a fresh item, need to create the relevant buffers
            CD3DX12_HEAP_PROPERTIES heapProperties(D3D12_HEAP_TYPE_UPLOAD);
            CD3DX12_RESOURCE_DESC resourceDesc = CD3DX12_RESOURCE_DESC::Buffer(allocSize);
            device->CreateCommittedResource(
                &heapProperties,
                D3D12_HEAP_FLAG_NONE,
                &resourceDesc,
                D3D12_RESOURCE_STATE_GENERIC_READ,
                nullptr,
                IID_PPV_ARGS(&item.mData.mConstantBuffer)
            );
            // Cannot get descriptors to work,
            // it causes device reset when running without the debug layer
            /*D3D12_CONSTANT_BUFFER_VIEW_DESC constantBufferView;
            constantBufferView.BufferLocation = item.mData.mConstantBuffer->GetGPUVirtualAddress();
            constantBufferView.SizeInBytes = allocSize;
            // Get the descriptor heap handle for the constant buffer view
            CD3DX12_CPU_DESCRIPTOR_HANDLE cbvHandle(d3d12.GetCBHeap()->GetCPUDescriptorHandleForHeapStart(), mCBOffset);
            CD3DX12_GPU_DESCRIPTOR_HANDLE gbvHandle(d3d12.GetCBHeap()->GetGPUDescriptorHandleForHeapStart(), mCBOffset);
            device->CreateConstantBufferView(&constantBufferView, cbvHandle);
            item.mData.mConstantBufferHandle = gbvHandle;
            mCBOffset += d3d12.GetDescriptorHandleSize();*/
        },
        [&](auto& item)  // Fill an item with data
        {
            // Copy data into this new one
            assert(item.mData.mConstantBuffer != nullptr);
            UINT8* cbDataBegin;
            if (SUCCEEDED(item.mData.mConstantBuffer->Map(0, nullptr, reinterpret_cast<void**>(&cbDataBegin)))) {
                std::memcpy(cbDataBegin, data.data(), cBuffer.mSize);
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
