#pragma once

#include <unordered_map>
#include <algorithm>
#include <memory>
#include <deque>

#include "D3DGraphicsDevice.h"
#include "D3DShader.h"
#include "GraphicsUtility.h"
#include "Material.h"

// Stores a cache of Constant Buffers that have been generated
// so that they can be efficiently reused where appropriate
    // The GPU data for a set of shaders, rendering state, and vertex attributes
struct D3DConstantBuffer
{
    ComPtr<ID3D12Resource> mConstantBuffer;
    D3D12_GPU_DESCRIPTOR_HANDLE mConstantBufferHandle;
    int mSize;
    int mDetatchRefCount;
};
class D3DConstantBufferCache : public PerFrameItemStore<D3DConstantBuffer>
{
    // Temporary data store for filling CB data (before hashing it)
    std::vector<uint8_t> tData;

public:
    D3DConstantBufferCache();

    // Find or allocate a constant buffer for the specified material and CB layout
    D3DConstantBuffer* RequireConstantBuffer(const Material& material
        , const ShaderBase::ConstantBuffer& cBuffer
        , D3DGraphicsDevice& d3d12);
    // Find or allocate a constant buffer for the specified material and CB layout
    D3DConstantBuffer* RequireConstantBuffer(std::span<const uint8_t> tData
        , D3DGraphicsDevice& d3d12);

};
