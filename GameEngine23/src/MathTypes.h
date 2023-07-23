#pragma once

// The DirectX Math library should be portable but otherwise,
// SimpleMath at least provides a consistent interface for
// math operations that can be replicated
#include "../inc/SimpleMath.h"

#include <algorithm>

typedef DirectX::SimpleMath::Plane Plane;
typedef DirectX::SimpleMath::Matrix Matrix;
typedef DirectX::SimpleMath::Quaternion Quaternion;
typedef DirectX::SimpleMath::Color Color;

struct Vector2;
struct Vector3;
struct Vector4;

struct Vector2 : public DirectX::SimpleMath::Vector2
{
	using DirectX::SimpleMath::Vector2::Vector2;
	Vector2(DirectX::SimpleMath::Vector2 o) : DirectX::SimpleMath::Vector2(o) { }
	inline Vector2& operator= (const DirectX::SimpleMath::Vector2& o) noexcept { x = o.x; y = o.y; return *this; }
	inline Vector2 operator +(Vector2 o) { return (Vector2&)(*(DirectX::SimpleMath::Vector2*)this + o); }
	inline Vector2 operator -(Vector2 o) { return (Vector2&)(*(DirectX::SimpleMath::Vector2*)this - o); }
	inline Vector2 operator *(Vector2 o) { return (Vector2&)(*(DirectX::SimpleMath::Vector2*)this * o); }
	inline Vector2 operator /(Vector2 o) { return (Vector2&)(*(DirectX::SimpleMath::Vector2*)this / o); }
	inline Vector2 operator +(float v) const { return Vector2(x + v, y + v); }
	inline Vector2 operator -(float v) const { return Vector2(x - v, y - v); }
	inline Vector2 operator *(float o) const { return (Vector2&)(*(DirectX::SimpleMath::Vector2*)this * o); }
	inline Vector2 operator /(float o) const { return (Vector2&)(*(DirectX::SimpleMath::Vector2*)this / o); }
};
struct Vector3 : public DirectX::SimpleMath::Vector3
{
	using DirectX::SimpleMath::Vector3::Vector3;
	Vector3(DirectX::SimpleMath::Vector3 o) : DirectX::SimpleMath::Vector3(o) { }
	inline Vector3& operator= (const DirectX::SimpleMath::Vector3& o) noexcept { x = o.x; y = o.y; z = o.z; return *this; }
	inline Vector2 xy() const { return Vector2(x, y); }
	inline Vector2 yz() const { return Vector2(y, z); }
	inline Vector2 xz() const { return Vector2(x, z); }
	inline Vector3 xzy() const { return Vector3(x, z, y); }
	inline Vector3 operator +(Vector3 o) const { return (Vector3&)(*(DirectX::SimpleMath::Vector3*)this + o); }
	inline Vector3 operator -(Vector3 o) const { return (Vector3&)(*(DirectX::SimpleMath::Vector3*)this - o); }
	inline Vector3 operator *(Vector3 o) const { return (Vector3&)(*(DirectX::SimpleMath::Vector3*)this * o); }
	inline Vector3 operator /(Vector3 o) const { return (Vector3&)(*(DirectX::SimpleMath::Vector3*)this / o); }
	inline Vector3 operator +(float v) const { return Vector3(x + v, y + v, z + v); }
	inline Vector3 operator -(float v) const { return Vector3(x - v, y - v, z + v); }
	inline Vector3 operator *(float o) const { return (Vector3&)(*(DirectX::SimpleMath::Vector3*)this * o); }
	inline Vector3 operator /(float o) const { return (Vector3&)(*(DirectX::SimpleMath::Vector3*)this / o); }
};
struct Vector4 : public DirectX::SimpleMath::Vector4
{
	using DirectX::SimpleMath::Vector4::Vector4;
	Vector4(DirectX::SimpleMath::Vector4 o) : DirectX::SimpleMath::Vector4(o) { }
	inline Vector4& operator= (const DirectX::SimpleMath::Vector4& o) noexcept { x = o.x; y = o.y; z = o.z; w = o.w; return *this; }
	inline Vector2 xy() const { return Vector2(x, y); }
	inline Vector2 yz() const { return Vector2(y, z); }
	inline Vector2 xz() const { return Vector2(x, z); }
	inline Vector3 xyz() const { return Vector3(x, y, z); }
	inline Vector4 operator +(float v) const { return Vector4(x + v, y + v, z + v, w + v); }
	inline Vector4 operator -(float v) const { return Vector4(x - v, y - v, z + v, w + v); }
};
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
	operator Vector2() { return Vector2((float)x, (float)y); }
};

struct RectInt {
	int x, y, width, height;
	RectInt(int x = 0, int y = 0, int w = 0, int h = 0) : x(x), y(y), width(w), height(h) { }
	inline int GetWidth() const { return width; }
	inline int GetHeight() const { return height; }
	inline Int2 GetMin() const { return Int2(x, y); }
	inline Int2 GetMax() const { return Int2(x + width, y + height); }
	static RectInt FromMinMax(Int2 min, Int2 max) { return RectInt(min.x, min.y, max.x - min.x, max.y - min.y); }
};

struct Ray
{
	Vector3 Origin;
	Vector3 Direction;
	Vector3 ProjectTo(const Plane& p) const
	{
		return Origin + Direction *
			(p.w - p.Normal().Dot(Origin)) / (p.Normal().Dot(Direction));
	}
	// Get the distance between a point and the nearest point
	// along this ray
	float GetDistanceSqr(Vector3 point) const
	{
		auto dirLen2 = Direction.LengthSquared();
		auto proj = Origin + Direction *
			(Direction.Dot(point - Origin) / dirLen2);
		return (point - proj).LengthSquared();
	}
};
