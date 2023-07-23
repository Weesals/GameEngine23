#pragma once

#if defined(VULKAN)

#include "VulkanShader.h"
#include "GraphicsDeviceBase.h"
#include "GraphicsUtility.h"
#include "WindowWin32.h"

// NOTE: Use this to allow dynamic loading
// (no need to link against vulkan-1.lib)
// NOTE: Seems to be broken with vulkan-hpp
//#include <volk.h>
#include <vulkan/vulkan.hpp>

#include <exception>
#include <vector>

class GraphicsDeviceVulkan;

// Store data relate to the usage of a bufer
struct VulkanBuffer
{
    vk::Buffer mBuffer;
    vk::DeviceMemory mMemory;
};

// Cache resources used by vulkan to build command buffer commands
class VulkanResourceCache
{
public:
    struct VulkanMesh
    {
        vk::Buffer mVertexBuffer;
        vk::DeviceMemory mVertexBufferMemory;
        vk::Buffer mIndexBuffer;
        vk::DeviceMemory mIndexBufferMemory;
        std::vector<vk::VertexInputAttributeDescription> mVertexAttributes;
        int mVertexStride;
        int mRevision;
    };
    struct VulkanPipelineLayout
    {
        vk::DescriptorSetLayout mDescLayout;
        vk::PipelineLayout mPipelineLayout;
    };
    struct VulkanPipeline
    {
        vk::Pipeline mPipeline;
        std::vector<VulkanShader::ConstantBuffer*> mBindings;
        VulkanPipelineLayout* mLayout;
    };
    struct VulkanDescriptorSet
    {
        vk::DescriptorSet mDescriptorSet;
    };
    HLSLToSPIRVCompiler mCompiler;
private:
    std::unordered_map<const Mesh*, std::unique_ptr<VulkanMesh>> meshMapping;
    std::unordered_map<size_t, std::unique_ptr<VulkanShader>> shaderMapping;
    std::unordered_map<size_t, std::unique_ptr<VulkanPipeline>> pipelineMapping;
    std::unordered_map<size_t, std::unique_ptr<VulkanPipelineLayout>> layoutMapping;
    void CreateBuffer(uint32_t size, vk::BufferUsageFlags usage, vk::MemoryPropertyFlags properties, vk::Buffer& buffer, vk::DeviceMemory& bufferMemory, GraphicsDeviceVulkan& vulkan);
    void CopyBuffer(vk::Buffer srcBuffer, vk::Buffer dstBuffer, vk::DeviceSize size, GraphicsDeviceVulkan& vulkan);
    void UpdateMeshData(const Mesh& mesh, VulkanMesh* vmesh, GraphicsDeviceVulkan& vulkan);
    int GenerateElementDesc(const Mesh& mesh, std::vector<vk::VertexInputAttributeDescription>& vertDesc);
public:
    VulkanMesh* RequireVulkanMesh(const Mesh& mesh, GraphicsDeviceVulkan& vulkan);
    VulkanShader* RequireVulkanShader(const Shader& shader, const std::string& profile, const std::string& entryPoint, GraphicsDeviceVulkan& vulkan);
    VulkanPipeline* RequireVulkanPipeline(size_t hash, GraphicsDeviceVulkan& vulkan);
    VulkanPipelineLayout* RequireLayoutPipeline(size_t hash, GraphicsDeviceVulkan& vulkan);
    PerFrameItemStore<VulkanDescriptorSet> descriptorSets;
    PerFrameItemStore<VulkanBuffer> buffers;
    void SetResourceLockIds(UINT64 lockFrameId, UINT64 writeFrameId);
};

class GraphicsDeviceVulkan : public GraphicsDeviceBase
{
public:
    struct DepthBuffer
    {
        vk::Format mFormat;
        vk::Image mImage;
        vk::DeviceMemory mMemory;
        vk::ImageView mView;
    };
    struct BackBuffer
    {
        vk::Image mImage;
        vk::CommandBuffer mCmd;
        vk::ImageView mView;
        vk::Framebuffer mFrameBuffer;
        vk::Fence mFence;
        vk::Semaphore mAcquiredSemaphore;
        vk::Semaphore mDrawSemaphore;
        vk::Semaphore mOwnershipSemaphore;
    };

private:
    std::shared_ptr<WindowWin32> mWindow;

    vk::Instance mInstance;
    vk::DebugUtilsMessengerEXT mDebugReportCallback;
    vk::SurfaceKHR mSurface;
    vk::Device mDevice;
    vk::SwapchainKHR mSwapChain;
    vk::Extent2D mExtents;

    vk::PipelineCache mPipelineCache;
    vk::DescriptorPool mDescriptorPool;

    vk::CommandPool mCommandPool;
    vk::Queue mQueue;
    vk::PhysicalDeviceMemoryProperties mMemoryProperties;

    uint32_t mQueueFamilyIndex;

    uint32_t mBackBufferIndex;
    uint32_t mImageIndex;   // I dont know why this is needed? It seems to always be == mFrameId
    uint64_t mFrameCounter;
    std::vector<BackBuffer> mBackBuffers;
    vk::RenderPass mRenderPass;
    DepthBuffer mDepth;

    VulkanResourceCache mResourceCache;

    vk::PhysicalDevice GetPhysicalDevice(vk::Instance instance);
    uint32_t FindQueueIndex(vk::PhysicalDevice physicalDevice, vk::SurfaceKHR surface, vk::QueueFlags flags = vk::QueueFlagBits::eGraphics);

    void CreateInstance(std::vector<const char*> outEnabledLayers);
    void CreateSurface(HWND hWnd);
    void CreateDevice(vk::PhysicalDevice gpu, const std::vector<const char*>& enabledLayers);

    void CreateSwapChain(vk::PhysicalDevice gpu);
    void CreateResources();

public:
    GraphicsDeviceVulkan(std::shared_ptr<WindowWin32>& window);
    ~GraphicsDeviceVulkan() override;

    vk::CommandPool GetCommandPool() const { return mCommandPool; }
    vk::Device GetDevice() const { return mDevice; }
    vk::RenderPass GetRenderPass() const { return mRenderPass; }
    int GetQueueFamilyIndex() const { return mQueueFamilyIndex; }
    const BackBuffer& GetBackBuffer() const { return mBackBuffers[mBackBufferIndex]; }
    vk::Queue GetQueue() const { return mQueue; }
    vk::Extent2D GetExtents() const { return mExtents; }
    vk::PipelineCache GetPipelineCache() const { return mPipelineCache; }
    vk::DescriptorPool GetDescriptorPool() const { return mDescriptorPool; }
    VulkanResourceCache& GetResourceCache() { return mResourceCache; }
    int GetBackBufferIndex() const { return mBackBufferIndex; }

    bool MemoryTypeFromProperties(uint32_t typeBits, vk::MemoryPropertyFlags requirements_mask, uint32_t& typeIndex);

    Vector2 GetClientSize() const override;

    CommandBuffer CreateCommandBuffer() override;

    void BeginFrame();
    void Present() override;
};

#endif
