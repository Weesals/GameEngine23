#pragma once

#include "Mesh.h"
#include "Material.h"
#include "GraphicsUtility.h"
#include "GraphicsBuffer.h"

class ShaderBase
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
        size_t GenerateHash() const {
            size_t hash = 0;
            for (auto& value : mValues) hash = AppendHash(((int)value.mNameId << 16) | value.mOffset, hash);
            return hash;
        }
    };
    enum ResourceTypes : uint8_t { R_Texture, R_SBuffer, };
    struct ResourceBinding {
        std::string mName;
        Identifier mNameId;
        int mBindPoint;
        int mStride;
        ResourceTypes mType;
    };
    enum ParameterTypes : uint8_t { P_Unknown, P_UInt, P_SInt, P_Float, };
    struct InputParameter {
        std::string mName;
        std::string mSemantic;
        Identifier mNameId;
        Identifier mSemanticId;
        int mSemanticIndex;
        int mRegister;
        uint8_t mMask;
        ParameterTypes mType;
    };
    struct ShaderReflection {
        std::vector<ConstantBuffer> mConstantBuffers;
        std::vector<ResourceBinding> mResourceBindings;
        std::vector<InputParameter> mInputParameters;
    };
};

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
    int mIndexBase = 0;
    int mIndexCount = 0;
    static DrawConfig MakeDefault() { return { 0, -1, }; }
};

struct PipelineLayout
{
    size_t mRootHash;       // Persistent
    size_t mPipelineHash;   // Persistent
    std::vector<const ShaderBase::ConstantBuffer*> mConstantBuffers;
    std::vector<const ShaderBase::ResourceBinding*> mResources;
    std::vector<const BufferLayout*> mBindings;
    bool operator == (const PipelineLayout& o) const { return mPipelineHash == o.mPipelineHash; }
    bool operator < (const PipelineLayout& o) const { return mPipelineHash < o.mPipelineHash; }
};

struct PipelineState
{
    size_t mRootHash;       // Persistent
    size_t mPipelineHash;   // Persistent
    size_t mResourceHash;   // Transient
    size_t mBuffersHash;    // Transient
    bool operator == (const PipelineState& other) const {
        return std::memcmp(this, &other, sizeof(PipelineState));
    }
    bool operator < (const PipelineState& other) const {
        return mRootHash != other.mRootHash ? mRootHash < other.mRootHash
            : mPipelineHash != other.mPipelineHash ? mPipelineHash < other.mPipelineHash
            : mResourceHash != other.mResourceHash ? mResourceHash < other.mResourceHash
            : mBuffersHash != other.mBuffersHash ? mBuffersHash < other.mBuffersHash
            : this < &other;
    }
};

// Draw commands are forwarded to a subclass of this class
class CommandBufferInteropBase
{
public:
    virtual ~CommandBufferInteropBase() { }
    virtual void Reset() = 0;
    virtual void ClearRenderTarget(const ClearConfig& clear) = 0;
    virtual void CopyBufferData(GraphicsBufferBase* buffer, const std::span<RangeInt>& ranges) { }
    virtual void DrawMesh(std::span<const BufferLayout*> bindings, const PipelineLayout* pso, std::span<void*> resources, const DrawConfig& config, int instanceCount = 1) { }
    virtual void DrawMesh(const Mesh* mesh, const Material* material, const DrawConfig& config) = 0;
    virtual void Execute() = 0;
};

// Use this interface to issue draw calls to the graphics device
class CommandBuffer {
protected:
    std::unique_ptr<CommandBufferInteropBase> mInterop;
public:
    CommandBuffer(CommandBuffer& other) = delete;
    CommandBuffer(CommandBuffer&& other) = default;
    CommandBuffer(CommandBufferInteropBase* interop) : mInterop(interop) { }
    void Reset() { mInterop->Reset(); }
    void ClearRenderTarget(const ClearConfig& config) { mInterop->ClearRenderTarget(config); }
    void CopyBufferData(GraphicsBufferBase* buffer, const std::span<RangeInt>& ranges)
    {
        mInterop->CopyBufferData(buffer, ranges);
    }
    void DrawMesh(std::span<const BufferLayout*> bindings, const PipelineLayout* pso, std::span<void*> resources, const DrawConfig& config, int instanceCount = 1)
    {
        mInterop->DrawMesh(bindings, pso, resources, config, instanceCount);
    }
    void DrawMesh(const Mesh* mesh, const Material* material)
    {
        DrawMesh(mesh, material, DrawConfig::MakeDefault());
    }
    void DrawMesh(const Mesh* mesh, const Material* material, const DrawConfig& config)
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

    // Calculate which PSO this draw call would land in
    virtual const PipelineLayout* RequirePipeline(std::span<const BufferLayout*> bindings, const Material* material) = 0;

    // Get shader reflection data for the specified shader
    virtual ShaderBase::ShaderReflection* RequireReflection(Shader& shader) = 0;

    // Rendering is complete; flip the backbuffer
    virtual void Present() = 0;

};

