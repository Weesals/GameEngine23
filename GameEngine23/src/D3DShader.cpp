#include <fstream>

#include "D3DShader.h"

#pragma comment(lib, "dxcompiler.lib")

using Microsoft::WRL::ComPtr;

#if 1
class DXCInclude : public IDxcIncludeHandler {
    std::wstring mLocalPath;
    std::wstring mAbsolutePath;
    ComPtr<IDxcUtils> mUtils;
    ComPtr<IDxcIncludeHandler> mIncludeHandler;
public:
    DXCInclude(const ComPtr<IDxcUtils>& utils, const ComPtr<IDxcIncludeHandler>& includeHandler)
        : mUtils(utils), mIncludeHandler(includeHandler) { }
    virtual ~DXCInclude() { }
    void SetLocalPath(std::wstring&& path) {
        mLocalPath = path;
    }
    void SetAbsolutePath(std::wstring&& path) {
        mAbsolutePath = path;
    }
    virtual HRESULT STDMETHODCALLTYPE LoadSource(
        _In_z_ LPCWSTR pFilename,                                 // Candidate filename.
        _COM_Outptr_result_maybenull_ IDxcBlob** ppIncludeSource  // Resultant source object for included file, nullptr if not found.
    ) override {
        //HRESULT hr = mIncludeHandler->LoadSource(pFilename, ppIncludeSource);
        //if (SUCCEEDED(hr)) return hr;

        std::ifstream file;
        if (!file.is_open()) file.open(mLocalPath + pFilename, std::ios::binary);
        if (!file.is_open()) file.open(mAbsolutePath + pFilename, std::ios::binary);
        if (!file.is_open()) return E_FAIL;

        std::vector<char> contents(
            (std::istreambuf_iterator<char>(file)),
            std::istreambuf_iterator<char>()
        );
        IDxcBlobEncoding* fileBlob;
        mUtils->CreateBlob(contents.data(), (UINT32)contents.size(), 0, &fileBlob);
        //std::memcpy(fileBlob->GetBufferPointer(), contents.data(), contents.size());
        *ppIncludeSource = fileBlob;
        return S_OK;

        /*std::wstring filePath;
        if (std::filesystem::exists(filePath = mLocalPath + pFilename));
        else if (std::filesystem::exists(filePath = mAbsolutePath + pFilename));
        else return E_FAIL;

        ComPtr<IDxcBlobEncoding> fileBlob;
        HRESULT hr = mUtils->LoadFile(filePath.c_str(), nullptr, &fileBlob);
        if (SUCCEEDED(hr)) {
            *ppIncludeSource = fileBlob.Detach();
        }
        return hr;*/
    }
    HRESULT STDMETHODCALLTYPE QueryInterface(
        REFIID riid,
        _COM_Outptr_ void __RPC_FAR* __RPC_FAR* ppvObject
    ) override {
        return mIncludeHandler->QueryInterface(riid, ppvObject);
    }
    ULONG STDMETHODCALLTYPE AddRef(void) override { return 0; }
    ULONG STDMETHODCALLTYPE Release(void) override { return 0; }
};

