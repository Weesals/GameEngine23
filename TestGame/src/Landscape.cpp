#include "Landscape.h"

Landscape::ControlCell Landscape::ControlCell::Default = {};
Landscape::HeightCell Landscape::HeightCell::Default = {};
Landscape::WaterCell Landscape::WaterCell::Default = {};

Landscape::Landscape()
	: mSizing(0)
	, mRevision(0)
{
}
void Landscape::SetLocation(Vector3 location)
{
	mSizing.Location = location;
}
void Landscape::SetSize(Int2 size)
{
	mSizing.Size = size;
	int cellCount = mSizing.Size.x * mSizing.Size.y;
	mHeightMap.resize(cellCount);
	mControlMap.resize(cellCount);
	if (GetIsWaterEnabled()) mWaterMap.resize(cellCount);
	std::fill(mHeightMap.begin(), mHeightMap.end(), HeightCell::Default);
	std::fill(mControlMap.begin(), mControlMap.end(), ControlCell::Default);
	std::fill(mWaterMap.begin(), mWaterMap.end(), WaterCell::Default);
}
void Landscape::SetScale(int scale1024) { mSizing.Scale1024 = scale1024; }
void Landscape::SetWaterEnabled(bool enable)
{
	// Resize water to either match heightmap or be erased
	mWaterMap.resize(enable ? mHeightMap.size() : 0);
}
