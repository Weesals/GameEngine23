#include "D3DUtility.h"

#include <sstream>

namespace D3D {
	D3D12_HEAP_PROPERTIES DefaultHeap(D3D12_HEAP_TYPE_DEFAULT);
	D3D12_HEAP_PROPERTIES UploadHeap(D3D12_HEAP_TYPE_UPLOAD);
    D3D12_HEAP_PROPERTIES ReadbackHeap(D3D12_HEAP_TYPE_READBACK);

    void WriteBufferData(uint8_t* data, const BufferLayout& binding, int itemSize, int byteOffset, int byteSize) {
        // Fast path
        if (itemSize <= 0 || (binding.GetElements().size() == 1 && binding.GetElements()[0].mBufferStride == itemSize)) {
            memcpy(data, (uint8_t*)binding.GetElements()[0].mData + byteOffset, byteSize);
            return;
        }
        int count = byteSize / itemSize;
        int toffset = 0;
        for (auto& element : binding.GetElements()) {
            auto elItemSize = element.GetItemByteSize();
            auto* dstData = data + toffset;
            auto* srcData = (uint8_t*)element.mData + byteOffset;
            for (int s = 0; s < count; ++s) {
                memcpy(dstData, srcData, elItemSize);
                dstData += itemSize;
                srcData += element.mBufferStride;
            }
            toffset += elItemSize;
        }
    }

    const BarrierHandle BarrierHandle::Invalid(-1);

    bool BarrierStateManager::SetResourceState(
        ID3D12Resource* d3dResource, PrimaryResourceState& resource,
        BarrierHandle handle, int subresource, D3D12_RESOURCE_STATES state,
        BarrierMeta meta) {
        assert(d3dResource != nullptr);
        // If there is only 1 subresource, always set all
        if (meta.mSubresourceCount <= 1) subresource = -1;
        // Setting all subresources
        if (subresource < 0) {
            if (resource.mSparseMask == 0xffffffff && resource.mLockCount == 0) {
                // Special case when this resource has no sparse pages
                if (resource.mState == state) return false;
                CreateBarrier(mDelayedBarriers, d3dResource, resource.mState, state, D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES, meta);
                if (resource.GetIsLocked()) --resource.mLockCount;
                resource.mState = state;
                if (resource.GetIsLocked()) ++resource.mLockCount;
                return true;
            }
            else {
                // Check if the primary page needs to change
                if (!resource.GetIsLocked() && resource.mState != state) {
                    CreateBarriers(mDelayedBarriers, d3dResource, resource.mState, state, 0, resource.mSparseMask, meta);
                    if (resource.GetIsLocked()) --resource.mLockCount;
                    resource.mState = state;
                    if (resource.GetIsLocked()) ++resource.mLockCount;
                }
                // Check if any sparse pages need to change
                auto pageRange = mSparseStates.equal_range(handle);
                int lockCount = 0;
                for (auto& page : std::ranges::subrange(pageRange.first, pageRange.second)) {
                    if (page.second.GetIsLocked()) { ++lockCount; continue; }
                    if (page.second.mState != state)
                        CreateBarriers(mDelayedBarriers, d3dResource, page.second.mState, state,
                            page.second.mPageOffset, page.second.mSparseMask, meta);
                    if (page.second.mPageOffset == 0) resource.mSparseMask |= page.second.mSparseMask;
                }
                if (lockCount == 0) {
                    // Nothing is locked, clear all sparse pages
                    mSparseStates.erase(pageRange.first, pageRange.second);
                    resource.mSparseMask = 0xffffffff;
                }
                else {
                    // A sparse page is still locked, remove 1 by 1
                    for (auto it = pageRange.first; it != pageRange.second; ) {
                        if (it->second.GetIsLocked()) ++it;
                        else it = mSparseStates.erase(it);
                    }
                }
            }
            return true;
        }
        D3D12_RESOURCE_STATES fromState = (D3D12_RESOURCE_STATES)(-1);
        if (subresource < 31) {
            // If this subresource is stored in primary, get its previous state
            if ((resource.mSparseMask & (1u << subresource)) != 0) {
                if (resource.mState == state) return false;
                fromState = resource.mState;
                resource.mSparseMask &= ~(1u << subresource);
            }
        }
        ResourceMap::iterator erasePage = mSparseStates.end();
        SparseResourceState* destPage = nullptr;
        if (fromState == -1) {
            // Otherwise find it in a sparse page
            int page = subresource / 31;
            auto values = mSparseStates.find(handle);
            auto pageRange = mSparseStates.equal_range(handle);
            for (auto it = pageRange.first; it != pageRange.second; ++it) {
                auto& page = *it;
                int delta = subresource - page.second.mPageOffset;
                if (delta < 0 || delta >= 31) continue;
                if (page.second.mState == state) destPage = &page.second;
                if ((page.second.mSparseMask & (1u << delta)) == 0) continue;
                fromState = page.second.mState;
                if (fromState == state) return false;

                page.second.mSparseMask &= ~(1u << delta);
                if (page.second.mSparseMask == 0) erasePage = it;
            }
            // Otherwise nothing was allocated for it, it is still common
            if (fromState == -1) fromState = D3D12_RESOURCE_STATE_COMMON;
        }
        // Ignore if no state change is required
        if (fromState == state) return false;
        // Change the state
        CreateBarrier(mDelayedBarriers, d3dResource, fromState, state, subresource, meta);
        // Add it to the destination page
        if (destPage != nullptr) {
            destPage->mSparseMask |= 1u << (subresource - destPage->mPageOffset);
        }
        // Erase its old page (if empty)
        if (erasePage != mSparseStates.end()) {
            if (erasePage->second.GetIsLocked()) --resource.mLockCount;
            // This invalidaes destPage
            mSparseStates.erase(erasePage);
        }
        // If no dest page, make a new page for it
        if (destPage == nullptr) {
            SparseResourceState sparseState;
            sparseState.mPageOffset = subresource / 31 * 31;
            sparseState.mSparseMask = 1u << (subresource - sparseState.mPageOffset);
            sparseState.mState = state;
            // This invalidaes erasePage
            mSparseStates.insert(std::make_pair(handle, sparseState));
            if (sparseState.GetIsLocked()) ++resource.mLockCount;
        }
        return true;
    }
    D3D12_RESOURCE_BARRIER BarrierStateManager::CreateBarrier(
        ID3D12Resource* d3dResource,
        D3D12_RESOURCE_STATES from, D3D12_RESOURCE_STATES to,
        int subresource, BarrierMeta meta) {
        D3D12_RESOURCE_BARRIER barrier;
        barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
        barrier.Flags = D3D12_RESOURCE_BARRIER_FLAG_NONE;
        barrier.Transition.pResource = d3dResource;
        barrier.Transition.StateBefore = from & (D3D12_RESOURCE_STATES)0x7fffffff;
        barrier.Transition.StateAfter = to & (D3D12_RESOURCE_STATES)0x7fffffff;
        barrier.Transition.Subresource = subresource;
#if false
        std::stringstream str;
        str << " " << D3D::GetResourceStateString(from);
        str << " " << D3D::GetResourceStateString(to);
        str << " " << subresource;
        str << " " << tex.mWidth << "x" << (int)tex.mMips;
        str << " " << std::hex << barrier.Transition.pResource;
        str << "\n";
        OutputDebugStringA(str.str().c_str());
#endif
        return barrier;
    }
}



// Unused Texture copy code
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

#endif

// Unused vsync without present
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

