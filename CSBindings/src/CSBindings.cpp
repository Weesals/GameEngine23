#define NOPREDECLARE

#include <Texture.h>
#include <NativePlatform.h>
#include <ResourceLoader.h>
#include <RetainedRenderer.h>
#include <Lighting.h>
#include <Containers.h>
#include <ui/font/FontRenderer.h>
#include <WindowBase.h>

#include <algorithm>
#include <iterator>
#include <memory>
#include <iostream>

#include "CSBindings.h"

template<class R, class... Args> R* create_shared(Args... args) {
	uint64_t data[4] = { };
	auto& ptr = (std::shared_ptr<R>&)data[0];
	ptr = std::make_shared<R>(args...);
	return ptr.get();
}
template<class R> void delete_shared(R* ptr) {
	std::shared_ptr<R> mat = ptr->GetSharedPtr();
	std::shared_ptr<R> del;
	memcpy(&del, &mat, sizeof(mat));
}

template<class Item>
CSSpan MakeSpan(std::span<Item> span) {
	return CSSpan(span.data(), (int)span.size());
}
template<class Item>
CSSpan MakeSpan(std::span<const Item> span) {
	return CSSpan(span.data(), (int)span.size());
}
template<class Item>
CSSpan MakeSpan(const std::vector<Item>& span) {
	return CSSpan(span.data(), (int)span.size());
}
template<class Item>
CSSpan MakeSpan(const std::vector<const Item>& span) {
	return CSSpan(span.data(), (int)span.size());
}
template<class Item>
CSSpanSPtr MakeSPtrSpan(std::span<std::shared_ptr<Item>> span) {
	return CSSpanSPtr(span.data(), (int)span.size());
}
template<class Item>
CSSpanSPtr MakeSPtrSpan(std::span<const std::shared_ptr<Item>> span) {
	return CSSpanSPtr(span.data(), (int)span.size());
}

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

CSString8 CSIdentifier::GetName(uint16_t id) {
	const auto& name = Identifier::GetName(Identifier(id));
	return CSString8(name.c_str(), (int)name.size());
}
CSString CSIdentifier::GetWName(uint16_t id) {
	const auto& name = Identifier::GetWName(Identifier(id));
	return CSString(name.c_str(), (int)name.size());
}
uint16_t CSIdentifier::GetIdentifier(CSString str) {
	return Identifier::RequireStringId(AllocString(str));
}
void CSTexture::SetSize(NativeTexture* tex, Int2 size) {
	tex->SetSize(size);
}
Int2C CSTexture::GetSize(NativeTexture* tex) {
	return ToC(tex->GetSize());
}
void CSTexture::SetFormat(NativeTexture* tex, BufferFormat fmt) { tex->SetBufferFormat(fmt); }
BufferFormat CSTexture::GetFormat(NativeTexture* tex) { return tex->GetBufferFormat(); }

void CSTexture::SetMipCount(NativeTexture* tex, int count) { tex->SetMipCount(count); }
int CSTexture::GetMipCount(NativeTexture* tex) { return tex->GetMipCount(); }
void CSTexture::SetArrayCount(NativeTexture* tex, int count) {
	tex->SetArrayCount(count);
}
int CSTexture::GetArrayCount(NativeTexture* tex) {
	return tex->GetArrayCount();
}
CSSpan CSTexture::GetTextureData(NativeTexture* tex, int mip, int slice) {
	auto data = tex->GetRawData(mip, slice);
	return MakeSpan(data);
}
void CSTexture::MarkChanged(NativeTexture* tex) {
	tex->MarkChanged();
}
NativeTexture* CSTexture::_Create(CSString name) {
	return new NativeTexture(ToWString(name));
}
void CSTexture::Dispose(NativeTexture* texture) {
	if (texture != nullptr) delete texture;
}

