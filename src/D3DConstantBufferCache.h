#pragma once

#include <unordered_map>
#include <algorithm>
#include <memory>
#include <deque>

#include "D3DGraphicsDevice.h"
#include "D3DShader.h"
#include "GraphicsUtility.h"
#include <d3dx12.h>

// Stores a cache of Constant Buffers that have been generated
// so that they can be efficiently reused where appropriate
    // The GPU data for a set of shaders, rendering state, and vertex attributes
struct D3DConstantBuffer
{
    ComPtr<ID3D12Resource> mConstantBuffer;
    D3D12_GPU_DESCRIPTOR_HANDLE mConstantBufferHandle;
    int mSize;
};
class D3DConstantBufferCache : public PerFrameItemStore<D3DConstantBuffer>
{
    // Temporary data store for filling CB data (before hashing it)
    std::vector<unsigned char> data;
    // The offset applied to the next constant buffer allocated
    int mCBOffset;

public:
    D3DConstantBufferCache()
        : mCBOffset(0) { }

    // Find or allocate a constant buffer for the specified material and CB layout
    D3DConstantBuffer* RequireConstantBuffer(const Material& material
        , const D3DShader::ConstantBuffer& cBuffer
        , D3DGraphicsDevice& d3d12)
    {
        // CB should be padded to multiples of 256
        auto allocSize = (cBuffer.mSize + 255) & ~255;

        // Copy data into the constant buffer
        // TODO: Generate a hash WITHOUT copying data?
        //  => Might be more expensive to evaluate props twice
        data.resize(cBuffer.mSize);
        for (auto& var : cBuffer.mValues)
        {
            auto varData = material.GetUniformBinaryData(var.mNameId);
            memcpy(data.data() + var.mOffset, varData.data(), varData.size());
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
                D3D12_GPU_VIRTUAL_ADDRESS constantBufferAddress = item.mData.mConstantBuffer->GetGPUVirtualAddress();
                D3D12_CONSTANT_BUFFER_VIEW_DESC constantBufferView;
                constantBufferView.BufferLocation = constantBufferAddress;
                constantBufferView.SizeInBytes = allocSize;
                // Get the descriptor heap handle for the constant buffer view
                auto cbvHandle = d3d12.GetCBHeap()->GetCPUDescriptorHandleForHeapStart();
                auto gbvHandle = d3d12.GetCBHeap()->GetGPUDescriptorHandleForHeapStart();
                cbvHandle.ptr += mCBOffset;
                gbvHandle.ptr += mCBOffset;
                device->CreateConstantBufferView(&constantBufferView, cbvHandle);
                item.mData.mConstantBufferHandle = gbvHandle;
                mCBOffset += d3d12.GetDescriptorHandleSize();
            },
            [&](auto& item)  // Fill an item with data
            {
                // Copy data into this new one
                assert(item.mData.mConstantBuffer != nullptr);
                UINT8* cbDataBegin;
                item.mData.mConstantBuffer->Map(0, nullptr, reinterpret_cast<void**>(&cbDataBegin));
                std::memcpy(cbDataBegin, data.data(), data.size());
                item.mData.mConstantBuffer->Unmap(0, nullptr);
            },
            [&](auto& item)  // An existing item was found to match the data
            {
            }
        );
        assert(resultItem.mLayoutHash == allocSize);

        return &resultItem.mData;
    }

};