class StandardInclude : public ID3DInclude {
    std::string mLocalPath;
    std::string mAbsolutePath;
public:
    void SetLocalPath(std::string&& path) {
        mLocalPath = path;
    }
    void SetAbsolutePath(std::string&& path) {
        mAbsolutePath = path;
    }
    HRESULT Open(D3D_INCLUDE_TYPE IncludeType, LPCSTR pFileName, LPCVOID pParentData, LPCVOID* ppData, UINT* pBytes) override {
        std::string filePath;
        if (IncludeType == D3D_INCLUDE_LOCAL)
            filePath = mLocalPath + pFileName;
        else if (IncludeType == D3D_INCLUDE_SYSTEM)
            filePath = mAbsolutePath + pFileName;
        else
            return E_FAIL;

        HANDLE fileHandle = CreateFileA(filePath.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_FLAG_SEQUENTIAL_SCAN, nullptr);
        if (fileHandle == INVALID_HANDLE_VALUE) return E_FAIL;

        LARGE_INTEGER fileSize;
        GetFileSizeEx(fileHandle, &fileSize);
        HANDLE mapHandle = CreateFileMappingA(fileHandle, nullptr, PAGE_READONLY, fileSize.HighPart, fileSize.LowPart, pFileName);
        *ppData = mapHandle != INVALID_HANDLE_VALUE ? MapViewOfFile(mapHandle, FILE_MAP_READ, 0, 0, 0) : nullptr;
        *pBytes = (UINT)fileSize.LowPart;

        if (mapHandle != INVALID_HANDLE_VALUE) CloseHandle(mapHandle);
        if (fileHandle != INVALID_HANDLE_VALUE) CloseHandle(fileHandle);

        return *ppData != nullptr;
    }
    HRESULT Close(LPCVOID pData) override {
        UnmapViewOfFile(pData);
        return S_OK;
    }
};

std::wstring ToWide(const std::string_view& str) {
    std::wstring out;
    out.resize(str.length());
    std::transform(str.begin(), str.end(), out.data(), [](char c) { return (wchar_t)c; });
    return out;
}
std::string ToAscii(const std::wstring_view& str) {
    std::string out;
    out.resize(str.length());
    std::transform(str.begin(), str.end(), out.data(), [](wchar_t c) { return (char)c; });
    return out;
}

