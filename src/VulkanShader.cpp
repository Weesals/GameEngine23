#include "VulkanShader.h"

#if defined(VULKAN)

#include <spirv_reflect.cpp>
#include <filesystem>

void HLSLToSPIRVCompiler::Initialise()
{
    //DxcCreateInstance(CLSID_DxcUtils, IID_PPV_ARGS(&mDXUtils));
    //DxcCreateInstance(CLSID_DxcCompiler, IID_PPV_ARGS(&pCompiler));
}
std::vector<uint8_t> HLSLToSPIRVCompiler::CompileHLSL(const std::wstring& file, const std::string& profile, const std::string& entryPoint)
{
    std::wstring wprofile(profile.begin(), profile.end());
    std::wstring wentryPoint(entryPoint.begin(), entryPoint.end());
    std::wstring outFile = std::filesystem::temp_directory_path().wstring() + file.substr(file.find_last_of(L"/\\") + 1) + L"." + wprofile + L".spv";
    std::wstringstream command;
    command << L"dxc -spirv"
        << L" -T " << wprofile
        << L" -D " << "VULKAN"
        << L" -E " << wentryPoint
        << L" " << file
        << L" -Fo " << outFile;

    STARTUPINFO startupInfo{};
    startupInfo.cb = sizeof(startupInfo);
    PROCESS_INFORMATION processInfo{};
    CreateProcess(nullptr, const_cast<wchar_t*>(command.str().c_str()), nullptr, nullptr, FALSE, 0, nullptr, nullptr, &startupInfo, &processInfo);
    WaitForSingleObject(processInfo.hProcess, INFINITE);
    DWORD exitCode;
    GetExitCodeProcess(processInfo.hProcess, &exitCode);
    CloseHandle(processInfo.hProcess);
    CloseHandle(processInfo.hThread);

    std::ifstream input(outFile, std::ios::binary);
    auto code = std::vector<uint8_t>(std::istreambuf_iterator<char>(input), {});

    return code;
}

void VulkanShader::LoadFromSPIRV(size_t dataSize, void* data, vk::Device device)
{
    mModule = device.createShaderModule(vk::ShaderModuleCreateInfo()
        .setCodeSize(dataSize).setPCode((const uint32_t*)data));

    // Generate reflection data for a shader
    SpvReflectShaderModule module;
    SpvReflectResult result = spvReflectCreateShaderModule(dataSize, data, &module);
    assert(result == SPV_REFLECT_RESULT_SUCCESS);

    // Enumerate and extract shader's input variables
    uint32_t var_count = 0;
    result = spvReflectEnumerateInputVariables(&module, &var_count, NULL);
    assert(result == SPV_REFLECT_RESULT_SUCCESS);
    SpvReflectInterfaceVariable** input_vars =
        (SpvReflectInterfaceVariable**)malloc(var_count * sizeof(SpvReflectInterfaceVariable*));
    result = spvReflectEnumerateInputVariables(&module, &var_count, input_vars);
    assert(result == SPV_REFLECT_RESULT_SUCCESS);

    uint32_t count = 0;
    result = spvReflectEnumerateDescriptorSets(&module, &count, NULL);
    std::vector<SpvReflectDescriptorSet*> sets(count);
    result = spvReflectEnumerateDescriptorSets(&module, &count, sets.data());
    assert(result == SPV_REFLECT_RESULT_SUCCESS);

    for (auto set : sets)
    {
        auto bindings = set->bindings[0];
        auto block = bindings->block;
        auto cb = ConstantBuffer();
        cb.mName = bindings->name;
        cb.mNameId = Identifier(cb.mName);
        cb.mSize = (int)block.size;
        cb.mBindPoint = (int)bindings->binding;
        for (int m = 0; m < block.member_count; ++m)
        {
            auto member = block.members[m];
            auto value = UniformValue();
            value.mName = member.name;
            value.mNameId = Identifier(value.mName);
            value.mOffset = (int)member.offset;
            value.mSize = (int)member.size;
            cb.mValues.push_back(value);
        }
        mConstantBuffers.push_back(cb);
    }

    // Destroy the reflection data when no longer required.
    spvReflectDestroyShaderModule(&module);
}

#endif
