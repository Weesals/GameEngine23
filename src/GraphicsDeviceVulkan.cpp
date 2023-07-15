#if defined(VULKAN)

// Need this to access Vulkan Win32 APIs
#define VK_USE_PLATFORM_WIN32_KHR
// Must be defined exactly once for volk provide implementation
#define VOLK_IMPLEMENTATION
#include "GraphicsDeviceVulkan.h"

#include <iostream>
#include <fstream>
#include <exception>
#include <sstream>
#include <algorithm>
#include <numeric>

#pragma comment(lib, "vulkan-1.lib")

VULKAN_HPP_DEFAULT_DISPATCH_LOADER_DYNAMIC_STORAGE;

// Set up debug reporting function pointers
PFN_vkCreateDebugUtilsMessengerEXT pfnVkCreateDebugUtilsMessengerEXT;
PFN_vkDestroyDebugUtilsMessengerEXT pfnVkDestroyDebugUtilsMessengerEXT;
VKAPI_ATTR VkResult VKAPI_CALL vkCreateDebugUtilsMessengerEXT(VkInstance instance,
    const VkDebugUtilsMessengerCreateInfoEXT* pCreateInfo,
    const VkAllocationCallbacks* pAllocator,
    VkDebugUtilsMessengerEXT* pMessenger) {
    return pfnVkCreateDebugUtilsMessengerEXT(instance, pCreateInfo, pAllocator, pMessenger);
}
VKAPI_ATTR void VKAPI_CALL vkDestroyDebugUtilsMessengerEXT(VkInstance instance, VkDebugUtilsMessengerEXT messenger,
    VkAllocationCallbacks const* pAllocator) {
    return pfnVkDestroyDebugUtilsMessengerEXT(instance, messenger, pAllocator);
}
static VKAPI_ATTR VkBool32 VKAPI_CALL debugReportCallbackFn(
    VkDebugUtilsMessageSeverityFlagBitsEXT messageSeverity,
    VkDebugUtilsMessageTypeFlagsEXT messageType,
    const VkDebugUtilsMessengerCallbackDataEXT* pCallbackData,
    void* pUserData) {

    std::wostringstream data;
    data << "Debug Report: " << pCallbackData->pMessage << std::endl;
    OutputDebugString(data.str().c_str());

    return VK_FALSE;
}

// Get or create a mesh, and ensure its data is up to date
// TODO: Queue the update so that it doesnt update for existing frames
VulkanResourceCache::VulkanMesh* VulkanResourceCache::RequireVulkanMesh(const Mesh& mesh, GraphicsDeviceVulkan& vulkan)
{
    auto vmesh = GetOrCreate(meshMapping, &mesh);
    if (vmesh->mRevision != mesh.GetRevision())
    {
        UpdateMeshData(mesh, vmesh, vulkan);
    }
    return vmesh;
}
// Get or load a shader (including reflection)
VulkanShader* VulkanResourceCache::RequireVulkanShader(const Shader& shader, const std::string& profile, const std::string& entryPoint, GraphicsDeviceVulkan& vulkan)
{
    size_t identifier = shader.GetIdentifier();
    identifier += ((size_t)profile[0] << 32) | ((size_t)entryPoint[0] << 40);
    auto vshader = GetOrCreate(shaderMapping, identifier);
    if (vshader->mModule == nullptr)
    {
        auto shaderData = vulkan.GetResourceCache().mCompiler.CompileHLSL(shader.GetPath(), profile, entryPoint);
        vshader->LoadFromSPIRV(shaderData.size(), shaderData.data(), vulkan.GetDevice());
    }
    return vshader;
}
// Get or create an (uninitialised) pipeline
VulkanResourceCache::VulkanPipeline* VulkanResourceCache::RequireVulkanPipeline(size_t hash, GraphicsDeviceVulkan& vulkan)
{
    return GetOrCreate(pipelineMapping, hash);
}
// Get or create an (uninitialised) layout mapping
VulkanResourceCache::VulkanPipelineLayout* VulkanResourceCache::RequireLayoutPipeline(size_t hash, GraphicsDeviceVulkan& vulkan)
{
    return GetOrCreate(layoutMapping, hash);
}


