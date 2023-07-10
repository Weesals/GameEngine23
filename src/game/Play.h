#pragma once

#include <memory>
#include <chrono>

#include "Platform.h"

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
    Matrix mCameraMatrix;
    float mTime;
    time_point mTimePoint;

public:
	std::shared_ptr<WindowBase> mWindow;
	std::shared_ptr<GraphicsDeviceBase> mGraphics;
	std::shared_ptr<Input> mInput;

	std::shared_ptr<World> mWorld;

	std::shared_ptr<Material> mRootMaterial;
	std::shared_ptr<Skybox> mSkybox;

	// Construct the game world and load assets
    void Initialise(Platform& platform);

	// Update the game world
	void Step();

	// Render the game world
    void Render(CommandBuffer& cmdBuffer);
};

