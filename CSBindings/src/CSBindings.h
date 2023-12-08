#pragma once

#include <stdint.h>
#include "Buffer.h"
#include "BridgeTypes.h"

class Mesh;
class Model;
class Texture;
class RenderTarget2D;
class Material;
struct PipelineLayout;
class FontInstance;
class RenderPass;
class WindowBase;
class Shader;

typedef Mesh NativeMesh;
typedef Model NativeModel;
typedef Texture NativeTexture;
typedef RenderTarget2D NativeRenderTarget;
typedef Shader NativeShader;
typedef Material NativeMaterial;
typedef PipelineLayout NativePipeline;
typedef FontInstance NativeFont;
typedef RenderPass NativeRenderPass;
typedef WindowBase NativeWindow;

class NativePlatform;
class NativeScene;
class NativeGraphics;

struct __declspec(dllexport) PerformanceTest {
	static float DoNothing();
	static float CSDLLInvoke(float f1, float f2);
	static float CPPVirtual();
	static float CPPDirect();
};

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

struct __declspec(dllexport) CSString {
	const wchar_t* mBuffer;
	int mSize;
};
struct __declspec(dllexport) CSString8 {
	const char* mBuffer;
	int mSize;
	CSString8() : mBuffer(nullptr), mSize(0) { }
	CSString8(const char* buffer, int size) : mBuffer(buffer), mSize(size) { }
};

struct __declspec(dllexport) CSIdentifier {
	uint16_t mId;
	CSIdentifier(uint16_t id) : mId(id) { }
	static CSString8 GetName(uint16_t id);
	static uint16_t GetIdentifier(CSString str);
};

