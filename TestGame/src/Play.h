#pragma once

#include <memory>
#include <chrono>

#include "Platform.h"

#include "Camera.h"
#include "World.h"

#include <InputDispatcher.h>
#include <RetainedRenderer.h>
#include <Lighting.h>

#include "SelectionManager.h"
#include "SelectionRenderer.h"

#include "Canvas.h"
class UIPlay;

using steady_clock = std::chrono::steady_clock;
using time_point = std::chrono::time_point<steady_clock>;

class Skybox
{
public:
	std::shared_ptr<Mesh> mMesh;
	std::shared_ptr<Material> mMaterial;
	void Initialise(std::shared_ptr<Material>& rootMaterial);
};

class Play
{
public:
	typedef Delegate<CommandBuffer&> OnRenderDelegate;

private:
	Camera mCamera;
    float mTime;
    time_point mTimePoint;

	std::shared_ptr<GraphicsDeviceBase> mGraphics;
	std::shared_ptr<Input> mInput;

	std::shared_ptr<Canvas> mCanvas;
	std::shared_ptr<UIPlay> mPlayUI;

	std::shared_ptr<RetainedScene> mScene;
	std::shared_ptr<RenderPassList> mRenderPasses;

	std::shared_ptr<RenderPass> mBasePass;
	std::shared_ptr<RenderPass> mShadowPass;
	std::shared_ptr<World> mWorld;

	std::shared_ptr<Material> mRootMaterial;
	std::shared_ptr<Skybox> mSkybox;
	std::shared_ptr<DirectionalLight> mSunLight;

	std::shared_ptr<InputDispatcher> mInputDispatcher;

	std::shared_ptr<SelectionManager> mSelection;
	std::shared_ptr<SelectionRenderer> mSelectionRenderer;

	std::shared_ptr<Systems::ActionDispatchSystem> mActionDispatch;

	OnRenderDelegate mOnRender;
public:
	// Construct the game world and load assets
    void Initialise(Platform& platform);

	Camera& GetCamera() { return mCamera; }

	const std::shared_ptr<const GraphicsDeviceBase>& GetGraphics() const { return mGraphics; }
	const std::shared_ptr<const Input>& GetInput() const { return mInput; }
	const std::shared_ptr<SelectionManager>& GetSelection() const { return mSelection; }

	const std::shared_ptr<RenderPass>& GetShadowPass() const { return mShadowPass; }

	const std::shared_ptr<World>& GetWorld() { return mWorld; }
	const std::shared_ptr<Material>& GetRootMaterial() { return mRootMaterial; }
	const std::shared_ptr<GraphicsDeviceBase>& GetGraphics() { return mGraphics; }
	const std::shared_ptr<Input>& GetInput() { return mInput; }

	// Update the game world
	void Step();

	// Render the game world
    void Render(CommandBuffer& cmdBuffer);

	// Send an action request (move, attack, etc.) to the specified entity
	void SendActionRequest(const Actions::ActionRequest& request);
	void SendActionRequest(flecs::entity entity, const Actions::ActionRequest& request);

	// Begin placing a building (or other placeable)
	void BeginPlacement(int protoId);
	int GetPlacementProtoId() const;

	// Allow external systems to render objects
	OnRenderDelegate::Reference RegisterOnRender(const OnRenderDelegate::Function& fn);
};

