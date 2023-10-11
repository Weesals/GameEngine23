#pragma once

// The DirectX Math library should be portable but otherwise,
// SimpleMath at least provides a consistent interface for
// math operations that can be replicated
#include "../inc/SimpleMath.h"

typedef DirectX::SimpleMath::Plane Plane;
typedef DirectX::SimpleMath::Vector2 Vector2;
typedef DirectX::SimpleMath::Vector3 Vector3;
typedef DirectX::SimpleMath::Vector4 Vector4;
typedef DirectX::SimpleMath::Matrix Matrix;
typedef DirectX::SimpleMath::Quaternion Quaternion;
typedef DirectX::SimpleMath::Color Color;
typedef DirectX::SimpleMath::ColorB4 ColorB4;

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

	static Int2 Min(Int2 v1, Int2 v2);
	static Int2 Max(Int2 v1, Int2 v2);
	static Int2 Clamp(Int2 v, Int2 min, Int2 max);
	static int Dot(Int2 v1, Int2 v2);
	static int CSum(Int2 v);
	static int CMul(Int2 v);

	static Int2 FloorToInt(Vector2 v);
	static Int2 CeilToInt(Vector2 v);

	inline Int2 operator =(Vector2 v) { return Int2((int)v.x, (int)v.y); }
	inline operator Vector2() const { return Vector2((float)x, (float)y); }
};
struct Int4
{
	int x, y, z, w;
	Int4() : Int4(0, 0, 0, 0) { }
	Int4(int v) : Int4(v, v, v, v) { }
	Int4(int x, int y, int z, int w) : x(x), y(y), z(z), w(w) { }
	Int4(const Vector4 o) : x((int)o.x), y((int)o.y), z((int)o.z), w((int)o.w) { }
	inline Int4 operator +(const Int4 o) const { return Int4(x + o.x, y + o.y, z + o.z, w + o.w); }
	inline Int4 operator -(const Int4 o) const { return Int4(x - o.x, y - o.y, z - o.z, w - o.w); }
	inline Int4 operator *(const Int4 o) const { return Int4(x * o.x, y * o.y, z * o.z, w * o.w); }
	inline Int4 operator /(const Int4 o) const { return Int4(x / o.x, y / o.y, z / o.z, w / o.w); }
	inline Int4 operator +(const int o) const { return Int4(x + o, y + o, z + o, w + o); }
	inline Int4 operator -(const int o) const { return Int4(x - o, y - o, z - o, w - o); }
	inline Int4 operator *(const int o) const { return Int4(x * o, y * o, z * o, w * o); }
	inline Int4 operator /(const int o) const { return Int4(x / o, y / o, z / o, w / o); }

	static Int4 Min(Int4 v1, Int4 v2);
	static Int4 Max(Int4 v1, Int4 v2);
	static Int4 Clamp(Int4 v, Int4 min, Int4 max);

	inline Int4 operator =(Vector4 v) { return Int4((int)v.x, (int)v.y, (int)v.z, (int)v.w); }
	inline operator Vector4() const { return Vector4((float)x, (float)y, (float)z, (float)w); }
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
	void end(int end) { length = end - start; }
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
	void IntersectPlane(Vector3 dir, float c, Vector3 points[4]) const;
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
	Matrix CalculateViewProj() const;
	float GetVisibility(Vector3 pos) const;
	float GetVisibility(Vector3 pos, Vector3 ext) const;
	bool GetIsVisible(Vector3 pos) const;
	bool GetIsVisible(Vector3 pos, Vector3 ext) const;

	Frustum TransformToLocal(Matrix tform) const;
protected:
	Vector2 GetProjectedDistancesNearFar(Vector3 pos) const;
};
