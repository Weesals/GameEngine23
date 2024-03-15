#pragma once

#define BufferAlignment 15

#include "D3DGraphicsDevice.h"
#include "Buffer.h"

#include <vector>
#include <unordered_map>
#include <bit>
#include <bitset>
#include <cassert>

namespace D3D {
    extern D3D12_HEAP_PROPERTIES DefaultHeap;
    extern D3D12_HEAP_PROPERTIES UploadHeap;

    void WriteBufferData(uint8_t* data, const BufferLayout& binding, int itemSize, int byteOffset, int byteSize);

    template<class F2>
    static void FillBuffer(ID3D12Resource* uploadBuffer, const F2& fillBuffer) {
        uint8_t* mappedData;
        D3D12_RANGE readRange(0, 0);
        ThrowIfFailed(uploadBuffer->Map(0, &readRange, (void**)&mappedData));
        fillBuffer(mappedData);
        uploadBuffer->Unmap(0, nullptr);
    };

    static const char* GetResourceStateString(D3D12_RESOURCE_STATES state) {
        switch (state) {
        case D3D12_RESOURCE_STATE_COMMON: return "Common";
        case D3D12_RESOURCE_STATE_RENDER_TARGET: return "RenderTarget";
        case D3D12_RESOURCE_STATE_DEPTH_WRITE: return "DepthWrite";
        }
        return "Other";
    }

    struct BarrierHandle {
        int mIndex;
        operator int() const { return mIndex; }
        BarrierHandle(int index) : mIndex(index) { }
        static const BarrierHandle Invalid;
    };
    struct BarrierMeta {
        int mSubresourceCount;
        BarrierMeta(int subresourceCount) : mSubresourceCount(subresourceCount) { }
    };
    struct TextureDescription {
        uint16_t mWidth;
        uint16_t mHeight;
        uint8_t mMips, mSlices;
        int GetSubresource(int mip, int slice) const { return mip + slice * mMips; }
        int GetSubresourceCount() const { return mMips * mSlices; }
        operator BarrierMeta() const { return BarrierMeta(GetSubresourceCount()); }
    };

    struct BarrierStateManager {
        struct BaseResourceState {
            D3D12_RESOURCE_STATES mState = D3D12_RESOURCE_STATE_COMMON;
            uint32_t mSparseMask = 0xffffffff;
            bool GetIsLocked() const { return (mState & 0x80000000) != 0; }
        };
        struct SparseResourceState : public BaseResourceState {
            uint32_t mPageOffset = 0;
        };
        struct PrimaryResourceState : public BaseResourceState {
            uint32_t mLockCount = 0;
        };
        struct ResourceState : public PrimaryResourceState {
            BarrierHandle mHandle = BarrierHandle::Invalid;
        };

        typedef std::unordered_multimap<int, SparseResourceState> ResourceMap;

        std::vector<PrimaryResourceState> mResourceStates;
        ResourceMap mSparseStates;
        int mNextHandle = 0x80000000;