Int2C CSRenderTarget::GetSize(NativeRenderTarget* target) { return ToC(target->GetResolution()); }
void CSRenderTarget::SetSize(NativeRenderTarget* target, Int2 size) { target->SetResolution(size); }
BufferFormat CSRenderTarget::GetFormat(NativeRenderTarget* target) { return target->GetFormat(); }
void CSRenderTarget::SetFormat(NativeRenderTarget* target, BufferFormat format) { target->SetFormat(format); }
int CSRenderTarget::GetMipCount(NativeRenderTarget* target) { return target->GetMipCount(); }
void CSRenderTarget::SetMipCount(NativeRenderTarget* target, int count) { target->SetMipCount(count); }
int CSRenderTarget::GetArrayCount(NativeRenderTarget* target) { return target->GetArrayCount(); }
void CSRenderTarget::SetArrayCount(NativeRenderTarget* target, int count) { target->SetArrayCount(count); }
NativeRenderTarget* CSRenderTarget::_Create(CSString name) {
	return create_shared<NativeRenderTarget>(ToWString(name));
}
void CSRenderTarget::Dispose(NativeRenderTarget* target) {
	//delete_shared<Material>(target);
	//delete target;
}

NativeTexture* CSFont::GetTexture(const NativeFont* font) { return font->GetTexture().get(); }
int CSFont::GetLineHeight(const NativeFont* font) { return font->GetLineHeight(); }
int CSFont::GetKerning(const NativeFont* font, wchar_t c1, wchar_t c2) { return font->GetKerning(c1, c2); }
int CSFont::GetGlyphId(const NativeFont* font, wchar_t chr) { return font->GetGlyphId(chr); }
const CSGlyph& CSFont::GetGlyph(const NativeFont* font, int id) { return (CSGlyph&)font->GetGlyph(id); }

int CSMaterial::GetParameterIdentifiers(NativeMaterial* material, CSIdentifier* outlist, int capacity) {
	assert(sizeof(Identifier) == sizeof(CSIdentifier));
	return material->GetParametersRaw().GetItemIdentifiers((Identifier*)outlist, capacity);
}
void CSMaterial::SetRenderPass(NativeMaterial* material, CSIdentifier identifier) {
	material->SetRenderPassOverride(IdentifierWithName(Identifier(identifier.mId)));
}
CSSpan CSMaterial::GetValueData(NativeMaterial* material, CSIdentifier identifier) {
	auto data = material->GetUniformBinaryData(identifier.mId);
	return MakeSpan(data);
}
int CSMaterial::GetValueType(NativeMaterial* material, CSIdentifier identifier) {
	auto type = material->GetParametersRaw().GetValueType(identifier.mId);
	return type == &TypeCache::Require<float>() ? 0
		: type == &TypeCache::Require<int>() ? 1
		: 2;
}
void CSMaterial::SetValueFloat(NativeMaterial* material, CSIdentifier identifier, const float* data, int count) {
	material->SetUniform(identifier.mId, std::span<const float>(data, count));
}
void CSMaterial::SetValueInt(NativeMaterial* material, CSIdentifier identifier, const int* data, int count) {
	material->SetUniform(identifier.mId, std::span<const int>(data, count));
}
void CSMaterial::SetValueTexture(NativeMaterial* material, CSIdentifier identifier, CSTexture texture) {
	material->SetUniformTexture(identifier.mId, texture.mTexture);
}
void CSMaterial::SetBlendMode(NativeMaterial* material, void* data) {
	material->SetBlendMode(*(BlendMode*)data);
}
void CSMaterial::SetRasterMode(NativeMaterial* material, void* data) {
	material->SetRasterMode(*(RasterMode*)data);
}
void CSMaterial::SetDepthMode(NativeMaterial* material, void* data) {
	material->SetDepthMode(*(DepthMode*)data);
}
void CSMaterial::InheritProperties(NativeMaterial* material, NativeMaterial* other) {
	material->InheritProperties(other->GetSharedPtr());
}
void CSMaterial::RemoveInheritance(NativeMaterial* material, NativeMaterial* other) {
	material->RemoveInheritance(other->GetSharedPtr());
}
CSSpan CSMaterial::ResolveResources(NativeGraphics* graphics, NativePipeline* pipeline, CSSpan materials) {
	InplaceVector<const Material*, 8> pomaterials;
	for (int i = 0; i < materials.mSize; ++i) {
		pomaterials.push_back(((const Material**)materials.mData)[i]);
	}
	auto result = MaterialEvaluator::ResolveResources(graphics->mCmdBuffer, (const PipelineLayout*)pipeline, pomaterials);
	return MakeSpan(result);
}
NativeMaterial* CSMaterial::_Create(CSString shaderPath) {
	if (shaderPath.mBuffer == nullptr) {
		return create_shared<Material>();
	} else {
		return create_shared<Material>(ToWString(shaderPath));
	}
}
void CSMaterial::Dispose(NativeMaterial* material) {
	delete_shared<Material>(material);
}


