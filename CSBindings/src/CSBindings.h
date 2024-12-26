#pragma once

#define DLLCLASS __declspec(dllexport)
#define DLLFUNC __cdecl

#include <stdint.h>
#include "Buffer.h"
#include "BridgeTypes.h"
#include <functional>
//#include "TextureCompression.h"

class Mesh;
class Model;
class Texture;
class GraphicsBufferBase;
class RenderTarget2D;
class Material;
struct PipelineLayout;
class FontInstance;
class GraphicsSurface;
class WindowBase;
class Input;
class PreprocessedShader;
class CompiledShader;

typedef Mesh NativeMesh;
typedef Model NativeModel;
typedef Texture NativeTexture;
typedef GraphicsBufferBase NativeBuffer;
typedef RenderTarget2D NativeRenderTarget;
typedef Material NativeMaterial;
typedef PipelineLayout NativePipeline;
typedef FontInstance NativeFont;
typedef GraphicsSurface NativeSurface;
typedef WindowBase NativeWindow;
typedef Input NativeInput;
typedef CompiledShader NativeCompiledShader;

class NativePlatform;
class NativeScene;
class NativeGraphics;

struct Bool {
	uint8_t mValue;
	Bool(bool value) : mValue((uint8_t)(value ? 1 : 0)) { }
	operator bool() const { return mValue; }
};

struct CSSpan {
	const void* mData;
	int mSize;
	CSSpan(const void* data, int size)
		: mData(data), mSize(size) { }
};
struct CSSpanSPtr {
	struct Ptr {
		void* mPointer;
		void* mData;
	};
	const Ptr* mData;
	int mSize;
	CSSpanSPtr(const void* data, int size)
		: mData((Ptr*)data), mSize(size) { }
};

struct DLLCLASS CSString {
	const wchar_t* mBuffer;
	int mSize;
};
struct DLLCLASS CSString8 {
	const char* mBuffer;
	int mSize;
	CSString8() : mBuffer(nullptr), mSize(0) { }
	CSString8(const char* buffer, int size) : mBuffer(buffer), mSize(size) { }
};

struct DLLCLASS CSIdentifier {
	uint16_t mId;
	CSIdentifier(uint16_t id) : mId(id) { }
	static CSString8 GetName(uint16_t id);
	static CSString GetWName(uint16_t id);
	static uint16_t GetIdentifier(CSString str);
	static uint16_t GetIdentifier(CSString8 str);
};

struct DLLCLASS CSBufferElement {
	CSIdentifier mBindName;
	uint16_t mBufferStride;
	BufferFormat mFormat;
	void* mData;
};
struct DLLCLASS CSBufferLayout {
	uint64_t identifier;
	int revision; int size;
	CSBufferElement* mElements;
	uint8_t mElementCount;
	uint8_t mUsage;
	int mOffset;
	int mCount;
};
struct DLLCLASS CSRenderTargetBinding {
	NativeRenderTarget* mTarget;
	int mMip, mSlice;
	CSRenderTargetBinding(NativeRenderTarget* target, int mip = 0, int slice = 0)
		: mTarget(target), mMip(mip), mSlice(slice) { }
};
/*struct DLLCLASS CSBufferFormat {
	enum Format : uint8_t { Float, Int, Short, Byte, };
	Format mFormat;
	uint8_t mComponents;
};
struct DLLCLASS CSBuffer {
	CSIdentifier mBindName;
	const void* mData;
	int mStride;
	CSBufferFormat mFormat;
};*/
struct DLLCLASS CSTexture {
	NativeTexture* mTexture;
public:
	CSTexture() : mTexture(nullptr) { }
	CSTexture(NativeTexture* tex) : mTexture(tex) { }
	void SetTexture(NativeTexture* tex) {
		mTexture = tex;
	}
	static void SetSize(NativeTexture* tex, Int3 size);
	static Int3C GetSize(NativeTexture* tex);
	static void SetFormat(NativeTexture* tex, BufferFormat fmt);
	static BufferFormat GetFormat(NativeTexture* tex);
	static void SetMipCount(NativeTexture* tex, int count);
	static int GetMipCount(NativeTexture* tex);
	static void SetArrayCount(NativeTexture* tex, int count);
	static int GetArrayCount(NativeTexture* tex);
	static void SetAllowUnorderedAccess(NativeTexture* tex, Bool enable);
	static Bool GetAllowUnorderedAccess(NativeTexture* tex);
	static CSSpan GetTextureData(NativeTexture* tex, int mip, int slice);
	static void MarkChanged(NativeTexture* tex);
	static NativeTexture* _Create(CSString name);
	static void Swap(NativeTexture* from, NativeTexture* to);
	static void Dispose(NativeTexture* tex);
};
struct DLLCLASS CSRenderTarget {
	NativeRenderTarget* mRenderTarget;
public:
	CSRenderTarget() : mRenderTarget(nullptr) { }
	CSRenderTarget(NativeRenderTarget* target) : mRenderTarget(target) { }
	static Int2C GetSize(NativeRenderTarget* target);
	static void SetSize(NativeRenderTarget* target, Int2 size);
	static BufferFormat GetFormat(NativeRenderTarget* target);
	static void SetFormat(NativeRenderTarget* target, BufferFormat format);
	static int GetMipCount(NativeRenderTarget* target);
	static void SetMipCount(NativeRenderTarget* target, int size);
	static int GetArrayCount(NativeRenderTarget* target);
	static void SetArrayCount(NativeRenderTarget* target, int size);
	static NativeRenderTarget* _Create(CSString name);
	static void Dispose(NativeRenderTarget* target);
};
struct CSGlyph {
	wchar_t mGlyph;
	Int2 mAtlasOffset;
	Int2 mSize;
	Int2 mOffset;
	int mAdvance;
};
class DLLCLASS CSFont {
	NativeFont* mFont;
public:
	CSFont(NativeFont* font) : mFont(font) { }
	static void Dispose(NativeFont* font);
private:
	static NativeTexture* GetTexture(const NativeFont* font);
	static int GetLineHeight(const NativeFont* font);
	static int GetKerning(const NativeFont* font, wchar_t c1, wchar_t c2);
	static int GetKerningCount(const NativeFont* font);
	static void GetKernings(const NativeFont* font, CSSpan kernings);
	static int GetGlyphCount(const NativeFont* font);
	static int GetGlyphId(const NativeFont* font, wchar_t chr);
	static const CSGlyph& GetGlyph(const NativeFont* font, int id);
};
static_assert(sizeof(BufferReference) == 16);

