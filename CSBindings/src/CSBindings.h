#pragma once

#include <stdint.h>

class NativePlatform;
class NativeScene;
class NativeModel;
class NativeMesh;
class NativeGraphics;

struct Bool {
	uint8_t mValue;
	Bool(bool value) : mValue((uint8_t)(value ? 1 : 0)) { }
	operator bool() const { return mValue; }
};

struct __declspec(dllexport) CSString {
	const wchar_t* mBuffer;
	int mSize;
};
struct __declspec(dllexport) CSString8 {
	const char* mBuffer;
	int mSize;
};

struct CSBufferFormat {
	enum Format : uint8_t { Float, Int, Short, Byte, };
	Format mFormat;
	uint8_t mComponents;
};
struct CSBuffer {
	const void* mData;
	int mStride;
	CSBufferFormat mFormat;
};
struct CSMeshData {
	int mVertexCount;
	int mIndexCount;
	CSString8 mName;
	CSBuffer mPositions;
	CSBuffer mNormals;
	CSBuffer mTexCoords;
	CSBuffer mColors;
	CSBuffer mIndices;
};
class __declspec(dllexport) CSMesh {
	NativeMesh* mMesh;
public:
	CSMesh(NativeMesh* mesh)
		: mMesh(mesh) { }
	int GetVertexCount() const;
	int GetIndexCount() const;
	void GetMeshData(CSMeshData* outdata) const;
	NativeMesh* GetNativeMesh() const { return mMesh; }
};
class __declspec(dllexport) CSModel {
	NativeModel* mModel;
public:
	CSModel(NativeModel* mesh)
		: mModel(mesh) { }
	int GetMeshCount();
	CSMesh GetMesh(int id) const;
	NativeModel* GetNativeModel() const { return mModel; }
};

class __declspec(dllexport) CSInstance {
	int mInstanceId;
public:
	CSInstance(int instanceId)
		: mInstanceId(instanceId) { }
	int GetInstanceId() { return mInstanceId; }
};

class __declspec(dllexport) CSGraphics {
	NativeGraphics* mGraphics = nullptr;
public:
	CSGraphics(NativeGraphics* graphics)
		: mGraphics(graphics) { }
	NativeGraphics* GetGraphics() const { return mGraphics; }
	void Clear();
	void Execute();
};

class __declspec(dllexport) CSScene {
	NativeScene* mScene;
public:
	CSScene(NativeScene* scene)
		: mScene(scene) { }
	CSInstance CreateInstance(CSMesh mesh);
	void UpdateInstanceData(CSInstance instance, const uint8_t* data, int dataLen);
	void Render(CSGraphics* graphics);
};

class __declspec(dllexport) CSInput {
	NativePlatform* mPlatform = nullptr;
public:
	CSInput(NativePlatform* platform)
		: mPlatform(platform) { }
	Bool GetKeyDown(char key);
	Bool GetKeyPressed(char key);
	Bool GetKeyReleased(char key);
};

class __declspec(dllexport) CSResources {
	void* MakeUnsafe;
public:
	CSModel LoadModel(CSString name);
};

class __declspec(dllexport) Platform {
	NativePlatform* mPlatform = nullptr;
public:
	Platform(NativePlatform* platform)
		: mPlatform(platform) { }
	virtual ~Platform();

	CSInput GetInput() const;
	CSGraphics GetGraphics() const;
	CSResources GetResources() const;
	CSScene CreateScene() const;

	int MessagePump();
	void Present();

	static Platform Create();
};
