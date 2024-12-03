#define NOPREDECLARE

#include <Texture.h>
#include <NativePlatform.h>
#include <ResourceLoader.h>
#include <Lighting.h>
#include <Containers.h>
#include <ui/font/FontRenderer.h>
#include <WindowBase.h>
#include <D3DShader.h>
#undef CreateWindow

#include <algorithm>
#include <iterator>
#include <memory>
#include <iostream>

#include "CSBindings.h"

#include <WindowWin32.h>

class PreprocessedShader {
public:
	std::string mSource;
	std::vector<std::string> mIncludedFiles;
};

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
std::string_view GetString(CSString8 string) {
	return std::string_view(string.mBuffer, string.mSize);
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
uint16_t CSIdentifier::GetIdentifier(CSString8 str) {
	return Identifier::RequireStringId(GetString(str));
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
void CSTexture::SetAllowUnorderedAccess(NativeTexture* tex, Bool enable) {
	tex->SetAllowUnorderedAccess(enable);
}
Bool CSTexture::GetAllowUnorderedAccess(NativeTexture* tex) {
	return tex->GetAllowUnorderedAccess();
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
	delete_shared<NativeRenderTarget>(target);
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


CSSpan CSConstantBuffer::GetValues(const CSConstantBufferData* cb) {
	auto* constantBuffer = ((ShaderBase::ConstantBuffer*)cb);
	return MakeSpan(constantBuffer->GetValues());
}

CSIdentifier CSPipeline::GetName(const NativePipeline* pipeline) {
	return CSIdentifier(pipeline->mName.mId);
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

CSString8 CSPreprocessedShader::GetSource(const PreprocessedShader* shader) {
	return CSString8(shader->mSource.c_str(), (int)shader->mSource.size());
}
int CSPreprocessedShader::GetIncludeFileCount(const PreprocessedShader* shader) {
	return (int)shader->mIncludedFiles.size();
}
CSString8 CSPreprocessedShader::GetIncludeFile(const PreprocessedShader* shader, int id) {
	const std::string& include = shader->mIncludedFiles[id];
	return CSString8(include.c_str(), (int)include.size());
}
void CSPreprocessedShader::Dispose(PreprocessedShader* shader) {
	if (shader != nullptr) delete shader;
}

NativeCompiledShader* CSCompiledShader::_Create(CSIdentifier name, int byteSize, int cbcount, int rbcount, int ipcount) {
	static_assert(sizeof(ShaderBase::UniformValue) == 4 * 4);
	static_assert(sizeof(ShaderBase::ConstantBuffer) == 24);
	auto* shader = new NativeCompiledShader();
	shader->AllocateBuffer(byteSize);
	shader->SetName(Identifier(name.mId));
	shader->GetReflection().mConstantBuffers.resize(cbcount);
	shader->GetReflection().mResourceBindings.resize(rbcount);
	shader->GetReflection().mInputParameters.resize(ipcount);
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
CSSpan CSCompiledShader::GetInputParameters(const NativeCompiledShader* shader) {
	return MakeSpan(shader->GetReflection().mInputParameters);
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
uint16_t CSGraphics::GetDeviceName(const NativeGraphics* graphics) { return Identifier::RequireStringId(graphics->mCmdBuffer.GetGraphics()->GetDeviceName().c_str()); }
CSGraphicsCapabilities CSGraphics::GetCapabilities(const NativeGraphics* graphics) { return (CSGraphicsCapabilities&)graphics->mCmdBuffer.GetGraphics()->mCapabilities; }
CSRenderStatistics CSGraphics::GetRenderStatistics(const NativeGraphics* graphics) { return (CSRenderStatistics&)graphics->mCmdBuffer.GetGraphics()->mStatistics; }
NativeSurface* CSGraphics::CreateSurface(NativeGraphics* graphics, NativeWindow* window) {
	auto surface = graphics->mCmdBuffer.GetGraphics()->CreateSurface(window);
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
PreprocessedShader* CSGraphics::PreprocessShader(CSString path, CSSpan macros) {
	size_t data[sizeof(std::string) / sizeof(size_t) + 1];
	std::string& source = *new(data) std::string;
	std::vector<std::string> includedFiles;
	source = D3DShader::PreprocessFile(ToWString(path), std::span<const MacroValue>((const MacroValue*)macros.mData, macros.mSize), &includedFiles);
	auto shader = new PreprocessedShader();
	shader->mSource = std::move(source);
	shader->mIncludedFiles = std::move(includedFiles);
	return shader;
}
const NativeCompiledShader* CSGraphics::CompileShader(NativeGraphics* graphics, CSString8 source, CSString entry, CSIdentifier profile, CSString dbgFilename) {
	auto compiledShader = graphics->mCmdBuffer.GetGraphics()->CompileShader(
		GetString(source), AllocString(entry),
		Identifier(profile.mId).GetName().c_str(), ToWString(dbgFilename));
	if (compiledShader.GetBinary().empty()) return nullptr;
	return new NativeCompiledShader(compiledShader);
}
const NativePipeline* RequirePipelineFromStages(NativeGraphics* graphics, CSSpan bindings,
	const ShaderStages& stages, void* materialState
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
		stages, *(MaterialState*)materialState,
		pobindings
	);
	assert(pipeline != nullptr);
	return pipeline;
}
const NativePipeline* CSGraphics::RequirePipeline(NativeGraphics* graphics, CSSpan bindings,
	NativeCompiledShader* vertexShader, NativeCompiledShader* pixelShader,
	void* materialState
) {
	ShaderStages stages = { nullptr };
	stages.mVertexShader = vertexShader;
	stages.mPixelShader = pixelShader;
	return RequirePipelineFromStages(graphics, bindings, stages, materialState);
}
const NativePipeline* CSGraphics::RequireMeshPipeline(NativeGraphics* graphics, CSSpan bindings,
	NativeCompiledShader* meshShader, NativeCompiledShader* pixelShader, void* materialState
) {
	ShaderStages stages = { nullptr };
	stages.mMeshShader = meshShader;
	stages.mPixelShader = pixelShader;
	return RequirePipelineFromStages(graphics, bindings, stages, materialState);
}
const NativePipeline* CSGraphics::RequireComputePSO(NativeGraphics* graphics, NativeCompiledShader* computeShader) {
	return graphics->mCmdBuffer.RequireComputePSO(*computeShader);
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
void CSGraphics::CopyBufferData(NativeGraphics* graphics, const CSBufferLayout* source, const CSBufferLayout* dest, int sourceOffset, int destOffset, int length) {
	graphics->mCmdBuffer.CopyBufferData(*(const BufferLayout*)source, *(const BufferLayout*)dest, sourceOffset, destOffset, length);
}
void CSGraphics::CommitTexture(NativeGraphics* graphics, const NativeTexture* texture) {
	graphics->mCmdBuffer.CommitTexture(texture);
}
void CSGraphics::Draw(NativeGraphics* graphics, CSPipeline pipeline, CSSpan bindings, CSSpan resources, CSDrawConfig config, int instanceCount) {
	static_assert(sizeof(BufferLayout) == sizeof(CSBufferLayout));
	InplaceVector<const BufferLayout*, 8> pobindings;
	pobindings.resize(bindings.mSize);
	/*InplaceVector<BufferLayout, 8> bindingsData;
	bindingsData.resize(bindings.mSize);
	for (int m = 0; m < bindings.mSize; ++m) {
		auto& csbuffer = ((CSBufferLayout*)bindings.mData)[m];
		bindingsData[m] = BufferLayout(
			csbuffer.identifier, csbuffer.size,
			(BufferLayout::Usage)csbuffer.mUsage, csbuffer.mCount);
		auto& buffer = bindingsData[m];
		buffer.mRevision = csbuffer.revision;
		buffer.mOffset = csbuffer.mOffset;
		buffer.mElements = (BufferLayout::Element*)csbuffer.mElements;
		buffer.mElementCount = csbuffer.mElementCount;
		pobindings[m] = &buffer;
	}*/
	for (int m = 0; m < bindings.mSize; ++m) {
		pobindings[m] = (const BufferLayout*)bindings.mData + m;
	}
	graphics->mCmdBuffer.DrawMesh(
		pobindings,
		(const PipelineLayout*)pipeline.GetNativePipeline(),
		std::span<const void*>((const void**)resources.mData, resources.mSize),
		*(DrawConfig*)&config,
		instanceCount
	);
}
void CSGraphics::Dispatch(NativeGraphics* graphics, CSPipeline pipeline, CSSpan resources, Int3 groupCount) {
	graphics->mCmdBuffer.DispatchCompute(
		(const PipelineLayout*)pipeline.GetNativePipeline(),
		std::span<const void*>((const void**)resources.mData, resources.mSize),
		groupCount
	);
}
void CSGraphics::Reset(NativeGraphics* graphics) {
	graphics->mCmdBuffer.Reset();
}
void CSGraphics::Clear(NativeGraphics* graphics, CSClearConfig clear) {
	//(Color(0, 0, 0, 0), 1.0f)
	graphics->mCmdBuffer.ClearRenderTarget((const ClearConfig&)clear);
}
void CSGraphics::Wait(NativeGraphics* graphics) {
	graphics->mCmdBuffer.GetGraphics()->WaitForGPU();
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
uint64_t CSGraphics::CreateReadback(NativeGraphics* graphics, NativeRenderTarget* rt) {
	auto readback = graphics->mCmdBuffer.CreateReadback(rt);
	return readback.mHandle;
}
int CSGraphics::GetReadbackResult(NativeGraphics* graphics, uint64_t readback) {
	return graphics->mCmdBuffer.GetReadbackResult(Readback{ readback });
}
int CSGraphics::CopyAndDisposeReadback(NativeGraphics* graphics, uint64_t readback, CSSpan data) {
	Readback rb{ readback };
	return graphics->mCmdBuffer.CopyAndDisposeReadback(rb, std::span<uint8_t>((uint8_t*)data.mData, data.mSize));
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
void CSWindow::SetStyle(NativeWindow* window, CSString style) {
	if (auto winwnd = dynamic_cast<WindowWin32*>(window)) {
		auto styleStr = ToWString(style);
		auto CompareStr = [&](const std::wstring_view s1, const std::wstring_view s2) {
			return std::equal(s1.begin(), s1.end(), s2.begin(), s2.end(), [](auto c1, auto c2) {
				return std::tolower(c1) == std::tolower(c2);
			});
		};
		if (CompareStr(styleStr, L"borderless")) {
			SetWindowLong(winwnd->GetHWND(), GWL_STYLE, WS_OVERLAPPED);
		}
	}
}
void CSWindow::SetVisible(NativeWindow* window, bool visible) {
	window->SetVisible(visible);
}
void CSWindow::SetInput(NativeWindow* window, NativeInput* input) {
	window->SetInput(input->This());
}
CSWindowFrame CSWindow::GetWindowFrame(const NativeWindow* window) {
	auto hwnd = ((WindowWin32*)window)->GetHWND();
	RECT windowRect;
	//GetWindowRect(hwnd, &windowRect);
	WINDOWPLACEMENT placement = { sizeof(WINDOWPLACEMENT), };
	GetWindowPlacement(hwnd, &placement);
	// TODO: Might need to convert rect space
	windowRect = placement.rcNormalPosition;
	POINT clientPoint = { 0, 0 };
	ClientToScreen(hwnd, &clientPoint);
	return {
		RectInt::FromMinMax(Int2(windowRect.left, windowRect.top), Int2(windowRect.right, windowRect.bottom)),
		Int2(clientPoint.x - windowRect.left, clientPoint.y - windowRect.top),
		placement.showCmd == SW_MAXIMIZE
	};
}
void CSWindow::SetWindowFrame(const NativeWindow* window, const RectInt* frame, bool maximized) {
	auto hwnd = ((WindowWin32*)window)->GetHWND();
	WINDOWPLACEMENT placement = { sizeof(WINDOWPLACEMENT), };
	GetWindowPlacement(hwnd, &placement);
	Int2 tl = frame->GetMin(), br = frame->GetMax();
	auto& wndRect = placement.rcNormalPosition;
	wndRect = { tl.x, tl.y, br.x, br.y };
	auto hMonitor = MonitorFromRect(&wndRect, MONITOR_DEFAULTTONEAREST);
	if (hMonitor) {
		MONITORINFO monitorInfo = { sizeof(MONITORINFO) };
		if (GetMonitorInfo(hMonitor, &monitorInfo)) {
			auto& workRect = monitorInfo.rcWork;
			auto width = std::min(wndRect.right - wndRect.left, workRect.right - workRect.left);
			auto height = std::min(wndRect.bottom - wndRect.top, workRect.bottom - workRect.top);
			wndRect.left = std::clamp(wndRect.left, workRect.left, workRect.right - width);
			wndRect.top = std::clamp(wndRect.top, workRect.top, workRect.bottom - height);
			wndRect.right = wndRect.left + width;
			wndRect.bottom = wndRect.top + height;
		}
	}
	placement.showCmd = maximized ? SW_MAXIMIZE : SW_RESTORE;
	SetWindowPlacement(hwnd, &placement);
}
void CSWindow::RegisterMovedCallback(const NativeWindow* window, void (*Callback)(), bool enable) {
	auto win32 = ((WindowWin32*)window);
	win32->RegisterMovedCallback(Callback, enable);
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
	return platform;
}
void Platform::Dispose(NativePlatform* platform) {
	if (platform != nullptr) {
		delete platform;
	}
}

void Platform::InitializeGraphics(NativePlatform* platform) {
	platform->Initialize();
}
int Platform::GetCoreCount() {
	std::vector<uint8_t> bufferPtr;
	DWORD bufferBytes = 0;
	if (GetLogicalProcessorInformationEx(RelationAll, nullptr, &bufferBytes)) return 0;
	if (GetLastError() != ERROR_INSUFFICIENT_BUFFER) return 0;
	bufferPtr.resize(bufferBytes);
	auto info = (SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX*)bufferPtr.data();
	if (!GetLogicalProcessorInformationEx(RelationAll, info, &bufferBytes)) return 0;
	int count = 0;
	for (; (void*)info < &bufferPtr.back(); *(uint8_t**)&info += info->Size) {
		if (info->Relationship == RelationProcessorCore) {
			++count;
		}
	}
	return count;
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