#pragma once

#include "Mesh.h"
#include "Material.h"
#include "GraphicsUtility.h"
#include "GraphicsBuffer.h"
#include "Containers.h"
#include "RenderTarget2D.h"

class GraphicsDeviceBase;
class WindowBase;

class ShaderBase {
public:
    // Reflected uniforms that can be set by the application
    struct UniformValue {
        Identifier mName;
        Identifier mType;
        int mOffset;
        int mSize;
        uint8_t mRows, mColumns;
        uint16_t mFlags;
        bool operator ==(const UniformValue& other) const = default;
        size_t GenerateHash() const {
            return ((int)mName.mId << 16) | mOffset;
        }
    };
    struct ConstantBuffer {
        Identifier mName;
        int mSize;
        int mBindPoint;
        int mValueCount;
        std::unique_ptr<UniformValue[]> mValues;
        ConstantBuffer() : mValueCount(0) { }
        ConstantBuffer(ConstantBuffer&&) = default;
        ConstantBuffer(const ConstantBuffer& other) {
            *this = other;
        }
        ~ConstantBuffer() { }
        ConstantBuffer& operator=(ConstantBuffer&&) = default;
        ConstantBuffer& operator=(const ConstantBuffer& o) {
            mName = o.mName;
            mSize = o.mSize;
            mBindPoint = o.mBindPoint;
            SetValuesCount(o.mValueCount);
            for (int v = 0; v < mValueCount; ++v) mValues.get()[v] = o.mValues.get()[v];
            return *this;
        }

        void SetValuesCount(int vcount) { mValues = std::unique_ptr<UniformValue[]>(new UniformValue[mValueCount = vcount]); }
        std::span<UniformValue> GetValues() const { return std::span<UniformValue>(mValues.get(), mValueCount); }
        int GetValueIndex(const std::string& name) const {
            for (size_t i = 0; i < GetValues().size(); i++)
            {
                if (GetValues()[i].mName.GetName() == name) return (int)i;
            }
            return -1;
        }
        size_t GenerateHash() const {
            size_t hash = 0;
            for (auto& value : GetValues()) hash = AppendHash(((int)value.mName << 16) | value.mOffset, hash);
            return hash;
        }
        bool operator ==(const ConstantBuffer& other) const {
            if (!(mName == other.mName && mSize == other.mSize && mBindPoint == other.mBindPoint &&
                mValueCount == other.mValueCount)) return false;
            for (int i = 0; i < mValueCount; ++i) {
                if (mValues.get()[i] != other.mValues.get()[i]) return false;
            }
            return true;
        }
    };
    enum ResourceTypes : uint8_t { R_Texture, R_SBuffer, };
    struct ResourceBinding {
        Identifier mName;
        int mBindPoint;
        int mStride;
        ResourceTypes mType;
        bool operator ==(const ResourceBinding& other) const = default;
    };
    enum ParameterTypes : uint8_t { P_Unknown, P_UInt, P_SInt, P_Float, };
    struct InputParameter {
        Identifier mName;
        Identifier mSemantic;
        int mSemanticIndex;
        int mRegister;
        uint8_t mMask;
        ParameterTypes mType;
    };
    struct ShaderReflection {
        std::vector<ConstantBuffer> mConstantBuffers;
        std::vector<ResourceBinding> mResourceBindings;
        std::vector<InputParameter> mInputParameters;
        struct Statistics {
            int mInstructionCount;
            int mTempRegCount;
            int mArrayIC;
            int mTexIC;
            int mFloatIC;
            int mIntIC;
            int mFlowIC;
        };
        Statistics mStatistics;
    };
};
class CompiledShader : public ShaderBase {
private:
    Identifier mName;
	uint64_t mSourceHash;
	std::vector<uint8_t> mCompiledBlob;
    uint64_t mCompiledBlobHash;
    ShaderReflection mReflection;
public:
    void SetName(Identifier name) { mName = name; }
    Identifier GetName() const { return mName; }
	uint64_t GetSourceHash() const { return mSourceHash; }
	const std::vector<uint8_t>& GetBinary() const { return mCompiledBlob; }
    uint64_t GetBinaryHash() const {
        if (mCompiledBlobHash == 0) const_cast<CompiledShader*>(this)->CalculateHash();
        return mCompiledBlobHash;
    }

