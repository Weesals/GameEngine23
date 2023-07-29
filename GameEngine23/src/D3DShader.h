#pragma once

#include <d3d12.h>
#include <d3dcompiler.h>
#include <dxgi1_6.h>
#include <wrl/client.h>

#include "Resources.h"

using Microsoft::WRL::ComPtr;

// Identifies a usage of a shader (by its path and entrypoint)
// (used to differentiate between vert and frag with the same file)
// TODO: Could also differentiate via keyword usage
struct ShaderKey {
public:
    int mPathId;
    int mEntryPointId;
    int compare(const ShaderKey& other) const {
        int compare;
        if (compare = (mPathId - other.mPathId)) return compare;
        if (compare = (mEntryPointId - other.mEntryPointId)) return compare;
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
        return ((std::hash<int>()(k.mPathId) ^ (std::hash<int>()(k.mEntryPointId) << 1)) >> 1);
    }
};

// Represents the D3D12 instance of a shader
class D3DShader {
public:
    // Reflected uniforms that can be set by the application
    struct UniformValue {
        std::string mName;
        Identifier mNameId;
        int mOffset;
        int mSize;
    };
    struct ConstantBuffer {
        std::string mName;
        Identifier mNameId;
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
    struct ResourceBinding {
        std::string mName;
        Identifier mNameId;
        int mBindPoint;
    };

    ComPtr<ID3DBlob> mShader;
    std::vector<ConstantBuffer> mConstantBuffers;
    std::vector<ResourceBinding> mResourceBindings;

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
            const char* errorMsg = nullptr;
            if (hr == ERROR_FILE_NOT_FOUND)
            {
                errorMsg = "File was not found!";
            }
            else
            {
                // Retrieve error messages
                errorMsg = compilationErrors != nullptr ? (const char*)compilationErrors->GetBufferPointer() : "";
            }
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

            D3D12_SHADER_INPUT_BIND_DESC bindDesc;
            for (UINT b = 0; b < shaderDesc.BoundResources; ++b)
            {
                pShaderReflection->GetResourceBindingDesc(b, &bindDesc);
                if (strcmp(bindDesc.Name, bufferDesc.Name) == 0) break;
            }

            // The data we have extracted for this constant buffer
            ConstantBuffer cbuffer;
            cbuffer.mName = bufferDesc.Name;
            cbuffer.mNameId = Resources::RequireStringId(cbuffer.mName);
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
                    Resources::RequireStringId(variableDesc.Name),
                    (int)variableDesc.StartOffset,
                    (int)variableDesc.Size,
                };
                cbuffer.mValues.push_back(value);
            }
            mConstantBuffers.push_back(cbuffer);
        }

        // Get all bound resources
        for (UINT i = 0; i < shaderDesc.BoundResources; ++i)
        {
            D3D12_SHADER_INPUT_BIND_DESC resourceDesc;
            pShaderReflection->GetResourceBindingDesc(i, &resourceDesc);
            if (resourceDesc.Type != D3D_SHADER_INPUT_TYPE::D3D_SIT_TEXTURE) continue;
            ResourceBinding rbinding;
            rbinding.mName = resourceDesc.Name;
            rbinding.mNameId = Resources::RequireStringId(rbinding.mName);
            rbinding.mBindPoint = resourceDesc.BindPoint;
            mResourceBindings.push_back(rbinding);
        }
    }
};
