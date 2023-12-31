#include "CanvasImGui.h"

#include <imgui.h>
#include <imgui_internal.h>

CanvasImGui::CanvasImGui()
{
	mMesh = std::make_shared<Mesh>("Canvas");

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
	io.Fonts->SetTexID((ImTextureID)&mFontTexture);
	mMaterial->SetUniformTexture("Texture", mFontTexture);
	// The Canvas is a CanvasRenderable, so it also needs a reference
	// to the canvas (itself)
	Initialise(this);
}
CanvasImGui::~CanvasImGui() {
	ImGui::DestroyContext();
}

void CanvasImGui::SetSize(Int2 size) {
	Canvas::SetSize(size);
	ImGuiIO& io = ImGui::GetIO();
	io.DisplaySize = ImVec2((float)size.x, (float)size.y);
}

// Check if the specified point is over any active windows
bool CanvasImGui::GetIsPointerOverUI(Vector2 v) const {
	auto context = ImGui::GetCurrentContext();
	for (auto window : context->Windows)
	{
		if (!window->Active) continue;
		auto rect = window->Rect();
		if (rect.Contains(ImVec2(v.x, v.y))) return true;
	}
	return Canvas::GetIsPointerOverUI(v);
}

void CanvasImGui::Update(const std::shared_ptr<Input>& input)
{
	ImGuiIO& io = ImGui::GetIO();
	const auto& pointers = input->GetPointers();
	if (pointers.size() > 0)
	{
		const auto& pointer = pointers[0];
		io.AddMousePosEvent(pointer->mPositionCurrent.x, pointer->mPositionCurrent.y);
		io.AddMouseButtonEvent(0, pointer->IsButtonDown(0));
	}
	Canvas::Update(input);
}

void CanvasImGui::Render(CommandBuffer& cmdBuffer) {
	ImGui::NewFrame();

	Canvas::Render(cmdBuffer);

	ImGui::Render();

	auto drawData = ImGui::GetDrawData();
	mMesh->SetVertexCount(drawData->TotalVtxCount);
	mMesh->SetIndexCount(drawData->TotalIdxCount);
	int vCount = 0, iCount = 0;
	auto inds = mMesh->GetIndicesV();
	mMesh->RequireVertexPositions(BufferFormat::FORMAT_R32G32_FLOAT);
	mMesh->RequireVertexTexCoords(0, BufferFormat::FORMAT_R16G16_UNORM);
	mMesh->RequireVertexColors(BufferFormat::FORMAT_R8G8B8A8_UNORM);
	auto positions = mMesh->GetPositionsV().Reinterpret<Vector2>();
	auto uvs = mMesh->GetTexCoordsV(0, true);
	auto colors = mMesh->GetColorsV(true);
	for (int n = 0; n < drawData->CmdListsCount; n++)
	{
		const auto& cmdList = drawData->CmdLists[n];
		// Copy vertices
		for (int i = 0; i < cmdList->VtxBuffer.Size; ++i)
		{
			auto& v = cmdList->VtxBuffer.Data[i];
			positions[vCount + i] = Vector2(v.pos.x, v.pos.y);
			uvs[vCount + i] = Vector2(v.uv.x, v.uv.y);
			auto c = ColorB4::FromABGR(v.col);
			colors[vCount + i] = c;
		}
		// Copy indices
		for (int i = 0; i < cmdList->IdxBuffer.Size; ++i)
		{
			inds[iCount + i] = vCount + cmdList->IdxBuffer.Data[i];
		}
		// Swap to D3D expected winding
		for (int i = iCount + 2; i < iCount + cmdList->IdxBuffer.Size; i += 3)
		{
			int t = inds[i - 1];
			inds[i - 1] = (int)inds[i];
			inds[i] = t;
			//auto l = inds[i - 1];
			//auto r = inds[i];
			//std::swap(l, r);
			//std::swap(inds[i - 1], inds[i]);
		}
		vCount += cmdList->VtxBuffer.Size;
		iCount += cmdList->IdxBuffer.Size;
	}
	mMesh->MarkChanged();
	vCount = 0;
	iCount = 0;
	mDrawCount = 0;
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
			auto texture = (std::shared_ptr<Texture>*)cmdBuf.GetTexID();
			if (texture != nullptr) mMaterial->SetUniformTexture("Texture", *texture);
			else mMaterial->SetUniformTexture("Texture", nullptr);
			cmdBuffer.DrawMesh(mMesh.get(), mMaterial.get(), drawConfig);
			++mDrawCount;
		}
		iCount += cmdList->IdxBuffer.Size;
	}
	mMaterial->SetUniformTexture("Texture", nullptr);
}