// Helper function to create a buffer
void VulkanResourceCache::CreateBuffer(uint32_t size, vk::BufferUsageFlags usage, vk::MemoryPropertyFlags properties, vk::Buffer& buffer, vk::DeviceMemory& bufferMemory
    , GraphicsDeviceVulkan& vulkan)
{
    // Allocate buffer
    auto vbufferInfo = vk::BufferCreateInfo({}, size, usage, vk::SharingMode::eExclusive);
    buffer = vulkan.GetDevice().createBuffer(vbufferInfo);
    
    // Allocate memory
    auto vmemRequirements = vulkan.GetDevice().getBufferMemoryRequirements(buffer);
    uint32_t memTypeIndex;
    vulkan.MemoryTypeFromProperties(vmemRequirements.memoryTypeBits, properties, memTypeIndex);
    bufferMemory = vulkan.GetDevice().allocateMemory(vk::MemoryAllocateInfo(vmemRequirements.size, memTypeIndex));
    vulkan.GetDevice().bindBufferMemory(buffer, bufferMemory, 0);
}
// Helper function to copy data into a buffer
// TODO: avoid allocating new command buffer and synchronising
void VulkanResourceCache::CopyBuffer(vk::Buffer srcBuffer, vk::Buffer dstBuffer, vk::DeviceSize size
    , GraphicsDeviceVulkan& vulkan)
{
    auto commandPool = vulkan.GetCommandPool();
    auto allocInfo = vk::CommandBufferAllocateInfo(commandPool, vk::CommandBufferLevel::ePrimary, 1);
    auto commandBuffer = vulkan.GetDevice().allocateCommandBuffers(allocInfo)[0];

    commandBuffer.begin(vk::CommandBufferBeginInfo(vk::CommandBufferUsageFlagBits::eOneTimeSubmit));
    auto copyRegion = vk::BufferCopy(0, 0, size);
    commandBuffer.copyBuffer(srcBuffer, dstBuffer, copyRegion);
    commandBuffer.end();

    auto submitInfo = vk::SubmitInfo(0, 0, 0, 1, &commandBuffer);
    vulkan.GetQueue().submit(submitInfo);
    vulkan.GetQueue().waitIdle();
    vulkan.GetDevice().freeCommandBuffers(commandPool, 1, &commandBuffer);
}
// Push mesh data into vulkan buffer
void VulkanResourceCache::UpdateMeshData(const Mesh& mesh, VulkanResourceCache::VulkanMesh* vmesh, GraphicsDeviceVulkan& vulkan)
{
    auto vstride = GenerateElementDesc(mesh, vmesh->mVertexAttributes);
    vmesh->mVertexStride = vstride;
    auto istride = sizeof(int);
    // Compute size of buffers
    auto vsize = vstride * mesh.GetVertexCount();
    auto isize = istride * mesh.GetIndexCount();
    // Ensure the vertex and index buffers are allocated
    if (vmesh->mVertexBuffer == nullptr)
    {
        CreateBuffer((uint32_t)vsize,
            vk::BufferUsageFlagBits::eTransferDst | vk::BufferUsageFlagBits::eVertexBuffer,
            vk::MemoryPropertyFlagBits::eHostVisible | vk::MemoryPropertyFlagBits::eHostCoherent,
            vmesh->mVertexBuffer, vmesh->mVertexBufferMemory, vulkan);

        CreateBuffer((uint32_t)isize,
            vk::BufferUsageFlagBits::eTransferDst | vk::BufferUsageFlagBits::eIndexBuffer,
            vk::MemoryPropertyFlagBits::eHostVisible | vk::MemoryPropertyFlagBits::eHostCoherent,
            vmesh->mIndexBuffer, vmesh->mIndexBufferMemory, vulkan);
    }

    vk::Buffer stagingBuffer;
    vk::DeviceMemory stagingBufferMemory;

    // Copy vertex data
    CreateBuffer(vsize,
        vk::BufferUsageFlagBits::eTransferSrc,
        vk::MemoryPropertyFlagBits::eHostVisible | vk::MemoryPropertyFlagBits::eHostCoherent,
        stagingBuffer, stagingBufferMemory, vulkan);
    auto data = vulkan.GetDevice().mapMemory(stagingBufferMemory, 0, vsize);
    uint32_t offset = 0;
    CopyElements(data, mesh.GetPositions(), PostIncrement(offset, 12u), vstride);
    auto nrms = mesh.GetNormals();
    if (!nrms.empty()) CopyElements(data, nrms, PostIncrement(offset, 12u), vstride);
    auto uvs = mesh.GetUVs();
    if (!uvs.empty()) CopyElements(data, uvs, PostIncrement(offset, 8u), vstride);
    auto colors = mesh.GetColors();
    if (!colors.empty()) CopyElements(data, colors, PostIncrement(offset, 16u), vstride);
    vulkan.GetDevice().unmapMemory(stagingBufferMemory);
    CopyBuffer(stagingBuffer, vmesh->mVertexBuffer, vsize, vulkan);
    vulkan.GetDevice().destroyBuffer(stagingBuffer);
    vulkan.GetDevice().freeMemory(stagingBufferMemory);

    // Copy index buffer
    CreateBuffer((uint32_t)isize,
        vk::BufferUsageFlagBits::eTransferSrc,
        vk::MemoryPropertyFlagBits::eHostVisible | vk::MemoryPropertyFlagBits::eHostCoherent,
        stagingBuffer, stagingBufferMemory, vulkan);
    data = vulkan.GetDevice().mapMemory(stagingBufferMemory, 0, isize);
    std::memcpy(data, mesh.GetIndices().data(), isize);
    vulkan.GetDevice().unmapMemory(stagingBufferMemory);
    CopyBuffer(stagingBuffer, vmesh->mIndexBuffer, isize, vulkan);
    vulkan.GetDevice().destroyBuffer(stagingBuffer);
    vulkan.GetDevice().freeMemory(stagingBufferMemory);

    // Update the mesh revision to signal that data is fresh
    vmesh->mRevision = mesh.GetRevision();
}
// Generate a descriptor of the required vertex attributes for this mesh
int VulkanResourceCache::GenerateElementDesc(const Mesh& mesh, std::vector<vk::VertexInputAttributeDescription>& vertDesc)
{
    unsigned int offset = 0;
    if (!mesh.GetPositions().empty())
        vertDesc.push_back(vk::VertexInputAttributeDescription(0, 0, vk::Format::eR32G32B32Sfloat, PostIncrement(offset, 12u)));
    if (!mesh.GetNormals().empty())
        vertDesc.push_back(vk::VertexInputAttributeDescription(0, 0, vk::Format::eR32G32B32Sfloat, PostIncrement(offset, 12u)));
    if (!mesh.GetUVs().empty())
        vertDesc.push_back(vk::VertexInputAttributeDescription(0, 0, vk::Format::eR32G32Sfloat, PostIncrement(offset, 8u)));
    if (!mesh.GetColors().empty())
        vertDesc.push_back(vk::VertexInputAttributeDescription(0, 0, vk::Format::eR32G32B32A32Sfloat, PostIncrement(offset, 16u)));
    return offset;
}
// Used to reuse resources where appropriate (when they are no longer required by a frame in the chain)
void VulkanResourceCache::SetResourceLockIds(UINT64 lockFrameId, UINT64 writeFrameId)
{
    buffers.SetResourceLockIds(lockFrameId, writeFrameId);
    descriptorSets.SetResourceLockIds(lockFrameId, writeFrameId);
}

