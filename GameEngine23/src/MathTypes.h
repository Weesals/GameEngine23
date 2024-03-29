#pragma once

// The DirectX Math library should be portable but otherwise,
// SimpleMath at least provides a consistent interface for
// math operations that can be replicated
#include "../inc/SimpleMath.h"
#include <cmath>

typedef DirectX::SimpleMath::Plane Plane;
typedef DirectX::SimpleMath::Vector2 Vector2;
typedef DirectX::SimpleMath::Vector3 Vector3;
typedef DirectX::SimpleMath::Vector4 Vector4;
typedef DirectX::SimpleMath::Matrix Matrix;
typedef DirectX::SimpleMath::Quaternion Quaternion;
typedef DirectX::SimpleMath::Color Color;
typedef DirectX::SimpleMath::ColorB4 ColorB4;

class Easing {
protected:
	struct Power2Ease {		// Smooth at start
		float operator () (float l) const { return l * l; }
	};
	struct ElasticEase {	// Smooth at start (ie. inverted from normal use)
		float mSteps = 2.5f;
		ElasticEase(float steps) : mSteps(steps) { }
		float operator () (float l) const { return std::cosf((1.0f - l) * mSteps * 3.1416f) * l * l; }
	};
	struct BackEase {
		float mAmplitude = 1.75f;
		BackEase(float amplitude) : mAmplitude(amplitude) { }
		float operator () (float l) const { auto l2 = l * l; return (1.0f + mAmplitude) * l2 * l - mAmplitude * l2; }
	};
	template<class T> struct _EaseMode {
		T mEase;
		_EaseMode(const T& ease) : mEase(ease) { }
	};
public:
	struct Modes {
		template<class T> struct _EaseIn : public _EaseMode<T> {
			using _EaseMode<T>::_EaseMode;
			float operator () (float l) const { return this->mEase(l); }
			auto WithFromTo(float from, float to) const { MakeWithFromTo(*this, from, to); }
		};
		template<class T> struct _EaseOut : public _EaseMode<T> {
			using _EaseMode<T>::_EaseMode;
			float operator () (float l) const { return 1.0f - this->mEase(1.0f - l); }
		};
		template<class T> struct _WithFromTo : public _EaseMode<T> {
			float mFrom, mRange;
			_WithFromTo(const T& ease, float from = 0.0f, float to = 1.0f) :
				_EaseMode<T>(ease), mFrom(from), mRange(to - from) { }
			float operator () (float l) const { return this->mEase(l) * mRange + mFrom; }
		};
		template<class T> struct _WithDuration : public _EaseMode<T> {
			float mDuration;
			_WithDuration(const T& ease, float duration = 1.0f) :
				_EaseMode<T>(ease), mDuration(duration) { }
			float operator () (float l) const { return l < 0 ? 0.0f : l > mDuration ? 1.0f : this->mEase(l / mDuration); }
		};
	};
	static auto MakeEaseIn(auto fn) { return Modes::_EaseIn<decltype(fn)>(fn); }
	static auto MakeEaseOut(auto fn) { return Modes::_EaseOut<decltype(fn)>(fn); }
	static auto MakeWithDuration(auto fn, float dur) { return Modes::_WithDuration<decltype(fn)>(fn, dur); }
	static auto MakeWithFromTo(auto fn, float from, float to) { return Modes::_WithFromTo<decltype(fn)>(fn, from, to); }
	static auto Power2In(float duration = 1.0f) {
		return MakeWithDuration(MakeEaseIn(Power2Ease()), duration);
	}
	static auto Power2Out(float duration = 1.0f) {
		return MakeWithDuration(MakeEaseOut(Power2Ease()), duration);
	}
	static auto ElasticIn(float duration = 1.0f, float steps = 2.5f) {
		return MakeWithDuration(MakeEaseIn(ElasticEase(steps)), duration);
	}
	static auto ElasticOut(float duration = 1.0f, float steps = 2.5f) {
		return MakeWithDuration(MakeEaseOut(ElasticEase(steps)), duration);
	}
	static auto BackIn(float duration = 1.0f, float amplitude = 1.75f) {
		return MakeWithDuration(MakeEaseIn(BackEase(amplitude)), duration);
	}
	static auto BackOut(float duration = 1.0f, float amplitude = 1.75f) {
		return MakeWithDuration(MakeEaseOut(BackEase(amplitude)), duration);
	}
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
	inline bool operator ==(Int2 o) const noexcept { return x == o.x && y == o.y; }
	inline bool operator !=(Int2 o) const noexcept { return x != o.x || y != o.y; }

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
struct Int3
{
	int x, y, z;
	Int3() : Int3(0, 0, 0) { }
	Int3(int v) : Int3(v, v, v) { }
	Int3(int x, int y, int z) : x(x), y(y), z(z) { }
	Int3(Int2 v, int z) : x(v.x), y(v.y), z(z) { }
	Int3(const Vector3 o) : x((int)o.x), y((int)o.y), z((int)o.z) { }
	inline Int3 operator +(const Int3 o) const { return Int3(x + o.x, y + o.y, z + o.z); }
	inline Int3 operator -(const Int3 o) const { return Int3(x - o.x, y - o.y, z - o.z); }
	inline Int3 operator *(const Int3 o) const { return Int3(x * o.x, y * o.y, z * o.z); }
	inline Int3 operator /(const Int3 o) const { return Int3(x / o.x, y / o.y, z / o.z); }
	inline Int3 operator +(const int o) const { return Int3(x + o, y + o, z + o); }
	inline Int3 operator -(const int o) const { return Int3(x - o, y - o, z - o); }
	inline Int3 operator *(const int o) const { return Int3(x * o, y * o, z * o); }
	inline Int3 operator /(const int o) const { return Int3(x / o, y / o, z / o); }
	inline bool operator ==(Int3 o) const noexcept { return x == o.x && y == o.y && z == o.z; }
	inline bool operator !=(Int3 o) const noexcept { return x != o.x || y != o.y || z != o.z; }

