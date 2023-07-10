#pragma once

#include <unordered_map>
#include <algorithm>
#include <memory>
#include <deque>

#include "D3DGraphicsDevice.h"
#include "D3DShader.h"
#include <d3dx12.h>

class D3DConstantBufferCache
{
public:
    // The GPU data for a set of shaders, rendering state, and vertex attributes
    struct D3DConstantBuffer
    {
        ComPtr<ID3D12Resource> mConstantBuffer;
        D3D12_GPU_DESCRIPTOR_HANDLE mConstantBufferHandle;
        size_t mDataHash;
        int mLastUsedFrame;
        int mSize;
    };
    struct OrderedCB {
        D3DConstantBuffer* mCB;
        OrderedCB(D3DConstantBuffer* cb) : mCB(cb) { }
        bool operator <(const OrderedCB& other) const { return mCB->mLastUsedFrame < other.mCB->mLastUsedFrame; }
        bool operator ==(const OrderedCB& other) const { return mCB == other.mCB; }
    };

private:
    std::unordered_map<size_t, D3DConstantBuffer*> mConstantBuffersByHash;
    std::deque<OrderedCB> mUsageQueue;
    int mCBOffset;
    int mLockFrameId;
    int mFrameCounter;

    size_t ComputeHash(const std::vector<unsigned char>& data)
    {
        int wsize = (int)(data.size() / sizeof(size_t));
        size_t hash = data.size();
        for (int i = 0; i < wsize; ++i)
        {
            auto x = ((size_t*)data.data())[i];
            hash = (hash * 0x9E3779B97F4A7C15L + 0x0123456789ABCDEFL) ^ x;
        }
        return hash;
    }

    std::vector<unsigned char> data;
public:
    D3DConstantBufferCache()
        : mCBOffset(0), mFrameCounter(0) { }

    void SetResourceLockIds(int lockFrameId, int writeFrameId)
    {
        mLockFrameId = lockFrameId;
        mFrameCounter = writeFrameId;
    }

    D3DConstantBuffer* RequireConstantBuffer(const Material& material
        , const D3DShader::ConstantBuffer& cBuffer
        , D3DGraphicsDevice& d3d12)
    {
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

        auto hash = ComputeHash(data);
        // TODO: Hash could produce conflicts, what to do in this situation?
        //  => It is exceptionally rare, and would only produce visual
        //     artifacts, so perhaps is an alright tradeoff?
        auto constantBufferKV = mConstantBuffersByHash.find(hash);
        // Matching buffer was found, move it to end of queue
        if (constantBufferKV != mConstantBuffersByHash.end())
        {
            // If this is the first time we're using it this frame
            // update its last used frame
            if (constantBufferKV->second->mLastUsedFrame != mFrameCounter)
            {
                auto q = std::find(mUsageQueue.begin(), mUsageQueue.end(), OrderedCB(constantBufferKV->second));
                //auto q = mUsageQueue.find(OrderedCB(constantBufferKV->second));
                if (q != mUsageQueue.end()) mUsageQueue.erase(q);
                constantBufferKV->second->mLastUsedFrame = mFrameCounter;
                mUsageQueue.push_back(OrderedCB(constantBufferKV->second));
            }
            return constantBufferKV->second;
        }

        D3DConstantBuffer* cbuffer = nullptr;
        // Try to reuse an existing one (based on age) of the same size
        if (mUsageQueue.size() > 400)
        {
            auto reuse = mUsageQueue.begin();
            while (reuse != mUsageQueue.end() && reuse->mCB->mSize != allocSize)
                ++reuse;
            if (reuse != mUsageQueue.end())
            {
                cbuffer = reuse->mCB;
                mUsageQueue.erase(reuse);
                mConstantBuffersByHash.erase(mConstantBuffersByHash.find(cbuffer->mDataHash));
            }
        }

        // If none are available to reuse, create a new one
        // TODO: Delete old ones if they get too old
        if (cbuffer == nullptr)
        {
            cbuffer = new D3DConstantBuffer();
            cbuffer->mSize = allocSize;

            auto device = d3d12.GetD3DDevice();
            if (cbuffer->mConstantBuffer == nullptr)
            {
                CD3DX12_HEAP_PROPERTIES heapProperties(D3D12_HEAP_TYPE_UPLOAD);
                CD3DX12_RESOURCE_DESC resourceDesc = CD3DX12_RESOURCE_DESC::Buffer(allocSize);
                device->CreateCommittedResource(
                    &heapProperties,
                    D3D12_HEAP_FLAG_NONE,
                    &resourceDesc,
                    D3D12_RESOURCE_STATE_GENERIC_READ,
                    nullptr,
                    IID_PPV_ARGS(&cbuffer->mConstantBuffer)
                );
                D3D12_GPU_VIRTUAL_ADDRESS constantBufferAddress = cbuffer->mConstantBuffer->GetGPUVirtualAddress();
                D3D12_CONSTANT_BUFFER_VIEW_DESC constantBufferView;
                constantBufferView.BufferLocation = constantBufferAddress;
                constantBufferView.SizeInBytes = allocSize;
                // Get the descriptor heap handle for the constant buffer view
                auto cbvHandle = d3d12.GetCBHeap()->GetCPUDescriptorHandleForHeapStart();
                auto gbvHandle = d3d12.GetCBHeap()->GetGPUDescriptorHandleForHeapStart();
                cbvHandle.ptr += mCBOffset;
                gbvHandle.ptr += mCBOffset;
                device->CreateConstantBufferView(&constantBufferView, cbvHandle);
                cbuffer->mConstantBufferHandle = gbvHandle;
                mCBOffset += d3d12.GetDescriptorHandleSize();
            }
        }

        // Copy data into this new one
        UINT8* cbDataBegin;
        cbuffer->mConstantBuffer->Map(0, nullptr, reinterpret_cast<void**>(&cbDataBegin));
        std::memcpy(cbDataBegin, data.data(), data.size());
        cbuffer->mConstantBuffer->Unmap(0, nullptr);
        cbuffer->mDataHash = hash;
        cbuffer->mLastUsedFrame = mFrameCounter;
        mUsageQueue.push_back(OrderedCB(cbuffer));

        mConstantBuffersByHash.insert({ hash, cbuffer, });

        return cbuffer;
    }

};