// Handles receiving rendering events from the user application
// and issuing relevant draw commands
class VulkanCommandBuffer : public CommandBufferInteropBase {
    GraphicsDeviceVulkan* mDevice;
    vk::CommandBuffer mCommandBuffer;
public:
    VulkanCommandBuffer(GraphicsDeviceVulkan* device)
        : mDevice(device)
    {
    }
    // Get this command buffer ready to begin rendering
    void Reset() override
    {
        // Need to use the correct command buffer for this frame
        mCommandBuffer = mDevice->GetBackBuffer().mCmd;
        mCommandBuffer.reset();
        // NOTE: Currently requires a call to Clear in order to begin the cmd buffer
    }
    // Clear the screen
    void ClearRenderTarget(const ClearConfig& clear) override
    {
        auto& backBuffer = mDevice->GetBackBuffer();
        auto extents = mDevice->GetExtents();

        vk::ClearValue const clearValues[2] = { vk::ClearColorValue(clear.ClearColor.x, clear.ClearColor.y, clear.ClearColor.z, clear.ClearColor.w),
                                       vk::ClearDepthStencilValue(clear.ClearDepth, clear.ClearStencil) };
        mCommandBuffer.begin(vk::CommandBufferBeginInfo().setFlags(vk::CommandBufferUsageFlagBits::eSimultaneousUse));

        mCommandBuffer.beginRenderPass(vk::RenderPassBeginInfo()
            .setRenderPass(mDevice->GetRenderPass())
            .setFramebuffer(backBuffer.mFrameBuffer)
            .setRenderArea(vk::Rect2D(vk::Offset2D{}, extents))
            .setClearValueCount(2)
            .setPClearValues(clearValues),
            vk::SubpassContents::eInline);

        mCommandBuffer.setViewport(0, vk::Viewport()
            .setX(0)
            .setY(0)
            .setWidth((float)extents.width)
            .setHeight((float)extents.height)
            .setMinDepth(0.0f)
            .setMaxDepth(1.0f));

        mCommandBuffer.setScissor(0, vk::Rect2D(vk::Offset2D{}, vk::Extent2D(extents.width, extents.height)));
    }

