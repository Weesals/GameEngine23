#define PIX 1

#include "D3DGraphicsDevice.h"
#include <sstream>
#include <stdexcept>

#include <d3dx12.h>

#pragma comment(lib, "d3d12.lib")
#pragma comment(lib, "dxgi.lib")
#pragma comment(lib, "dxguid.lib")
#pragma comment(lib, "d3dcompiler.lib")


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

// From DirectXTK wiki
inline void ThrowIfFailed(HRESULT hr)
{
    if (FAILED(hr))
    {
        std::ostringstream err;
        err << "Exception thrown, Code: " << std::hex << hr << std::endl;
        OutputDebugStringA(err.str().c_str());
        throw std::runtime_error(err.str());
    }
}

// Initialise D3D with the specified window
D3DGraphicsDevice::D3DGraphicsDevice(const WindowWin32& window)
{
#if PIX
    if (GetModuleHandle(L"WinPixGpuCapturer.dll") == 0) {
        auto path = GetLatestWinPixGpuCapturerPath();
        if (!path.empty()) LoadLibrary(path.c_str());
    }
#endif
    CoInitialize(nullptr);

    auto hWnd = window.GetHWND();

    UINT dxgiFactoryFlags = 0;

    // Enable debug mode in debug builds
#if defined(_DEBUG)
    {
        ComPtr<ID3D12Debug> debugController;
        if (SUCCEEDED(D3D12GetDebugInterface(IID_PPV_ARGS(&debugController))))
        {
            debugController->EnableDebugLayer();
        }
    }
    dxgiFactoryFlags |= DXGI_CREATE_FACTORY_DEBUG;
#endif
    ComPtr<IDXGIFactory4> d3dFactory;
    ThrowIfFailed(CreateDXGIFactory2(dxgiFactoryFlags, IID_PPV_ARGS(&d3dFactory)));

    // Find adapters
    std::vector<ComPtr<IDXGIAdapter1>> adapters;
    ComPtr<IDXGIAdapter1> pAdapter = nullptr;
    for (UINT adapterIndex = 0; d3dFactory->EnumAdapters1(adapterIndex, &pAdapter) != DXGI_ERROR_NOT_FOUND; ++adapterIndex)
    {
        DXGI_ADAPTER_DESC1 adapterDesc;
        if (SUCCEEDED(pAdapter->GetDesc1(&adapterDesc)))
        {
            OutputDebugString(adapterDesc.Description);
            OutputDebugString(L"\n");
            adapters.push_back(pAdapter);
        }
    }

    // Create the device
    ThrowIfFailed(D3D12CreateDevice(adapters[0].Get(), D3D_FEATURE_LEVEL_11_0, IID_PPV_ARGS(&mD3DDevice)));

    // Create the command queue
    D3D12_COMMAND_QUEUE_DESC queueDesc = {};
    queueDesc.Flags = D3D12_COMMAND_QUEUE_FLAG_NONE;
    queueDesc.Type = D3D12_COMMAND_LIST_TYPE_DIRECT;
    ThrowIfFailed(mD3DDevice->CreateCommandQueue(&queueDesc, IID_PPV_ARGS(&mCmdQueue)));

    // Create descriptor heaps.
    {
        // Describe and create a shader resource view (SRV) and constant 
        // buffer view (CBV) descriptor heap.
        D3D12_DESCRIPTOR_HEAP_DESC cbvSrvHeapDesc = {};
        cbvSrvHeapDesc.NumDescriptors = 1024;
        cbvSrvHeapDesc.Type = D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV;
        cbvSrvHeapDesc.Flags = D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE;
        ThrowIfFailed(mD3DDevice->CreateDescriptorHeap(&cbvSrvHeapDesc, IID_PPV_ARGS(&mSRVHeap)));

        // Describe and create a sampler descriptor heap.
        D3D12_DESCRIPTOR_HEAP_DESC samplerHeapDesc = {};
        samplerHeapDesc.NumDescriptors = 64;
        samplerHeapDesc.Type = D3D12_DESCRIPTOR_HEAP_TYPE_SAMPLER;
        samplerHeapDesc.Flags = D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE;
        ThrowIfFailed(mD3DDevice->CreateDescriptorHeap(&samplerHeapDesc, IID_PPV_ARGS(&mSamplerHeap)));

        // Describe and create a render target view (RTV) descriptor heap.
        D3D12_DESCRIPTOR_HEAP_DESC rtvHeapDesc = {};
        rtvHeapDesc.NumDescriptors = 128;
        rtvHeapDesc.Type = D3D12_DESCRIPTOR_HEAP_TYPE_RTV;
        rtvHeapDesc.Flags = D3D12_DESCRIPTOR_HEAP_FLAG_NONE;
        ThrowIfFailed(mD3DDevice->CreateDescriptorHeap(&rtvHeapDesc, IID_PPV_ARGS(&mRTVHeap)));

        // Describe and create a depth stencil view (DSV) descriptor heap.
        D3D12_DESCRIPTOR_HEAP_DESC dsvHeapDesc = {};
        dsvHeapDesc.NumDescriptors = 64;
        dsvHeapDesc.Type = D3D12_DESCRIPTOR_HEAP_TYPE_DSV;
        dsvHeapDesc.Flags = D3D12_DESCRIPTOR_HEAP_FLAG_NONE;
        ThrowIfFailed(mD3DDevice->CreateDescriptorHeap(&dsvHeapDesc, IID_PPV_ARGS(&mDSVHeap)));

        mDescriptorHandleSizeRTV = mD3DDevice->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_RTV);
        mDescriptorHandleSizeDSV = mD3DDevice->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_DSV);
        mDescriptorHandleSizeSRV = mD3DDevice->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV);
    }

    // Check the window for how large the backbuffer should be
    mResolution = window.GetClientSize();

    // Create the swap chain
    DXGI_SWAP_CHAIN_DESC1 swapChainDesc = {};
    swapChainDesc.BufferCount = FrameCount;
    swapChainDesc.Width = mResolution.x;
    swapChainDesc.Height = mResolution.y;
    swapChainDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    swapChainDesc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    swapChainDesc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;
    swapChainDesc.SampleDesc.Count = 1;

    ComPtr<IDXGISwapChain1> swapChain;
    ThrowIfFailed(d3dFactory->CreateSwapChainForHwnd(mCmdQueue.Get(), hWnd, &swapChainDesc, nullptr, nullptr, &swapChain));
    ThrowIfFailed(swapChain.As(&mSwapChain));
    mSwapChain->SetColorSpace1(DXGI_COLOR_SPACE_RGB_FULL_G22_NONE_P709);
}

D3DGraphicsDevice::~D3DGraphicsDevice()
{
    CoUninitialize();
}
void D3DGraphicsDevice::SetResolution(Int2 res) {
    if (mResolution == res) return;
    mResolution = res;
    ResizeSwapBuffers();
}
void D3DGraphicsDevice::ResizeSwapBuffers() {
    auto hr = mSwapChain->ResizeBuffers(0, (UINT)mResolution.x, (UINT)mResolution.y, DXGI_FORMAT_UNKNOWN, 0);
    ThrowIfFailed(hr);
}
