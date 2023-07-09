#pragma once

#include <d3d12.h>
#include <d3dcompiler.h>
#include <dxgi1_6.h>
#include <wrl/client.h>

#include <d3dx12.h>

using Microsoft::WRL::ComPtr;

// Identifies a usage of a shader (by its path and entrypoint)
// (used to differentiate between vert and frag with the same file)
// TODO: Could also differentiate via keyword usage
struct ShaderKey {
public:
    std::wstring mPath;
    std::string mEntryPoint;
    int compare(const ShaderKey& other) const {
        int compare;
        if (compare = mPath.compare(other.mPath)) return compare;
        if (compare = mEntryPoint.compare(other.mEntryPoint)) return compare;
        return 0;
    }
    bool operator <(const ShaderKey& other) const { return compare(other) < 0; }
    bool operator ==(const ShaderKey& other) const { return compare(other) == 0; }
    bool operator !=(const ShaderKey& other) const { return compare(other) != 0; }
};
// Weird C++ quirk; hash function needs to be defined externally to the class
template <> struct std::hash<ShaderKey>
{
    std::size_t operator()(const ShaderKey& k) const
    {
        return ((std::hash<std::wstring>()(k.mPath) ^ (std::hash<std::string>()(k.mEntryPoint) << 1)) >> 1);
    }
};

// Represents the D3D12 instance of a shader
class D3DShader {
public:
    // Reflected uniforms that can be set by the application
    struct UniformValue {
        std::string mName;
        int mOffset;
        int mSize;
    };
    struct ConstantBuffer {
        std::string mName;
        int mSize;
        int mBindPoint;
        std::vector<UniformValue> mValues;

        int GetValueIndex(const std::string& name) const {
            for (size_t i = 0; i < mValues.size(); i++)
            {
                if (mValues[i].mName == name) return (int)i;
            }
            return -1;
        }
    };

    ComPtr<ID3DBlob> mShader;
    std::vector<ConstantBuffer> mConstantBuffers;

    // Compile shader and reflect uniform values / buffers
    void CompileFromFile(const std::wstring& path, const std::string& entry, const std::string& profile)
    {
        ComPtr<ID3D10Blob> compilationErrors = nullptr;
        auto hr = D3DCompileFromFile(
            path.c_str(),
            nullptr,
            D3D_COMPILE_STANDARD_FILE_INCLUDE,
            entry.c_str(),
            profile.c_str(),
            0,
            0,
            &mShader,
            &compilationErrors
        );

        if (FAILED(hr))
        {
            // Retrieve error messages
            const char* errorMsg = compilationErrors != nullptr ? (const char*)compilationErrors->GetBufferPointer() : "";
            throw std::exception(errorMsg);
        }

        ComPtr<ID3D12ShaderReflection> pShaderReflection = nullptr;
        D3DReflect(mShader->GetBufferPointer(), mShader->GetBufferSize(), IID_PPV_ARGS(&pShaderReflection));

        D3D12_SHADER_DESC shaderDesc;
        pShaderReflection->GetDesc(&shaderDesc);

        // Iterate constant buffers
        for (UINT i = 0; i < shaderDesc.ConstantBuffers; ++i)
        {
            ID3D12ShaderReflectionConstantBuffer* pBufferReflection = pShaderReflection->GetConstantBufferByIndex(i);

            D3D12_SHADER_BUFFER_DESC bufferDesc;
            pBufferReflection->GetDesc(&bufferDesc);

            D3D12_SHADER_INPUT_BIND_DESC bindDesc;
            pShaderReflection->GetResourceBindingDesc(i, &bindDesc);

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
                UniformValue value = {
                    variableDesc.Name,
                    (int)variableDesc.StartOffset,
                    (int)variableDesc.Size,
                };
                cbuffer.mValues.push_back(value);
            }
            mConstantBuffers.push_back(cbuffer);
        }
    }
};
