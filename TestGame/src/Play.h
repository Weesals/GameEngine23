#pragma once

#include <memory>
#include <chrono>

#include "Platform.h"

#include "Camera.h"
#include "World.h"

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
private:
	Camera mCamera;
    float mTime;
    time_point mTimePoint;

	std::shared_ptr<GraphicsDeviceBase> mGraphics;
	std::shared_ptr<Input> mInput;

	std::shared_ptr<World> mWorld;

	std::shared_ptr<Material> mRootMaterial;
	std::shared_ptr<Skybox> mSkybox;

public:
	// Construct the game world and load assets
    void Initialise(Platform& platform);

	std::shared_ptr<World>& GetWorld() { return mWorld; }
	std::shared_ptr<Material>& GetRootMaterial() { return mRootMaterial; }
	std::shared_ptr<GraphicsDeviceBase>& GetGraphics() { return mGraphics; }
	std::shared_ptr<Input>& GetInput() { return mInput; }

	// Update the game world
	void Step();

	// Render the game world
    void Render(CommandBuffer& cmdBuffer);
};