class DLLCLASS CSInstance {
	int mInstanceId;
public:
	CSInstance(int instanceId)
		: mInstanceId(instanceId) { }
	int GetInstanceId() { return mInstanceId; }
};

struct DLLCLASS CSUniformValue {
	CSIdentifier mName;
	CSIdentifier mType;
	int mOffset;
	int mSize;
	uint8_t mRows, mColumns;
	uint16_t mFlags;
};
struct CSConstantBufferData {
	CSIdentifier mName;
	int mSize;
	int mBindPoint;
};
class DLLCLASS CSConstantBuffer {
	CSConstantBufferData* mConstantBuffer;
public:
	CSConstantBuffer(CSConstantBufferData* data) : mConstantBuffer(data) { }
	static CSSpan GetValues(const CSConstantBufferData* cb);
};
class DLLCLASS CSResourceBinding {
public:
	CSIdentifier mName;
	int mBindPoint;
	int mStride;
	uint8_t mType;
};
class DLLCLASS CSInputParameter {
public:
	CSIdentifier mName;
	CSIdentifier mSemantic;
	int mSemanticIndex;
	int mRegister;
	uint8_t mMask;
	uint8_t mType;
};
class DLLCLASS CSPipeline {
	const NativePipeline* mPipeline;
public:
	CSPipeline(const NativePipeline* pipeline)
		: mPipeline(pipeline) { }
	const NativePipeline* GetNativePipeline() const { return mPipeline; }
private:
	static short GetName(const NativePipeline* pipeline);
	static int GetHasStencilState(const NativePipeline* pipeline);
	static int GetExpectedBindingCount(const NativePipeline* pipeline);
	static int GetExpectedConstantBufferCount(const NativePipeline* pipeline);
	static int GetExpectedResourceCount(const NativePipeline* pipeline);
	static CSSpan GetConstantBuffers(const NativePipeline* pipeline);
	static CSSpan GetResources(const NativePipeline* pipeline);
	static CSSpan GetBindings(const NativePipeline* pipeline);
};
struct CSDrawConfig {
	int mIndexBase;
	int mIndexCount;
	int mInstanceBase = 0;
	CSDrawConfig(int indexStart, int indexCount)
		: mIndexBase(indexStart), mIndexCount(indexCount) { }
};
class DLLCLASS CSPreprocessedShader {
	PreprocessedShader* mShader;
public:
	CSPreprocessedShader(PreprocessedShader* shader)
		: mShader(shader) { }
	static CSString8 GetSource(const PreprocessedShader* shader);
	static int GetIncludeFileCount(const PreprocessedShader* shader);
	static CSString8 GetIncludeFile(const PreprocessedShader* shader, int id);
	static void Dispose(PreprocessedShader* shader);
};
class DLLCLASS CSCompiledShader {
	NativeCompiledShader* mShader = nullptr;
public:
	CSCompiledShader(NativeCompiledShader* shader)
		: mShader(shader) { }
	NativeCompiledShader* GetNativeShader() const { return mShader; }