// Compile shader and reflect uniform values / buffers
void D3DShader::CompileFromFile(const std::wstring_view& path, const std::string_view& entry, const std::string_view& profile, const DxcDefine* macros) {
    ComPtr<IDxcCompiler3> compiler;
    DxcCreateInstance(CLSID_DxcCompiler, IID_PPV_ARGS(&compiler));

    ComPtr<IDxcUtils> dxcUtils;
    DxcCreateInstance(CLSID_DxcUtils, IID_PPV_ARGS(&dxcUtils));
        
    ComPtr<IDxcIncludeHandler> includeHandler;
    dxcUtils->CreateDefaultIncludeHandler(&includeHandler);
    DXCInclude inc(dxcUtils, includeHandler);
    inc.SetLocalPath(std::filesystem::path(path).parent_path().wstring() + L"/");
    inc.SetAbsolutePath(L"Assets/include/");

    // Create DXC Compiler arguments
    std::vector<LPCWSTR> arguments;
    std::wstring wEntry = ToWide(entry);
    std::wstring wProfile = ToWide(profile);
    arguments.push_back(L"-E");
    arguments.push_back(wEntry.c_str());
    arguments.push_back(L"-T");
    arguments.push_back(wProfile.c_str());
    arguments.push_back(L"-HV 2021");
    for (const DxcDefine* macro = macros; macro->Name != nullptr; ++macro) {
        arguments.push_back(L"-D");
        arguments.push_back(macro->Name);
        arguments.push_back(macro->Value);
    }
    //arguments.push_back(L"/Od"); // Example: Disable optimization

    ComPtr<IDxcResult> pResult;
    ComPtr<IDxcBlob> dxcOutput;
    ComPtr<IDxcBlob> dxcReflection;
    while (true) {
        ComPtr<IDxcBlobEncoding> sourceBlob;
        auto hr = dxcUtils->LoadFile(path.data(), nullptr, &sourceBlob);

        if (SUCCEEDED(hr)) {
            StandardInclude stdInclude;
            stdInclude.SetLocalPath(std::filesystem::path(path).parent_path().string() + "/");
            stdInclude.SetAbsolutePath("Assets/include/");
            auto aPath = ToAscii(path);
            std::vector<D3D_SHADER_MACRO> d3dMacros;
            std::vector<std::string> d3dMacroStore;
            for (const DxcDefine* macro = macros; macro->Name != nullptr; ++macro) {
                d3dMacroStore.push_back(ToAscii(macro->Name));
                d3dMacroStore.push_back(ToAscii(macro->Value));
            }
            for (int m = 0; m < d3dMacroStore.size(); m += 2) {
                d3dMacros.push_back(D3D_SHADER_MACRO{ .Name = d3dMacroStore[m].c_str(), .Definition = d3dMacroStore[m + 1].c_str(), });
            }
            d3dMacros.push_back(D3D_SHADER_MACRO{ .Name = nullptr, .Definition = nullptr, });
            auto srcDat = sourceBlob->GetBufferPointer();
            auto srcLen = sourceBlob->GetBufferSize();
            ComPtr<ID3DBlob> preprocessed;
            ComPtr<ID3DBlob> preErrors;
            D3DPreprocess(srcDat, srcLen,
                aPath.c_str(), d3dMacros.data(), &stdInclude, &preprocessed, &preErrors);
            dxcUtils->CreateBlob(preprocessed->GetBufferPointer(), (UINT32)preprocessed->GetBufferSize(), 0, &sourceBlob);
        }

        if (SUCCEEDED(hr)) {
            std::vector<DxcDefine> defines;
            int macroCount = 0;
            if (macros != nullptr)
                for (; macros[macroCount].Name != nullptr; ++macroCount);
        }

        if (SUCCEEDED(hr)) {
            // Compile shader
            DxcBuffer sourceBuffer = { sourceBlob->GetBufferPointer(), sourceBlob->GetBufferSize(), 0, };
            hr = compiler->Compile(&sourceBuffer,
                arguments.data(), (UINT32)arguments.size(),
                &inc, IID_PPV_ARGS(pResult.GetAddressOf()));
        }
        /*auto hr = compiler->Compile(sourceBlob.Get(), path.c_str(), wEntry.c_str(),
            wProfile.c_str(), arguments.data(), (UINT32)arguments.size(),
            macros, macroCount, includeHandler.Get(), &pResult);*/

        ComPtr<IDxcBlobUtf16> pDebugDataPath;
        if (pResult != nullptr) {
            pResult->GetOutput(DXC_OUT_OBJECT, IID_PPV_ARGS(&dxcOutput), &pDebugDataPath);
        }
        //if (dxcOutput->GetBufferSize() == 0)
        {
            ComPtr<IDxcBlobEncoding> errors;
            if (pResult != nullptr) {
                pResult->GetErrorBuffer(&errors);
            }

            // Retrieve error messages
            const char* errorMsg =
                hr == ERROR_FILE_NOT_FOUND ? "File was not found!" :
                errors != nullptr ? (const char*)errors->GetBufferPointer() :
                FAILED(hr) ? "Failed to compile shader. Unknown error" :
                nullptr;
            if (errorMsg != nullptr) {
                OutputDebugStringA(errorMsg);
                if (dxcOutput == nullptr || dxcOutput->GetBufferSize() == 0)
                {
                    MessageBoxA(0, errorMsg, "Shader Compile Fail", 0);
                    //throw std::exception(errorMsg);
                    continue;
                }
            }
        }
        pResult->GetOutput(DXC_OUT_REFLECTION, IID_PPV_ARGS(&dxcReflection), nullptr);
        break;
    }

    ComPtr<ID3D12ShaderReflection> pShaderReflection = nullptr;

    auto dataSize = (UINT32)dxcOutput->GetBufferSize();
    D3DCreateBlob(dataSize, &mShader);
    std::memcpy(mShader->GetBufferPointer(), dxcOutput->GetBufferPointer(), dataSize);
    if (dxcReflection != nullptr) {
        //D3DReflect(mShader->GetBufferPointer(), mShader->GetBufferSize(), IID_PPV_ARGS(&pShaderReflection));
        DxcBuffer reflectionBuffer;
        reflectionBuffer.Ptr = dxcReflection->GetBufferPointer();
        reflectionBuffer.Size = dxcReflection->GetBufferSize();
        reflectionBuffer.Encoding = 0;
        dxcUtils->CreateReflection(&reflectionBuffer, IID_PPV_ARGS(&pShaderReflection));

        D3D12_SHADER_DESC shaderDesc;
        pShaderReflection->GetDesc(&shaderDesc);

        // Get all constant buffers
        for (UINT i = 0; i < shaderDesc.ConstantBuffers; ++i)
        {
            auto pBufferReflection = pShaderReflection->GetConstantBufferByIndex(i);

            D3D12_SHADER_BUFFER_DESC bufferDesc;
            pBufferReflection->GetDesc(&bufferDesc);

            if (bufferDesc.Type != D3D_CT_CBUFFER) continue;

            D3D12_SHADER_INPUT_BIND_DESC bindDesc;
            for (UINT b = 0; b < shaderDesc.BoundResources; ++b)
            {
                pShaderReflection->GetResourceBindingDesc(b, &bindDesc);
                if (strcmp(bindDesc.Name, bufferDesc.Name) == 0) break;
            }

            // The data we have extracted for this constant buffer
            ConstantBuffer cbuffer;
            cbuffer.mName = bufferDesc.Name;
            cbuffer.mSize = bufferDesc.Size;
            cbuffer.mBindPoint = bindDesc.BindPoint;

            // Iterate variables
            cbuffer.SetValuesCount(bufferDesc.Variables);
            for (UINT j = 0; j < bufferDesc.Variables; ++j)
            {
                ID3D12ShaderReflectionVariable* pVariableReflection = pBufferReflection->GetVariableByIndex(j);

                D3D12_SHADER_VARIABLE_DESC variableDesc;
                pVariableReflection->GetDesc(&variableDesc);

                // The values for this uniform
                UniformValue value{
                    .mName = variableDesc.Name,
                    .mOffset = (int)variableDesc.StartOffset,
                    .mSize = (int)variableDesc.Size,
                };
                cbuffer.GetValues()[j] = (value);
            }
            mReflection.mConstantBuffers.emplace_back(std::move(cbuffer));
        }

        // Get all bound resources
        for (UINT i = 0; i < shaderDesc.BoundResources; ++i)
        {
            D3D12_SHADER_INPUT_BIND_DESC resourceDesc;
            pShaderReflection->GetResourceBindingDesc(i, &resourceDesc);
            if (resourceDesc.Type == D3D_SHADER_INPUT_TYPE::D3D_SIT_TEXTURE)
            {
                ResourceBinding rbinding;
                rbinding.mName = resourceDesc.Name;
                rbinding.mBindPoint = resourceDesc.BindPoint;
                rbinding.mStride = -1;
                rbinding.mType = ResourceTypes::R_Texture;
                mReflection.mResourceBindings.push_back(rbinding);
            }
            if (resourceDesc.Type == D3D_SHADER_INPUT_TYPE::D3D_SIT_STRUCTURED)
            {
                ResourceBinding bbinding;
                bbinding.mName = resourceDesc.Name;
                bbinding.mBindPoint = resourceDesc.BindPoint;
                bbinding.mStride = resourceDesc.NumSamples;
                bbinding.mType = ResourceTypes::R_SBuffer;
                mReflection.mResourceBindings.push_back(bbinding);
            }
        }
        for (UINT i = 0; i < shaderDesc.InputParameters; ++i)
        {
            D3D12_SIGNATURE_PARAMETER_DESC inputDesc;
            pShaderReflection->GetInputParameterDesc(i, &inputDesc);
            ShaderBase::InputParameter parameter;
            parameter.mName = "";
            parameter.mSemantic = inputDesc.SemanticName;
            parameter.mSemanticIndex = inputDesc.SemanticIndex;
            parameter.mRegister = inputDesc.Register;
            parameter.mMask = inputDesc.Mask;
            parameter.mType =
                inputDesc.ComponentType == D3D_REGISTER_COMPONENT_UINT32 ? ParameterTypes::P_UInt :
                inputDesc.ComponentType == D3D_REGISTER_COMPONENT_SINT32 ? ParameterTypes::P_SInt :
                inputDesc.ComponentType == D3D_REGISTER_COMPONENT_FLOAT32 ? ParameterTypes::P_Float :
                ParameterTypes::P_Unknown;
            mReflection.mInputParameters.push_back(parameter);
        }
    }
    int end = 0;
}
#else
// Represents the D3D12 instance of a shader
class D3DShader : ShaderBase {
public:

