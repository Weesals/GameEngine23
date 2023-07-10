#pragma once

#include "../Math.h"

class Camera
{
	// Projection parameters
	float mFOV;
	float mAspect;

	// View parameters
	Vector3 mPosition;
	Quaternion mOrientation;

	// Cached matrices
	Matrix mProjMatrix;
	Matrix mViewMatrix;

	// Invalidate matrices when configuration is changed
	void InvalidateProj() { mProjMatrix.m[3][3] = 0.0f; }
	void InvalidateView() { mViewMatrix.m[3][3] = 0.0f; }

public:
	Camera();

	void SetFOV(float fov) { mFOV = fov; InvalidateProj(); }
	void SetAspect(float aspect) { mAspect = aspect; InvalidateProj(); }

	void SetPosition(Vector3 pos) { mPosition = pos; InvalidateView(); }
	void SetOrientation(Quaternion ori) { mOrientation = ori; InvalidateView(); }

	const Vector3& GetPosition() { return mPosition; }
	const Quaternion& GetOrientation() { return mOrientation; }

	const Matrix& GetProjectionMatrix();
	const Matrix& GetViewMatrix();

	Ray ViewportToRay(Vector2 vpos);

};

