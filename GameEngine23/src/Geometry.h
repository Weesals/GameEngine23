#pragma once

#include "MathTypes.h"
#include <algorithm>

struct Geometry
{
	static bool RayTriangleIntersection(const Ray& ray, const Vector3& v0, const Vector3& v1, const Vector3& v2, Vector3& bc, float& t);
	static bool RayBoxIntersection(const Ray& ray, const Vector3& pos, const Vector3& size, float& t);
};
