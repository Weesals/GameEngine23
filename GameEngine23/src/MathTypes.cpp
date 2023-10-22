#include "MathTypes.h"

#include <cmath>
#include <algorithm>

#pragma optimize("gty", on)

Int2 Int2::Min(Int2 v1, Int2 v2) { return Int2(std::min(v1.x, v2.x), std::min(v1.y, v2.y)); }
Int2 Int2::Max(Int2 v1, Int2 v2) { return Int2(std::max(v1.x, v2.x), std::max(v1.y, v2.y)); }
Int2 Int2::Clamp(Int2 v, Int2 min, Int2 max) { return Int2(std::min(std::max(v.x, min.x), max.x), std::min(std::max(v.y, min.y), max.y)); }
int Int2::Dot(Int2 v1, Int2 v2) { return v1.x * v2.x + v1.y * v2.y; }
int Int2::CSum(Int2 v) { return v.x + v.y; }
int Int2::CMul(Int2 v) { return v.x * v.y; }

Int2 Int2::FloorToInt(Vector2 v) { return Int2((int)std::floorf(v.x), (int)std::floorf(v.y)); }
Int2 Int2::CeilToInt(Vector2 v) { return Int2((int)std::ceilf(v.x), (int)std::ceilf(v.y)); }

Int4 Int4::Min(Int4 v1, Int4 v2) { return Int4(std::min(v1.x, v2.x), std::min(v1.y, v2.y), std::min(v1.z, v2.z), std::min(v1.w, v2.w)); }
Int4 Int4::Max(Int4 v1, Int4 v2) { return Int4(std::max(v1.x, v2.x), std::max(v1.y, v2.y), std::max(v1.z, v2.z), std::max(v1.w, v2.w)); }
Int4 Int4::Clamp(Int4 v, Int4 min, Int4 max) { return Int4(std::min(std::max(v.x, min.x), max.x), std::min(std::max(v.y, min.y), max.y), std::min(std::max(v.z, min.z), max.z), std::min(std::max(v.w, min.w), max.w)); }

