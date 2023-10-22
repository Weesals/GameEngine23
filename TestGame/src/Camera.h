#pragma once

#include <MathTypes.h>

class Camera
{
	// Projection parameters
	float mFOV;
	float mAspect;
	float mNearPlane;
	float mFarPlane;

	// View parameters
	Vector3 mPosition;
	Quaternion mOrientation;

	Vector3 mMomentum;

	// Cached matrices
	Matrix mProjMatrix;
	Matrix mViewMatrix;

	// Invalidate matrices when configuration is changed
	void InvalidateProj() { mProjMatrix.m[3][3] = 0.0f; }
	void InvalidateView() { mViewMatrix.m[3][3] = 0.0f; }

public:
	Camera();

	// Getters/setters
	void SetFOV(float fov) { mFOV = fov; InvalidateProj(); }
	void SetAspect(float aspect) { mAspect = aspect; InvalidateProj(); }
	void SetNearPlane(float near) { mNearPlane = near; InvalidateProj(); }
	void SetFarPlane(float far) { mFarPlane = far; InvalidateProj(); }

	Vector3 GetRight() const { return Vector3::Transform(Vector3(1.0f, 0.0f, 0.0f), GetOrientation()); }
	Vector3 GetUp() const { return Vector3::Transform(Vector3(0.0f, 1.0f, 0.0f), GetOrientation()); }
	Vector3 GetForward() const { return Vector3::Transform(Vector3(0.0f, 0.0f, 1.0f), GetOrientation()); }

	void SetPosition(Vector3 pos) { mPosition = pos; InvalidateView(); }
	const Vector3& GetPosition() const { return mPosition; }

	void SetOrientation(Quaternion ori) { mOrientation = ori; InvalidateView(); }
	const Quaternion& GetOrientation() const { return mOrientation; }

	// Move along the horizontal plane, relative to camera orientation
	// Also smooths motion
	void MovePlanar(Vector2 delta, float dt);

	// Get (and calculate if needed) the camera matrices
	const Matrix& GetProjectionMatrix();
	const Matrix& GetViewMatrix();

	// Viewport space is [0, 1]
	Ray ViewportToRay(Vector2 vpos);

};