    // Draw a mesh with the specified material
    void DrawMesh(std::shared_ptr<Mesh>& mesh, std::shared_ptr<Material>& material) override
    {
        auto& resCache = mDevice->GetResourceCache();
        auto vmesh = resCache.RequireVulkanMesh(*mesh, *mDevice);
        auto device = mDevice->GetDevice();

        auto& vertshader = material->GetVertexShader();
        auto& fragshader = material->GetPixelShader();

        auto pipeHash = std::accumulate(vmesh->mVertexAttributes.begin(), vmesh->mVertexAttributes.end(), (size_t)0, [](size_t i, auto v) { return (size_t)v.format + i * 53; });
        pipeHash = pipeHash * 53 + vertshader.GetIdentifier();
        pipeHash = pipeHash * 53 + fragshader.GetIdentifier();

        auto vpipeline = resCache.RequireVulkanPipeline(pipeHash, *mDevice);
        if (vpipeline->mPipeline == nullptr) {
            // Compile shaders
            auto vshader = resCache.RequireVulkanShader(fragshader, "vs_6_0", "VSMain", *mDevice);
            auto fshader = resCache.RequireVulkanShader(fragshader, "ps_6_0", "PSMain", *mDevice);

            // Get uniform bindings
            for (auto l : { &vshader->mConstantBuffers, &fshader->mConstantBuffers })
            {
                for (auto& cb : *l)
                {
                    auto item = std::find_if(vpipeline->mBindings.begin(), vpipeline->mBindings.end(),
                        [&](auto item) { return (item->mBindPoint == cb.mBindPoint); }
                    );
                    if (item == vpipeline->mBindings.end()) vpipeline->mBindings.push_back(&cb);
                }
            }

            if (vpipeline->mLayout == nullptr) {
                // Require a layout (based on uniform bindings)
                auto bindingHash = std::accumulate(vpipeline->mBindings.begin(), vpipeline->mBindings.end(), (size_t)0, [](size_t i, auto b) { return (size_t)b->mBindPoint + i * 53; });
                auto vlayout = resCache.RequireLayoutPipeline(bindingHash, *mDevice);
                if (vlayout->mPipelineLayout == nullptr)
                {
                    std::vector<vk::DescriptorSetLayoutBinding> layoutBindings;
                    for (auto b = 0; b < vpipeline->mBindings.size(); ++b)
                    {
                        auto binding = vpipeline->mBindings[b];
                        layoutBindings.push_back(vk::DescriptorSetLayoutBinding()
                            .setBinding(binding->mBindPoint)
                            .setDescriptorType(vk::DescriptorType::eUniformBuffer)
                            .setDescriptorCount(1)
                            .setStageFlags(vk::ShaderStageFlagBits::eVertex | vk::ShaderStageFlagBits::eFragment)
                            .setPImmutableSamplers(nullptr));
                    };

                    auto const descriptorLayoutInfo = vk::DescriptorSetLayoutCreateInfo().setBindings(layoutBindings);
                    vlayout->mDescLayout = device.createDescriptorSetLayout(descriptorLayoutInfo);
                    auto const pipelineLayoutInfo = vk::PipelineLayoutCreateInfo().setSetLayouts(vlayout->mDescLayout);
                    vlayout->mPipelineLayout = device.createPipelineLayout(pipelineLayoutInfo);
                }
                vpipeline->mLayout = vlayout;
            }

            // Fill out vertex attributes
            std::vector<vk::VertexInputAttributeDescription> vertexAttributes;
            std::vector<vk::VertexInputBindingDescription> vertexBindings;
            for (auto a = 0; a < vmesh->mVertexAttributes.size(); ++a)
            {
                auto& attribute = vmesh->mVertexAttributes[a];
                vertexAttributes.push_back(vk::VertexInputAttributeDescription(a, 0, attribute.format, attribute.offset));
            }
            vertexBindings.push_back(vk::VertexInputBindingDescription(0, vmesh->mVertexStride));

            // Bind shaders and set up pipeline state
            std::array<vk::PipelineShaderStageCreateInfo, 2> const shaderStageInfo = {
                vk::PipelineShaderStageCreateInfo().setStage(vk::ShaderStageFlagBits::eVertex).setModule(vshader->mModule).setPName("VSMain"),
                vk::PipelineShaderStageCreateInfo().setStage(vk::ShaderStageFlagBits::eFragment).setModule(fshader->mModule).setPName("PSMain")
            };
            vk::PipelineVertexInputStateCreateInfo const vertexInputInfo = vk::PipelineVertexInputStateCreateInfo()
                .setVertexAttributeDescriptions(vertexAttributes)
                .setVertexBindingDescriptions(vertexBindings);
            auto const inputAssemblyInfo = vk::PipelineInputAssemblyStateCreateInfo().setTopology(vk::PrimitiveTopology::eTriangleList);
            auto const viewportInfo = vk::PipelineViewportStateCreateInfo().setViewportCount(1).setScissorCount(1);
            auto const rasterizationInfo = vk::PipelineRasterizationStateCreateInfo()
                .setDepthClampEnable(VK_FALSE)
                .setRasterizerDiscardEnable(VK_FALSE)
                .setPolygonMode(vk::PolygonMode::eFill)
                .setCullMode(vk::CullModeFlagBits::eBack)
                .setFrontFace(vk::FrontFace::eClockwise)
                .setDepthBiasEnable(VK_FALSE)
                .setLineWidth(1.0f);
            auto const multisampleInfo = vk::PipelineMultisampleStateCreateInfo();
            auto const stencilOp = vk::StencilOpState().setFailOp(vk::StencilOp::eKeep).setPassOp(vk::StencilOp::eKeep).setCompareOp(vk::CompareOp::eAlways);
            auto const depthStencilInfo = vk::PipelineDepthStencilStateCreateInfo()
                .setDepthTestEnable(VK_TRUE)
                .setDepthWriteEnable(VK_TRUE)
                .setDepthCompareOp(vk::CompareOp::eLessOrEqual)
                .setDepthBoundsTestEnable(VK_FALSE)
                .setStencilTestEnable(VK_FALSE)
                .setFront(stencilOp)
                .setBack(stencilOp);
            auto const colorBlendAttachments = std::array<vk::PipelineColorBlendAttachmentState, 1>({
                vk::PipelineColorBlendAttachmentState().setColorWriteMask(vk::ColorComponentFlagBits::eR | vk::ColorComponentFlagBits::eG | vk::ColorComponentFlagBits::eB | vk::ColorComponentFlagBits::eA)
            });
            auto const colorBlendInfo = vk::PipelineColorBlendStateCreateInfo().setAttachments(colorBlendAttachments);
            auto const dynamicStates = std::array<vk::DynamicState, 2>({ vk::DynamicState::eViewport, vk::DynamicState::eScissor });
            auto const dynamicStateInfo = vk::PipelineDynamicStateCreateInfo().setDynamicStates(dynamicStates);
            auto pipelines = device.createGraphicsPipelines(mDevice->GetPipelineCache(), vk::GraphicsPipelineCreateInfo()
                .setStages(shaderStageInfo)
                .setPVertexInputState(&vertexInputInfo)
                .setPInputAssemblyState(&inputAssemblyInfo)
                .setPViewportState(&viewportInfo)
                .setPRasterizationState(&rasterizationInfo)
                .setPMultisampleState(&multisampleInfo)
                .setPDepthStencilState(&depthStencilInfo)
                .setPColorBlendState(&colorBlendInfo)
                .setPDynamicState(&dynamicStateInfo)
                .setLayout(vpipeline->mLayout->mPipelineLayout)
                .setRenderPass(mDevice->GetRenderPass()));
            vpipeline->mPipeline = pipelines.value.at(0);
        }

        size_t descriptorSetHash = 0;
        std::vector<vk::Buffer> vuniformBuffers;
        for (int i = 0; i < vpipeline->mBindings.size(); ++i)
        {
            auto binding = vpipeline->mBindings[i];

            // Copy data into the constant buffer
            // TODO: Generate a hash WITHOUT copying data?
            //  => Might be more expensive to evaluate props twice
            std::vector<uint8_t> data(binding->mSize);
            for (auto& var : binding->mValues)
            {
                auto varData = material->GetUniformBinaryData(var.mNameId);
                memcpy(data.data() + var.mOffset, varData.data(), varData.size());
            }
            // Generate hash of data
            int wsize = (int)(data.size() / sizeof(size_t));
            auto dataHash = std::accumulate((size_t*)data.data(), (size_t*)data.data() + wsize, data.size(),
                [](size_t i, auto d) { return (i * 0x9E3779B97F4A7C15L + 0x0123456789ABCDEFL) ^ d; });
            descriptorSetHash += dataHash;

            // Get buffers for this hash
            auto vbuffer = &resCache.buffers.RequireItem(dataHash, binding->mSize,
                [&](auto& item) // Allocate a new buffer
                {
                    assert(item.mData.mBuffer == nullptr);
                    item.mData.mBuffer = device.createBuffer(vk::BufferCreateInfo()
                        .setSize(data.size()).setUsage(vk::BufferUsageFlagBits::eUniformBuffer));

                    auto memoryReq = device.getBufferMemoryRequirements(item.mData.mBuffer);
                    auto memoryInfo = vk::MemoryAllocateInfo().setAllocationSize(memoryReq.size).setMemoryTypeIndex(0);
                    bool const pass = mDevice->MemoryTypeFromProperties(
                        memoryReq.memoryTypeBits,
                        vk::MemoryPropertyFlagBits::eHostVisible | vk::MemoryPropertyFlagBits::eHostCoherent,
                        memoryInfo.memoryTypeIndex);

                    item.mData.mMemory = device.allocateMemory(memoryInfo);
                    device.bindBufferMemory(item.mData.mBuffer, item.mData.mMemory, 0);
                },
                [&](auto& item) // Fill the buffer with data
                {
                    // Write contents
                    void* vdata = device.mapMemory(item.mData.mMemory, 0, data.size());
                    memcpy(vdata, data.data(), data.size());
                    device.unmapMemory(item.mData.mMemory);
                },
                [&](auto& item) // An existing buffer was found
                {
                }
            ).mData;
            vuniformBuffers.push_back(vbuffer->mBuffer);
        }
        // Find or create a descriptor set to hold our uniform bindings
        auto vdescriptorSet = &resCache.descriptorSets.RequireItem(descriptorSetHash, pipeHash,
            [&](auto& item) // Allocate a new descriptor set
            {
                auto const allocInfo = vk::DescriptorSetAllocateInfo()
                    .setDescriptorPool(mDevice->GetDescriptorPool())
                    .setSetLayouts(vpipeline->mLayout->mDescLayout);
                auto descriptors = device.allocateDescriptorSets(allocInfo);
                item.mData.mDescriptorSet = descriptors[0];
            },
            [&](auto& item) // Fill with data
            {
                // Update descriptor set data
                std::vector<vk::DescriptorBufferInfo> bufferWrites(vuniformBuffers.size());
                std::vector<vk::WriteDescriptorSet> writes(vuniformBuffers.size());
                for (int b = 0; b < vpipeline->mBindings.size(); ++b) {
                    auto binding = vpipeline->mBindings[b];
                    bufferWrites[b] = vk::DescriptorBufferInfo()
                        .setRange(binding->mSize)
                        .setBuffer(vuniformBuffers[b]);
                    writes[b] = vk::WriteDescriptorSet()
                        .setDstSet(item.mData.mDescriptorSet)
                        .setDstBinding(binding->mBindPoint)
                        .setDescriptorCount(1)
                        .setDescriptorType(vk::DescriptorType::eUniformBuffer)
                        .setPBufferInfo(&bufferWrites[b]);
                }
                device.updateDescriptorSets(writes, {});
            },
            [&](auto& item) // Existing one was found
            {
            }
        ).mData;

        // Bind pipeline state and uniforms
        auto& backBuffer = mDevice->GetBackBuffer();
        mCommandBuffer.bindPipeline(vk::PipelineBindPoint::eGraphics, vpipeline->mPipeline);
        mCommandBuffer.bindDescriptorSets(vk::PipelineBindPoint::eGraphics, vpipeline->mLayout->mPipelineLayout, 0, vdescriptorSet->mDescriptorSet, {});

        // Bind buffers
        vk::DeviceSize offsets[] = {0};
        mCommandBuffer.bindVertexBuffers(0, 1, &vmesh->mVertexBuffer, offsets);
        mCommandBuffer.bindIndexBuffer(vmesh->mIndexBuffer, 0, vk::IndexType::eUint32);

        // Issue draw call
        mCommandBuffer.drawIndexed(mesh->GetIndexCount(), 1, 0, 0, 0);
    }
    // Send the commands to the GPU
    void Execute() override
    {
        mCommandBuffer.endRenderPass();
        mCommandBuffer.end();

        // Wait for the image acquired semaphore to be signaled to ensure
        // that the image won't be rendered to until the presentation
        // engine has fully released ownership to the application, and it is
        // okay to render to the image.
        auto& backBuffer = mDevice->GetBackBuffer();
        auto pipelineFlags = (vk::PipelineStageFlags)vk::PipelineStageFlagBits::eColorAttachmentOutput;
        mDevice->GetQueue().submit(vk::SubmitInfo()
            .setWaitDstStageMask(pipelineFlags)
            .setWaitSemaphores(backBuffer.mAcquiredSemaphore)
            .setCommandBuffers(mCommandBuffer)
            .setSignalSemaphores(backBuffer.mDrawSemaphore),
            backBuffer.mFence);
    }
};
GraphicsDeviceVulkan::GraphicsDeviceVulkan(std::shared_ptr<WindowWin32>& window)
    : mWindow(window)
    , mBackBufferIndex(0)
    , mFrameCounter(0)
{
    //volkInitialize();
    std::vector<const char*> enabledLayers;
    CreateInstance(enabledLayers);
    //volkLoadInstance(mInstance);
    CreateSurface(window->GetHWND());
    auto gpu = GetPhysicalDevice(mInstance);
    CreateDevice(gpu, enabledLayers);
    //volkLoadDevice(mDevice);
    CreateSwapChain(gpu);
    CreateResources();

    BeginFrame();
}
GraphicsDeviceVulkan::~GraphicsDeviceVulkan() { }

