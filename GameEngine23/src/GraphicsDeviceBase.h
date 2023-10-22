#pragma once

#include "Mesh.h"
#include "Material.h"
#include "GraphicsUtility.h"
#include "GraphicsBuffer.h"
#include "Containers.h"
#include "RenderTarget2D.h"

class GraphicsDeviceBase;

class ShaderBase
{
public:
    // Reflected uniforms that can be set by the application
    struct UniformValue {
        IdentifierWithName mName;
        int mOffset;
        int mSize;
        bool operator ==(const UniformValue& other) const = default;
        size_t GenerateHash() const {
            return ((int)mName.mId << 16) | mOffset;
        }
    };
    struct ConstantBuffer {
        IdentifierWithName mName;
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
            for (auto& value : mValues) hash = AppendHash(((int)value.mName << 16) | value.mOffset, hash);
            return hash;
        }
        bool operator ==(const ConstantBuffer& other) const = default;
    };
    enum ResourceTypes : uint8_t { R_Texture, R_SBuffer, };
    struct ResourceBinding {
        IdentifierWithName mName;
        int mBindPoint;
        int mStride;
        ResourceTypes mType;
        bool operator ==(const ResourceBinding& other) const = default;
    };
    enum ParameterTypes : uint8_t { P_Unknown, P_UInt, P_SInt, P_Float, };
    struct InputParameter {
        IdentifierWithName mName;
        IdentifierWithName mSemantic;
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
    IdentifierWithName mRenderPass;
    size_t mRootHash;       // Persistent
    size_t mPipelineHash;   // Persistent
    std::vector<const ShaderBase::ConstantBuffer*> mConstantBuffers;
    std::vector<const ShaderBase::ResourceBinding*> mResources;
    std::vector<const BufferLayout*> mBindings;
    bool operator == (const PipelineLayout& o) const { return mPipelineHash == o.mPipelineHash; }
    bool operator < (const PipelineLayout& o) const { return mPipelineHash < o.mPipelineHash; }
    bool IsValid() const { return mPipelineHash != 0; }
    int GetResourceCount() const { return (int)(mConstantBuffers.size() + mResources.size()); }
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
    virtual GraphicsDeviceBase* GetGraphics() const = 0;
    virtual void Reset() = 0;
    virtual void SetRenderTarget(const RenderTarget2D* target) { }
    virtual void ClearRenderTarget(const ClearConfig& clear) = 0;
    virtual void* RequireConstantBuffer(std::span<const uint8_t> data) { return 0; }
    virtual void CopyBufferData(GraphicsBufferBase* buffer, const std::span<RangeInt>& ranges) { }
    virtual void DrawMesh(std::span<const BufferLayout*> bindings, const PipelineLayout* pso, std::span<const void*> resources, const DrawConfig& config, int instanceCount = 1, const char* name = nullptr) { }
    virtual void Execute() = 0;
};

// Use this interface to issue draw calls to the graphics device
class CommandBuffer {
protected:
    std::unique_ptr<CommandBufferInteropBase> mInterop;
    ExpandableMemoryArena mArena;
    std::vector<const BufferLayout*> tBindingLayout;
public:
    CommandBuffer(CommandBuffer& other) = delete;
    CommandBuffer(CommandBuffer&& other) = default;
    CommandBuffer(CommandBufferInteropBase* interop) : mInterop(interop) { }
    CommandBuffer& operator = (CommandBuffer&& other) = default;
    GraphicsDeviceBase* GetGraphics() const { return mInterop->GetGraphics(); }
    void Reset() { mInterop->Reset(); mArena.Clear(); }
    void SetRenderTarget(const RenderTarget2D* target) { mInterop->SetRenderTarget(target); }
    void ClearRenderTarget(const ClearConfig& config) { mInterop->ClearRenderTarget(config); }
    int GetFrameDataConsumed() const { return mArena.SumConsumedMemory(); }
    void* RequireFrameData(int size) { return mArena.Require(size); }
    template<class T> std::span<T> RequireFrameData(int count) { return std::span<T>((T*)RequireFrameData(count * sizeof(T)), count); }
    template<class T> std::span<T> RequireFrameData(std::span<T> data) {
        auto outData = std::span<T>((T*)RequireFrameData((int)(data.size() * sizeof(T))), data.size());
        for (int i = 0; i < outData.size(); ++i) outData[i] = data[i];
        return outData;
    }
    template<class R, class T, class Fn> std::span<R> RequireFrameData(std::span<T> data, Fn fn) {
        auto outData = RequireFrameData<R>((int)data.size());
        std::transform(data.begin(), data.end(), outData.data(), fn);
        return outData;
    }
    void* RequireConstantBuffer(std::span<const uint8_t> data)
    {
        return mInterop->RequireConstantBuffer(data);
    }
    void CopyBufferData(GraphicsBufferBase* buffer, const std::span<RangeInt>& ranges)
    {
        mInterop->CopyBufferData(buffer, ranges);
    }
    void DrawMesh(
        std::span<const BufferLayout*> bindings,
        const PipelineLayout* pso, std::span<const void*> resources,
        const DrawConfig& config, int instanceCount = 1, const char* name = nullptr)
    {
        mInterop->DrawMesh(bindings, pso, resources, config, instanceCount, name);
    }
    void DrawMesh(const Mesh* mesh, const Material* material, const DrawConfig& config, const char* name = nullptr);
    void DrawMesh(const Mesh* mesh, const Material* material, const char* name = nullptr)
    {
        if (mesh->GetVertexCount() == 0) return;
        DrawMesh(mesh, material, DrawConfig::MakeDefault(), name);
    }
    void Execute() { mInterop->Execute(); }
};

struct RenderStatistics {
    int mBufferCreates;
    int mBufferWrites;
    size_t mBufferBandwidth;
    int mDrawCount;
    int mInstanceCount;
    void BufferWrite(size_t size) {
        mBufferWrites++;
        mBufferBandwidth += size;
    }
};

// Base class for a graphics device
class GraphicsDeviceBase
{
public:
    RenderStatistics mStatistics;

    virtual ~GraphicsDeviceBase() { }

    // Get the resolution of the client area
    virtual Vector2 GetClientSize() const = 0;

    // Create a command buffer which allows draw calls to be submitted
    virtual CommandBuffer CreateCommandBuffer() = 0;

    // Calculate which PSO this draw call would land in
    const PipelineLayout* RequirePipeline(std::span<const BufferLayout*> bindings, std::span<const Material*> materials);
    virtual const PipelineLayout* RequirePipeline(std::span<const BufferLayout*> bindings, std::span<const Material*> materials, const IdentifierWithName& renderPass) = 0;

    // Rendering is complete; flip the backbuffer
    virtual void Present() = 0;

};

