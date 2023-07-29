#include "Landscape.h"

#include <Geometry.h>

Landscape::ControlCell Landscape::ControlCell::Default = {};
Landscape::HeightCell Landscape::HeightCell::Default = {};
Landscape::WaterCell Landscape::WaterCell::Default = {};

Landscape::LandscapeChangeEvent::LandscapeChangeEvent() { }
Landscape::LandscapeChangeEvent::LandscapeChangeEvent(RectInt range, bool heightMap, bool controlMap, bool waterMap)
	: Range(range), HeightMapChanged(heightMap), ControlMapChanged(controlMap), WaterMapChanged(waterMap) { }
bool Landscape::LandscapeChangeEvent::GetHasChanges() { return HeightMapChanged || ControlMapChanged || WaterMapChanged; }

// Expand this to include the passed in range/flags
void Landscape::LandscapeChangeEvent::CombineWith(const LandscapeChangeEvent& other)
{
	// If this is the first change, just use it as is.
	if (!GetHasChanges()) { *this = other; return; }
	// Otherwise inflate our range and integrate changed flags
	auto min = Int2::Min(Range.GetMin(), other.Range.GetMin());
	auto max = Int2::Max(Range.GetMax(), other.Range.GetMax());
	Range = RectInt(min.x, min.y, max.x - min.x, max.y - min.y);
	HeightMapChanged |= other.HeightMapChanged;
	ControlMapChanged |= other.ControlMapChanged;
}
// Create a changed event that covers everything for the entire terrain
Landscape::LandscapeChangeEvent Landscape::LandscapeChangeEvent::All(Int2 size)
{
	return Landscape::LandscapeChangeEvent(RectInt(0, 0, size.x, size.y), true, true, true);
}
// Create a changed event that includes nothing
Landscape::LandscapeChangeEvent Landscape::LandscapeChangeEvent::None()
{
	return Landscape::LandscapeChangeEvent(RectInt(), false, false, false);
}
float Landscape::HeightMapReadOnly::GetHeightAtF(Vector2 pos) const
{
	Vector2 l;
	auto p00 = mSizing.WorldToLandscape(pos, l);
	p00 = Int2::Min(Int2::Max(p00, 0), mSizing.Size - 2);
	auto h00 = GetAt(p00);
	auto h10 = GetAt(p00 + Int2(1, 0));
	auto h01 = GetAt(p00 + Int2(0, 1));
	auto h11 = GetAt(p00 + Int2(1, 1));
	return (
		(float)h00.Height * (1.0f - l.x) * (1.0f - l.y)
		+ (float)h10.Height * (l.x) * (1.0f - l.y)
		+ (float)h01.Height * (1.0f - l.x) * (l.y)
		+ (float)h11.Height * (l.x) * (l.y)
		) / (float)HeightScale;
}



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


bool Landscape::Raycast(const Ray& ray, LandscapeHit& hit, float maxDst) const
{
	// Easier to work in "local" space (only translation, not scale)
	// TODO: Also apply scaling (including HeightScale) to work with native units
	auto localRay = Ray(ray.Origin - mSizing.Location, ray.Direction);
	auto from = localRay.Origin.xz();
	auto dir = localRay.Direction.xz();
	auto terScale = (float)mSizing.Scale1024 / 1024.0f;
	auto maxExtents = ((Vector2)mSizing.Size) * terScale;
	auto dirSign = Int2(dir.x < 0.0f ? -1 : 1, dir.y < 0.0f ? -1 : 1);
	auto dirEdge = Int2(dir.x < 0.0f ? 0 : 1, dir.y < 0.0f ? 0 : 1);
	// Move the ray forward until it is within the landscape range
	float dst = 0.0f;
	if (dir.x != 0.0f) dst = std::max(dst, (maxExtents.x * (1 - dirEdge.x) - from.x) / dir.x);
	if (dir.y != 0.0f) dst = std::max(dst, (maxExtents.y * (1 - dirEdge.y) - from.y) / dir.y);
	auto fromC = Int2::Clamp((Int2)((from + dir * dst) / terScale), 0, mSizing.Size - 2);
	while (dst < maxDst)
	{
		// Out of range, cancel iteration
		if ((uint32_t)fromC.x >= (uint32_t)mSizing.Size.x - 1) break;
		if ((uint32_t)fromC.y >= (uint32_t)mSizing.Size.y - 1) break;

		// TODO: Compare against the min/max extents of each terrain chunk
		// (once the hierarchical cache is generated)

		// Get the heights of the 4 corners
		auto terHeight00 = (float)mHeightMap[mSizing.ToIndex(fromC + Int2(0, 0))].GetHeightF();
		auto terHeight10 = (float)mHeightMap[mSizing.ToIndex(fromC + Int2(1, 0))].GetHeightF();
		auto terHeight01 = (float)mHeightMap[mSizing.ToIndex(fromC + Int2(0, 1))].GetHeightF();
		auto terHeight11 = (float)mHeightMap[mSizing.ToIndex(fromC + Int2(1, 1))].GetHeightF();
		// Raycast against the triangles that make up this cell
		Vector3 bc; float t;
		if (Geometry::RayTriangleIntersection(localRay,
			Vector3((Vector2)(fromC + Int2(0, 0)) * terScale, terHeight00).xzy(),
			Vector3((Vector2)(fromC + Int2(1, 1)) * terScale, terHeight11).xzy(),
			Vector3((Vector2)(fromC + Int2(1, 0)) * terScale, terHeight10).xzy(),
			bc, t
		) || Geometry::RayTriangleIntersection(localRay,
			Vector3((Vector2)(fromC + Int2(0, 0)) * terScale, terHeight00).xzy(),
			Vector3((Vector2)(fromC + Int2(0, 1)) * terScale, terHeight01).xzy(),
			Vector3((Vector2)(fromC + Int2(1, 1)) * terScale, terHeight11).xzy(),
			bc, t)
		)
		{
			// If a hit was found, return the data
			hit = LandscapeHit{
				.mHitPosition = ray.Origin + ray.Direction * t,
			};
			return true;
		}
		// If no hit was found, iterate to the next cell (in just 1 of the horizontal directions)
		float xNext = maxDst;
		float yNext = maxDst;
		auto nextEdgeDelta = (Vector2)(fromC + dirEdge) * terScale - from;
		if (dir.x != 0.0f) xNext = std::min(xNext, nextEdgeDelta.x / dir.x);
		if (dir.y != 0.0f) yNext = std::min(yNext, nextEdgeDelta.y / dir.y);
		fromC.x += xNext < yNext ? dirSign.x : 0;
		fromC.y += xNext < yNext ? 0 : dirSign.y;
		dst = std::min(xNext, yNext);
	}
	return false;
}

