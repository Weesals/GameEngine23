#pragma once

#if defined(VULKAN)

#define NOMINMAX
#include <atlbase.h>
#include <dxcapi.h>
#include <d3dcompiler.h>
#include <fstream>
#include <sstream>
#include <vector>
#include <string>

#include "Resources.h"

#include <vulkan/vulkan.hpp>

class HLSLToSPIRVCompiler
{
public:
    CComPtr<IDxcUtils> mDXUtils;
    CComPtr<IDxcCompiler> pCompiler;

    // Not needed, but it would be good to convert to IDxcCompiler
    // once I can figure out how to pass the spirv argument
    void Initialise();

    // Outputs the raw compiled shader data itself
    std::vector<uint8_t> CompileHLSL(const std::wstring& file, const std::string& profile, const std::string& entryPoint);
};
class VulkanShader
{
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

    std::vector<ConstantBuffer> mConstantBuffers;

    vk::ShaderModule mModule;

    void LoadFromSPIRV(size_t dataSize, void* data, vk::Device device);

};

#endif
