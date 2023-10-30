#include "CSBindings.h"

#include <NativePlatform.h>
#include <ResourceLoader.h>
#include <RetainedRenderer.h>

#include <algorithm>
#include <iterator>
#include <memory>
#include <iostream>

class NativeModel {
public:
	std::shared_ptr<Model> mModel;
	NativeModel(const std::shared_ptr<Model>& model)
		: mModel(model) { }
};
class NativeScene {
public:
	std::shared_ptr<RetainedScene> mScene;
	std::shared_ptr<RetainedRenderer> mRenderer;
	std::shared_ptr<RootMaterial> mDefaultMaterial;
	RenderQueue mDrawList;
	NativeScene() {
		mScene = std::make_shared<RetainedScene>();
		mRenderer = std::make_shared<RetainedRenderer>();
		mRenderer->SetScene(mScene);
		mDefaultMaterial = std::make_shared<RootMaterial>(L"../TestGame/assets/retained.hlsl");
	}
};
class NativeGraphics {
public:
	CommandBuffer mCmdBuffer;
	NativeGraphics(CommandBuffer&& cmdBuffer)
		: mCmdBuffer(std::move(cmdBuffer)) { }
};

std::string AllocString(CSString string) {
	std::wstring_view str(string.mBuffer, string.mSize);
	std::string outstr;
	outstr.reserve(string.mSize);
	std::transform(str.begin(), str.end(),
		std::back_inserter(outstr), [=](auto c) { return (char)c; });
	return outstr;
}
std::wstring_view ToWString(CSString string) {
	return std::wstring_view(string.mBuffer, string.mSize);
}

int CSMesh::GetVertexCount() const {
	auto mesh = (Mesh*)mMesh;
	return mesh->GetVertexCount();
}
int CSMesh::GetIndexCount() const {
	auto mesh = (Mesh*)mMesh;
	return mesh->GetIndexCount();
}
void CSMesh::GetMeshData(CSMeshData* data) const {
	auto mesh = (Mesh*)mMesh;
	data->mVertexCount = mesh->GetVertexCount();
	data->mIndexCount = mesh->GetIndexCount();
	auto& name = mesh->GetName();
	data->mName = CSString8{ .mBuffer = name.c_str(), .mSize = (int)name.size() };
	for (auto& element : mesh->GetVertexBuffer().GetElements()) {
		auto* target =
			element.mBindName == "POSITION" ? &data->mPositions :
			element.mBindName == "NORMAL" ? &data->mNormals :
			element.mBindName == "TEXCOORD" ? &data->mTexCoords :
			element.mBindName == "COLOR" ? &data->mColors :
			nullptr;
		if (target == nullptr) continue;
		auto type = BufferFormatType::GetType(element.mFormat);
		target->mFormat = CSBufferFormat{
			.mFormat = type.IsFloat() ? CSBufferFormat::Float :
				type.GetSize() == BufferFormatType::Size32 ? CSBufferFormat::Int :
				type.GetSize() == BufferFormatType::Size16 ? CSBufferFormat::Short :
				type.GetSize() == BufferFormatType::Size8 ? CSBufferFormat::Byte :
				(CSBufferFormat::Format)(-1),
			.mComponents = (uint8_t)type.GetComponentCount(),
		};
		target->mStride = element.mBufferStride;
		target->mData = element.mData;
	}
}
int CSModel::GetMeshCount() {
	return (int)mModel->mModel->GetMeshes().size();
}
CSMesh CSModel::GetMesh(int id) const {
	return CSMesh((NativeMesh*)mModel->mModel->GetMeshes()[id].get());
}

void CSGraphics::Clear() {
	mGraphics->mCmdBuffer.Reset();
	mGraphics->mCmdBuffer.SetRenderTarget(nullptr);
	mGraphics->mCmdBuffer.ClearRenderTarget(ClearConfig(Color(0, 0, 0, 0), 1.0f));
}
void CSGraphics::Execute() {
	mGraphics->mCmdBuffer.Execute();
}

CSInstance CSScene::CreateInstance(CSMesh mesh) {
	auto sceneId = mScene->mScene->AllocateInstance(10 * sizeof(Vector4));
	std::array<Vector4, 10> instanceData;
	*(Matrix*)instanceData.data() = Matrix::Identity;
	mScene->mScene->UpdateInstanceData(sceneId, instanceData);
	InplaceVector<const Material*, 10> materials;
	if (mScene->mDefaultMaterial != nullptr) materials.push_back(mScene->mDefaultMaterial.get());
	mScene->mRenderer->AppendInstance((Mesh*)mesh.GetNativeMesh(), materials, sceneId);
	return CSInstance(sceneId);
}
void CSScene::UpdateInstanceData(CSInstance instance, const uint8_t* data, int dataLen) {
	mScene->mScene->UpdateInstanceData(instance.GetInstanceId(),
		std::span<const uint8_t>(data, dataLen));
}
void CSScene::Render(CSGraphics* graphics) {
	auto res = graphics->GetGraphics()->mCmdBuffer.GetGraphics()->GetClientSize();
	mScene->mDefaultMaterial->SetResolution(res);
	auto& cmdBuffer = graphics->GetGraphics()->mCmdBuffer;
	auto& viewProj = *(const Matrix*)mScene->mDefaultMaterial->GetUniformBinaryData("ViewProjection").data();
	mScene->mScene->SubmitGPUMemory(cmdBuffer);
	mScene->mDrawList.Clear();
	mScene->mRenderer->SubmitToRenderQueue(cmdBuffer, mScene->mDrawList, Frustum(viewProj));
	mScene->mDrawList.Render(cmdBuffer);
}

Bool CSInput::GetKeyDown(char key) {
	return mPlatform->GetInput()->IsKeyDown(key);
}
Bool CSInput::GetKeyPressed(char key) {
	return mPlatform->GetInput()->IsKeyPressed(key);
}
Bool CSInput::GetKeyReleased(char key) {
	return mPlatform->GetInput()->IsKeyReleased(key);
}

CSModel CSResources::LoadModel(CSString name) {
	try {
		auto path = ToWString(name);
		auto mesh = ResourceLoader::GetSingleton().LoadModel(path);
		return CSModel(new NativeModel(mesh));
	}
	catch (...) {
		std::wcerr << "Failed to load mesh " << ToWString(name) << std::endl;
		return CSModel(nullptr);
	}
}

Platform Platform::Create() {
	auto* platform = new NativePlatform();
	platform->Initialize();
	return Platform(platform);
}
Platform::~Platform() {
	if (mPlatform != nullptr) {
		delete mPlatform;
		mPlatform = nullptr;
	}
}

CSInput Platform::GetInput() const {
	return CSInput(mPlatform);
}
CSGraphics Platform::GetGraphics() const {
	return CSGraphics(new NativeGraphics(mPlatform->GetGraphics()->CreateCommandBuffer()));
}
CSResources Platform::GetResources() const {
	return CSResources();
}
CSScene Platform::CreateScene() const {
	return CSScene(new NativeScene());
}

int Platform::MessagePump() {
	return mPlatform->MessagePump();
}
void Platform::Present() {
	mPlatform->Present();
}
