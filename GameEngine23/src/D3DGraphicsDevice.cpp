#define PIX 0

#include "D3DGraphicsDevice.h"
#include <sstream>
#include <stdexcept>
#include <../../CSBindings/src/CSBindings.h>

#include <d3dx12.h>

#pragma comment(lib, "d3d12.lib")
#pragma comment(lib, "dxgi.lib")
#pragma comment(lib, "dxguid.lib")
#pragma comment(lib, "d3dcompiler.lib")

extern void* SimpleProfilerMarker(const char* name);
extern void SimpleProfilerMarkerEnd(void* zone);

extern "C" HMODULE gPixModule = 0;

void ThrowIfFailed(HRESULT hr) {
    if (FAILED(hr)) {
        std::ostringstream err;
        err << "Exception thrown, Code: " << std::hex << hr << std::endl;
        OutputDebugStringA(err.str().c_str());
        throw std::runtime_error(err.str());
    }
}

#if PIX
#include <filesystem>
#include <shlobj.h>

static std::wstring GetLatestWinPixGpuCapturerPath() {
    LPWSTR programFilesPath = nullptr;
    SHGetKnownFolderPath(FOLDERID_ProgramFiles, KF_FLAG_DEFAULT, NULL, &programFilesPath);

    std::filesystem::path pixInstallationPath = programFilesPath;
    pixInstallationPath /= "Microsoft PIX";

    std::wstring newestVersionFound;

    for (auto const& directory_entry : std::filesystem::directory_iterator(pixInstallationPath)) {
        if (directory_entry.is_directory()) {
            if (newestVersionFound.empty() || newestVersionFound < directory_entry.path().filename().c_str()) {
                newestVersionFound = directory_entry.path().filename().c_str();
            }
        }
    }

    if (newestVersionFound.empty()) return { };

    return pixInstallationPath / newestVersionFound / L"WinPixGpuCapturer.dll";
}

#endif