int CSMesh::GetVertexCount(const NativeMesh* mesh) {
	return mesh->GetVertexCount();
}
int CSMesh::GetIndexCount(const NativeMesh* mesh) {
	return mesh->GetIndexCount();
}
void CSMesh::SetVertexCount(NativeMesh* mesh, int count) {
	mesh->SetVertexCount(count);
}
void CSMesh::SetIndexCount(NativeMesh* mesh, int count) {
	mesh->SetIndexCount(count);
}
const CSBufferLayout* CSMesh::GetVertexBuffer(NativeMesh* mesh) {
	return (CSBufferLayout*)&mesh->GetVertexBuffer();
}
const CSBufferLayout* CSMesh::GetIndexBuffer(NativeMesh* mesh) {
	return (CSBufferLayout*)&mesh->GetIndexBuffer();
}
void CSMesh::RequireVertexNormals(NativeMesh* mesh, uint8_t fmt) {
	mesh->RequireVertexNormals((BufferFormat)fmt);
}
void CSMesh::RequireVertexTexCoords(NativeMesh* mesh, uint8_t fmt) {
	mesh->RequireVertexTexCoords((BufferFormat)fmt);
}
void CSMesh::RequireVertexColors(NativeMesh* mesh, uint8_t fmt) {
	mesh->RequireVertexColors((BufferFormat)fmt);
}
void CSMesh::GetMeshData(const NativeMesh* mesh, CSMeshData* data) {
	static Identifier PositionName = "POSITION";
	static Identifier NormalName = "NORMAL";
	static Identifier TexCoordName = "TEXCOORD";
	static Identifier ColorName = "COLOR";
	static Identifier IndexName = "INDEX";
	data->mVertexCount = mesh->GetVertexCount();
	data->mIndexCount = mesh->GetIndexCount();
	auto& name = mesh->GetName();
	data->mName = CSString8(name.c_str(), (int)name.size());
	for (auto& element : mesh->GetVertexBuffer().GetElements()) {
		auto* target =
			element.mBindName == PositionName ? &data->mPositions :
			element.mBindName == NormalName ? &data->mNormals :
			element.mBindName == TexCoordName ? &data->mTexCoords :
			element.mBindName == ColorName ? &data->mColors :
			nullptr;
		if (target == nullptr) continue;
		*target = CSBufferElement{
			.mBindName = CSIdentifier(element.mBindName),
			.mBufferStride = element.mBufferStride,
			.mFormat = element.mFormat,
			.mData = element.mData,
		};
	}
	for (auto& element : mesh->GetIndexBuffer().GetElements()) {
		auto* target =
			element.mBindName == IndexName ? &data->mIndices :
			nullptr;
		if (target == nullptr) continue;
		*target = CSBufferElement{
			.mBindName = CSIdentifier(element.mBindName),
			.mBufferStride = element.mBufferStride,
			.mFormat = element.mFormat,
			.mData = element.mData,
		};
	}
}
NativeMaterial* CSMesh::GetMaterial(NativeMesh* mesh) {
	return mesh->GetMaterial(false).get();
}
const BoundingBox& CSMesh::GetBoundingBox(NativeMesh* mesh) {
	return mesh->GetBoundingBox();
}
NativeMesh* CSMesh::_Create(CSString name) {
	return new NativeMesh(AllocString(name));
}
int CSModel::GetMeshCount(const NativeModel* model) {
	return (int)model->GetMeshes().size();
}
CSSpanSPtr CSModel::GetMeshes(const NativeModel* model) {
	auto meshes = model->GetMeshes();
	int psize = sizeof(*meshes.data());
	return MakeSPtrSpan(meshes);
}
CSMesh CSModel::GetMesh(const NativeModel* model, int id) {
	auto meshes = model->GetMeshes();
	return CSMesh(meshes[id].get());
}

