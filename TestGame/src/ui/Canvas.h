#pragma once

#include "GraphicsDeviceBase.h"
#include <Input.h>
#include <Mesh.h>
#include <Texture.h>
#include <Delegate.h>

#include <InputDispatcher.h>

#include "CanvasMeshBuilder.h"
#include "CanvasTransform.h"

class Canvas;
class CanvasRenderable;

struct CanvasBinding {
	Canvas* mCanvas;
	CanvasRenderable* mParent;
	CanvasBinding();
	CanvasBinding(Canvas* canvas);
	CanvasBinding(CanvasRenderable* parent);
};

// An item that forms a part of the UI
class CanvasRenderable {
protected:
	CanvasBinding mBinding;
	std::vector<std::shared_ptr<CanvasRenderable>> mChildren;
	CanvasTransform mTransform;
	CanvasLayout mLayoutCache;
public:
	CanvasRenderable();
	virtual ~CanvasRenderable() { }
	Canvas* GetCanvas() { return mBinding.mCanvas; }
	CanvasRenderable* GetParent() { return mBinding.mParent; }
	virtual void Initialise(CanvasBinding binding);
	virtual void Uninitialise(CanvasBinding binding);
	virtual void AppendChild(const std::shared_ptr<CanvasRenderable>& child);
	virtual void RemoveChild(const std::shared_ptr<CanvasRenderable>& child);
	void SetTransform(const CanvasTransform& transform);
	virtual void UpdateLayout(const CanvasLayout& parent);
	virtual void Render(CommandBuffer& cmdBuffer);

	template<class T>
	const std::shared_ptr<T>& FindChild() {
		static std::shared_ptr<T> null;
		for (auto& child : mChildren) {
			auto typed = std::dynamic_pointer_cast<T>(child);
			if (typed != nullptr) return typed;
		}
		return null;
	}
};

// The root of the UI; coordinates rendering of all its children
class Canvas : public CanvasRenderable, std::enable_shared_from_this<Canvas>
{
public:
	typedef Delegate<const std::shared_ptr<Input>&> OnInput;

protected:
	std::shared_ptr<Mesh> mMesh;
	std::shared_ptr<Material> mMaterial;

	Int2 mSize;
	OnInput mOnInput;
	int mDrawCount;

	CanvasMeshBuilder mMeshBuilder;

public:

	Canvas();
	~Canvas();

	virtual void SetSize(Int2 size);
	Int2 GetSize() const;
	virtual bool GetIsPointerOverUI(Vector2 v) const;
	int GetDrawCount() const { return mDrawCount; }

	CanvasMeshBuilder& GetBuilder() { return mMeshBuilder; }

	OnInput::Reference RegisterInputIntercept(const OnInput::Function& callback);

	virtual void Update(const std::shared_ptr<Input>& input);
	virtual void Render(CommandBuffer& cmdBuffer) override;
};

// Intercepts input pointer events and prevents the user
// from interacting with the game world (via other interactions)
// when it is over a UI window
class CanvasInterceptInteraction : public InteractionBase
{
	std::shared_ptr<Canvas> mCanvas;
public:
	CanvasInterceptInteraction(const std::shared_ptr<Canvas>& canvas);
	ActivationScore GetActivation(Performance performance) override;
	void OnUpdate(Performance& performance) override;
};
