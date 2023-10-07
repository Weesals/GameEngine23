#include "Camera.h"
#include <numbers>

// Sensible defaults
Camera::Camera()
	: mFOV((float)(std::numbers::pi / 4.0f))
	, mAspect(1.0f)
	, mOrientation(Quaternion(0.0f, 1.0f, 0.0f, 0.0f))
{ }

// Move along the horizontal plane, relative to camera orientation
void Camera::MovePlanar(Vector2 delta, float dt)
{
	auto fwd = Vector3::Transform(Vector3::Forward, GetOrientation()).xz().Normalize();
	auto rgt = Vector2(-fwd.y, fwd.x);
	auto desiredVel = Vector3(rgt * delta.x + fwd * delta.y, 0.0f).xzy();
	mMomentum = Vector3::MoveTowards(mMomentum, desiredVel, dt * 10.0f);
	SetPosition(GetPosition() + mMomentum * (20.0f * dt));
}

// Regenerate matrix if is invalidated
const Matrix& Camera::GetProjectionMatrix()
{
	if (mProjMatrix.m[3][3] == 0)
	{
		mProjMatrix = Matrix::CreatePerspectiveFieldOfView(
			mFOV,
			mAspect,
			0.5f, 300.0f
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
	return Ray(origin.xyz(), (dest - origin).xyz());
}
