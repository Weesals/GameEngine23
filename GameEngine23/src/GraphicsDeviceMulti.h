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
        // The command buffers for all relevant devices
        std::vector<CommandBuffer> mCmdBuffers;
    public:
        ForkedCommandBuffer(GraphicsDeviceMulti* graphics)
        {
            // Create a new command buffer for each device
            std::transform(graphics->mDevices.begin(), graphics->mDevices.end(),
                std::back_inserter(mCmdBuffers), [](auto& item)
                {
                    return item->CreateCommandBuffer();
                }
            );
        }
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
        void DrawMesh(const std::shared_ptr<Mesh>& mesh, const std::shared_ptr<Material>& material, const DrawConfig& config) override
        {
            // Forward to all devices
            std::for_each(mCmdBuffers.begin(), mCmdBuffers.end(), [&](auto& cmd) { cmd.DrawMesh(mesh, material); });
        }
        void Execute() override
        {
            // Forward to all devices
            std::for_each(mCmdBuffers.begin(), mCmdBuffers.end(), [](auto& cmd) { cmd.Execute(); });
        }
    };
public:
    GraphicsDeviceMulti(std::vector<std::shared_ptr<GraphicsDeviceBase>> devices)
        : mDevices(devices) { }

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

    // Flip the back bufer for each device
    void Present() override
    {
        std::for_each(mDevices.begin(), mDevices.end(), [](auto& device) { device->Present(); });
    }
};
