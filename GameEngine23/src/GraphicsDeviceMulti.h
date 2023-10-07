#pragma once

#include "GraphicsDeviceBase.h"

#include <algorithm>
#include <vector>
#include <iterator>

// Handles dispatching commands to multiple graphics devices
// Only really used for testing
// (running both a D3D and Vulkan head at the same time)
class GraphicsDeviceMulti : public GraphicsDeviceBase
{
    std::vector<std::shared_ptr<GraphicsDeviceBase>> mDevices;

    // A command buffer that forwards calls to all of the bound devices
    class ForkedCommandBuffer : public CommandBufferInteropBase
    {
        GraphicsDeviceMulti* mGraphicsDevice;
        // The command buffers for all relevant devices
        std::vector<CommandBuffer> mCmdBuffers;
    public:
        ForkedCommandBuffer(GraphicsDeviceMulti* graphics)
            : mGraphicsDevice(graphics)
        {
            // Create a new command buffer for each device
            std::transform(graphics->mDevices.begin(), graphics->mDevices.end(),
                std::back_inserter(mCmdBuffers), [](auto& item)
                {
                    return item->CreateCommandBuffer();
                }
            );
        }
        GraphicsDeviceBase* GetGraphics() const { return mGraphicsDevice; }
        void Reset() override
        {
            // Forward to all devices
            std::for_each(mCmdBuffers.begin(), mCmdBuffers.end(), [](auto& cmd) { cmd.Reset(); });
        }
        void ClearRenderTarget(const ClearConfig& clear) override
        {
            // Forward to all devices
            std::for_each(mCmdBuffers.begin(), mCmdBuffers.end(), [&](auto& cmd) { cmd.ClearRenderTarget(clear); });
        }
        void DrawMesh(std::span<const BufferLayout*> bindings, const PipelineLayout* pso, std::span<void*> resources, const DrawConfig& config, int instanceCount = 1) override
        {
            // Forward to all devices
            std::for_each(mCmdBuffers.begin(), mCmdBuffers.end(), [&](auto& cmd) { cmd.DrawMesh(bindings, pso, resources, config, instanceCount); });
        }
        void Execute() override
        {
            // Forward to all devices
            std::for_each(mCmdBuffers.begin(), mCmdBuffers.end(), [](auto& cmd) { cmd.Execute(); });
        }
    };
public:
    GraphicsDeviceMulti(std::vector<std::shared_ptr<GraphicsDeviceBase>> devices)
        : mDevices(std::move(devices)) { }

    // Just get the first devices size
    Vector2 GetClientSize() const override
    {
        return mDevices[0]->GetClientSize();
    }

    // Create command buffers for each device
    CommandBuffer CreateCommandBuffer() override
    {
        return CommandBuffer(new ForkedCommandBuffer(this));
    }

    // Calculate which PSO this draw call would land in
    const PipelineLayout* RequirePipeline(std::span<const BufferLayout*> bindings, const Material* material) override {
        return nullptr;
    }

    // Flip the back bufer for each device
    void Present() override
    {
        std::for_each(mDevices.begin(), mDevices.end(), [](auto& device) { device->Present(); });
    }
};