        void Clear() {
            mResourceStates.clear();
            mSparseStates.clear();
        }
        D3D12_RESOURCE_STATES GetResourceState(BarrierHandle handle, int subresource) {
            if (subresource < 31) {
                if (handle >= (int)mResourceStates.size())
                    return D3D12_RESOURCE_STATE_COMMON;
                auto& resource = mResourceStates[handle];
                if ((resource.mSparseMask & (1u << subresource)) != 0)
                    return resource.mState;
            }
            auto pageRange = mSparseStates.equal_range(handle);
            for(auto& page : std::ranges::subrange(pageRange.first, pageRange.second)) {
                int delta = subresource - page.second.mPageOffset;
                if (delta < 0 || delta >= 31) continue;
                if ((page.second.mSparseMask & (1u << delta)) != 0)
                    return page.second.mState;
            }
            return D3D12_RESOURCE_STATE_COMMON;
        }
        bool UnlockResourceState(BarrierHandle handle, int subresource,
            D3D12_RESOURCE_STATES state, BarrierMeta meta) {
            if (handle >= (int)mResourceStates.size()) return false;
            auto& resource = mResourceStates[handle];
            return UnlockResourceState(resource, handle, subresource, state, meta);
        }
        bool UnlockResourceState(ResourceState& resource, int subresource,
            D3D12_RESOURCE_STATES state, BarrierMeta meta) {
            return UnlockResourceState(resource, resource.mHandle, subresource, state, meta);
        }
        bool UnlockResourceState(PrimaryResourceState& resource, BarrierHandle handle,
            int subresource, D3D12_RESOURCE_STATES state, BarrierMeta meta) {
            assert(resource.mLockCount > 0);
            auto lockedState = CreateLocked(state);
            if (resource.mState == lockedState) {
                resource.mState = state;
                --resource.mLockCount;
            }
            auto pageRange = mSparseStates.equal_range(handle);
            for (auto& page : std::ranges::subrange(pageRange.first, pageRange.second)) {
                if (page.second.mState != lockedState) continue;
                page.second.mState = state;
                --resource.mLockCount;
            }
            return true;
        }
        bool SetResourceState(std::vector<D3D12_RESOURCE_BARRIER>& barriers,
            ID3D12Resource* d3dResource, BarrierHandle handle, int subresource,
            D3D12_RESOURCE_STATES state, BarrierMeta meta) {
            if (handle >= (int)mResourceStates.size()) {
                if (state == D3D12_RESOURCE_STATE_COMMON) return false;
                mResourceStates.resize(std::bit_ceil((uint32_t)handle + 16));
            }
            auto& resource = mResourceStates[handle];
            return SetResourceState(barriers, d3dResource, resource, handle, subresource, state, meta);
        }
        bool SetResourceState(std::vector<D3D12_RESOURCE_BARRIER>& barriers,
            ID3D12Resource* d3dResource, ResourceState& resource, int subresource,
            D3D12_RESOURCE_STATES state, BarrierMeta meta) {
            if (resource.mHandle == -1) resource.mHandle = mNextHandle++;
            return SetResourceState(barriers, d3dResource, resource, resource.mHandle, subresource, state, meta);
        }
        // Returns true if a barrier MIGHT have been added
        bool SetResourceState(std::vector<D3D12_RESOURCE_BARRIER>& barriers,
            ID3D12Resource* d3dResource, PrimaryResourceState& resource,
            BarrierHandle handle, int subresource, D3D12_RESOURCE_STATES state, BarrierMeta meta);
        static void CreateBarriers(std::vector<D3D12_RESOURCE_BARRIER>& barriers,
            ID3D12Resource* d3dResource, D3D12_RESOURCE_STATES from, D3D12_RESOURCE_STATES to,
            int pageBegin, uint32_t bits, BarrierMeta meta) {
            if (from == to) return;
            while (bits) {
                int bit = std::countr_zero(bits);
                if (bit >= meta.mSubresourceCount) break;
                bits &= (0xfffffffe << bit);
                barriers.push_back(CreateBarrier(d3dResource, from, to, pageBegin + bit, meta));
            }
        }
        template<class TContainer>
        static void CreateBarrier(TContainer& barriers, ID3D12Resource* d3dResource,
            D3D12_RESOURCE_STATES from, D3D12_RESOURCE_STATES to,
            int subresource, BarrierMeta meta) {
            if (Match(from, to)) return;
            barriers.push_back(CreateBarrier(d3dResource, from, to, subresource, meta));
        }
        static D3D12_RESOURCE_BARRIER CreateBarrier(
            ID3D12Resource* d3dResource,
            D3D12_RESOURCE_STATES from, D3D12_RESOURCE_STATES to,
            int subresource, BarrierMeta meta);

        static D3D12_RESOURCE_STATES CreateLocked(D3D12_RESOURCE_STATES state) {
            return state | (D3D12_RESOURCE_STATES)0x80000000;
        }
        static D3D12_RESOURCE_STATES CreateUnlocked(D3D12_RESOURCE_STATES state) {
            return state & (D3D12_RESOURCE_STATES)0x7fffffff;
        }
        static bool Match(D3D12_RESOURCE_STATES state1, D3D12_RESOURCE_STATES state2) {
            return ((state1 ^ state2) & 0x7fffffff) == 0;
        }
    };
}
