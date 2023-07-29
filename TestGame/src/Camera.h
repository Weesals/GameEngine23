#pragma once

#include <MathTypes.h>

class Camera
{
	// Projection parameters
	float mFOV;
	float mAspect;

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

	void SetPosition(Vector3 pos) { mPosition = pos; InvalidateView(); }
	const Vector3& GetPosition() { return mPosition; }

	void SetOrientation(Quaternion ori) { mOrientation = ori; InvalidateView(); }
	const Quaternion& GetOrientation() { return mOrientation; }

	// Move along the horizontal plane, relative to camera orientation
	// Also smooths motion
	void MovePlanar(Vector2 delta, float dt);

	// Get (and calculate if needed) the camera matrices
	const Matrix& GetProjectionMatrix();
	const Matrix& GetViewMatrix();

	// Viewport space is [0, 1]
	Ray ViewportToRay(Vector2 vpos);

};

