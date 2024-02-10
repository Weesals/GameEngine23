#pragma once

#define DLLCLASS __declspec(dllexport)
#define DLLFUNC __cdecl

#include <stdint.h>
#include "Buffer.h"
#include "BridgeTypes.h"

class Mesh;
class Model;
class Texture;
class GraphicsBufferBase;
class RenderTarget2D;
class Material;
struct PipelineLayout;
class FontInstance;
class RenderPass;
class GraphicsSurface;
class WindowBase;
class Shader;

typedef Mesh NativeMesh;
typedef Model NativeModel;
typedef Texture NativeTexture;
typedef GraphicsBufferBase NativeBuffer;
typedef RenderTarget2D NativeRenderTarget;
typedef Shader NativeShader;
typedef Material NativeMaterial;
typedef PipelineLayout NativePipeline;
typedef FontInstance NativeFont;
typedef RenderPass NativeRenderPass;
typedef GraphicsSurface NativeSurface;
typedef WindowBase NativeWindow;

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
};

struct DLLCLASS CSBufferElement {
	CSIdentifier mBindName;
	uint16_t mBufferStride;
	BufferFormat mFormat;
	void* mData;
};
struct DLLCLASS CSBufferLayout {
	uint64_t identifier; int revision; int size;
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
	static void SetSize(NativeTexture* tex, Int2 size);
	static Int2C GetSize(NativeTexture* tex);
	static void SetFormat(NativeTexture* tex, BufferFormat fmt);
	static BufferFormat GetFormat(NativeTexture* tex);
	static void SetMipCount(NativeTexture* tex, int count);
	static int GetMipCount(NativeTexture* tex);
	static void SetArrayCount(NativeTexture* tex, int count);
	static int GetArrayCount(NativeTexture* tex);
	static CSSpan GetTextureData(NativeTexture* tex, int mip, int slice);
	static void MarkChanged(NativeTexture* tex);
	static NativeTexture* _Create(CSString name);
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
	static NativeTexture* GetTexture(const NativeFont* font);
	static int GetLineHeight(const NativeFont* font);
	static int GetKerning(const NativeFont* font, wchar_t c1, wchar_t c2);
	static int GetGlyphId(const NativeFont* font, wchar_t chr);
	static const CSGlyph& GetGlyph(const NativeFont* font, int id);
};
class DLLCLASS CSMaterial {
	NativeMaterial* mMaterial;
public:
	CSMaterial(NativeMaterial* material) : mMaterial(material) { }
	static int GetParameterIdentifiers(NativeMaterial* material, CSIdentifier* outlist, int capacity);
	static void SetRenderPass(NativeMaterial* material, CSIdentifier identifier);
	static CSSpan GetValueData(NativeMaterial* material, CSIdentifier identifier);
	static int GetValueType(NativeMaterial* material, CSIdentifier identifier);
	static void SetValueFloat(NativeMaterial* material, CSIdentifier identifier, const float* data, int count);
	static void SetValueInt(NativeMaterial* material, CSIdentifier identifier, const int* data, int count);
	static void SetValueTexture(NativeMaterial* material, CSIdentifier identifier, CSTexture texture);
	static void SetBlendMode(NativeMaterial* material, void* data);
	static void SetRasterMode(NativeMaterial* material, void* data);
	static void SetDepthMode(NativeMaterial* material, void* data);
	static void InheritProperties(NativeMaterial* material, NativeMaterial* other);
	static void RemoveInheritance(NativeMaterial* material, NativeMaterial* other);
	static CSSpan ResolveResources(NativeGraphics* graphics, NativePipeline* pipeline, CSSpan materials);
	static NativeMaterial* _Create(CSString shaderPath);
	static void Dispose(NativeMaterial* material);
	NativeMaterial* GetNativeMaterial() { return mMaterial; }
};
struct CSMeshData {
	int mVertexCount;
	int mIndexCount;
	CSString8 mName;
	CSBufferElement mPositions;
	CSBufferElement mNormals;
	CSBufferElement mTexCoords;
	CSBufferElement mColors;
	CSBufferElement mIndices;
};
class DLLCLASS CSMesh {
	NativeMesh* mMesh;
public:
	CSMesh(NativeMesh* mesh)
		: mMesh(mesh) { }
	static int GetVertexCount(const NativeMesh* mesh);
	static int GetIndexCount(const NativeMesh* mesh);
	static void SetVertexCount(NativeMesh* mesh, int count);
	static void SetIndexCount(NativeMesh* mesh, int count);
	static const CSBufferLayout* GetVertexBuffer(NativeMesh* mesh);
	static const CSBufferLayout* GetIndexBuffer(NativeMesh* mesh);
	static void RequireVertexNormals(NativeMesh* mesh, uint8_t fmt);
	static void RequireVertexTexCoords(NativeMesh* mesh, uint8_t fmt);
	static void RequireVertexColors(NativeMesh* mesh, uint8_t fmt);
	static void GetMeshData(const NativeMesh* mesh, CSMeshData* outdata);
	static NativeMaterial* GetMaterial(NativeMesh* mesh);
	static const BoundingBox& GetBoundingBox(NativeMesh* mesh);
	NativeMesh* GetNativeMesh() const { return mMesh; }
	static NativeMesh* _Create(CSString name);
};
class DLLCLASS CSModel {
	NativeModel* mModel;
public:
	CSModel(NativeModel* mesh)
		: mModel(mesh) { }
	static int GetMeshCount(const NativeModel* model);
	static CSSpanSPtr GetMeshes(const NativeModel* model);
	static CSMesh GetMesh(const NativeModel* model, int id);
	NativeModel* GetNativeModel() const { return mModel; }
};