struct __declspec(dllexport) CSBufferElement {
	CSIdentifier mBindName;
	uint16_t mBufferStride;
	BufferFormat mFormat;
	void* mData;
};
struct __declspec(dllexport) CSBufferLayout {
	uint64_t identifier; int revision; int size;
	CSBufferElement* mElements;
	uint8_t mElementCount;
	uint8_t mUsage;
	int mOffset;
	int mCount;
};
/*struct __declspec(dllexport) CSBufferFormat {
	enum Format : uint8_t { Float, Int, Short, Byte, };
	Format mFormat;
	uint8_t mComponents;
};
struct __declspec(dllexport) CSBuffer {
	CSIdentifier mBindName;
	const void* mData;
	int mStride;
	CSBufferFormat mFormat;
};*/
struct __declspec(dllexport) CSTexture {
	NativeTexture* mTexture;
public:
	CSTexture() : mTexture(nullptr) { }
	CSTexture(NativeTexture* tex) : mTexture(tex) { }
	void SetTexture(NativeTexture* tex) {
		mTexture = tex;
	}
	static void SetSize(NativeTexture* tex, Int2 size);
	static Int2C GetSize(NativeTexture* tex);
	static CSSpan GetTextureData(NativeTexture* tex);
	static void MarkChanged(NativeTexture* tex);
	static NativeTexture* _Create();
	static void Dispose(NativeTexture* tex);
};
struct __declspec(dllexport) CSRenderTarget {
	NativeRenderTarget* mRenderTarget;
public:
	CSRenderTarget() : mRenderTarget(nullptr) { }
	CSRenderTarget(NativeRenderTarget* target) : mRenderTarget(target) { }
	static Int2C GetSize(NativeRenderTarget* target);
	static void SetSize(NativeRenderTarget* target, Int2 size);
	static BufferFormat GetFormat(NativeRenderTarget* target);
	static void SetFormat(NativeRenderTarget* target, BufferFormat format);
	static NativeRenderTarget* _Create();
	static void Dispose(NativeRenderTarget* target);
};
struct CSGlyph {
	wchar_t mGlyph;
	Int2 mAtlasOffset;
	Int2 mSize;
	Int2 mOffset;
	int mAdvance;
};
class __declspec(dllexport) CSFont {
	NativeFont* mFont;
public:
	CSFont(NativeFont* font) : mFont(font) { }
	static NativeTexture* GetTexture(const NativeFont* font);
	static int GetLineHeight(const NativeFont* font);
	static int GetKerning(const NativeFont* font, wchar_t c1, wchar_t c2);
	static int GetGlyphId(const NativeFont* font, wchar_t chr);
	static const CSGlyph& GetGlyph(const NativeFont* font, int id);
};
class __declspec(dllexport) CSMaterial {
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
class __declspec(dllexport) CSMesh {
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
class __declspec(dllexport) CSModel {
	NativeModel* mModel;
public:
	CSModel(NativeModel* mesh)
		: mModel(mesh) { }
	static int GetMeshCount(const NativeModel* model);
	static CSSpanSPtr GetMeshes(const NativeModel* model);
	static CSMesh GetMesh(const NativeModel* model, int id);
	NativeModel* GetNativeModel() const { return mModel; }
};

class __declspec(dllexport) CSInstance {
	int mInstanceId;
public:
	CSInstance(int instanceId)
		: mInstanceId(instanceId) { }
	int GetInstanceId() { return mInstanceId; }
};

struct __declspec(dllexport) CSUniformValue {
	CSIdentifier mName;
	int mOffset;
	int mSize;
};
struct CSConstantBufferData {
	CSIdentifier mName;
	int mSize;
	int mBindPoint;
};
class __declspec(dllexport) CSConstantBuffer {
	CSConstantBufferData* mConstantBuffer;
public:
	static CSSpan GetValues(const CSConstantBufferData* cb);
};
class __declspec(dllexport) CSResourceBinding {
public:
	CSIdentifier mName;
	int mBindPoint;
	int mStride;
	uint8_t mType;
};
class __declspec(dllexport) CSPipeline {
	const NativePipeline* mPipeline;
public:
	CSPipeline(const NativePipeline* pipeline)
		: mPipeline(pipeline) { }
	static int GetExpectedBindingCount(const NativePipeline* pipeline);
	static int GetExpectedConstantBufferCount(const NativePipeline* pipeline);
	static int GetExpectedResourceCount(const NativePipeline* pipeline);
	static CSSpan GetConstantBuffers(const NativePipeline* pipeline);
	static CSSpan GetResources(const NativePipeline* pipeline);
	static CSSpan GetBindings(const NativePipeline* pipeline);
	const NativePipeline* GetNativePipeline() const { return mPipeline; }
};
struct CSDrawConfig {
	int mIndexBase;
	int mIndexCount;
	CSDrawConfig(int indexStart, int indexCount)
		: mIndexBase(indexStart), mIndexCount(indexCount) { }
};
class __declspec(dllexport) CSGraphics {
	NativeGraphics* mGraphics = nullptr;
public:
	CSGraphics(NativeGraphics* graphics)
		: mGraphics(graphics) { }
	static void Dispose(NativeGraphics* graphics);
	static Int2C GetResolution(const NativeGraphics* graphics);
	static void SetResolution(const NativeGraphics* graphics, Int2 res);
	static void SetRenderTarget(NativeGraphics* graphics, const NativeRenderTarget* target);
	static const NativePipeline* RequirePipeline(NativeGraphics* graphics, CSSpan bindings, CSSpan materials);
	static const NativePipeline* RequirePipeline(NativeGraphics* graphics, CSSpan bindings, NativeShader* vertexShader, NativeShader* pixelShader, void* materialState, CSSpan macros, CSIdentifier renderPass);
	static void* RequireFrameData(NativeGraphics* graphics, int byteSize);
	static CSSpan ImmortalizeBufferLayout(NativeGraphics* graphics, CSSpan bindings);
	static void* RequireConstantBuffer(NativeGraphics* graphics, CSSpan span);
	static void Draw(NativeGraphics* graphics, CSPipeline pipeline, CSSpan buffers, CSSpan resources, CSDrawConfig config, int instanceCount = 1);
	static void Reset(NativeGraphics* graphics);
	static void Clear(NativeGraphics* graphics);
	static void Execute(NativeGraphics* graphics);
	static void SetViewport(NativeGraphics* graphics, RectInt viewport);
	static bool IsTombstoned(NativeGraphics* graphics);
	NativeGraphics* GetNativeGraphics() const { return mGraphics; }
};
class __declspec(dllexport) CSWindow {
	NativeWindow* mWindow = nullptr;
public:
	CSWindow(NativeWindow* window)
		: mWindow(window) { }
	static void Dispose(NativeWindow* window);
	static Int2C GetResolution(const NativeWindow* window);
};

class __declspec(dllexport) CSRenderPass {
	NativeRenderPass* mRenderPass;
public:
	CSRenderPass(NativeRenderPass* renderPass)
		: mRenderPass(renderPass) { }
	static CSString8 GetName(NativeRenderPass* renderPass);
	static const Frustum& GetFrustum(NativeRenderPass* renderPass);
	static void SetViewProjection(NativeRenderPass* renderPass, const Matrix& view, const Matrix& projection);
	static const Matrix& GetView(NativeRenderPass* renderPass);
	static const Matrix& GetProjection(NativeRenderPass* renderPass);
	static void AddInstance(NativeRenderPass* renderPass, CSInstance instance, CSMesh mesh, CSSpan materials);
	static void RemoveInstance(NativeRenderPass* renderPass, CSInstance instance);
	static void SetVisible(NativeRenderPass* renderPass, CSInstance instance, bool visible);
	static NativeMaterial* GetOverrideMaterial(NativeRenderPass* renderPass);
	static void SetTargetTexture(NativeRenderPass* renderPass, NativeRenderTarget* target);
	static NativeRenderTarget* GetTargetTexture(NativeRenderPass* renderPass);
	static void Bind(NativeRenderPass* renderPass, NativeGraphics* graphics);
	static void AppendDraw(NativeRenderPass* renderPass, NativeGraphics* graphics, NativePipeline* pipeline, CSSpan bindings, CSSpan resources, Int2 instanceRange);
	static void Render(NativeRenderPass* renderPass, NativeGraphics* graphics);
	static NativeRenderPass* Create(NativeScene* scene, CSString name);
};
class __declspec(dllexport) CSScene {
	NativeScene* mScene;
public:
	CSScene(NativeScene* scene)
		: mScene(scene) { }
	static void Dispose(NativeScene* scene);
	static NativeMaterial* GetRootMaterial(NativeScene* scene);
	static int CreateInstance(NativeScene* scene);
	static void UpdateInstanceData(NativeScene* scene, CSInstance instance, const uint8_t* data, int dataLen);
	static CSSpan GetInstanceData(NativeScene* scene, CSInstance instance);
	static NativeTexture* GetGPUBuffer(NativeScene* scene);
	static int GetGPURevision(NativeScene* scene);
	static void SubmitToGPU(NativeScene* scene, NativeGraphics* graphics);
	static NativeRenderPass* GetBasePass(NativeScene* scene);
	static NativeRenderPass* GetShadowPass(NativeScene* scene);
	static void Render(NativeScene* scene, NativeGraphics* graphics);
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
class __declspec(dllexport) CSInput {
	NativePlatform* mPlatform = nullptr;
public:
	CSInput(NativePlatform* platform)
		: mPlatform(platform) { }
	CSSpanSPtr GetPointers(NativePlatform* platform);
	Bool GetKeyDown(NativePlatform* platform, char key);
	Bool GetKeyPressed(NativePlatform* platform, char key);
	Bool GetKeyReleased(NativePlatform* platform, char key);
};

class __declspec(dllexport) CSResources {
	void* MakeUnsafe;
public:
	static NativeShader* LoadShader(CSString path, CSString entryPoint);
	static NativeModel* LoadModel(CSString path);
	static NativeTexture* LoadTexture(CSString path);
	static NativeFont* LoadFont(CSString path);
};

class __declspec(dllexport) Platform {
	NativePlatform* mPlatform = nullptr;
public:
	Platform(NativePlatform* platform)
		: mPlatform(platform) { }

	static NativeWindow* GetWindow(const NativePlatform* platform);
	static NativeGraphics* CreateGraphics(const NativePlatform* platform);
	static NativeScene* CreateScene(const NativePlatform* platform);

	static int MessagePump(NativePlatform* platform);
	static void Present(NativePlatform* platform);
	static void Dispose(NativePlatform* platform);

	static NativePlatform* Create();
};