	Int2 xy() const { return *(Int2*)this; }
	Int2& xy() { return *(Int2*)this; }

	static Int3 Min(Int3 v1, Int3 v2);
	static Int3 Max(Int3 v1, Int3 v2);
	static Int3 Clamp(Int3 v, Int3 min, Int3 max);

	inline Int3 operator =(Vector3 v) { return Int3((int)v.x, (int)v.y, (int)v.z); }
	inline operator Vector3() const { return Vector3((float)x, (float)y, (float)z); }
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
namespace std
{
	template<> struct less<Int2>
	{
		bool operator()(const Int2& V1, const Int2& V2) const noexcept
		{
			return ((V1.x < V2.x) || ((V1.x == V2.x) && (V1.y < V2.y)));
		}
	};
	template<> struct less<Int4>
	{
		bool operator()(const Int4& V1, const Int4& V2) const noexcept
		{
			return ((V1.x < V2.x)
				|| ((V1.x == V2.x) && (V1.y < V2.y))
				|| ((V1.x == V2.x) && (V1.y == V2.y) && (V1.z < V2.z))
				|| ((V1.x == V2.x) && (V1.y == V2.y) && (V1.z == V2.z) && (V1.w < V2.w)));
		}
	};
	template <> struct std::hash<Int2>
	{
		std::size_t operator()(const Int2& k) const
		{
			return *(size_t*)&k;
		}
	};
	template <> struct std::hash<Int4>
	{
		std::size_t operator()(const Int4& k) const
		{
			return ((size_t*)&k)[0] + ((size_t*)&k)[1] * 1234;
		}
	};
}

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
	int end() const { return start + length; }
	void end(int end) { length = end - start; }
	RangeInt() : start(0), length(0) { }
	RangeInt(int start, int length) : start(start), length(length) { }
	bool Contains(int value) const { value -= start; return (uint32_t)value < (uint32_t)length; }
	static RangeInt FromBeginEnd(int begin, int end) { return RangeInt(begin, end - begin); }
};

struct BoundingBox {
	Vector3 mMin, mMax;
	Vector3 Centre() const { return (mMin + mMax) / 2.0f; }
	Vector3 Extents() const { return (mMax - mMin) / 2.0f; }
	BoundingBox() : BoundingBox(Vector3::Zero, Vector3::Zero) { }
	BoundingBox(Vector3 min, Vector3 max)
		: mMin(min), mMax(max) { }
	static BoundingBox FromMinMax(Vector3 min, Vector3 max) { return BoundingBox(min, max); }
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
	void Normalize();
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
	void Normalize();
	Matrix CalculateViewProj() const;
	float GetVisibility(Vector3 pos) const;
	float GetVisibility(Vector3 pos, Vector3 ext) const;
	bool GetIsVisible(Vector3 pos) const;
	bool GetIsVisible(Vector3 pos, Vector3 ext) const;
	void GetCorners(Vector3 corners[8]) const;

	Frustum TransformToLocal(const Matrix& tform) const;
protected:
	Vector2 GetProjectedDistancesNearFar(Vector3 pos) const;
};