void GraphicsDeviceVulkan::CreateInstance(std::vector<const char*> outEnabledLayers)
{
    std::vector<const char*> enabledExtensions;

    auto availableLayers = vk::enumerateInstanceLayerProperties();
    auto availableExtensions = vk::enumerateInstanceExtensionProperties();
    auto EnableLayer = [&](const char* name)->bool {
        if (!std::any_of(availableLayers.begin(), availableLayers.end(), [=](auto p) { return strcmp(p.layerName, name) == 0; }))
            return false;
        outEnabledLayers.push_back(name);
        return true;
    };
    auto EnableExtension = [&](const char* name)->bool {
        if (!std::any_of(availableExtensions.begin(), availableExtensions.end(), [=](auto p) { return strcmp(p.extensionName, name) == 0; }))
            return false;
        enabledExtensions.push_back(name);
        return true;
    };

    // Enable the extensions we need
    EnableExtension(VK_KHR_GET_PHYSICAL_DEVICE_PROPERTIES_2_EXTENSION_NAME);
    EnableExtension(VK_KHR_PORTABILITY_ENUMERATION_EXTENSION_NAME);
    EnableExtension(VK_KHR_SURFACE_EXTENSION_NAME);
    EnableExtension(VK_KHR_WIN32_SURFACE_EXTENSION_NAME);

    // Under debug builds, enable the validation layer
#if defined(_DEBUG)
    //if (!EnableLayer("VK_LAYER_LUNARG_standard_validation"))
        EnableLayer("VK_LAYER_KHRONOS_validation");
    EnableExtension(VK_EXT_DEBUG_REPORT_EXTENSION_NAME);
    EnableExtension(VK_EXT_DEBUG_UTILS_EXTENSION_NAME);
#endif

    // Create the instance
    auto appInfo = vk::ApplicationInfo()
        .setPEngineName("GameEngine23")
        .setEngineVersion(VK_MAKE_VERSION(1, 0, 0))
        .setApiVersion(VK_API_VERSION_1_0);

    auto instInfo = vk::InstanceCreateInfo()
        .setPApplicationInfo(&appInfo)
        .setPEnabledLayerNames(outEnabledLayers)
        .setPEnabledExtensionNames(enabledExtensions);
    mInstance = (vk::createInstance(instInfo));

    // Setup callback for VK_EXT_DEBUG_REPORT_EXTENSION_NAME
#if defined(_DEBUG)
    {
        // Load methods
        pfnVkCreateDebugUtilsMessengerEXT =
            reinterpret_cast<PFN_vkCreateDebugUtilsMessengerEXT>(mInstance.getProcAddr("vkCreateDebugUtilsMessengerEXT"));
        pfnVkDestroyDebugUtilsMessengerEXT =
            reinterpret_cast<PFN_vkDestroyDebugUtilsMessengerEXT>(mInstance.getProcAddr("vkDestroyDebugUtilsMessengerEXT"));

        vk::DebugUtilsMessageSeverityFlagsEXT severityFlags(vk::DebugUtilsMessageSeverityFlagBitsEXT::eWarning |
            vk::DebugUtilsMessageSeverityFlagBitsEXT::eError);
        vk::DebugUtilsMessageTypeFlagsEXT messageTypeFlags(vk::DebugUtilsMessageTypeFlagBitsEXT::eGeneral |
            vk::DebugUtilsMessageTypeFlagBitsEXT::ePerformance |
            vk::DebugUtilsMessageTypeFlagBitsEXT::eValidation);
        auto reportInfo = vk::DebugUtilsMessengerCreateInfoEXT({}, severityFlags, messageTypeFlags,
            debugReportCallbackFn, static_cast<void*>(this));

        // Create and register callback.
        mDebugReportCallback = mInstance.createDebugUtilsMessengerEXT(reportInfo);
    }
#endif
}