Frustum4::Frustum4(Matrix vp) {
	mPlaneXs = vp.m[0][3] + Vector4(vp.m[0][0], -vp.m[0][0], vp.m[0][1], -vp.m[0][1]);
	mPlaneYs = vp.m[1][3] + Vector4(vp.m[1][0], -vp.m[1][0], vp.m[1][1], -vp.m[1][1]);
	mPlaneZs = vp.m[2][3] + Vector4(vp.m[2][0], -vp.m[2][0], vp.m[2][1], -vp.m[2][1]);
	mPlaneDs = vp.m[3][3] + Vector4(vp.m[3][0], -vp.m[3][0], vp.m[3][1], -vp.m[3][1]);
}
void Frustum4::Normalize() {
	Vector4 factors = 1.0f / Vector4(
		Vector3(mPlaneXs[0], mPlaneYs[0], mPlaneZs[0]).Length(),
		Vector3(mPlaneXs[1], mPlaneYs[1], mPlaneZs[1]).Length(),
		Vector3(mPlaneXs[2], mPlaneYs[2], mPlaneZs[2]).Length(),
		Vector3(mPlaneXs[3], mPlaneYs[3], mPlaneZs[3]).Length()
	);
	mPlaneXs *= factors;
	mPlaneYs *= factors;
	mPlaneZs *= factors;
	mPlaneDs *= factors;
}
Vector3 Frustum4::Left() const { return Vector3(mPlaneXs.x, mPlaneYs.x, mPlaneZs.x); }
Vector3 Frustum4::Right() const { return Vector3(mPlaneXs.y, mPlaneYs.y, mPlaneZs.y); }
Vector3 Frustum4::Down() const { return Vector3(mPlaneXs.z, mPlaneYs.z, mPlaneZs.z); }
Vector3 Frustum4::Up() const { return Vector3(mPlaneXs.w, mPlaneYs.w, mPlaneZs.w); }
float Frustum4::GetVisibility(Vector3 pos)  const {
	Vector4 distances = GetProjectedDistances(pos);
	return cmin(distances);
}
float Frustum4::GetVisibility(Vector3 pos, Vector3 ext)  const {
	Vector4 distances = GetProjectedDistances(pos)
		+ dot4(Vector4::Abs(mPlaneXs), Vector4::Abs(mPlaneYs), Vector4::Abs(mPlaneZs), ext.x, ext.y, ext.z);
	return cmin(distances);
}
bool Frustum4::GetIsVisible(Vector3 pos)  const {
	return GetVisibility(pos) > 0;
}
bool Frustum4::GetIsVisible(Vector3 pos, Vector3 ext)  const {
	return GetVisibility(pos, ext) > 0;
}
void Frustum4::IntersectPlane(Vector3 dir, float c, Vector3 points[4]) const {
	auto crossXs = mPlaneYs.xzyw() * mPlaneZs.zywx() - mPlaneZs.xzyw() * mPlaneYs.zywx();
	auto crossYs = mPlaneZs.xzyw() * mPlaneXs.zywx() - mPlaneXs.xzyw() * mPlaneZs.zywx();
	auto crossZs = mPlaneXs.xzyw() * mPlaneYs.zywx() - mPlaneYs.xzyw() * mPlaneXs.zywx();

	auto up = Vector4(dir, c);
	auto crossUpXs = up.y * mPlaneZs.xzyw() - up.z * mPlaneYs.xzyw();
	auto crossUpYs = up.z * mPlaneXs.xzyw() - up.x * mPlaneZs.xzyw();
	auto crossUpZs = up.x * mPlaneYs.xzyw() - up.y * mPlaneXs.xzyw();

	auto dets = crossXs * up.x + crossYs * up.y + crossZs * up.z;

	auto posXs = (mPlaneDs.xzyw() * crossUpXs.yzwx() + up.w * crossXs - mPlaneDs.zywx() * crossUpXs.xyzw()) / dets;
	auto posYs = (mPlaneDs.xzyw() * crossUpYs.yzwx() + up.w * crossYs - mPlaneDs.zywx() * crossUpYs.xyzw()) / dets;
	auto posZs = (mPlaneDs.xzyw() * crossUpZs.yzwx() + up.w * crossZs - mPlaneDs.zywx() * crossUpZs.xyzw()) / dets;

	points[0] = Vector3(posXs.x, posYs.x, posZs.x);
	points[1] = Vector3(posXs.y, posYs.y, posZs.y);
	points[2] = Vector3(posXs.z, posYs.z, posZs.z);
	points[3] = Vector3(posXs.w, posYs.w, posZs.w);
}
Vector4 Frustum4::GetProjectedDistances(Vector3 pos)  const {
	return dot4(mPlaneXs, mPlaneYs, mPlaneZs, pos.x, pos.y, pos.z) + mPlaneDs;
}
Vector4 Frustum4::dot4(Vector4 xs, Vector4 ys, Vector4 zs, Vector4 mx, Vector4 my, Vector4 mz) {
	return xs * mx + ys * my + zs * mz;
}
float Frustum4::cmin(Vector2 v) { return std::min(v.x, v.y); }
float Frustum4::cmin(Vector4 v) { return std::min(std::min(v.x, v.y), std::min(v.z, v.w)); }

