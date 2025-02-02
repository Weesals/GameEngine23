#include "D3DRaytracing.h"
#include "D3DUtility.h"

#include <d3dx12.h>
#include <cassert>
#include <sstream>

static const D3D12_RESOURCE_DESC BASIC_BUFFER_DESC = {
    .Dimension = D3D12_RESOURCE_DIMENSION_BUFFER,
    .Width = 0, // Will be changed in copies
    .Height = 1,
    .DepthOrArraySize = 1,
    .MipLevels = 1,
    .SampleDesc = DefaultSampleDesc(),
    .Layout = D3D12_TEXTURE_LAYOUT_ROW_MAJOR
};

D3D12_GPU_VIRTUAL_ADDRESS  D3DAccelerationStructure::GetGPUAddress() {
    return mBuffer->GetGPUVirtualAddress();
}
void D3DAccelerationStructure::CreateBuffers(ID3D12Device5* device,
    const D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_INPUTS& inputs,
    UINT64* updateScratchSize
) {
    auto makeBuffer = [&](UINT64 size, auto initialState) {
        auto desc = BASIC_BUFFER_DESC;
        desc.Width = size;
        desc.Flags = D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS;
        ComPtr<ID3D12Resource> buffer;
        device->CreateCommittedResource(&D3D::DefaultHeap, D3D12_HEAP_FLAG_NONE,
                                        &desc, initialState, nullptr,
                                        IID_PPV_ARGS(&buffer));
        return buffer;
    };

    D3D12_RAYTRACING_ACCELERATION_STRUCTURE_PREBUILD_INFO prebuildInfo;
    device->GetRaytracingAccelerationStructurePrebuildInfo(&inputs,
        &prebuildInfo);
    if (updateScratchSize)
        *updateScratchSize = prebuildInfo.UpdateScratchDataSizeInBytes;

    mBuffer = makeBuffer(prebuildInfo.ResultDataMaxSizeInBytes,
        D3D12_RESOURCE_STATE_RAYTRACING_ACCELERATION_STRUCTURE);
    mScratchBuffer = makeBuffer(prebuildInfo.ScratchDataSizeInBytes,
        D3D12_RESOURCE_STATE_COMMON);
    if (updateScratchSize != nullptr) {
        mUpdateScratchBuffer = makeBuffer(std::max(prebuildInfo.UpdateScratchDataSizeInBytes, 8ULL),
            D3D12_RESOURCE_STATE_COMMON);
    }
}
void D3DAccelerationStructure::Update(ID3D12GraphicsCommandList4* cmdList,
    const D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_INPUTS& inputs) {
    D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_DESC buildDesc = {
        .DestAccelerationStructureData = mBuffer->GetGPUVirtualAddress(),
        .Inputs = inputs,
        .ScratchAccelerationStructureData = mScratchBuffer->GetGPUVirtualAddress() };
    cmdList->BuildRaytracingAccelerationStructure(&buildDesc, 0, nullptr);
}

std::shared_ptr<D3DAccelerationStructure> D3DRaytracing::MakeBLAS(ID3D12Device5* device,
    ID3D12GraphicsCommandList4* cmdList,
    D3D12_GPU_VIRTUAL_ADDRESS_AND_STRIDE vertexBuffer, DXGI_FORMAT vertexFormat, UINT vertexCount,
    D3D12_GPU_VIRTUAL_ADDRESS indexBuffer, DXGI_FORMAT indexFormat, UINT indexCount
) {
    D3D12_RAYTRACING_GEOMETRY_DESC geometryDesc = {
        .Type = D3D12_RAYTRACING_GEOMETRY_TYPE_TRIANGLES,
        .Flags = D3D12_RAYTRACING_GEOMETRY_FLAG_OPAQUE,
        .Triangles = {
            .Transform3x4 = 0,
            .IndexFormat = indexFormat,
            .VertexFormat = vertexFormat,
            .IndexCount = indexCount,
            .VertexCount = vertexCount,
            .IndexBuffer = indexBuffer,
            .VertexBuffer = vertexBuffer,
        }
    };
    D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_INPUTS inputs = {
        .Type = D3D12_RAYTRACING_ACCELERATION_STRUCTURE_TYPE_BOTTOM_LEVEL,
        .Flags = D3D12_RAYTRACING_ACCELERATION_STRUCTURE_BUILD_FLAG_PREFER_FAST_TRACE,
        .NumDescs = 1,
        .DescsLayout = D3D12_ELEMENTS_LAYOUT_ARRAY,
        .pGeometryDescs = &geometryDesc
    };

    auto blas = std::make_shared<D3DAccelerationStructure>();
    blas->CreateBuffers(device, inputs);
    blas->Update(cmdList, inputs);
    return blas;
}
std::shared_ptr<D3DAccelerationStructure> D3DRaytracing::MakeTLAS(
    ID3D12Device5* device,
    ID3D12GraphicsCommandList4* cmdList,
    ID3D12Resource* instances, UINT numInstances,
    UINT64* updateScratchSize) {
    D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_INPUTS inputs = {
        .Type = D3D12_RAYTRACING_ACCELERATION_STRUCTURE_TYPE_TOP_LEVEL,
        .Flags = D3D12_RAYTRACING_ACCELERATION_STRUCTURE_BUILD_FLAG_ALLOW_UPDATE,
        .NumDescs = numInstances,
        .DescsLayout = D3D12_ELEMENTS_LAYOUT_ARRAY,
        .InstanceDescs = instances->GetGPUVirtualAddress() };
    auto tlas = std::make_shared<D3DAccelerationStructure>();
    tlas->CreateBuffers(device, inputs, updateScratchSize);
    tlas->Update(cmdList, inputs);
    return tlas;
}
