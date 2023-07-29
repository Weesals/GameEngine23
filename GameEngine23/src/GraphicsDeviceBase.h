#pragma once

#include "Mesh.h"
#include "Material.h"


// Control what and how a render target is cleared
struct ClearConfig {
    Color ClearColor;
    float ClearDepth;
    __int32 ClearStencil;
    ClearConfig(Color color = GetInvalidColor(), float depth = -1) : ClearColor(color), ClearDepth(depth), ClearStencil(0) { }
    bool HasClearColor() const { return ClearColor != GetInvalidColor(); }
    bool HasClearDepth() const { return ClearDepth != -1; }
    bool HasClearScencil() const { return ClearStencil != 0; }
private:
    static const Color GetInvalidColor() { return Color(-1, -1, -1, -1); }
};
struct DrawConfig
{
    int mIndexBase;
    int mIndexCount;
    static DrawConfig MakeDefault() { return { 0, -1, }; }
};

// Draw commands are forwarded to a subclass of this class
class CommandBufferInteropBase {
public:
    virtual ~CommandBufferInteropBase() { }
    virtual void Reset() = 0;
    virtual void ClearRenderTarget(const ClearConfig& clear) = 0;
    virtual void DrawMesh(const std::shared_ptr<Mesh>& mesh, const std::shared_ptr<Material>& material, const DrawConfig& config) = 0;
    virtual void Execute() = 0;
};

// Use this interface to issue draw calls to the graphics device
class CommandBuffer {
protected:
    std::unique_ptr<CommandBufferInteropBase> mInterop;
public:
    CommandBuffer(CommandBufferInteropBase* interop) : mInterop(interop) { }
    void Reset() { mInterop->Reset(); }
    void ClearRenderTarget(const ClearConfig& config) { mInterop->ClearRenderTarget(config); }
    void DrawMesh(const std::shared_ptr<Mesh>& mesh, const std::shared_ptr<Material>& material)
    {
        DrawMesh(mesh, material, DrawConfig::MakeDefault());
    }
    void DrawMesh(const std::shared_ptr<Mesh>& mesh, const std::shared_ptr<Material>& material, const DrawConfig& config)
    {
        if (mesh->GetVertexCount() == 0) return;
        mInterop->DrawMesh(mesh, material, config);
    }
    void Execute() { mInterop->Execute(); }
};

// Base class for a graphics device
class GraphicsDeviceBase
{
public:
    virtual ~GraphicsDeviceBase() { }

    // Get the resolution of the client area
    virtual Vector2 GetClientSize() const = 0;

    // Create a command buffer which allows draw calls to be submitted
    virtual CommandBuffer CreateCommandBuffer() = 0;

    // Rendering is complete; flip the backbuffer
    virtual void Present() = 0;

};