CSSpan CSConstantBuffer::GetValues(const CSConstantBufferData* cb) {
	auto* constantBuffer = ((ShaderBase::ConstantBuffer*)cb);
	return MakeSpan(constantBuffer->mValues);
}

int CSPipeline::GetHasStencilState(const NativePipeline* pipeline) {
	return pipeline->mMaterialState.mDepthMode.GetStencilEnable();
}
int CSPipeline::GetExpectedBindingCount(const NativePipeline* pipeline) {
	return (int)pipeline->mBindings.size();
}
int CSPipeline::GetExpectedConstantBufferCount(const NativePipeline* pipeline) {
	return (int)pipeline->mConstantBuffers.size();
}
int CSPipeline::GetExpectedResourceCount(const NativePipeline* pipeline) {
	return (int)pipeline->mResources.size();
}
CSSpan CSPipeline::GetConstantBuffers(const NativePipeline* pipeline) {
	return MakeSpan(pipeline->mConstantBuffers);
}
CSSpan CSPipeline::GetResources(const NativePipeline* pipeline) {
	return MakeSpan(pipeline->mResources);
}
CSSpan CSPipeline::GetBindings(const NativePipeline* pipeline) {
	return MakeSpan(pipeline->mBindings);
}

void CSGraphics::Dispose(NativeGraphics* graphics) {
	if (graphics != nullptr) {
		delete graphics;
		graphics = nullptr;
	}
}
NativeSurface* CSGraphics::GetPrimarySurface(const NativeGraphics* graphics) {
	return graphics->mCmdBuffer.GetGraphics()->GetPrimarySurface();
}
Int2C CSGraphics::GetResolution(const NativeGraphics* graphics) {
	auto* natgraphics = graphics->mCmdBuffer.GetGraphics();
	return ToC(natgraphics->GetResolution());
}
void CSGraphics::SetResolution(const NativeGraphics* graphics, Int2 res) {
	auto* natgraphics = graphics->mCmdBuffer.GetGraphics();
	natgraphics->SetResolution(res);
}
void CSGraphics::SetRenderTargets(NativeGraphics* graphics, CSSpan colorTargets, CSRenderTargetBinding depthTarget) {
	auto* bindings = (const CSRenderTargetBinding*)colorTargets.mData;
	InplaceVector<RenderTargetBinding, 16> nativeTargets;
	for (int i = 0; i < colorTargets.mSize; ++i) {
		auto& binding = bindings[i];
		nativeTargets.push_back(RenderTargetBinding(binding.mTarget, binding.mMip, binding.mSlice));
	}
	graphics->mCmdBuffer.SetRenderTargets(nativeTargets, RenderTargetBinding(depthTarget.mTarget, depthTarget.mMip, depthTarget.mSlice));
}
const NativePipeline* CSGraphics::RequirePipeline(NativeGraphics* graphics, CSSpan bindings,
	NativeShader* vertexShader, NativeShader* pixelShader, void* materialState,
	CSSpan macros, CSIdentifier renderPass
) {
	InplaceVector<BufferLayout, 10> bindingsData;
	InplaceVector<const BufferLayout*, 10> pobindings;
	for (int m = 0; m < bindings.mSize; ++m) {
		auto& csbuffer = ((CSBufferLayout*)bindings.mData)[m];
		BufferLayout buffer(
			csbuffer.identifier, csbuffer.size,
			(BufferLayout::Usage)csbuffer.mUsage, csbuffer.mCount);
		buffer.mElements = (BufferLayout::Element*)csbuffer.mElements;
		buffer.mElementCount = csbuffer.mElementCount;
		bindingsData.push_back(buffer);
		pobindings.push_back(&bindingsData.back());
	}
	auto pipeline = graphics->mCmdBuffer.RequirePipeline(
		*(Shader*)vertexShader, *(Shader*)pixelShader, *(MaterialState*)materialState,
		pobindings, std::span<const MacroValue>((const MacroValue*)macros.mData, macros.mSize), IdentifierWithName(Identifier(renderPass.mId))
	);
	return pipeline;
}
void* CSGraphics::RequireFrameData(NativeGraphics* graphics, int byteSize) {
	// TODO: Alignment?
	return graphics->mCmdBuffer.RequireFrameData<uint8_t>(byteSize).data();
}
CSSpan CSGraphics::ImmortalizeBufferLayout(NativeGraphics* graphics, CSSpan bindings) {
	InplaceVector<const BufferLayout*> bindingsPtr;
	for (int i = 0; i < bindings.mSize; ++i) bindingsPtr.push_back(&((const BufferLayout*)bindings.mData)[i]);
	auto ibindings = RenderQueue::ImmortalizeBufferLayout(graphics->mCmdBuffer, bindingsPtr);
	return MakeSpan(ibindings);
}
void* CSGraphics::RequireConstantBuffer(NativeGraphics* graphics, CSSpan span) {
	return graphics->mCmdBuffer.RequireConstantBuffer(std::span<uint8_t>((uint8_t*)span.mData, span.mSize));
}
void CSGraphics::CopyBufferData(NativeGraphics* graphics, const CSBufferLayout* buffer, CSSpan ranges) {
	graphics->mCmdBuffer.CopyBufferData(*(const BufferLayout*)buffer, std::span<const RangeInt>((const RangeInt*)ranges.mData, ranges.mSize));
}
void CSGraphics::Draw(NativeGraphics* graphics, CSPipeline pipeline, CSSpan bindings, CSSpan resources, CSDrawConfig config, int instanceCount) {
	InplaceVector<BufferLayout, 8> bindingsData;
	InplaceVector<const BufferLayout*, 8> pobindings;
	for (int m = 0; m < bindings.mSize; ++m) {
		auto& csbuffer = ((CSBufferLayout*)bindings.mData)[m];
		BufferLayout buffer(
			csbuffer.identifier, csbuffer.size,
			(BufferLayout::Usage)csbuffer.mUsage, csbuffer.mCount);
		buffer.mRevision = csbuffer.revision;
		buffer.mOffset = csbuffer.mOffset;
		buffer.mElements = (BufferLayout::Element*)csbuffer.mElements;
		buffer.mElementCount = csbuffer.mElementCount;
		bindingsData.push_back(buffer);
		pobindings.push_back(&bindingsData.back());
	}
	graphics->mCmdBuffer.DrawMesh(
		pobindings,
		(const PipelineLayout*)pipeline.GetNativePipeline(),
		std::span<const void*>((const void**)resources.mData, resources.mSize),
		*(DrawConfig*)&config,
		instanceCount
	);
}
void CSGraphics::Reset(NativeGraphics* graphics) {
	graphics->mCmdBuffer.Reset();
	graphics->mCmdBuffer.SetRenderTargets({ }, nullptr);
}
void CSGraphics::Clear(NativeGraphics* graphics) {
	graphics->mCmdBuffer.ClearRenderTarget(ClearConfig(Color(0, 0, 0, 0), 1.0f));
}
void CSGraphics::Execute(NativeGraphics* graphics) {
	graphics->mCmdBuffer.Execute();
}
void CSGraphics::SetViewport(NativeGraphics* graphics, RectInt viewport) {
	graphics->mCmdBuffer.SetViewport(viewport);
}
bool CSGraphics::IsTombstoned(NativeGraphics* graphics) {
	return false;
}
uint64_t CSGraphics::GetGlobalPSOHash(NativeGraphics* graphics) {
	return graphics->mCmdBuffer.GetGlobalPSOHash();
}

