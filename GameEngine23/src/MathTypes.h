#pragma once

// The DirectX Math library should be portable but otherwise,
// SimpleMath at least provides a consistent interface for
// math operations that can be replicated
#include "../inc/SimpleMath.h"

#include <algorithm>

typedef DirectX::SimpleMath::Plane Plane;
typedef DirectX::SimpleMath::Vector2 Vector2;
typedef DirectX::SimpleMath::Vector3 Vector3;
typedef DirectX::SimpleMath::Vector4 Vector4;
typedef DirectX::SimpleMath::Matrix Matrix;
typedef DirectX::SimpleMath::Quaternion Quaternion;
typedef DirectX::SimpleMath::Color Color;

struct Int2
{
	int x, y;
	Int2() : Int2(0, 0) { }
	Int2(int v) : Int2(v, v) { }
	Int2(int x, int y) : x(x), y(y) { }
	Int2(const Vector2 o) : x((int)o.x), y((int)o.y) { }
	inline Int2 operator +(const Int2 o) const { return Int2(x + o.x, y + o.y); }
	inline Int2 operator -(const Int2 o) const { return Int2(x - o.x, y - o.y); }
	inline Int2 operator *(const Int2 o) const { return Int2(x * o.x, y * o.y); }
	inline Int2 operator /(const Int2 o) const { return Int2(x / o.x, y / o.y); }
	inline Int2 operator +(const int o) const { return Int2(x + o, y + o); }
	inline Int2 operator -(const int o) const { return Int2(x - o, y - o); }
	inline Int2 operator *(const int o) const { return Int2(x * o, y * o); }
	inline Int2 operator /(const int o) const { return Int2(x / o, y / o); }

	template<auto Fn>
	static Int2 Apply(Int2 v1, Int2 v2) { return Int2(Fn(v1.x, v2.x), Fn(v1.y, v2.y)); }

	static Int2 Min(Int2 v1, Int2 v2) { return Int2(std::min(v1.x, v2.x), std::min(v1.y, v2.y)); }
	static Int2 Max(Int2 v1, Int2 v2) { return Int2(std::max(v1.x, v2.x), std::max(v1.y, v2.y)); }
	static Int2 Clamp(Int2 v, Int2 min, Int2 max) { return Int2(std::min(std::max(v.x, min.x), max.x), std::min(std::max(v.y, min.y), max.y)); }

	inline Int2 operator =(Vector2 v) { return Int2((int)v.x, (int)v.y); }
	inline operator Vector2() const { return Vector2((float)x, (float)y); }
};

struct RectInt
{
	int x, y, width, height;
	RectInt(int x = 0, int y = 0, int w = 0, int h = 0) : x(x), y(y), width(w), height(h) { }
	inline int GetWidth() const { return width; }
	inline int GetHeight() const { return height; }
	inline Int2 GetMin() const { return Int2(x, y); }
	inline Int2 GetMax() const { return Int2(x + width, y + height); }
	static RectInt FromMinMax(Int2 min, Int2 max) { return RectInt(min.x, min.y, max.x - min.x, max.y - min.y); }
};
struct RangeInt
{
	int start, length;
	int end() { return start + length; }
	RangeInt() : start(0), length(0) { }
	RangeInt(int start, int length) : start(start), length(length) { }
	static RangeInt FromBeginEnd(int begin, int end) { return RangeInt(begin, end - begin); }
};

struct BoundingBox {
	Vector3 mMin, mMax;
	Vector3 Centre() const { return (mMin + mMax) / 2.0f; }
	Vector3 Extents() const { return (mMax - mMin) / 2.0f; }
	BoundingBox() : BoundingBox(Vector3::Zero, Vector3::Zero) { }
	BoundingBox(Vector3 min, Vector3 max)
		: mMin(min), mMax(max) { }
	static BoundingBox FromMinMax(Vector3 min, Vector3 max) { BoundingBox(min, max); }
};

struct Ray
{
	Vector3 Origin;
	Vector3 Direction;
	Ray() { }
	Ray(Vector3 origin, Vector3 dir) : Origin(origin), Direction(dir) { }
	Vector3 ProjectTo(const Plane& p) const
	{
		return Origin + Direction *
			(p.w - Vector3::Dot(p.Normal(), Origin)) / Vector3::Dot(p.Normal(), Direction);
	}
	// Get the distance between a point and the nearest point
	// along this ray
	float GetDistanceSqr(Vector3 point) const
	{
		auto dirLen2 = Direction.LengthSquared();
		auto proj = Origin + Direction *
			(Vector3::Dot(Direction, point - Origin) / dirLen2);
		return (point - proj).LengthSquared();
	}
	Vector3 GetPoint(float d) const { return Origin + Direction * d; }
	Ray Normalize() const { return Ray(Origin, Direction.Normalize()); }
};

struct Frustum4
{
	Vector4 mPlaneXs, mPlaneYs, mPlaneZs, mPlaneDs;
	Vector3 Left() const;
	Vector3 Right() const;
	Vector3 Down() const;
	Vector3 Up() const;
	Frustum4(Matrix vp);
	float GetVisibility(Vector3 pos) const;
	float GetVisibility(Vector3 pos, Vector3 ext) const;
	bool GetIsVisible(Vector3 pos) const;
	bool GetIsVisible(Vector3 pos, Vector3 ext) const;
protected:
	Vector4 GetProjectedDistances(Vector3 pos) const;
	static Vector4 dot4(Vector4 xs, Vector4 ys, Vector4 zs, Vector4 mx, Vector4 my, Vector4 mz);
	static float cmin(Vector2 v);
	static float cmin(Vector4 v);
};
struct Frustum : public Frustum4
{
	Vector4 mNearPlane;
	Vector4 mFarPlane;
	Vector3 Backward() const;
	Vector3 Forward() const;
	Frustum(Matrix vp);
	float GetVisibility(Vector3 pos) const;
	float GetVisibility(Vector3 pos, Vector3 ext) const;
	bool GetIsVisible(Vector3 pos) const;
	bool GetIsVisible(Vector3 pos, Vector3 ext) const;
protected:
	Vector2 GetProjectedDistancesNearFar(Vector3 pos) const;
};
