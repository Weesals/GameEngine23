#include "D3DGraphicsDevice.h"
#include "D3DShader.h"
#include "GraphicsUtility.h"
#include "D3DUtility.h"
#include "D3DResourceCache.h"

#include <memory>

class D3DAccelerationStructure : public std::enable_shared_from_this<D3DAccelerationStructure>{
    ComPtr<ID3D12Resource> mBuffer;
    ComPtr<ID3D12Resource> mScratchBuffer;
    ComPtr<ID3D12Resource> mUpdateScratchBuffer;
public:
    virtual ~D3DAccelerationStructure() {}
    D3D12_GPU_VIRTUAL_ADDRESS GetGPUAddress();
    void CreateBuffers(ID3D12Device5* device,
        const D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_INPUTS& inputs,
        UINT64* updateScratchSize = nullptr
    );
    void Update(ID3D12GraphicsCommandList4* cmdList, const D3D12_BUILD_RAYTRACING_ACCELERATION_STRUCTURE_INPUTS& inputs);
};

class D3DRaytracing {
    D3DAccelerationStructure mTLAS;
public:

    std::shared_ptr<D3DAccelerationStructure> MakeBLAS(ID3D12Device5* device,
        ID3D12GraphicsCommandList4* cmdList,
        D3D12_GPU_VIRTUAL_ADDRESS_AND_STRIDE vertexBuffer, DXGI_FORMAT vertexFormat, UINT vertexCount,
        D3D12_GPU_VIRTUAL_ADDRESS indexBuffer, DXGI_FORMAT indexFormat, UINT indexCount
    );

    std::shared_ptr<D3DAccelerationStructure> MakeTLAS(ID3D12Device5* device,
        ID3D12GraphicsCommandList4* cmdList,
        ID3D12Resource* instances, UINT numInstances,
        UINT64* updateScratchSize);

};
