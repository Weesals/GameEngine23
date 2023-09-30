#include "MathTypes.h"

Frustum4::Frustum4(Matrix vp) {
	mPlaneXs = vp.m[0][3] + Vector4(vp.m[0][0], -vp.m[0][0], vp.m[0][1], -vp.m[0][1]);
	mPlaneYs = vp.m[1][3] + Vector4(vp.m[1][0], -vp.m[1][0], vp.m[1][1], -vp.m[1][1]);
	mPlaneZs = vp.m[2][3] + Vector4(vp.m[2][0], -vp.m[2][0], vp.m[2][1], -vp.m[2][1]);
	mPlaneDs = vp.m[3][3] + Vector4(vp.m[3][0], -vp.m[3][0], vp.m[3][1], -vp.m[3][1]);
	/*Vector4 lengths = Vector4(
		1.f / Vector3(mPlaneXs[0], mPlaneYs[0], mPlaneZs[0]).Length(),
		1.f / Vector3(mPlaneXs[1], mPlaneYs[1], mPlaneZs[1]).Length(),
		1.f / Vector3(mPlaneXs[2], mPlaneYs[2], mPlaneZs[2]).Length(),
		1.f / Vector3(mPlaneXs[3], mPlaneYs[3], mPlaneZs[3]).Length()
	);
	mPlaneXs *= lengths;
	mPlaneYs *= lengths;
	mPlaneZs *= lengths;
	mPlaneDs *= lengths;*/
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
	mNearPlane /= mNearPlane.xyz().Length();
	mFarPlane /= mFarPlane.xyz().Length();
}
Vector3 Frustum::Backward()  const { return mNearPlane.xyz(); }
Vector3 Frustum::Forward()  const { return mFarPlane.xyz(); }
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
Vector2 Frustum::GetProjectedDistancesNearFar(Vector3 pos)  const {
	return Vector2(
		Vector3::Dot(mNearPlane.xyz(), pos) + mNearPlane.w,
		Vector3::Dot(mFarPlane.xyz(), pos) + mFarPlane.w
	);
}