class DLLCLASS CSInstance {
	int mInstanceId;
public:
	CSInstance(int instanceId)
		: mInstanceId(instanceId) { }
	int GetInstanceId() { return mInstanceId; }
};

struct DLLCLASS CSUniformValue {
	CSIdentifier mName;
	int mOffset;
	int mSize;
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
class DLLCLASS CSPipeline {
	const NativePipeline* mPipeline;
public:
	CSPipeline(const NativePipeline* pipeline)
		: mPipeline(pipeline) { }
	const NativePipeline* GetNativePipeline() const { return mPipeline; }
private:
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
	CSDrawConfig(int indexStart, int indexCount)
		: mIndexBase(indexStart), mIndexCount(indexCount) { }
};
class DLLCLASS CSGraphics {
	NativeGraphics* mGraphics = nullptr;
public:
	CSGraphics(NativeGraphics* graphics)
		: mGraphics(graphics) { }
	NativeGraphics* GetNativeGraphics() const { return mGraphics; }
private:
	static void Dispose(NativeGraphics* graphics);
	static NativeSurface* GetPrimarySurface(const NativeGraphics* graphics);
	static Int2C GetResolution(const NativeGraphics* graphics);
	static void SetResolution(const NativeGraphics* graphics, Int2 res);
	static void SetRenderTargets(NativeGraphics* graphics, CSSpan colorTargets, CSRenderTargetBinding depthTarget);
	static const NativePipeline* RequirePipeline(NativeGraphics* graphics, CSSpan bindings, NativeShader* vertexShader, NativeShader* pixelShader, void* materialState, CSSpan macros, CSIdentifier renderPass);
	static void* RequireFrameData(NativeGraphics* graphics, int byteSize);
	static CSSpan ImmortalizeBufferLayout(NativeGraphics* graphics, CSSpan bindings);
	static void* RequireConstantBuffer(NativeGraphics* graphics, CSSpan span);
	static void CopyBufferData(NativeGraphics* graphics, const CSBufferLayout* layout, CSSpan ranges);
	static void Draw(NativeGraphics* graphics, CSPipeline pipeline, CSSpan buffers, CSSpan resources, CSDrawConfig config, int instanceCount);
	static void Reset(NativeGraphics* graphics);
	static void Clear(NativeGraphics* graphics);
	static void Execute(NativeGraphics* graphics);
	static void SetViewport(NativeGraphics* graphics, RectInt viewport);
	static bool IsTombstoned(NativeGraphics* graphics);
	static uint64_t GetGlobalPSOHash(NativeGraphics* graphics);
};
class DLLCLASS CSGraphicsSurface {
	NativeSurface* mSurface;
public:
	CSGraphicsSurface(NativeSurface* surface)
		: mSurface(surface) { }
	NativeSurface* GetNativeSurface() const { return mSurface; }
	static void RegisterDenyPresent(NativeSurface* surface, int delta);
};
class DLLCLASS CSWindow {
	NativeWindow* mWindow = nullptr;
public:
	CSWindow(NativeWindow* window)
		: mWindow(window) { }
	static void Dispose(NativeWindow* window);
	static Int2C GetResolution(const NativeWindow* window);
};

struct CSPointer {
	unsigned int mDeviceId;
	Vector2 mPositionCurrent;
	Vector2 mPositionPrevious;
	Vector2 mPositionDown;
	float mTotalDrag;
	unsigned int mCurrentButtonState;
	unsigned int mPreviousButtonState;
};
struct CSKey {
	unsigned char mKeyId;
};
class DLLCLASS CSInput {
	NativePlatform* mPlatform = nullptr;
public:
	CSInput(NativePlatform* platform)
		: mPlatform(platform) { }
private:
	CSSpanSPtr GetPointers(NativePlatform* platform);
	Bool GetKeyDown(NativePlatform* platform, unsigned char key);
	Bool GetKeyPressed(NativePlatform* platform, unsigned char key);
	Bool GetKeyReleased(NativePlatform* platform, unsigned char key);
	CSSpan GetPressKeys(NativePlatform* platform);
	CSSpan GetDownKeys(NativePlatform* platform);
	CSSpan GetReleaseKeys(NativePlatform* platform);
	CSSpan GetCharBuffer(NativePlatform* platform);
	void ReceiveTickEvent(NativePlatform* platform);
};

class DLLCLASS CSResources {
public:
	static NativeShader* LoadShader(CSString path, CSString entryPoint);
	static NativeModel* LoadModel(CSString path);
	static NativeTexture* LoadTexture(CSString path);
	static NativeFont* LoadFont(CSString path);
};

class DLLCLASS Platform {
	NativePlatform* mPlatform = nullptr;
public:
	Platform(NativePlatform* platform)
		: mPlatform(platform) { }

	static NativeWindow* GetWindow(const NativePlatform* platform);
	static NativeGraphics* CreateGraphics(const NativePlatform* platform);

	static int MessagePump(NativePlatform* platform);
	static void Present(NativePlatform* platform);
	static void Dispose(NativePlatform* platform);

	static NativePlatform* Create();
};