	struct ShaderStats {
		int mInstructionCount;
		int mTempRegCount;
		int mArrayIC;
		int mTexIC;
		int mFloatIC;
		int mIntIC;
		int mFlowIC;
	};
private:
	static NativeCompiledShader* _Create(CSIdentifier name, int byteSize, int cbcount, int rbcount, int ipcount);
	static void InitializeValues(NativeCompiledShader* shader, int cb, int vcount);
	static CSSpan GetValues(NativeCompiledShader* shader, int cb);
	static CSSpan GetConstantBuffers(const NativeCompiledShader* shader);
	static CSSpan GetResources(const NativeCompiledShader* shader);
	static CSSpan GetInputParameters(const NativeCompiledShader* shader);
	static CSSpan GetBinaryData(const NativeCompiledShader* shader);
	static const ShaderStats& GetStatistics(const NativeCompiledShader* shader);
};
struct CSClearConfig {
	Vector4 ClearColor;
	float ClearDepth;
	__int32 ClearStencil;
	CSClearConfig(Vector4 color, float depth = -1) : ClearColor(color), ClearDepth(depth), ClearStencil(0) { }
	bool HasClearColor() const { return !(ClearColor == GetInvalidColor()); }
	bool HasClearDepth() const { return ClearDepth != -1; }
	bool HasClearScencil() const { return ClearStencil != 0; }
	static const Vector4 GetInvalidColor() { return Vector4(-1, -1, -1, -1); }
};
struct CSGraphicsCapabilities {
	Bool mComputeShaders;
	Bool mMeshShaders;
	Bool mMinPrecision;
};
struct CSRenderStatistics {
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
class DLLCLASS CSGraphics {
	NativeGraphics* mGraphics = nullptr;
public:
	CSGraphics(NativeGraphics* graphics)
		: mGraphics(graphics) { }
	NativeGraphics* GetNativeGraphics() const { return mGraphics; }
private:
	static void Dispose(NativeGraphics* graphics);
	static uint16_t GetDeviceName(const NativeGraphics* graphics);
	static CSGraphicsCapabilities GetCapabilities(const NativeGraphics* graphics);
	static CSRenderStatistics GetRenderStatistics(const NativeGraphics* graphics);
	static void BeginScope(NativeGraphics* graphics, CSString name);
	static void EndScope(NativeGraphics* graphics);
	static NativeSurface* CreateSurface(NativeGraphics* graphics, NativeWindow* window);
	static void SetSurface(NativeGraphics* graphics, NativeSurface* surface);
	static NativeSurface* GetSurface(NativeGraphics* graphics);
	static void SetRenderTargets(NativeGraphics* graphics, CSSpan colorTargets, CSRenderTargetBinding depthTarget);
	static PreprocessedShader* PreprocessShader(CSString path, CSSpan macros);
	static const NativeCompiledShader* CompileShader(NativeGraphics* graphics, CSString8 source, CSString entry, CSIdentifier identifier, CSString dbgFilename);
	static const NativePipeline* RequirePipeline(NativeGraphics* graphics, CSSpan bindings,
		NativeCompiledShader* vertexShader, NativeCompiledShader* pixelShader, void* materialState);
	static const NativePipeline* RequireMeshPipeline(NativeGraphics* graphics, CSSpan bindings,
		NativeCompiledShader* meshShader, NativeCompiledShader* pixelShader, void* materialState);
	static const NativePipeline* RequireComputePSO(NativeGraphics* graphics, NativeCompiledShader* computeShader);
	static void* RequireFrameData(NativeGraphics* graphics, int byteSize);
	static void* RequireConstantBuffer(NativeGraphics* graphics, CSSpan span, size_t hash = 0);
	static void CopyBufferData(NativeGraphics* graphics, const CSBufferLayout* layout, CSSpan ranges);
	static void CopyBufferData(NativeGraphics* graphics, const CSBufferLayout* source, const CSBufferLayout* dest, int sourceOffset, int destOffset, int length);
	static void CommitTexture(NativeGraphics* graphics, const NativeTexture* texture);
	static void Draw(NativeGraphics* graphics, CSPipeline pipeline, CSSpan buffers, CSSpan resources, CSDrawConfig config, int instanceCount);
	static void Dispatch(NativeGraphics* graphics, CSPipeline pipeline, CSSpan resources, Int3 groupCount);
	static void Reset(NativeGraphics* graphics);
	static void Clear(NativeGraphics* graphics, CSClearConfig clear);
	static void Wait(NativeGraphics* graphics);
	static void Execute(NativeGraphics* graphics);
	static void SetViewport(NativeGraphics* graphics, RectInt viewport);
	static bool IsTombstoned(NativeGraphics* graphics);
	static uint64_t GetGlobalPSOHash(NativeGraphics* graphics);
	static uint64_t CreateReadback(NativeGraphics* graphics, NativeRenderTarget* rt);
	static int GetReadbackResult(NativeGraphics* graphics, uint64_t readback);
	static int CopyAndDisposeReadback(NativeGraphics* graphics, uint64_t readback, CSSpan data);
};
class DLLCLASS CSGraphicsSurface {
	NativeSurface* mSurface;
public:
	CSGraphicsSurface(NativeSurface* surface)
		: mSurface(surface) { }
	NativeSurface* GetNativeSurface() const { return mSurface; }
	static void Dispose(NativeSurface* surface);
private:
	static NativeRenderTarget* GetBackBuffer(const NativeSurface* surface);
	static Int2C GetResolution(const NativeSurface* surface);
	static void SetResolution(NativeSurface* surface, Int2 res);
	static void RegisterDenyPresent(NativeSurface* surface, int delta);
	static void Present(NativeSurface* surface);
};
struct CSWindowFrame {
	RectInt Position;
	Int2 ClientOffset;
	bool Maximized;
};
class DLLCLASS CSWindow {
	NativeWindow* mWindow = nullptr;
public:
	CSWindow(NativeWindow* window)
		: mWindow(window) { }
	NativeWindow* GetNativeWindow() { return mWindow; }
	static NativeWindow* CreateChildWindow(NativeWindow* parent, CSString name, RectInt rect);
private:
	static void Dispose(NativeWindow* window);
	static int GetStatus(NativeWindow* window);
	static Int2C GetSize(const NativeWindow* window);
	static void SetSize(NativeWindow* window, Int2 size);
	static void SetStyle(NativeWindow* window, CSString style);
	static void SetVisible(NativeWindow* window, bool visible);
	static void SetInput(NativeWindow* window, NativeInput* input);
	static CSWindowFrame GetWindowFrame(const NativeWindow* window);
	static void SetWindowFrame(const NativeWindow* window, const RectInt* frame, bool maximized);
	static void RegisterMovedCallback(const NativeWindow* window, void (*Callback)(), bool enable);
};

struct CSPointer {
	unsigned int mDeviceId;
	int mDeviceType;
	Vector2 mPositionCurrent;
	Vector2 mPositionPrevious;
	Vector2 mPositionDown;
	float mTotalDrag;
	unsigned int mCurrentButtonState;
	unsigned int mPreviousButtonState;
	int mMouseScroll;
};
struct CSKey {
	unsigned char mKeyId;
};
class DLLCLASS CSInput {
	NativeInput* mInput = nullptr;
public:
	CSInput(NativeInput* input)
		: mInput(input) { }
	NativeInput* GetNativeInput() { return mInput; }
private:
	CSSpanSPtr GetPointers(NativeInput* platform);
	Bool GetKeyDown(NativeInput* platform, unsigned char key);
	Bool GetKeyPressed(NativeInput* platform, unsigned char key);
	Bool GetKeyReleased(NativeInput* platform, unsigned char key);
	CSSpan GetPressKeys(NativeInput* platform);
	CSSpan GetDownKeys(NativeInput* platform);
	CSSpan GetReleaseKeys(NativeInput* platform);
	CSSpan GetCharBuffer(NativeInput* platform);
	void ReceiveTickEvent(NativeInput* platform);
};

class DLLCLASS CSResources {
public:
	static NativeModel* LoadModel(CSString path);
	static NativeTexture* LoadTexture(CSString path);
	static NativeFont* LoadFont(CSString path);
};

class DLLCLASS Platform {
	NativePlatform* mPlatform = nullptr;
public:
	Platform(NativePlatform* platform)
		: mPlatform(platform) { }

	static void InitializeGraphics(NativePlatform* platform);

	static int GetCoreCount();

	static NativeWindow* CreateWindow(NativePlatform* platform, CSString name);
	static NativeInput* CreateInput(NativePlatform* platform);
	static NativeGraphics* CreateGraphics(NativePlatform* platform);

	static int MessagePump(NativePlatform* platform);
	static void Dispose(NativePlatform* platform);

	static NativePlatform* Create();
};

/*
extern "C" void __declspec(dllexport) NVTTCompressTextureBC1(InputData * img, void* outData);
extern "C" void __declspec(dllexport) NVTTCompressTextureBC2(InputData * img, void* outData);
extern "C" void __declspec(dllexport) NVTTCompressTextureBC3(InputData * img, void* outData);
extern "C" void __declspec(dllexport) NVTTCompressTextureBC4(InputData * img, void* outData);
extern "C" void __declspec(dllexport) NVTTCompressTextureBC5(InputData * img, void* outData);
*/