void CSGraphicsSurface::RegisterDenyPresent(NativeSurface* surface, int delta) {
	surface->RegisterDenyPresent(delta);
}

void CSWindow::Dispose(NativeWindow* window) {
	window->Close();
}
Int2C CSWindow::GetResolution(const NativeWindow* window) {
	return ToC(window->GetClientSize());
}

CSSpanSPtr CSInput::GetPointers(NativePlatform* platform) {
	auto pointers = platform->GetInput()->GetPointers();
	return MakeSPtrSpan(pointers);
}
Bool CSInput::GetKeyDown(NativePlatform* platform, unsigned char key) {
	return platform->GetInput()->IsKeyDown(key);
}
Bool CSInput::GetKeyPressed(NativePlatform* platform, unsigned char key) {
	return platform->GetInput()->IsKeyPressed(key);
}
Bool CSInput::GetKeyReleased(NativePlatform* platform, unsigned char key) {
	return platform->GetInput()->IsKeyReleased(key);
}
CSSpan CSInput::GetPressKeys(NativePlatform* platform) {
	return MakeSpan(platform->GetInput()->GetPressKeys());
}
CSSpan CSInput::GetDownKeys(NativePlatform* platform) {
	return MakeSpan(platform->GetInput()->GetDownKeys());
}
CSSpan CSInput::GetReleaseKeys(NativePlatform* platform) {
	return MakeSpan(platform->GetInput()->GetReleaseKeys());
}
CSSpan CSInput::GetCharBuffer(NativePlatform* platform) {
	const auto& buffer = platform->GetInput()->GetCharBuffer();
	return CSSpan(buffer.data(), (int)buffer.size());
}
void CSInput::ReceiveTickEvent(NativePlatform* platform) {
	platform->GetInput()->GetMutator().ReceiveTickEvent();
}

