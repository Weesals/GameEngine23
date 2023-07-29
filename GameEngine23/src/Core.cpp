#include "Delegate.h"
#include "Geometry.h"

bool Geometry::RayTriangleIntersection(const Ray& ray, const Vector3& v0, const Vector3& v1, const Vector3& v2, Vector3& bc, float& t)
{
	Vector3 edge1 = v1 - v0, edge2 = v2 - v0;
	auto h = Vector3::Cross(ray.Direction, edge2);
	auto a = Vector3::Dot(edge1, h);

	// Ray parallel to triangle or triangle is degenerate
	if (fabs(a) < std::numeric_limits<float>::epsilon()) return false;

	auto f = 1.0f / a;
	auto s = ray.Origin - v0;
	auto u = Vector3::Dot(s, h) * f;

	// Outside of range of edge1
	if (u < 0.0f || u > 1.0f) return false;

	auto q = Vector3::Cross(s, edge1);
	auto v = Vector3::Dot(ray.Direction, q) * f;

	// Out of range of other edges
	if (v < 0.0f || u + v > 1.0f) return false;

	// Compute barycentric coords, and ray distance
	bc = Vector3(1.0f - u - v, u, v);
	t = Vector3::Dot(edge2, q) * f;

	return t >= 0.0;
}
bool Geometry::RayBoxIntersection(const Ray& ray, const Vector3& pos, const Vector3& size, float& t)
{
	auto minFaces = pos - ray.Origin;
	auto maxFaces = minFaces;
	minFaces.x -= size.x * (ray.Direction.x < 0.0f ? -0.5f : 0.5f);
	minFaces.y -= size.y * (ray.Direction.y < 0.0f ? -0.5f : 0.5f);
	minFaces.z -= size.z * (ray.Direction.z < 0.0f ? -0.5f : 0.5f);
	maxFaces.x += size.x * (ray.Direction.x < 0.0f ? -0.5f : 0.5f);
	maxFaces.y += size.y * (ray.Direction.y < 0.0f ? -0.5f : 0.5f);
	maxFaces.z += size.z * (ray.Direction.z < 0.0f ? -0.5f : 0.5f);
	float entry = std::numeric_limits<float>::min();
	if (ray.Direction.x != 0.0f) entry = std::max(entry, minFaces.x / ray.Direction.x);
	if (ray.Direction.y != 0.0f) entry = std::max(entry, minFaces.y / ray.Direction.y);
	if (ray.Direction.z != 0.0f) entry = std::max(entry, minFaces.z / ray.Direction.z);
	float exit = std::numeric_limits<float>::max();
	if (ray.Direction.x != 0.0f) exit = std::min(exit, maxFaces.x / ray.Direction.x);
	if (ray.Direction.y != 0.0f) exit = std::min(exit, maxFaces.y / ray.Direction.y);
	if (ray.Direction.z != 0.0f) exit = std::min(exit, maxFaces.z / ray.Direction.z);
	t = entry;
	return entry <= exit;
}