// Find a valid physical device (just use the first one)
vk::PhysicalDevice GraphicsDeviceVulkan::GetPhysicalDevice(vk::Instance instance)
{
    return instance.enumeratePhysicalDevices().front();
}
// Find a valid family queue that satisfies our requirements
uint32_t GraphicsDeviceVulkan::FindQueueIndex(vk::PhysicalDevice physicalDevice, vk::SurfaceKHR surface, vk::QueueFlags flags)
{
    // Get all queue families
    auto queueFamilies = physicalDevice.getQueueFamilyProperties();

    // Find one that is suitable
    for (uint32_t i = 0; i < queueFamilies.size(); ++i)
    {
        if ((queueFamilies[i].queueFlags & flags) == flags) return i;
    }
    throw std::runtime_error("Could not find valid queue");
}
bool GraphicsDeviceVulkan::MemoryTypeFromProperties(uint32_t typeBits, vk::MemoryPropertyFlags requirements_mask, uint32_t& typeIndex) {
    // Search memtypes to find first index with those properties
    for (uint32_t i = 0; i < vk::MaxMemoryTypes; i++) {
        // bitmask not set for this type
        if ((typeBits & (1 << i)) == 0) continue;

        // Type is available, does it match user properties?
        if ((mMemoryProperties.memoryTypes[i].propertyFlags & requirements_mask) == requirements_mask) {
            typeIndex = i;
            return true;
        }
    }
    // No memory types matched, return failure
    return false;
}

void GraphicsDeviceVulkan::CreateSurface(HWND hWnd)
{
    // Create the window surface to render to
    auto surCreateInfo = vk::Win32SurfaceCreateInfoKHR({}, GetModuleHandle(nullptr), hWnd);
    mSurface = mInstance.createWin32SurfaceKHR(surCreateInfo);
}
// Create the logical device
void GraphicsDeviceVulkan::CreateDevice(vk::PhysicalDevice gpu, const std::vector<const char*>& enabledLayers)
{
    // Find an appropriate queue
    mQueueFamilyIndex = FindQueueIndex(gpu, mSurface);

    // Configure extensions for this device
    std::vector<const char*> enabledExtensions;
    auto availableExtensions = gpu.enumerateDeviceExtensionProperties();
    auto EnableExtension = [&](const char* name)->bool {
        if (!std::any_of(availableExtensions.begin(), availableExtensions.end(), [=](auto p) { return strcmp(p.extensionName, name) == 0; }))
            return false;
        enabledExtensions.push_back(name);
        return true;
    };
    EnableExtension(VK_KHR_SWAPCHAIN_EXTENSION_NAME);

    // Initialise the device
    float priorities = 1.0f;
    std::vector<vk::DeviceQueueCreateInfo> queues = {
        vk::DeviceQueueCreateInfo().setQueueFamilyIndex(mQueueFamilyIndex).setQueuePriorities(priorities)
    };
    auto deviceInfo = vk::DeviceCreateInfo()
        .setQueueCreateInfos(queues)
        .setPEnabledExtensionNames(enabledExtensions);
    mDevice = gpu.createDevice(deviceInfo);

    // Get a handle to the queue for command submission
    mQueue = mDevice.getQueue(mQueueFamilyIndex, 0);

    // Create the command pool
    auto cmd_pool_info = vk::CommandPoolCreateInfo(vk::CommandPoolCreateFlagBits::eResetCommandBuffer, mQueueFamilyIndex);
    mCommandPool = mDevice.createCommandPool(cmd_pool_info);
}