NativeShader* CSResources::LoadShader(CSString path, CSString entryPoint) {
	try {
		auto shader = create_shared<Shader>(ToWString(path), AllocString(entryPoint));
		return shader;
	}
	catch (...) {
		std::wcerr << "Failed to load mesh " << ToWString(path) << std::endl;
		return nullptr;
	}
}
NativeModel* CSResources::LoadModel(CSString path) {
	try {
		auto wpath = ToWString(path);
		auto model = ResourceLoader::GetSingleton().LoadModel(wpath);
		return (NativeModel*)model.get();
	}
	catch (...) {
		std::wcerr << "Failed to load mesh " << ToWString(path) << std::endl;
		return nullptr;
	}
}
NativeTexture* CSResources::LoadTexture(CSString path) {
	try {
		auto wpath = ToWString(path);
		auto texture = ResourceLoader::GetSingleton().LoadTexture(wpath);
		return (NativeTexture*)texture.get();
	}
	catch (...) {
		std::wcerr << "Failed to load texture " << ToWString(path) << std::endl;
		return nullptr;
	}
}
NativeFont* CSResources::LoadFont(CSString path) {
	try {
		auto wpath = ToWString(path);
		auto font = ResourceLoader::GetSingleton().LoadFont(wpath);
		return (NativeFont*)font.get();
	}
	catch (...) {
		std::wcerr << "Failed to load font " << ToWString(path) << std::endl;
		return nullptr;
	}
}

NativePlatform* Platform::Create() {
	auto* platform = new NativePlatform();
	platform->Initialize();
	return platform;
}
void Platform::Dispose(NativePlatform* platform) {
	if (platform != nullptr) {
		delete platform;
	}
}

NativeWindow* Platform::GetWindow(const NativePlatform* platform) {
	return platform->GetWindow().get();
}
NativeGraphics* Platform::CreateGraphics(const NativePlatform* platform) {
	return new NativeGraphics(platform->GetGraphics()->CreateCommandBuffer());
}

int Platform::MessagePump(NativePlatform* platform) {
	return platform->MessagePump();
}
void Platform::Present(NativePlatform* platform) {
	platform->GetGraphics()->Present();
}