Frustum::Frustum(Matrix vp)
	: Frustum4(vp)
{
	mNearPlane = Vector4(vp.m[0][3] + vp.m[0][2], vp.m[1][3] + vp.m[1][2], vp.m[2][3] + vp.m[2][2], vp.m[3][3] + vp.m[3][2]);
	mFarPlane  = Vector4(vp.m[0][3] - vp.m[0][2], vp.m[1][3] - vp.m[1][2], vp.m[2][3] - vp.m[2][2], vp.m[3][3] - vp.m[3][2]);
}
void Frustum::Normalize() {
	Frustum4::Normalize();
	mNearPlane /= mNearPlane.xyz().Length();
	mFarPlane /= mFarPlane.xyz().Length();
}
Vector3 Frustum::Backward()  const { return mNearPlane.xyz(); }
Vector3 Frustum::Forward()  const { return mFarPlane.xyz(); }
Matrix Frustum::CalculateViewProj() const {
	Matrix r;
	r.m[0][3] = (mPlaneXs.x + mPlaneXs.y) / 2.0f;
	r.m[1][3] = (mPlaneYs.x + mPlaneYs.y) / 2.0f;
	r.m[2][3] = (mPlaneZs.x + mPlaneZs.y) / 2.0f;
	r.m[3][3] = (mPlaneDs.x + mPlaneDs.y) / 2.0f;
	r.m[0][0] = mPlaneXs.x - r.m[0][3];
	r.m[1][0] = mPlaneYs.x - r.m[1][3];
	r.m[2][0] = mPlaneZs.x - r.m[2][3];
	r.m[3][0] = mPlaneDs.x - r.m[3][3];
	r.m[0][1] = mPlaneXs.z - r.m[0][3];
	r.m[1][1] = mPlaneYs.z - r.m[1][3];
	r.m[2][1] = mPlaneZs.z - r.m[2][3];
	r.m[3][1] = mPlaneDs.z - r.m[3][3];
	r.m[0][2] = mNearPlane.x - r.m[0][3];
	r.m[1][2] = mNearPlane.y - r.m[1][3];
	r.m[2][2] = mNearPlane.z - r.m[2][3];
	r.m[3][2] = mNearPlane.w - r.m[3][3];
	return r;
}
float Frustum::GetVisibility(Vector3 pos) const {
	Vector4 distances = GetProjectedDistances(pos);
	Vector2 nfdistances = GetProjectedDistancesNearFar(pos);
	return std::min(cmin(distances), cmin(nfdistances));
}
float Frustum::GetVisibility(Vector3 pos, Vector3 ext)  const {
	Vector4 distances = GetProjectedDistances(pos)
		+ dot4(Vector4::Abs(mPlaneXs), Vector4::Abs(mPlaneYs), Vector4::Abs(mPlaneZs), ext.x, ext.y, ext.z);
	Vector2 nfdistances = GetProjectedDistancesNearFar(pos)
		+ Vector2(Vector3::Dot(Vector3::Abs(mNearPlane.xyz()), ext), Vector3::Dot(Vector3::Abs(mFarPlane.xyz()), ext));
	return std::min(cmin(distances), cmin(nfdistances));
}
bool Frustum::GetIsVisible(Vector3 pos)  const {
	return GetVisibility(pos) > 0;
}
bool Frustum::GetIsVisible(Vector3 pos, Vector3 ext)  const {
	return GetVisibility(pos, ext) > 0;
}
void Frustum::GetCorners(Vector3 corners[8]) const {
	auto vp = CalculateViewProj().Invert();
	int i = 0;
	for (float z = -1.0f; z < 1.5f; z += 2.0f) {
		for (float y = -1.0f; y < 1.5f; y += 2.0f) {
			for (float x = -1.0f; x < 1.5f; x += 2.0f) {
				auto point = Vector4::Transform(Vector4(x, y, z, 1.0f), vp);
				corners[i++] = point.xyz() / point.w;
			}
		}
	}
}
Frustum Frustum::TransformToLocal(const Matrix& tform) const {
	return Frustum(tform * CalculateViewProj());
}
Vector2 Frustum::GetProjectedDistancesNearFar(Vector3 pos) const {
	return Vector2(
		Vector3::Dot(mNearPlane.xyz(), pos) + mNearPlane.w,
		Vector3::Dot(mFarPlane.xyz(), pos) + mFarPlane.w
	);
}
#pragma optimize("gty", off)