    ComPtr<ID3DBlob> mShader;
    ShaderReflection mReflection;

    class StandardInclude : public ID3DInclude {
        std::string mLocalPath;
        std::string mAbsolutePath;
    public:
        void SetLocalPath(std::string&& path) {
            mLocalPath = path;
        }
        void SetAbsolutePath(std::string&& path) {
            mAbsolutePath = path;
        }
        HRESULT Open(D3D_INCLUDE_TYPE IncludeType, LPCSTR pFileName, LPCVOID pParentData, LPCVOID* ppData, UINT* pBytes) override {
            std::string filePath;
            if (IncludeType == D3D_INCLUDE_LOCAL)
                filePath = mLocalPath + pFileName;
            else if (IncludeType == D3D_INCLUDE_SYSTEM)
                filePath = mAbsolutePath + pFileName;
            else
                return E_FAIL;

            HANDLE fileHandle = CreateFileA(filePath.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING, FILE_FLAG_SEQUENTIAL_SCAN, nullptr);
            if (fileHandle == INVALID_HANDLE_VALUE) return E_FAIL;

            LARGE_INTEGER fileSize;
            GetFileSizeEx(fileHandle, &fileSize);
            HANDLE mapHandle = CreateFileMappingA(fileHandle, nullptr, PAGE_READONLY, fileSize.HighPart, fileSize.LowPart, pFileName);
            *ppData = mapHandle != INVALID_HANDLE_VALUE ? MapViewOfFile(mapHandle, FILE_MAP_READ, 0, 0, 0) : nullptr;
            *pBytes = (UINT)fileSize.LowPart;

            if (mapHandle != INVALID_HANDLE_VALUE) CloseHandle(mapHandle);
            if (fileHandle != INVALID_HANDLE_VALUE) CloseHandle(fileHandle);

            return *ppData != nullptr;
        }
        HRESULT Close(LPCVOID pData) override {
            UnmapViewOfFile(pData);
            return S_OK;
        }
    };

