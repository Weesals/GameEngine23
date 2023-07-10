#pragma once

// The DirectX Math library should be portable but otherwise,
// SimpleMath at least provides a consistent interface for
// math operations that can be replicated
#include "SimpleMath.h"

typedef DirectX::SimpleMath::Vector2 Vector2;
typedef DirectX::SimpleMath::Vector3 Vector3;
typedef DirectX::SimpleMath::Vector4 Vector4;
typedef DirectX::SimpleMath::Plane Plane;
typedef DirectX::SimpleMath::Matrix Matrix;
typedef DirectX::SimpleMath::Quaternion Quaternion;
typedef DirectX::SimpleMath::Color Color;
//typedef DirectX::SimpleMath::Ray Ray;

struct Ray {
	Vector3 Origin;
	Vector3 Direction;
	Vector3 ProjectTo(Plane& p) const
	{
		auto dirLen2 = Direction.LengthSquared();
		return Origin + Direction *
			(p.w - Direction.Dot(Origin)) / dirLen2;
	}
	// Get the distance between a point and the nearest point
	// along this ray
	float GetDistanceSqr(Vector3 point) const
	{
		auto dirLen2 = Direction.LengthSquared();
		auto proj = Origin + Direction *
			Direction.Dot(point - Origin) / dirLen2;
		return (point - proj).LengthSquared();
	}
};