void GraphicsDeviceVulkan::CreateSwapChain(vk::PhysicalDevice gpu)
{
    vk::SurfaceFormatKHR format;
    uint32_t swapChainCount;
    // Get format and size
    {
        auto surfaceFormats = gpu.getSurfaceFormatsKHR(mSurface);

        if (surfaceFormats.size() == 0)
            throw std::exception("No valid formats found");

        format = *std::find_if(surfaceFormats.begin(), surfaceFormats.end(), [](auto item)
            {
                return item == vk::Format::eR8G8B8A8Unorm || item == vk::Format::eB8G8R8A8Unorm
                    || item == vk::Format::eA2B10G10R10UnormPack32 || item == vk::Format::eA2R10G10B10UnormPack32
                    || item == vk::Format::eR16G16B16A16Sfloat;
            });

        vk::SurfaceCapabilitiesKHR surfCapabilities;
        auto caps_result = gpu.getSurfaceCapabilitiesKHR(mSurface, &surfCapabilities);

        // Get the size of the render surface
        mExtents = surfCapabilities.currentExtent;
        if (mExtents.width == (uint32_t)-1)
        {
            auto clientSize = mWindow->GetClientSize();
            mExtents = vk::Extent2D(clientSize.first, clientSize.second);
        }

        // Get the number of back buffers
        swapChainCount = surfCapabilities.minImageCount + 1;
        if (surfCapabilities.maxImageCount > 0)
            swapChainCount = std::min(swapChainCount, surfCapabilities.maxImageCount);

        // Create swap chain
        mSwapChain = mDevice.createSwapchainKHR(vk::SwapchainCreateInfoKHR()
            .setSurface(mSurface)
            .setMinImageCount(swapChainCount)
            .setImageFormat(format.format)
            .setImageColorSpace(format.colorSpace)
            .setImageExtent({ mExtents.width, mExtents.height })
            .setImageArrayLayers(1)
            .setImageUsage(vk::ImageUsageFlagBits::eColorAttachment)
            .setImageSharingMode(vk::SharingMode::eExclusive)
            .setPreTransform(surfCapabilities.currentTransform)
            .setCompositeAlpha(vk::CompositeAlphaFlagBitsKHR::eOpaque)
            .setPresentMode(vk::PresentModeKHR::eFifo)
            .setClipped(true));
    }
    mMemoryProperties = gpu.getMemoryProperties();

    // Create depth buffer
    {
        mDepth.mFormat = vk::Format::eD16Unorm;

        auto const image = vk::ImageCreateInfo()
            .setImageType(vk::ImageType::e2D)
            .setFormat(mDepth.mFormat)
            .setExtent({ mExtents.width, mExtents.height, 1 })
            .setMipLevels(1)
            .setArrayLayers(1)
            .setSamples(vk::SampleCountFlagBits::e1)
            .setTiling(vk::ImageTiling::eOptimal)
            .setUsage(vk::ImageUsageFlagBits::eDepthStencilAttachment)
            .setSharingMode(vk::SharingMode::eExclusive)
            .setInitialLayout(vk::ImageLayout::eUndefined);

        mDepth.mImage = mDevice.createImage(image);

        auto memoryReqs = mDevice.getImageMemoryRequirements(mDepth.mImage);
        vk::MemoryAllocateInfo allocInfo;
        allocInfo.setAllocationSize(memoryReqs.size);
        allocInfo.setMemoryTypeIndex(0);
        mDepth.mMemory = mDevice.allocateMemory(allocInfo);
        mDevice.bindImageMemory(mDepth.mImage, mDepth.mMemory, 0);

        auto viewInfo = vk::ImageViewCreateInfo()
            .setImage(mDepth.mImage)
            .setViewType(vk::ImageViewType::e2D)
            .setFormat(mDepth.mFormat)
            .setSubresourceRange(vk::ImageSubresourceRange(vk::ImageAspectFlagBits::eDepth, 0, 1, 0, 1));
        mDepth.mView = mDevice.createImageView(viewInfo);
    }

    // Create back buffers
    {
        auto swapChainImages = mDevice.getSwapchainImagesKHR(mSwapChain);
        assert(swapChainImages.size() == swapChainCount);
        mBackBuffers.resize(swapChainImages.size());
        for (uint32_t i = 0; i < mBackBuffers.size(); i++) {
            mBackBuffers[i].mImage = swapChainImages[i];
            auto viewInfo = vk::ImageViewCreateInfo()
                .setViewType(vk::ImageViewType::e2D)
                .setFormat(format.format)
                .setSubresourceRange(vk::ImageSubresourceRange(vk::ImageAspectFlagBits::eColor, 0, 1, 0, 1))
                .setImage(mBackBuffers[i].mImage);
            mBackBuffers[i].mView = mDevice.createImageView(viewInfo);
        }
    }

    std::array<vk::AttachmentDescription, 2> const attachments =
    {
        vk::AttachmentDescription()
            .setFormat(format.format)
            .setSamples(vk::SampleCountFlagBits::e1)
            .setLoadOp(vk::AttachmentLoadOp::eClear)
            .setStoreOp(vk::AttachmentStoreOp::eStore)
            .setStencilLoadOp(vk::AttachmentLoadOp::eDontCare)
            .setStencilStoreOp(vk::AttachmentStoreOp::eDontCare)
            .setInitialLayout(vk::ImageLayout::eUndefined)
            .setFinalLayout(vk::ImageLayout::ePresentSrcKHR),
        vk::AttachmentDescription()
            .setFormat(mDepth.mFormat)
            .setSamples(vk::SampleCountFlagBits::e1)
            .setLoadOp(vk::AttachmentLoadOp::eClear)
            .setStoreOp(vk::AttachmentStoreOp::eDontCare)
            .setStencilLoadOp(vk::AttachmentLoadOp::eDontCare)
            .setStencilStoreOp(vk::AttachmentStoreOp::eDontCare)
            .setInitialLayout(vk::ImageLayout::eUndefined)
            .setFinalLayout(vk::ImageLayout::eDepthStencilAttachmentOptimal
        )
    };

    auto const colorReference = vk::AttachmentReference().setAttachment(0).setLayout(vk::ImageLayout::eColorAttachmentOptimal);
    auto const depthReference = vk::AttachmentReference().setAttachment(1).setLayout(vk::ImageLayout::eDepthStencilAttachmentOptimal);

    auto const subpass = vk::SubpassDescription()
        .setPipelineBindPoint(vk::PipelineBindPoint::eGraphics)
        .setColorAttachments(colorReference)
        .setPDepthStencilAttachment(&depthReference);

    vk::PipelineStageFlags stages = vk::PipelineStageFlagBits::eEarlyFragmentTests | vk::PipelineStageFlagBits::eLateFragmentTests;
    std::array<vk::SubpassDependency, 2> const dependencies = {
        vk::SubpassDependency()  // Depth buffer is shared between swapchain images
            .setSrcSubpass(VK_SUBPASS_EXTERNAL)
            .setDstSubpass(0)
            .setSrcStageMask(stages)
            .setDstStageMask(stages)
            .setSrcAccessMask(vk::AccessFlagBits::eDepthStencilAttachmentWrite)
            .setDstAccessMask(vk::AccessFlagBits::eDepthStencilAttachmentRead | vk::AccessFlagBits::eDepthStencilAttachmentWrite)
            .setDependencyFlags(vk::DependencyFlags()),
        vk::SubpassDependency()  // Image layout transition
            .setSrcSubpass(VK_SUBPASS_EXTERNAL)
            .setDstSubpass(0)
            .setSrcStageMask(vk::PipelineStageFlagBits::eColorAttachmentOutput)
            .setDstStageMask(vk::PipelineStageFlagBits::eColorAttachmentOutput)
            .setSrcAccessMask(vk::AccessFlagBits())
            .setDstAccessMask(vk::AccessFlagBits::eColorAttachmentWrite | vk::AccessFlagBits::eColorAttachmentRead)
            .setDependencyFlags(vk::DependencyFlags()),
    };

    mRenderPass = mDevice.createRenderPass(
        vk::RenderPassCreateInfo().setAttachments(attachments).setSubpasses(subpass).setDependencies(dependencies));

    std::array<vk::ImageView, 2> bbattachments;
    bbattachments[1] = mDepth.mView;

    for (auto& backBuffer : mBackBuffers) {
        bbattachments[0] = backBuffer.mView;
        // Frame buffer to render to
        backBuffer.mFrameBuffer = mDevice.createFramebuffer(vk::FramebufferCreateInfo()
            .setRenderPass(mRenderPass)
            .setAttachments(bbattachments)
            .setWidth(mExtents.width)
            .setHeight(mExtents.height)
            .setLayers(1));
        // Command buffer for this frames draw calls
        backBuffer.mCmd = mDevice.allocateCommandBuffers(vk::CommandBufferAllocateInfo()
            .setCommandPool(mCommandPool)
            .setLevel(vk::CommandBufferLevel::ePrimary)
            .setCommandBufferCount(1))[0];

        // Synchronisaton primitives
        backBuffer.mFence = mDevice.createFence(
            vk::FenceCreateInfo().setFlags(vk::FenceCreateFlagBits::eSignaled));
        backBuffer.mAcquiredSemaphore = mDevice.createSemaphore(vk::SemaphoreCreateInfo());
        backBuffer.mDrawSemaphore = mDevice.createSemaphore(vk::SemaphoreCreateInfo());
    }
}
void GraphicsDeviceVulkan::CreateResources()
{
    mPipelineCache = mDevice.createPipelineCache(vk::PipelineCacheCreateInfo());

    // Create descriptor pool
    std::array<vk::DescriptorPoolSize, 1> const poolSizes = {
        vk::DescriptorPoolSize()
            .setType(vk::DescriptorType::eUniformBuffer)
            .setDescriptorCount(static_cast<uint32_t>(3000))
    };
    mDescriptorPool = mDevice.createDescriptorPool(
        vk::DescriptorPoolCreateInfo().setMaxSets(static_cast<uint32_t>(3000)).setPoolSizes(poolSizes));

    mResourceCache.mCompiler.Initialise();
}

