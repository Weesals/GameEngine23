#include "Camera.h"
#include <numbers>

// Sensible defaults
Camera::Camera()
	: mFOV((float)(std::numbers::pi / 4.0f))
	, mAspect(1.0f)
	, mOrientation(Quaternion::Identity)
{ }

// Regenerate matrix if is invalidated
const Matrix& Camera::GetProjectionMatrix()
{
	if (mProjMatrix.m[3][3] == 0)
	{
		mProjMatrix = Matrix::CreatePerspectiveFieldOfView(
			mFOV,
			mAspect,
			0.1f, 100.0f
		);
	}
	return mProjMatrix;
}

// Regenerate matrix if is invalidated
const Matrix& Camera::GetViewMatrix()
{
	if (mViewMatrix.m[3][3] == 0)
	{
		mViewMatrix = Matrix::CreateFromQuaternion(mOrientation)
			* Matrix::CreateTranslation(mPosition);
		mViewMatrix = mViewMatrix.Invert();
	}
	return mViewMatrix;
}

Ray Camera::ViewportToRay(Vector2 vpos)
{
	auto viewProj = (GetViewMatrix() * GetProjectionMatrix()).Invert();
	auto pos4 = Vector4(vpos.x * 2.0f - 1.0f, 1.0f - vpos.y * 2.0f, 0.0f, 1.0f);
	auto origin = Vector4::Transform(pos4, viewProj);
	pos4.z = 1.0f;
	auto dest = Vector4::Transform(pos4, viewProj);
	origin /= origin.w;
	dest /= dest.w;
	return Ray((Vector3)origin, (Vector3)(dest - origin));
}