	std::span<uint8_t> AllocateBuffer(int size) {
		mCompiledBlob.resize(size);
		return mCompiledBlob;
	}
    void CalculateHash() {
        mCompiledBlobHash = ArrayHash((std::span<uint8_t>)mCompiledBlob);
    }
    const ShaderReflection& GetReflection() const { return mReflection; }
    ShaderReflection& GetReflection() { return mReflection; }
};

struct MacroValue {
    Identifier mName;
    Identifier mValue;
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
struct DrawConfig {
    int mIndexBase = 0;
    int mIndexCount = 0;
    int mInstanceBase = 0;
    static DrawConfig MakeDefault() { return { 0, -1, }; }
};

struct PipelineLayout {
    size_t mRootHash;       // Persistent
    size_t mPipelineHash;   // Persistent
    std::vector<const ShaderBase::ConstantBuffer*> mConstantBuffers;
    std::vector<const ShaderBase::ResourceBinding*> mResources;
    std::vector<const BufferLayout*> mBindings;
    MaterialState mMaterialState;
    bool operator == (const PipelineLayout& o) const { return mPipelineHash == o.mPipelineHash; }
    bool operator < (const PipelineLayout& o) const { return mPipelineHash < o.mPipelineHash; }
    bool IsValid() const { return mPipelineHash != 0; }
    int GetResourceCount() const { return (int)(mConstantBuffers.size() + mResources.size()); }
};

struct PipelineState {
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
struct RenderTargetBinding {
    const RenderTarget2D* mTarget;
    int mMip;
    int mSlice;
    RenderTargetBinding() : RenderTargetBinding(nullptr) { }
    RenderTargetBinding(const RenderTarget2D* target, int mip = 0, int slice = 0) : mTarget(target), mMip(mip), mSlice(slice) { }
    RenderTargetBinding(const RenderTargetBinding& other) = default;
};
class GraphicsSurface : public std::enable_shared_from_this<GraphicsSurface> {
public:
    virtual const std::shared_ptr<RenderTarget2D>& GetBackBuffer() const = 0;
    virtual Int2 GetResolution() const = 0;
    virtual void SetResolution(Int2 res) = 0;
    virtual bool GetIsOccluded() const { return false; }
    virtual void RegisterDenyPresent(int delta = 1) { }
    virtual int Present() = 0;
    virtual int WaitForFrame() { return 0; }
    virtual void WaitForGPU() { }
    std::shared_ptr<GraphicsSurface> This() { return shared_from_this(); }
};

// Draw commands are forwarded to a subclass of this class
class CommandBufferInteropBase
{
public:
    virtual ~CommandBufferInteropBase() { }
    virtual GraphicsDeviceBase* GetGraphics() const = 0;
    virtual void Reset() = 0;
    virtual std::shared_ptr<GraphicsSurface> CreateSurface(WindowBase* window) = 0;
    virtual void SetSurface(GraphicsSurface* surface) = 0;
    virtual GraphicsSurface* GetSurface() = 0;
    virtual void SetRenderTargets(std::span<RenderTargetBinding> colorTargets, RenderTargetBinding depthTarget) { }
    virtual void SetViewport(RectInt viewport) { }
    virtual void ClearRenderTarget(const ClearConfig& clear) = 0;
    virtual uint64_t GetGlobalPSOHash() const { return (uint64_t)this; }
    virtual void* RequireConstantBuffer(std::span<const uint8_t> data, size_t hash) { return 0; }
    virtual void CopyBufferData(const BufferLayout& buffer, std::span<const RangeInt> ranges) { }
    virtual const PipelineLayout* RequirePipeline(
        const CompiledShader& vertexShader, const CompiledShader& pixelShader,
        const MaterialState& materialState, std::span<const BufferLayout*> bindings) {
        return nullptr;
    }
    virtual void DrawMesh(std::span<const BufferLayout*> bindings, const PipelineLayout* pso, std::span<const void*> resources, const DrawConfig& config, int instanceCount = 1, const char* name = nullptr) { }
    virtual void Execute() = 0;
};

// Use this interface to issue draw calls to the graphics device
class CommandBuffer {
protected:
    std::unique_ptr<CommandBufferInteropBase> mInterop;
    ExpandableMemoryArena mArena;
    std::vector<const BufferLayout*> tBindingLayout;
    void* RequireFrameData(int size) { return mArena.Require(size); }
public:
    CommandBuffer(CommandBuffer& other) = delete;
    CommandBuffer(CommandBuffer&& other) = default;
    CommandBuffer(CommandBufferInteropBase* interop) : mInterop(interop) { }
    CommandBuffer& operator = (CommandBuffer&& other) = default;
    GraphicsDeviceBase* GetGraphics() const { return mInterop->GetGraphics(); }
    void Reset() { mInterop->Reset(); mArena.Clear(); }
    std::shared_ptr<GraphicsSurface> CreateSurface(WindowBase* window) { return mInterop->CreateSurface(window); }
    void SetSurface(GraphicsSurface* surface) { mInterop->SetSurface(surface); }
    GraphicsSurface* GetSurface() { return mInterop->GetSurface(); }
    void SetViewport(RectInt viewport) { mInterop->SetViewport(viewport); }
    void SetRenderTargets(std::span<RenderTargetBinding> colorTargets, RenderTargetBinding depthTarget) { mInterop->SetRenderTargets(colorTargets, depthTarget); }
    void ClearRenderTarget(const ClearConfig& config) { mInterop->ClearRenderTarget(config); }
    uint64_t GetGlobalPSOHash() const { return mInterop->GetGlobalPSOHash(); }
    int GetFrameDataConsumed() const { return mArena.SumConsumedMemory(); }
    const PipelineLayout* RequirePipeline(
        const CompiledShader& vertexShader, const CompiledShader& pixelShader,
        const MaterialState& materialState, std::span<const BufferLayout*> bindings) {
        return mInterop->RequirePipeline(vertexShader, pixelShader, materialState, bindings);
    }
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
    void* RequireConstantBuffer(std::span<const uint8_t> data, size_t hash = 0) {
        return mInterop->RequireConstantBuffer(data, hash);
    }
    void CopyBufferData(const BufferLayout& buffer, std::span<const RangeInt> ranges) {
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
    void DrawMesh(const Mesh* mesh, const Material* material, const char* name = nullptr) {
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
class GraphicsDeviceBase {
public:
    RenderStatistics mStatistics;

    virtual ~GraphicsDeviceBase() { }

    // Get the resolution of the client area
    //virtual GraphicsSurface* GetPrimarySurface() const { return nullptr; }
    //virtual Int2 GetResolution() const = 0;
    //virtual void SetResolution(Int2 res) = 0;

    // Create a command buffer which allows draw calls to be submitted
    virtual CommandBuffer CreateCommandBuffer() = 0;

    virtual CompiledShader CompileShader(const std::wstring_view& path, const std::string_view& entry,
        const std::string_view& profile, std::span<const MacroValue> macros) { return { }; }

    // Calculate which PSO this draw call would land in
    //const PipelineLayout* RequirePipeline(std::span<const BufferLayout*> bindings, std::span<const Material*> materials);
    //const PipelineLayout* RequirePipeline(std::span<const BufferLayout*> bindings, std::span<const Material*> materials, const IdentifierWithName& renderPass);
    /*virtual const PipelineLayout* RequirePipeline(
        const Shader& vertexShader, const Shader& pixelShader,
        const MaterialState& materialState, std::span<const BufferLayout*> bindings,
        std::span<const MacroValue> macros, const IdentifierWithName& renderPass
    ) = 0;*/

    // Rendering is complete; flip the backbuffer
    //virtual void Present() = 0;

};