// Initialise D3D with the specified window
D3DGraphicsDevice::D3DGraphicsDevice()
{
#if PIX
    auto* pixZone = SimpleProfilerMarker("Load PIX");
    gPixModule = GetModuleHandle(L"WinPixGpuCapturer.dll");
    if (gPixModule == 0) {
        auto path = GetLatestWinPixGpuCapturerPath();
        if (GetFileAttributes(path.c_str()) == INVALID_FILE_ATTRIBUTES) path.clear();
        if (!path.empty()) gPixModule = LoadLibrary(path.c_str());
    }
    SimpleProfilerMarkerEnd(pixZone);
#endif
    CoInitialize(nullptr);

    UINT dxgiFactoryFlags = 0;

    // Enable debug mode in debug builds
#if defined(_DEBUG)
    {
        auto* d3dDbgZone = SimpleProfilerMarker("Load D3DDebug");
        ComPtr<ID3D12Debug> debugController;
        if (SUCCEEDED(D3D12GetDebugInterface(IID_PPV_ARGS(&debugController)))) {
            debugController->EnableDebugLayer();
        }
        SimpleProfilerMarkerEnd(d3dDbgZone);
    }
    dxgiFactoryFlags |= DXGI_CREATE_FACTORY_DEBUG;
#endif
    ThrowIfFailed(CreateDXGIFactory2(dxgiFactoryFlags, IID_PPV_ARGS(&mD3DFactory)));

    auto adapterZone = SimpleProfilerMarker("Enum Adapters");
    // Find adapters
    std::vector<ComPtr<IDXGIAdapter1>> adapters;
    ComPtr<IDXGIAdapter1> pAdapter = nullptr;
    for (UINT adapterIndex = 0; mD3DFactory->EnumAdapters1(adapterIndex, &pAdapter) != DXGI_ERROR_NOT_FOUND; ++adapterIndex)
    {
        DXGI_ADAPTER_DESC1 adapterDesc;
        if (SUCCEEDED(pAdapter->GetDesc1(&adapterDesc)))
        {
            OutputDebugString(L"[Graphics] Adapter Found - ");
            OutputDebugString(adapterDesc.Description);
            OutputDebugString(L"\n");
            adapters.push_back(pAdapter);
        }
    }
    SimpleProfilerMarkerEnd(adapterZone);

    auto createDeviceZone = SimpleProfilerMarker("Create Device");

    // Create the device
    SYSTEM_POWER_STATUS sps;
    bool useLowPower = GetSystemPowerStatus(&sps) && (sps.ACLineStatus == 0);
    if ((GetKeyState(VK_CAPITAL) & 0x00ff)) useLowPower = !useLowPower;

    int DeviceId = 0;// useLowPower ? 0 : 1;
    auto result = D3D12CreateDevice(adapters[std::min(DeviceId, (int)adapters.size() - 1)].Get(),
        D3D_FEATURE_LEVEL_11_1, IID_PPV_ARGS(&mD3DDevice));
    ThrowIfFailed(result);
    mD3DDevice->SetName(L"Device");

    SimpleProfilerMarkerEnd(createDeviceZone);

#if defined(_DEBUG)
    // Prevent GPU clocks from changing
    //mD3DDevice->SetStablePowerState(TRUE);
#endif

    auto createQueueZone = SimpleProfilerMarker("Create Queue");

    // Create the command queue
    D3D12_COMMAND_QUEUE_DESC queueDesc = {};
    queueDesc.Flags = D3D12_COMMAND_QUEUE_FLAG_NONE;
    queueDesc.Type = D3D12_COMMAND_LIST_TYPE_DIRECT;
    ThrowIfFailed(mD3DDevice->CreateCommandQueue(&queueDesc, IID_PPV_ARGS(&mCmdQueue)));
    mCmdQueue->SetName(L"CmdQueue");

    SimpleProfilerMarkerEnd(createQueueZone);

    // Create descriptor heaps.
    {
        // Describe and create a shader resource view (SRV) and constant 
        // buffer view (CBV) descriptor heap.
        D3D12_DESCRIPTOR_HEAP_DESC cbvSrvHeapDesc = {};
        cbvSrvHeapDesc.NumDescriptors = 1024;
        cbvSrvHeapDesc.Type = D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV;
        cbvSrvHeapDesc.Flags = D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE;
        ThrowIfFailed(mD3DDevice->CreateDescriptorHeap(&cbvSrvHeapDesc, IID_PPV_ARGS(&mSRVHeap)));
        mSRVHeap->SetName(L"SRV Heap");

        // Describe and create a sampler descriptor heap.
        // NOTE: Currently unused/unsupported
        D3D12_DESCRIPTOR_HEAP_DESC samplerHeapDesc = {};
        samplerHeapDesc.NumDescriptors = 64;
        samplerHeapDesc.Type = D3D12_DESCRIPTOR_HEAP_TYPE_SAMPLER;
        samplerHeapDesc.Flags = D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE;
        ThrowIfFailed(mD3DDevice->CreateDescriptorHeap(&samplerHeapDesc, IID_PPV_ARGS(&mSamplerHeap)));
        mSamplerHeap->SetName(L"Sampler Heap");

        // Describe and create a render target view (RTV) descriptor heap.
        D3D12_DESCRIPTOR_HEAP_DESC rtvHeapDesc = {};
        rtvHeapDesc.NumDescriptors = 128;
        rtvHeapDesc.Type = D3D12_DESCRIPTOR_HEAP_TYPE_RTV;
        rtvHeapDesc.Flags = D3D12_DESCRIPTOR_HEAP_FLAG_NONE;
        ThrowIfFailed(mD3DDevice->CreateDescriptorHeap(&rtvHeapDesc, IID_PPV_ARGS(&mRTVHeap)));
        mRTVHeap->SetName(L"RTV Heap");

        // Describe and create a depth stencil view (DSV) descriptor heap.
        D3D12_DESCRIPTOR_HEAP_DESC dsvHeapDesc = {};
        dsvHeapDesc.NumDescriptors = 64;
        dsvHeapDesc.Type = D3D12_DESCRIPTOR_HEAP_TYPE_DSV;
        dsvHeapDesc.Flags = D3D12_DESCRIPTOR_HEAP_FLAG_NONE;
        ThrowIfFailed(mD3DDevice->CreateDescriptorHeap(&dsvHeapDesc, IID_PPV_ARGS(&mDSVHeap)));
        mDSVHeap->SetName(L"DSV Heap");

        mDescriptorHandleSizeRTV = mD3DDevice->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_RTV);
        mDescriptorHandleSizeDSV = mD3DDevice->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_DSV);
        mDescriptorHandleSizeSRV = mD3DDevice->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV);
    }
}

D3DGraphicsDevice::~D3DGraphicsDevice() {
    CoUninitialize();
}
void D3DGraphicsDevice::CheckDeviceState() const {
    auto reason = mD3DDevice->GetDeviceRemovedReason();
    if (reason != S_OK) {
        WCHAR* errorString = nullptr;
        FormatMessage(FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_IGNORE_INSERTS,
            nullptr, reason, MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT),
            (LPWSTR)&errorString, 0, nullptr);
        OutputDebugStringW(errorString);
        throw "Device is lost!";
    }
}
