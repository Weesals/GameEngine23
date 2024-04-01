#define NOPREDECLARE

#include <Texture.h>
#include <NativePlatform.h>
#include <ResourceLoader.h>
#include <Lighting.h>
#include <Containers.h>
#include <ui/font/FontRenderer.h>
#include <WindowBase.h>

#include <algorithm>
#include <iterator>
#include <memory>
#include <iostream>

#include "CSBindings.h"

template<typename T>
void increment_shared(const std::shared_ptr<T>& ptr) {
	uint64_t data[4] = { };
	(std::shared_ptr<T>&)data[0] = ptr;
}
template<typename T>
void decrement_shared(const std::shared_ptr<T>& ptr) {
	std::shared_ptr<T> del;
	memcpy(&del, &ptr, sizeof(ptr));
}
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
void CSTexture::SetSize(NativeTexture* tex, Int3 size) {
	tex->SetSize3D(size);
}
Int3C CSTexture::GetSize(NativeTexture* tex) {
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
void CSTexture::Swap(NativeTexture* from, NativeTexture* to) {
	std::swap(*from, *to);
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

void CSFont::Dispose(NativeFont* font) { }		// Dont do anything, fonts are always cached by resources
NativeTexture* CSFont::GetTexture(const NativeFont* font) { return font->GetTexture().get(); }
int CSFont::GetLineHeight(const NativeFont* font) { return font->GetLineHeight(); }
int CSFont::GetKerning(const NativeFont* font, wchar_t c1, wchar_t c2) { return font->GetKerning(c1, c2); }
int CSFont::GetKerningCount(const NativeFont* font) { return font->GetKerningCount(); }
void CSFont::GetKernings(const NativeFont* font, CSSpan kernings) {
	short* items = (short*)kernings.mData;
	for (auto& kerning : font->GetKernings()) {
		items[0] = std::get<0>(kerning.first);
		items[1] = std::get<1>(kerning.first);
		items += 2;
	}
}
int CSFont::GetGlyphCount(const NativeFont* font) { return font->GetGlyphCount(); }
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
	return MakeSpan(constantBuffer->GetValues());
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

NativeCompiledShader* CSCompiledShader::_Create(CSIdentifier name, int byteSize, int cbcount, int rbcount) {
	static_assert(sizeof(ShaderBase::UniformValue) == 4 * 4);
	static_assert(sizeof(ShaderBase::ConstantBuffer) == 24);
	auto* shader = new NativeCompiledShader();
	shader->AllocateBuffer(byteSize);
	shader->SetName(Identifier(name.mId));
	shader->GetReflection().mConstantBuffers.resize(cbcount);
	shader->GetReflection().mResourceBindings.resize(rbcount);
	return shader;
}
void CSCompiledShader::InitializeValues(NativeCompiledShader* shader, int cb, int vcount) {
	shader->GetReflection().mConstantBuffers[cb].SetValuesCount(vcount);
}
CSSpan CSCompiledShader::GetValues(NativeCompiledShader* shader, int cb) {
	return MakeSpan(shader->GetReflection().mConstantBuffers[cb].GetValues());
}
CSSpan CSCompiledShader::GetConstantBuffers(const NativeCompiledShader* shader) {
	return MakeSpan(shader->GetReflection().mConstantBuffers);
}
CSSpan CSCompiledShader::GetResources(const NativeCompiledShader* shader) {
	return MakeSpan(shader->GetReflection().mResourceBindings);
}
CSSpan CSCompiledShader::GetBinaryData(const NativeCompiledShader* shader) {
	return MakeSpan(shader->GetBinary());
}
const CSCompiledShader::ShaderStats& CSCompiledShader::GetStatistics(const NativeCompiledShader* shader) {
	static_assert(sizeof(CSCompiledShader::ShaderStats) == sizeof(ShaderBase::ShaderReflection::Statistics));
	return (CSCompiledShader::ShaderStats&)shader->GetReflection().mStatistics;
}

void CSGraphics::Dispose(NativeGraphics* graphics) {
	if (graphics != nullptr) {
		delete graphics;
		graphics = nullptr;
	}
}
NativeSurface* CSGraphics::CreateSurface(NativeGraphics* graphics, NativeWindow* window) {
	auto surface = graphics->mCmdBuffer.CreateSurface(window);
	increment_shared(surface);
	return surface.get();
}
void CSGraphics::SetSurface(NativeGraphics* graphics, NativeSurface* surface) {
	graphics->mCmdBuffer.SetSurface(surface);
	graphics->mCmdBuffer.SetRenderTargets({ }, nullptr);
}
NativeSurface* CSGraphics::GetSurface(NativeGraphics* graphics) {
	return graphics->mCmdBuffer.GetSurface();
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
const NativeCompiledShader* CSGraphics::CompileShader(NativeGraphics* graphics, CSString path, CSString entry, CSIdentifier identifier, CSSpan macros) {
	return new NativeCompiledShader(graphics->mCmdBuffer.GetGraphics()->CompileShader(
		ToWString(path), AllocString(entry), Identifier(identifier.mId).GetName().c_str(),
		std::span<const MacroValue>((const MacroValue*)macros.mData, macros.mSize)));
}
const NativePipeline* CSGraphics::RequirePipeline(NativeGraphics* graphics, CSSpan bindings,
	NativeCompiledShader* vertexShader, NativeCompiledShader* pixelShader,
	void* materialState
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
		*vertexShader, *pixelShader, *(MaterialState*)materialState,
		pobindings
	);
	return pipeline;
}
void* CSGraphics::RequireFrameData(NativeGraphics* graphics, int byteSize) {
	// TODO: Alignment?
	return graphics->mCmdBuffer.RequireFrameData<uint8_t>(byteSize).data();
}
void* CSGraphics::RequireConstantBuffer(NativeGraphics* graphics, CSSpan span, size_t hash) {
	return graphics->mCmdBuffer.RequireConstantBuffer(std::span<uint8_t>((uint8_t*)span.mData, span.mSize), hash);
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

void CSGraphicsSurface::Dispose(NativeSurface* surface) { decrement_shared(surface->This()); }
NativeRenderTarget* CSGraphicsSurface::GetBackBuffer(const NativeSurface* surface) {
	return surface->GetBackBuffer().get();
}
Int2C CSGraphicsSurface::GetResolution(const NativeSurface* surface) {
	return ToC(surface->GetResolution());
}
void CSGraphicsSurface::SetResolution(NativeSurface* surface, Int2 res) {
	surface->SetResolution(res);
}
void CSGraphicsSurface::RegisterDenyPresent(NativeSurface* surface, int delta) {
	surface->RegisterDenyPresent(delta);
}
void CSGraphicsSurface::Present(NativeSurface* surface) {
	surface->Present();
}

void CSWindow::Dispose(NativeWindow* window) {
	window->Close();
}
int CSWindow::GetStatus(NativeWindow* window) {
	return (int)window->GetStatus();
}
Int2C CSWindow::GetSize(const NativeWindow* window) {
	return ToC(window->GetClientSize());
}
void CSWindow::SetSize(NativeWindow* window, Int2 size) {
	window->SetClientSize(size);
}
void CSWindow::SetInput(NativeWindow* window, NativeInput* input) {
	window->SetInput(input->This());
}

CSSpanSPtr CSInput::GetPointers(NativeInput* input) {
	auto pointers = input->GetPointers();
	return MakeSPtrSpan(pointers);
}
Bool CSInput::GetKeyDown(NativeInput* input, unsigned char key) {
	return input->IsKeyDown(key);
}
Bool CSInput::GetKeyPressed(NativeInput* input, unsigned char key) {
	return input->IsKeyPressed(key);
}
Bool CSInput::GetKeyReleased(NativeInput* input, unsigned char key) {
	return input->IsKeyReleased(key);
}
CSSpan CSInput::GetPressKeys(NativeInput* input) {
	return MakeSpan(input->GetPressKeys());
}
CSSpan CSInput::GetDownKeys(NativeInput* input) {
	return MakeSpan(input->GetDownKeys());
}
CSSpan CSInput::GetReleaseKeys(NativeInput* input) {
	return MakeSpan(input->GetReleaseKeys());
}
CSSpan CSInput::GetCharBuffer(NativeInput* input) {
	const auto& buffer = input->GetCharBuffer();
	return CSSpan(buffer.data(), (int)buffer.size());
}
void CSInput::ReceiveTickEvent(NativeInput* input) {
	input->GetMutator().ReceiveTickEvent();
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

NativeWindow* Platform::CreateWindow(NativePlatform* platform, CSString name) {
	auto window = platform->CreateWindow(ToWString(name));
	increment_shared(window);
	return window.get();
}
NativeInput* Platform::CreateInput(NativePlatform* platform) {
	auto input = create_shared<Input>();
	return input;
}
NativeGraphics* Platform::CreateGraphics(NativePlatform* platform) {
	return new NativeGraphics(platform->GetGraphics()->CreateCommandBuffer());
}

int Platform::MessagePump(NativePlatform* platform) {
	return platform->MessagePump();
}
/*
void NVTTCompressTextureBC1(InputData* img, void* outData) {
	NVTTCompress::CompressTextureBC1(img, outData);
}
void NVTTCompressTextureBC2(InputData* img, void* outData) {
	NVTTCompress::CompressTextureBC2(img, outData);
}
void NVTTCompressTextureBC3(InputData* img, void* outData) {
	NVTTCompress::CompressTextureBC3(img, outData);
}
void NVTTCompressTextureBC4(InputData* img, void* outData) {
	NVTTCompress::CompressTextureBC4(img, outData);
}
void NVTTCompressTextureBC5(InputData* img, void* outData) {
	NVTTCompress::CompressTextureBC5(img, outData);
}
*/