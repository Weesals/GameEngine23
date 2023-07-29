#include "Canvas.h"

#include <imgui.h>
#include <imgui_internal.h>

// All renderables will receive a reference to the canvas
// when they are added to the canvas hierarchy
void CanvasRenderable::Initialise(Canvas* canvas)
{
	mCanvas = canvas;
}
void CanvasRenderable::AppendChild(const std::shared_ptr<CanvasRenderable>& child)
{
	child->Initialise(mCanvas);
	mChildren.push_back(child);
}
void CanvasRenderable::RemoveChild(const std::shared_ptr<CanvasRenderable>& child)
{
	auto i = std::find(mChildren.begin(), mChildren.end(), child);
	if (i != mChildren.end()) mChildren.erase(i);
}
void CanvasRenderable::Render(CommandBuffer& cmdBuffer)
{
	for (auto& item : mChildren)
	{
		item->Render(cmdBuffer);
	}
}

Canvas::Canvas()
{
	// ImGui buffers are pushed into this mesh for rendering
	mMesh = std::make_shared<Mesh>();
	mMaterial = std::make_shared<Material>(std::make_shared<Shader>(L"assets/ui.hlsl"), std::make_shared<Shader>(L"assets/ui.hlsl"));

	// Initialise the ImGui system
	IMGUI_CHECKVERSION();
	ImGui::CreateContext();
	ImGui::StyleColorsLight();

	// Create a texture for font data
	ImGuiIO& io = ImGui::GetIO();
	unsigned char* px = nullptr;
	int tex_w, tex_h;
	io.Fonts->GetTexDataAsRGBA32(&px, &tex_w, &tex_h);
	mFontTexture = std::make_shared<Texture>();
	mFontTexture->SetSize(Int2(tex_w, tex_h));
	mFontTexture->SetPixels32Bit(std::span<const uint32_t>((const uint32_t*)px, (const uint32_t*)px + tex_w * tex_h));
	io.Fonts->SetTexID((ImTextureID)"Font");
	mMaterial->SetUniform("Texture", mFontTexture);
	// The Canvas is a CanvasRenderable, so it also needs a reference
	// to the canvas (itself)
	Initialise(this);
}
Canvas::~Canvas()
{
	ImGui::DestroyContext();
}

void Canvas::SetSize(Int2 size)
{
	mSize = size;
	ImGuiIO& io = ImGui::GetIO();
	io.DisplaySize = ImVec2((float)size.x, (float)size.y);
	mMaterial->SetUniform("Projection", Matrix::CreateOrthographicOffCenter(0.0f, io.DisplaySize.x, io.DisplaySize.y, 0.0f, 0.0f, 500.0f));
}
Int2 Canvas::GetSize() const
{
	return mSize;
}

// Check if the specified point is over any active windows
bool Canvas::GetIsPointerOverUI(Vector2 v) const
{
	auto context = ImGui::GetCurrentContext();
	for (auto window : context->Windows)
	{
		if (!window->Active) continue;
		auto rect = window->Rect();
		if (rect.Contains(ImVec2(v.x, v.y))) return true;
	}
	return false;
}

void Canvas::AppendChild(const std::shared_ptr<CanvasRenderable>& child)
{
	CanvasRenderable::AppendChild(child);
}
void Canvas::RemoveChild(const std::shared_ptr<CanvasRenderable>& child)
{
	CanvasRenderable::RemoveChild(child);
}

void Canvas::Update(const std::shared_ptr<Input>& input)
{
	ImGuiIO& io = ImGui::GetIO();
	const auto& pointers = input->GetPointers();
	if (pointers.size() > 0)
	{
		const auto& pointer = pointers[0];
		io.AddMousePosEvent(pointer->mPositionCurrent.x, pointer->mPositionCurrent.y);
		io.AddMouseButtonEvent(0, pointer->IsButtonDown(0));
	}
}
void Canvas::Render(CommandBuffer& cmdBuffer)
{
	ImGui::NewFrame();

	CanvasRenderable::Render(cmdBuffer);

	ImGui::Render();

	auto drawData = ImGui::GetDrawData();
	mMesh->SetVertexCount(drawData->TotalVtxCount);
	mMesh->SetIndexCount(drawData->TotalIdxCount);
	int vCount = 0, iCount = 0;
	auto positions = mMesh->GetPositions();
	auto uvs = mMesh->GetUVs(true);
	auto colors = mMesh->GetColors(true);
	auto inds = mMesh->GetIndices();
	for (int n = 0; n < drawData->CmdListsCount; n++)
	{
		const auto& cmdList = drawData->CmdLists[n];
		// Copy vertices
		for (int i = 0; i < cmdList->VtxBuffer.Size; ++i)
		{
			auto& v = cmdList->VtxBuffer.Data[i];
			positions[vCount + i] = Vector3(v.pos.x, v.pos.y, 0.0f);
			uvs[vCount + i] = Vector2(v.uv.x, v.uv.y);
			colors[vCount + i] = Color(
				(float)(uint8_t)(v.col >> 16) / 255.0f,
				(float)(uint8_t)(v.col >> 8) / 255.0f,
				(float)(uint8_t)(v.col >> 0) / 255.0f,
				(float)(uint8_t)(v.col >> 24) / 255.0f
			);
		}
		// Copy indices
		for (int i = 0; i < cmdList->IdxBuffer.Size; ++i)
		{
			inds[iCount + i] = vCount + cmdList->IdxBuffer.Data[i];
		}
		// Swap to D3D expected winding
		for (int i = iCount + 2; i < iCount + cmdList->IdxBuffer.Size; i += 3)
		{
			std::swap(inds[i - 1], inds[i]);
		}
		vCount += cmdList->VtxBuffer.Size;
		iCount += cmdList->IdxBuffer.Size;
	}
	vCount = 0;
	iCount = 0;
	for (int n = 0; n < drawData->CmdListsCount; n++)
	{
		const auto& cmdList = drawData->CmdLists[n];
		for (int c = 0; c < cmdList->CmdBuffer.Size; c++)
		{
			const auto& cmdBuf = cmdList->CmdBuffer[c];
			DrawConfig drawConfig;
			drawConfig.mIndexBase = iCount + cmdBuf.IdxOffset;
			drawConfig.mIndexCount = cmdBuf.ElemCount;
			mMaterial->SetBlendMode(BlendMode::AlphaBlend());
			mMaterial->SetRasterMode(RasterMode::MakeDefault().SetCull(RasterMode::CullModes::None));
			mMaterial->SetDepthMode(DepthMode::MakeOff());
			cmdBuffer.DrawMesh(mMesh, mMaterial, drawConfig);
		}
		iCount += cmdList->IdxBuffer.Size;
	}
}

CanvasInterceptInteraction::CanvasInterceptInteraction(const std::shared_ptr<Canvas>& canvas)
	: mCanvas(canvas) { }
ActivationScore CanvasInterceptInteraction::GetActivation(Performance performance)
{
	if (mCanvas->GetIsPointerOverUI(performance.GetPositionCurrent())) return ActivationScore::MakeActive();
	return ActivationScore::MakeNone();
}
void CanvasInterceptInteraction::OnUpdate(Performance& performance)
{
	if (!performance.IsDown() && !mCanvas->GetIsPointerOverUI(performance.GetPositionCurrent()))
	{
		performance.SetInteraction(nullptr, true);
	}
}
