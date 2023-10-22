#pragma once

#include <d3d12.h>
#include <d3dcompiler.h>
#include <dxgi1_6.h>
#include <wrl/client.h>

#include "Resources.h"
#include "GraphicsDeviceBase.h"

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
class D3DShader : ShaderBase {
public:

    ComPtr<ID3DBlob> mShader;
    ShaderReflection mReflection;

    // Compile shader and reflect uniform values / buffers
    void CompileFromFile(const std::wstring& path, const std::string& entry, const std::string& profile)
    {
        ComPtr<ID3D10Blob> compilationErrors = nullptr;
        while (true) {
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
