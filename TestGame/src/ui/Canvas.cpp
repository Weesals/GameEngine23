#include "Canvas.h"

CanvasBinding::CanvasBinding()
	: mCanvas(nullptr), mParent(nullptr) { }
CanvasBinding::CanvasBinding(Canvas* canvas)
	: mCanvas(canvas), mParent(nullptr) { }
CanvasBinding::CanvasBinding(CanvasRenderable* parent)
	: mCanvas(parent->GetCanvas()), mParent(parent) { }

// All renderables will receive a reference to the canvas
// when they are added to the canvas hierarchy
CanvasRenderable::CanvasRenderable() {
	mTransform = CanvasTransform::MakeDefault();
	mLayoutCache.mRevision = -1;
}
void CanvasRenderable::Initialise(CanvasBinding binding) {
	mBinding = binding;
	if (mBinding.mCanvas != nullptr)
		for (auto& child : mChildren) child->Initialise(CanvasBinding(this));
}
void CanvasRenderable::Uninitialise(CanvasBinding binding) {
	if (mBinding.mCanvas != nullptr)
		for (auto& child : mChildren) child->Uninitialise(CanvasBinding(this));
	mBinding = { };
}
void CanvasRenderable::AppendChild(const std::shared_ptr<CanvasRenderable>& child) {
	if (mBinding.mCanvas != nullptr)
		child->Initialise(CanvasBinding(this));
	mChildren.push_back(child);
}
void CanvasRenderable::RemoveChild(const std::shared_ptr<CanvasRenderable>& child) {
	if (mBinding.mCanvas != nullptr)
		child->Uninitialise(CanvasBinding(this));
	auto i = std::find(mChildren.begin(), mChildren.end(), child);
	if (i != mChildren.end()) mChildren.erase(i);
}
void CanvasRenderable::SetTransform(const CanvasTransform& transform) {
	mTransform = transform;
}
void CanvasRenderable::UpdateLayout(const CanvasLayout& parent) {
	mTransform.Apply(parent, mLayoutCache);
	for (auto& item : mChildren) {
		item->UpdateLayout(mLayoutCache);
	}
}
void CanvasRenderable::Compose(CanvasCompositor::Context& composer) {
	for (auto& item : mChildren) {
		auto childContext = composer.InsertChild((int)(uintptr_t)item.get());
		item->Compose(childContext);
		childContext.ClearRemainder();
	}
}
void CanvasRenderable::Render(CommandBuffer& cmdBuffer) {
	for (auto& item : mChildren) {
		item->Render(cmdBuffer);
	}
}

Canvas::Canvas()
	: mCompositor(&mMeshBuilder)
{
	// ImGui buffers are pushed into this mesh for rendering
	mMesh = std::make_shared<Mesh>("Canvas");
	mMaterial = std::make_shared<Material>(L"assets/ui.hlsl");

	// The Canvas is a CanvasRenderable, so it also needs a reference
	// to the canvas (itself)
	Initialise(this);
}
Canvas::~Canvas()
{
	mChildren.clear();
}

void Canvas::SetSize(Int2 size)
{
	mSize = size;
	mMaterial->SetUniform("Projection", Matrix::CreateOrthographicOffCenter(0.0f, (float)mSize.x, (float)mSize.y, 0.0f, 0.0f, 500.0f));
}
Int2 Canvas::GetSize() const
{
	return mSize;
}
bool Canvas::GetIsPointerOverUI(Vector2 v) const { return false; }

Canvas::OnInput::Reference Canvas::RegisterInputIntercept(const Canvas::OnInput::Function& callback)
{
	return mOnInput.Add(callback);
}

void Canvas::Update(const std::shared_ptr<Input>& input)
{
	mOnInput.Invoke(input);
}
void Canvas::Render(CommandBuffer& cmdBuffer)
{
	CanvasLayout rootLayout;
	rootLayout.mAxisX = Vector4(1, 0, 0, 1) * (float)mSize.x;
	rootLayout.mAxisY = Vector4(0, 1, 0, 1) * (float)mSize.y;
	rootLayout.mAxisZ = Vector3(0, 0, 1);
	rootLayout.mPosition = Vector3::Zero;
	CanvasRenderable::UpdateLayout(rootLayout);
	CanvasRenderable::Render(cmdBuffer);

	auto builder = mCompositor.CreateBuilder();
	auto root = mCompositor.CreateRoot(&builder);
	Compose(root);
	root.ClearRemainder();
	mCompositor.Render(cmdBuffer, mMaterial.get());
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
