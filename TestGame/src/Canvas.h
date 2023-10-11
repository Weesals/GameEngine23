#pragma once

#include "GraphicsDeviceBase.h"
#include <Input.h>
#include <Mesh.h>
#include <Texture.h>
#include <Delegate.h>

#include <InputDispatcher.h>

class Canvas;

// An item that forms a part of the UI
class CanvasRenderable
{
protected:
	Canvas* mCanvas;
	std::vector<std::shared_ptr<CanvasRenderable>> mChildren;
public:
	virtual void Initialise(Canvas* canvas);
	virtual void AppendChild(const std::shared_ptr<CanvasRenderable>& child);
	virtual void RemoveChild(const std::shared_ptr<CanvasRenderable>& child);
	virtual void Render(CommandBuffer& cmdBuffer) = 0;

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

private:
	std::shared_ptr<Mesh> mMesh;
	std::shared_ptr<Material> mMaterial;
	std::shared_ptr<Texture> mFontTexture;

	Int2 mSize;
	OnInput mOnInput;
	int mDrawCount;

public:

	Canvas();
	~Canvas();

	void SetSize(Int2 size);
	Int2 GetSize() const;
	int GetDrawCount() { return mDrawCount; }

	OnInput::Reference RegisterInputIntercept(const OnInput::Function& callback);
	bool GetIsPointerOverUI(Vector2 v) const;

	void AppendChild(const std::shared_ptr<CanvasRenderable>& child) override;
	void RemoveChild(const std::shared_ptr<CanvasRenderable>& child) override;

	void Update(const std::shared_ptr<Input>& input);
	void Render(CommandBuffer& cmdBuffer) override;
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