// Size of the renderable area
Vector2 GraphicsDeviceVulkan::GetClientSize() const
{
    return Vector2((float)mExtents.width, (float)mExtents.height);
}

// Create an interface to allow draw call submission
// from the client program
CommandBuffer GraphicsDeviceVulkan::CreateCommandBuffer()
{
    return CommandBuffer(new VulkanCommandBuffer(this));
}

// Begin renering a frame
void GraphicsDeviceVulkan::BeginFrame()
{
    // Wait for the frame to be ready
    auto result = mDevice.waitForFences(mBackBuffers[mBackBufferIndex].mFence, VK_TRUE, UINT64_MAX);
    mDevice.resetFences({ mBackBuffers[mBackBufferIndex].mFence });

    vk::Result acquire_result;
    do {
        acquire_result =
            mDevice.acquireNextImageKHR(mSwapChain, UINT64_MAX, mBackBuffers[mBackBufferIndex].mAcquiredSemaphore, vk::Fence(), &mImageIndex);
        if (acquire_result == vk::Result::eErrorOutOfDateKHR) {
            // demo.swapchain is out of date (e.g. the window was resized) and
            // must be recreated:
            //resize();
        }
        else if (acquire_result == vk::Result::eSuboptimalKHR) {
            // swapchain is not as optimal as it could be, but the platform's
            // presentation engine will still present the image correctly.
            break;
        }
        else if (acquire_result == vk::Result::eErrorSurfaceLostKHR) {
            //inst.destroySurfaceKHR(surface);
            //create_surface();
            //resize();
        }
        else {
            //VERIFY(acquire_result == vk::Result::eSuccess);
        }
    } while (acquire_result != vk::Result::eSuccess);
}

void GraphicsDeviceVulkan::Present()
{
    const auto presentInfo = vk::PresentInfoKHR()
        .setWaitSemaphores(mBackBuffers[mBackBufferIndex].mDrawSemaphore)
        .setSwapchains(mSwapChain)
        .setImageIndices(mImageIndex);

    // If we are using separate queues we have to wait for image ownership,
    // otherwise wait for draw complete
    auto present_queue = mDevice.getQueue(mQueueFamilyIndex, 0);
    auto present_result = present_queue.presentKHR(&presentInfo);

    // Set to next frame (ready to start the next frame)
    mBackBufferIndex = (mBackBufferIndex + 1) % mBackBuffers.size();

    // Notify resources of the current frame, so old resources can be reused
    ++mFrameCounter;
    auto lockFrame = mFrameCounter <= mBackBuffers.size() ? 0 : mFrameCounter - mBackBuffers.size();
    mResourceCache.SetResourceLockIds(lockFrame, mFrameCounter);

    // Get ready for rendering the next frame
    BeginFrame();
}

#endif