    // Compile shader and reflect uniform values / buffers
    void CompileFromFile(const std::wstring& path, const std::string& entry, const std::string& profile, const D3D_SHADER_MACRO* macros = nullptr)
    {
        ComPtr<ID3D10Blob> compilationErrors = nullptr;
        while (true) {
            StandardInclude inc;
            inc.SetLocalPath(std::filesystem::path(path).parent_path().string() + "/");
            inc.SetAbsolutePath("Assets/include/");
            auto hr = D3DCompileFromFile(
                path.c_str(),
                macros,
                &inc,
                entry.c_str(),
                profile.c_str(),
                0,
                0,
                &mShader,
                &compilationErrors
            );

            if (!FAILED(hr)) break;

            // Retrieve error messages
            const char* errorMsg =
                hr == ERROR_FILE_NOT_FOUND ? "File was not found!" :
                compilationErrors != nullptr ? (const char*)compilationErrors->GetBufferPointer() :
                "";
            if (errorMsg != nullptr)
            {
                OutputDebugStringA(errorMsg);
                throw std::exception(errorMsg);
            }
        }

        ComPtr<ID3D12ShaderReflection> pShaderReflection = nullptr;
        D3DReflect(mShader->GetBufferPointer(), mShader->GetBufferSize(), IID_PPV_ARGS(&pShaderReflection));

        D3D12_SHADER_DESC shaderDesc;
        pShaderReflection->GetDesc(&shaderDesc);

        // Get all constant buffers
        for (UINT i = 0; i < shaderDesc.ConstantBuffers; ++i)
        {
            auto pBufferReflection = pShaderReflection->GetConstantBufferByIndex(i);

            D3D12_SHADER_BUFFER_DESC bufferDesc;
            pBufferReflection->GetDesc(&bufferDesc);

            if (bufferDesc.Type != D3D_CT_CBUFFER) continue;

            D3D12_SHADER_INPUT_BIND_DESC bindDesc;
            for (UINT b = 0; b < shaderDesc.BoundResources; ++b)
            {
                pShaderReflection->GetResourceBindingDesc(b, &bindDesc);
                if (strcmp(bindDesc.Name, bufferDesc.Name) == 0) break;
            }

            // The data we have extracted for this constant buffer
            ConstantBuffer cbuffer;
            cbuffer.mName = bufferDesc.Name;
            cbuffer.mSize = bufferDesc.Size;
            cbuffer.mBindPoint = bindDesc.BindPoint;

            // Iterate variables
            for (UINT j = 0; j < bufferDesc.Variables; ++j)
            {
                ID3D12ShaderReflectionVariable* pVariableReflection = pBufferReflection->GetVariableByIndex(j);

                D3D12_SHADER_VARIABLE_DESC variableDesc;
                pVariableReflection->GetDesc(&variableDesc);

                // The values for this uniform
                UniformValue value {
                    .mName = variableDesc.Name,
                    .mOffset = (int)variableDesc.StartOffset,
                    .mSize = (int)variableDesc.Size,
                };
                cbuffer.mValues.emplace_back(value);
            }
            mReflection.mConstantBuffers.push_back(cbuffer);
        }

        // Get all bound resources
        for (UINT i = 0; i < shaderDesc.BoundResources; ++i)
        {
            D3D12_SHADER_INPUT_BIND_DESC resourceDesc;
            pShaderReflection->GetResourceBindingDesc(i, &resourceDesc);
            if (resourceDesc.Type == D3D_SHADER_INPUT_TYPE::D3D_SIT_TEXTURE)
            {
                ResourceBinding rbinding;
                rbinding.mName = resourceDesc.Name;
                rbinding.mBindPoint = resourceDesc.BindPoint;
                rbinding.mType = ResourceTypes::R_Texture;
                mReflection.mResourceBindings.push_back(rbinding);
            }
            if (resourceDesc.Type == D3D_SHADER_INPUT_TYPE::D3D_SIT_STRUCTURED)
            {
                ResourceBinding bbinding;
                bbinding.mName = resourceDesc.Name;
                bbinding.mBindPoint = resourceDesc.BindPoint;
                bbinding.mStride = resourceDesc.NumSamples;
                bbinding.mType = ResourceTypes::R_SBuffer;
                mReflection.mResourceBindings.push_back(bbinding);
            }
        }
        for (UINT i = 0; i < shaderDesc.InputParameters; ++i)
        {
            D3D12_SIGNATURE_PARAMETER_DESC inputDesc;
            pShaderReflection->GetInputParameterDesc(i, &inputDesc);
            ShaderBase::InputParameter parameter;
            parameter.mName = "";
            parameter.mSemantic = inputDesc.SemanticName;
            parameter.mSemanticIndex = inputDesc.SemanticIndex;
            parameter.mRegister = inputDesc.Register;
            parameter.mMask = inputDesc.Mask;
            parameter.mType =
                inputDesc.ComponentType == D3D_REGISTER_COMPONENT_UINT32 ? ParameterTypes::P_UInt :
                inputDesc.ComponentType == D3D_REGISTER_COMPONENT_SINT32 ? ParameterTypes::P_SInt :
                inputDesc.ComponentType == D3D_REGISTER_COMPONENT_FLOAT32 ? ParameterTypes::P_Float :
                ParameterTypes::P_Unknown;
            mReflection.mInputParameters.push_back(parameter);
        }
    }
};
#endif