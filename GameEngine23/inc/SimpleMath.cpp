//-------------------------------------------------------------------------------------
// SimpleMath.cpp -- Simplified C++ Math wrapper for DirectXMath
//
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
//
// http://go.microsoft.com/fwlink/?LinkId=248929
// http://go.microsoft.com/fwlink/?LinkID=615561
//-------------------------------------------------------------------------------------

#include "SimpleMath.h"

#if (defined(_WIN32) || defined(WINAPI_FAMILY)) && !(defined(_XBOX_ONE) && defined(_TITLE)) && !defined(_GAMING_XBOX)
#include <dxgi1_2.h>
#endif

#ifdef __clang__
#pragma clang diagnostic push
#pragma clang diagnostic ignored "-Wfloat-equal"
#pragma clang diagnostic ignored "-Wunknown-warning-option"
#pragma clang diagnostic ignored "-Wunsafe-buffer-usage"
#endif


/****************************************************************************
 *
 * Constants
 *
 ****************************************************************************/

namespace DirectX
{
    namespace SimpleMath
    {

        const Vector2 Vector2::Zero = { 0.f, 0.f };
        const Vector2 Vector2::One = { 1.f, 1.f };
        const Vector2 Vector2::UnitX = { 1.f, 0.f };
        const Vector2 Vector2::UnitY = { 0.f, 1.f };

        const Vector3 Vector3::Zero = { 0.f, 0.f, 0.f };
        const Vector3 Vector3::One = { 1.f, 1.f, 1.f };
        const Vector3 Vector3::UnitX = { 1.f, 0.f, 0.f };
        const Vector3 Vector3::UnitY = { 0.f, 1.f, 0.f };
        const Vector3 Vector3::UnitZ = { 0.f, 0.f, 1.f };
        const Vector3 Vector3::Up = { 0.f, 1.f, 0.f };
        const Vector3 Vector3::Down = { 0.f, -1.f, 0.f };
        const Vector3 Vector3::Right = { 1.f, 0.f, 0.f };
        const Vector3 Vector3::Left = { -1.f, 0.f, 0.f };
        const Vector3 Vector3::Forward = { 0.f, 0.f, 1.f };
        const Vector3 Vector3::Backward = { 0.f, 0.f, -1.f };

        const Vector4 Vector4::Zero = { 0.f, 0.f, 0.f, 0.f };
        const Vector4 Vector4::One = { 1.f, 1.f, 1.f, 1.f };
        const Vector4 Vector4::UnitX = { 1.f, 0.f, 0.f, 0.f };
        const Vector4 Vector4::UnitY = { 0.f, 1.f, 0.f, 0.f };
        const Vector4 Vector4::UnitZ = { 0.f, 0.f, 1.f, 0.f };
        const Vector4 Vector4::UnitW = { 0.f, 0.f, 0.f, 1.f };

        const Matrix Matrix::Identity = { 1.f, 0.f, 0.f, 0.f,
                                          0.f, 1.f, 0.f, 0.f,
                                          0.f, 0.f, 1.f, 0.f,
                                          0.f, 0.f, 0.f, 1.f };

        const Quaternion Quaternion::Identity = { 0.f, 0.f, 0.f, 1.f };

    }
}

using namespace DirectX;
using namespace DirectX::SimpleMath;

/****************************************************************************
 *
 * Vector2
 *
 ****************************************************************************/


//------------------------------------------------------------------------------
// Vector operations
//------------------------------------------------------------------------------

bool Vector2::InBounds(const Vector2& Bounds) const noexcept
{
    using namespace DirectX;
    const XMVECTOR v1 = XMLoadFloat2(this);
    const XMVECTOR v2 = XMLoadFloat2(&Bounds);
    return XMVector2InBounds(v1, v2);
}

float Vector2::Length() const noexcept
{
    using namespace DirectX;
    const XMVECTOR v1 = XMLoadFloat2(this);
    const XMVECTOR X = XMVector2Length(v1);
    return XMVectorGetX(X);
}

float Vector2::LengthSquared() const noexcept
{
    using namespace DirectX;
    const XMVECTOR v1 = XMLoadFloat2(this);
    const XMVECTOR X = XMVector2LengthSq(v1);
    return XMVectorGetX(X);
}

float Vector2::Dot(const Vector2& V1, const Vector2& V2) noexcept
{
    using namespace DirectX;
    const XMVECTOR v1 = XMLoadFloat2(&V1);
    const XMVECTOR v2 = XMLoadFloat2(&V2);
    const XMVECTOR X = XMVector2Dot(v1, v2);
    return XMVectorGetX(X);
}

float Vector2::Cross(const Vector2& V1, const Vector2& V2) noexcept
{
    using namespace DirectX;
    const XMVECTOR v1 = XMLoadFloat2(&V1);
    const XMVECTOR v2 = XMLoadFloat2(&V2);
    const XMVECTOR R = XMVector2Cross(v1, v2);
    Vector2 result;
    XMStoreFloat2(&result, R);
    return result.x;
}

Vector2 Vector2::Normalize() noexcept
{
    using namespace DirectX;
    const XMVECTOR v1 = XMLoadFloat2(this);
    const XMVECTOR X = XMVector2Normalize(v1);
    return X;
}

void Vector2::Normalize(Vector2& result) const noexcept
{
    using namespace DirectX;
    const XMVECTOR v1 = XMLoadFloat2(this);
    const XMVECTOR X = XMVector2Normalize(v1);
    XMStoreFloat2(&result, X);
}

Vector2 Vector2::Clamp(const Vector2& V, const Vector2& vmin, const Vector2& vmax) noexcept
{
    using namespace DirectX;
    const XMVECTOR v1 = XMLoadFloat2(&V);
    const XMVECTOR v2 = XMLoadFloat2(&vmin);
    const XMVECTOR v3 = XMLoadFloat2(&vmax);
    const XMVECTOR X = XMVectorClamp(v1, v2, v3);
    return X;
}

//------------------------------------------------------------------------------
// Static functions
//------------------------------------------------------------------------------

float Vector2::Distance(const Vector2& v1, const Vector2& v2) noexcept
{
    using namespace DirectX;
    const XMVECTOR x1 = XMLoadFloat2(&v1);
    const XMVECTOR x2 = XMLoadFloat2(&v2);
    const XMVECTOR V = XMVectorSubtract(x2, x1);
    const XMVECTOR X = XMVector2Length(V);
    return XMVectorGetX(X);
}

float Vector2::DistanceSquared(const Vector2& v1, const Vector2& v2) noexcept
{
    using namespace DirectX;
    const XMVECTOR x1 = XMLoadFloat2(&v1);
    const XMVECTOR x2 = XMLoadFloat2(&v2);
    const XMVECTOR V = XMVectorSubtract(x2, x1);
    const XMVECTOR X = XMVector2LengthSq(V);
    return XMVectorGetX(X);
}

void Vector2::Min(const Vector2& v1, const Vector2& v2, Vector2& result) noexcept
{
    using namespace DirectX;
    const XMVECTOR x1 = XMLoadFloat2(&v1);
    const XMVECTOR x2 = XMLoadFloat2(&v2);
    const XMVECTOR X = XMVectorMin(x1, x2);
    XMStoreFloat2(&result, X);
}

Vector2 Vector2::Min(const Vector2& v1, const Vector2& v2) noexcept
{
    using namespace DirectX;
    const XMVECTOR x1 = XMLoadFloat2(&v1);
    const XMVECTOR x2 = XMLoadFloat2(&v2);
    const XMVECTOR X = XMVectorMin(x1, x2);

    Vector2 result;
    XMStoreFloat2(&result, X);
    return result;
}

void Vector2::Max(const Vector2& v1, const Vector2& v2, Vector2& result) noexcept
{
    using namespace DirectX;
    const XMVECTOR x1 = XMLoadFloat2(&v1);
    const XMVECTOR x2 = XMLoadFloat2(&v2);
    const XMVECTOR X = XMVectorMax(x1, x2);
    XMStoreFloat2(&result, X);
}

Vector2 Vector2::Max(const Vector2& v1, const Vector2& v2) noexcept
{
    using namespace DirectX;
    const XMVECTOR x1 = XMLoadFloat2(&v1);
    const XMVECTOR x2 = XMLoadFloat2(&v2);
    const XMVECTOR X = XMVectorMax(x1, x2);

    Vector2 result;
    XMStoreFloat2(&result, X);
    return result;
}

void Vector2::Lerp(const Vector2& v1, const Vector2& v2, float t, Vector2& result) noexcept
{
    using namespace DirectX;
    const XMVECTOR x1 = XMLoadFloat2(&v1);
    const XMVECTOR x2 = XMLoadFloat2(&v2);
    const XMVECTOR X = XMVectorLerp(x1, x2, t);
    XMStoreFloat2(&result, X);
}

Vector2 Vector2::Lerp(const Vector2& v1, const Vector2& v2, float t) noexcept
{
    using namespace DirectX;
    const XMVECTOR x1 = XMLoadFloat2(&v1);
    const XMVECTOR x2 = XMLoadFloat2(&v2);
    const XMVECTOR X = XMVectorLerp(x1, x2, t);

    Vector2 result;
    XMStoreFloat2(&result, X);
    return result;
}

void Vector2::SmoothStep(const Vector2& v1, const Vector2& v2, float t, Vector2& result) noexcept
{
    using namespace DirectX;
    t = (t > 1.0f) ? 1.0f : ((t < 0.0f) ? 0.0f : t);  // Clamp value to 0 to 1
    t = t * t * (3.f - 2.f * t);
    const XMVECTOR x1 = XMLoadFloat2(&v1);
    const XMVECTOR x2 = XMLoadFloat2(&v2);
    const XMVECTOR X = XMVectorLerp(x1, x2, t);
    XMStoreFloat2(&result, X);
}

Vector2 Vector2::SmoothStep(const Vector2& v1, const Vector2& v2, float t) noexcept
{
    using namespace DirectX;
    t = (t > 1.0f) ? 1.0f : ((t < 0.0f) ? 0.0f : t);  // Clamp value to 0 to 1
    t = t * t * (3.f - 2.f * t);
    const XMVECTOR x1 = XMLoadFloat2(&v1);
    const XMVECTOR x2 = XMLoadFloat2(&v2);
    const XMVECTOR X = XMVectorLerp(x1, x2, t);

    Vector2 result;
    XMStoreFloat2(&result, X);
    return result;
}

void Vector2::Barycentric(const Vector2& v1, const Vector2& v2, const Vector2& v3, float f, float g, Vector2& result) noexcept
{
    using namespace DirectX;
    const XMVECTOR x1 = XMLoadFloat2(&v1);
    const XMVECTOR x2 = XMLoadFloat2(&v2);
    const XMVECTOR x3 = XMLoadFloat2(&v3);
    const XMVECTOR X = XMVectorBaryCentric(x1, x2, x3, f, g);
    XMStoreFloat2(&result, X);
}

Vector2 Vector2::Barycentric(const Vector2& v1, const Vector2& v2, const Vector2& v3, float f, float g) noexcept
{
    using namespace DirectX;
    const XMVECTOR x1 = XMLoadFloat2(&v1);
    const XMVECTOR x2 = XMLoadFloat2(&v2);
    const XMVECTOR x3 = XMLoadFloat2(&v3);
    const XMVECTOR X = XMVectorBaryCentric(x1, x2, x3, f, g);

    Vector2 result;
    XMStoreFloat2(&result, X);
    return result;
}

void Vector2::CatmullRom(const Vector2& v1, const Vector2& v2, const Vector2& v3, const Vector2& v4, float t, Vector2& result) noexcept
{
    using namespace DirectX;
    const XMVECTOR x1 = XMLoadFloat2(&v1);
    const XMVECTOR x2 = XMLoadFloat2(&v2);
    const XMVECTOR x3 = XMLoadFloat2(&v3);
    const XMVECTOR x4 = XMLoadFloat2(&v4);
    const XMVECTOR X = XMVectorCatmullRom(x1, x2, x3, x4, t);
    XMStoreFloat2(&result, X);
}

Vector2 Vector2::CatmullRom(const Vector2& v1, const Vector2& v2, const Vector2& v3, const Vector2& v4, float t) noexcept
{
    using namespace DirectX;
    const XMVECTOR x1 = XMLoadFloat2(&v1);
    const XMVECTOR x2 = XMLoadFloat2(&v2);
    const XMVECTOR x3 = XMLoadFloat2(&v3);
    const XMVECTOR x4 = XMLoadFloat2(&v4);
    const XMVECTOR X = XMVectorCatmullRom(x1, x2, x3, x4, t);

    Vector2 result;
    XMStoreFloat2(&result, X);
    return result;
}

void Vector2::Hermite(const Vector2& v1, const Vector2& t1, const Vector2& v2, const Vector2& t2, float t, Vector2& result) noexcept
{
    using namespace DirectX;
    const XMVECTOR x1 = XMLoadFloat2(&v1);
    const XMVECTOR x2 = XMLoadFloat2(&t1);
    const XMVECTOR x3 = XMLoadFloat2(&v2);
    const XMVECTOR x4 = XMLoadFloat2(&t2);
    const XMVECTOR X = XMVectorHermite(x1, x2, x3, x4, t);
    XMStoreFloat2(&result, X);
}

Vector2 Vector2::Hermite(const Vector2& v1, const Vector2& t1, const Vector2& v2, const Vector2& t2, float t) noexcept
{
    using namespace DirectX;
    const XMVECTOR x1 = XMLoadFloat2(&v1);
    const XMVECTOR x2 = XMLoadFloat2(&t1);
    const XMVECTOR x3 = XMLoadFloat2(&v2);
    const XMVECTOR x4 = XMLoadFloat2(&t2);
    const XMVECTOR X = XMVectorHermite(x1, x2, x3, x4, t);

    Vector2 result;
    XMStoreFloat2(&result, X);
    return result;
}

void Vector2::Reflect(const Vector2& ivec, const Vector2& nvec, Vector2& result) noexcept
{
    using namespace DirectX;
    const XMVECTOR i = XMLoadFloat2(&ivec);
    const XMVECTOR n = XMLoadFloat2(&nvec);
    const XMVECTOR X = XMVector2Reflect(i, n);
    XMStoreFloat2(&result, X);
}

Vector2 Vector2::Reflect(const Vector2& ivec, const Vector2& nvec) noexcept
{
    using namespace DirectX;
    const XMVECTOR i = XMLoadFloat2(&ivec);
    const XMVECTOR n = XMLoadFloat2(&nvec);
    const XMVECTOR X = XMVector2Reflect(i, n);

    Vector2 result;
    XMStoreFloat2(&result, X);
    return result;
}

void Vector2::Refract(const Vector2& ivec, const Vector2& nvec, float refractionIndex, Vector2& result) noexcept
{
    using namespace DirectX;
    const XMVECTOR i = XMLoadFloat2(&ivec);
    const XMVECTOR n = XMLoadFloat2(&nvec);
    const XMVECTOR X = XMVector2Refract(i, n, refractionIndex);
    XMStoreFloat2(&result, X);
}

Vector2 Vector2::Refract(const Vector2& ivec, const Vector2& nvec, float refractionIndex) noexcept
{
    using namespace DirectX;
    const XMVECTOR i = XMLoadFloat2(&ivec);
    const XMVECTOR n = XMLoadFloat2(&nvec);
    const XMVECTOR X = XMVector2Refract(i, n, refractionIndex);

    Vector2 result;
    XMStoreFloat2(&result, X);
    return result;
}

void Vector2::Transform(const Vector2& v, const Quaternion& quat, Vector2& result) noexcept
{
    using namespace DirectX;
    const XMVECTOR v1 = XMLoadFloat2(&v);
    const XMVECTOR q = XMLoadFloat4(&quat);
    const XMVECTOR X = XMVector3Rotate(v1, q);
    XMStoreFloat2(&result, X);
}

Vector2 Vector2::Transform(const Vector2& v, const Quaternion& quat) noexcept
{
    using namespace DirectX;
    const XMVECTOR v1 = XMLoadFloat2(&v);
    const XMVECTOR q = XMLoadFloat4(&quat);
    const XMVECTOR X = XMVector3Rotate(v1, q);

    Vector2 result;
    XMStoreFloat2(&result, X);
    return result;
}

void Vector2::Transform(const Vector2& v, const Matrix& m, Vector2& result) noexcept
{
    using namespace DirectX;
    const XMVECTOR v1 = XMLoadFloat2(&v);
    const XMMATRIX M = XMLoadFloat4x4(&m);
    const XMVECTOR X = XMVector2TransformCoord(v1, M);
    XMStoreFloat2(&result, X);
}

Vector2 Vector2::Transform(const Vector2& v, const Matrix& m) noexcept
{
    using namespace DirectX;
    const XMVECTOR v1 = XMLoadFloat2(&v);
    const XMMATRIX M = XMLoadFloat4x4(&m);
    const XMVECTOR X = XMVector2TransformCoord(v1, M);

    Vector2 result;
    XMStoreFloat2(&result, X);
    return result;
}

_Use_decl_annotations_
void Vector2::Transform(const Vector2* varray, size_t count, const Matrix& m, Vector2* resultArray) noexcept
{
    using namespace DirectX;
    const XMMATRIX M = XMLoadFloat4x4(&m);
    XMVector2TransformCoordStream(resultArray, sizeof(XMFLOAT2), varray, sizeof(XMFLOAT2), count, M);
}

void Vector2::Transform(const Vector2& v, const Matrix& m, Vector4& result) noexcept
{
    using namespace DirectX;
    const XMVECTOR v1 = XMLoadFloat2(&v);
    const XMMATRIX M = XMLoadFloat4x4(&m);
    const XMVECTOR X = XMVector2Transform(v1, M);
    XMStoreFloat4(&result, X);
}

_Use_decl_annotations_
void Vector2::Transform(const Vector2* varray, size_t count, const Matrix& m, Vector4* resultArray) noexcept
{
    using namespace DirectX;
    const XMMATRIX M = XMLoadFloat4x4(&m);
    XMVector2TransformStream(resultArray, sizeof(XMFLOAT4), varray, sizeof(XMFLOAT2), count, M);
}

void Vector2::TransformNormal(const Vector2& v, const Matrix& m, Vector2& result) noexcept
{
    using namespace DirectX;
    const XMVECTOR v1 = XMLoadFloat2(&v);
    const XMMATRIX M = XMLoadFloat4x4(&m);
    const XMVECTOR X = XMVector2TransformNormal(v1, M);
    XMStoreFloat2(&result, X);
}

Vector2 Vector2::TransformNormal(const Vector2& v, const Matrix& m) noexcept
{
    using namespace DirectX;
    const XMVECTOR v1 = XMLoadFloat2(&v);
    const XMMATRIX M = XMLoadFloat4x4(&m);
    const XMVECTOR X = XMVector2TransformNormal(v1, M);

    Vector2 result;
    XMStoreFloat2(&result, X);
    return result;
}

_Use_decl_annotations_
void Vector2::TransformNormal(const Vector2* varray, size_t count, const Matrix& m, Vector2* resultArray) noexcept
{
    using namespace DirectX;
    const XMMATRIX M = XMLoadFloat4x4(&m);
    XMVector2TransformNormalStream(resultArray, sizeof(XMFLOAT2), varray, sizeof(XMFLOAT2), count, M);
}

Vector2 Vector2::MoveTowards(Vector2 from, Vector2 to, float dst) noexcept
{
    if (dst <= 0.0f) return from;
    auto delta = to - from;
    auto deltaL = delta.Length();
    return from + delta * (dst >= deltaL ? 1.0f : dst / deltaL);
}


/****************************************************************************
 *
 * Vector3
 *
 ****************************************************************************/

//------------------------------------------------------------------------------
// Vector operations
//------------------------------------------------------------------------------

bool Vector3::InBounds(const Vector3& Bounds) const noexcept
{
    using namespace DirectX;
    const XMVECTOR v1 = XMLoadFloat3(this);
    const XMVECTOR v2 = XMLoadFloat3(&Bounds);
    return XMVector3InBounds(v1, v2);
}

Vector3 Vector3::Normalize() const noexcept
{
    using namespace DirectX;
    const XMVECTOR v1 = XMLoadFloat3(this);
    const XMVECTOR X = XMVector3Normalize(v1);
    return X;
}

float Vector3::Length() const noexcept
{
    using namespace DirectX;
    const XMVECTOR v1 = XMLoadFloat3(this);
    const XMVECTOR X = XMVector3Length(v1);
    return XMVectorGetX(X);
}

float Vector3::LengthSquared() const noexcept
{
    using namespace DirectX;
    const XMVECTOR v1 = XMLoadFloat3(this);
    const XMVECTOR X = XMVector3LengthSq(v1);
    return XMVectorGetX(X);
}

//------------------------------------------------------------------------------
// Static functions
//------------------------------------------------------------------------------

float Vector3::Dot(const Vector3& V1, const Vector3& V2) noexcept
{
    using namespace DirectX;
    const XMVECTOR v1 = XMLoadFloat3(&V1);
    const XMVECTOR v2 = XMLoadFloat3(&V2);
    const XMVECTOR X = XMVector3Dot(v1, v2);
    return XMVectorGetX(X);
}

Vector3 Vector3::Cross(const Vector3& V1, Vector3& V2) noexcept
{
    using namespace DirectX;
    const XMVECTOR v1 = XMLoadFloat3(&V1);
    const XMVECTOR v2 = XMLoadFloat3(&V2);
    const XMVECTOR R = XMVector3Cross(v1, v2);
    return R;
}

Vector3 Vector3::Clamp(const Vector3& value, const Vector3& vmin, const Vector3& vmax) noexcept
{
    using namespace DirectX;
    const XMVECTOR v1 = XMLoadFloat3(&value);
    const XMVECTOR v2 = XMLoadFloat3(&vmin);
    const XMVECTOR v3 = XMLoadFloat3(&vmax);
    const XMVECTOR X = XMVectorClamp(v1, v2, v3);
    return X;
}

void Vector3::Min(const Vector3& v1, const Vector3& v2, Vector3& result) noexcept
{
    using namespace DirectX;
    const XMVECTOR x1 = XMLoadFloat3(&v1);
    const XMVECTOR x2 = XMLoadFloat3(&v2);
    const XMVECTOR X = XMVectorMin(x1, x2);
    XMStoreFloat3(&result, X);
}

Vector3 Vector3::Min(const Vector3& v1, const Vector3& v2) noexcept
{
    using namespace DirectX;
    const XMVECTOR x1 = XMLoadFloat3(&v1);
    const XMVECTOR x2 = XMLoadFloat3(&v2);
    const XMVECTOR X = XMVectorMin(x1, x2);

    Vector3 result;
    XMStoreFloat3(&result, X);
    return result;
}

void Vector3::Max(const Vector3& v1, const Vector3& v2, Vector3& result) noexcept
{
    using namespace DirectX;
    const XMVECTOR x1 = XMLoadFloat3(&v1);
    const XMVECTOR x2 = XMLoadFloat3(&v2);
    const XMVECTOR X = XMVectorMax(x1, x2);
    XMStoreFloat3(&result, X);
}

Vector3 Vector3::Max(const Vector3& v1, const Vector3& v2) noexcept
{
    using namespace DirectX;
    const XMVECTOR x1 = XMLoadFloat3(&v1);
    const XMVECTOR x2 = XMLoadFloat3(&v2);
    const XMVECTOR X = XMVectorMax(x1, x2);

    Vector3 result;
    XMStoreFloat3(&result, X);
    return result;
}

void Vector3::Lerp(const Vector3& v1, const Vector3& v2, float t, Vector3& result) noexcept
{
    using namespace DirectX;
    const XMVECTOR x1 = XMLoadFloat3(&v1);
    const XMVECTOR x2 = XMLoadFloat3(&v2);
    const XMVECTOR X = XMVectorLerp(x1, x2, t);
    XMStoreFloat3(&result, X);
}

Vector3 Vector3::Lerp(const Vector3& v1, const Vector3& v2, float t) noexcept
{
    using namespace DirectX;
    const XMVECTOR x1 = XMLoadFloat3(&v1);
    const XMVECTOR x2 = XMLoadFloat3(&v2);
    const XMVECTOR X = XMVectorLerp(x1, x2, t);

    Vector3 result;
    XMStoreFloat3(&result, X);
    return result;
}

void Vector3::SmoothStep(const Vector3& v1, const Vector3& v2, float t, Vector3& result) noexcept
{
    using namespace DirectX;
    t = (t > 1.0f) ? 1.0f : ((t < 0.0f) ? 0.0f : t);  // Clamp value to 0 to 1
    t = t * t * (3.f - 2.f * t);
    const XMVECTOR x1 = XMLoadFloat3(&v1);
    const XMVECTOR x2 = XMLoadFloat3(&v2);
    const XMVECTOR X = XMVectorLerp(x1, x2, t);
    XMStoreFloat3(&result, X);
}

Vector3 Vector3::SmoothStep(const Vector3& v1, const Vector3& v2, float t) noexcept
{
    using namespace DirectX;
    t = (t > 1.0f) ? 1.0f : ((t < 0.0f) ? 0.0f : t);  // Clamp value to 0 to 1
    t = t * t * (3.f - 2.f * t);
    const XMVECTOR x1 = XMLoadFloat3(&v1);
    const XMVECTOR x2 = XMLoadFloat3(&v2);
    const XMVECTOR X = XMVectorLerp(x1, x2, t);

    Vector3 result;
    XMStoreFloat3(&result, X);
    return result;
}

void Vector3::Barycentric(const Vector3& v1, const Vector3& v2, const Vector3& v3, float f, float g, Vector3& result) noexcept
{
    using namespace DirectX;
    const XMVECTOR x1 = XMLoadFloat3(&v1);
    const XMVECTOR x2 = XMLoadFloat3(&v2);
    const XMVECTOR x3 = XMLoadFloat3(&v3);
    const XMVECTOR X = XMVectorBaryCentric(x1, x2, x3, f, g);
    XMStoreFloat3(&result, X);
}

Vector3 Vector3::Barycentric(const Vector3& v1, const Vector3& v2, const Vector3& v3, float f, float g) noexcept
{
    using namespace DirectX;
    const XMVECTOR x1 = XMLoadFloat3(&v1);
    const XMVECTOR x2 = XMLoadFloat3(&v2);
    const XMVECTOR x3 = XMLoadFloat3(&v3);
    const XMVECTOR X = XMVectorBaryCentric(x1, x2, x3, f, g);

    Vector3 result;
    XMStoreFloat3(&result, X);
    return result;
}

void Vector3::CatmullRom(const Vector3& v1, const Vector3& v2, const Vector3& v3, const Vector3& v4, float t, Vector3& result) noexcept
{
    using namespace DirectX;
    const XMVECTOR x1 = XMLoadFloat3(&v1);
    const XMVECTOR x2 = XMLoadFloat3(&v2);
    const XMVECTOR x3 = XMLoadFloat3(&v3);
    const XMVECTOR x4 = XMLoadFloat3(&v4);
    const XMVECTOR X = XMVectorCatmullRom(x1, x2, x3, x4, t);
    XMStoreFloat3(&result, X);
}

Vector3 Vector3::CatmullRom(const Vector3& v1, const Vector3& v2, const Vector3& v3, const Vector3& v4, float t) noexcept
{
    using namespace DirectX;
    const XMVECTOR x1 = XMLoadFloat3(&v1);
    const XMVECTOR x2 = XMLoadFloat3(&v2);
    const XMVECTOR x3 = XMLoadFloat3(&v3);
    const XMVECTOR x4 = XMLoadFloat3(&v4);
    const XMVECTOR X = XMVectorCatmullRom(x1, x2, x3, x4, t);

    Vector3 result;
    XMStoreFloat3(&result, X);
    return result;
}

void Vector3::Hermite(const Vector3& v1, const Vector3& t1, const Vector3& v2, const Vector3& t2, float t, Vector3& result) noexcept
{
    using namespace DirectX;
    const XMVECTOR x1 = XMLoadFloat3(&v1);
    const XMVECTOR x2 = XMLoadFloat3(&t1);
    const XMVECTOR x3 = XMLoadFloat3(&v2);
    const XMVECTOR x4 = XMLoadFloat3(&t2);
    const XMVECTOR X = XMVectorHermite(x1, x2, x3, x4, t);
    XMStoreFloat3(&result, X);
}

Vector3 Vector3::Hermite(const Vector3& v1, const Vector3& t1, const Vector3& v2, const Vector3& t2, float t) noexcept
{
    using namespace DirectX;
    const XMVECTOR x1 = XMLoadFloat3(&v1);
    const XMVECTOR x2 = XMLoadFloat3(&t1);
    const XMVECTOR x3 = XMLoadFloat3(&v2);
    const XMVECTOR x4 = XMLoadFloat3(&t2);
    const XMVECTOR X = XMVectorHermite(x1, x2, x3, x4, t);

    Vector3 result;
    XMStoreFloat3(&result, X);
    return result;
}

void Vector3::Reflect(const Vector3& ivec, const Vector3& nvec, Vector3& result) noexcept
{
    using namespace DirectX;
    const XMVECTOR i = XMLoadFloat3(&ivec);
    const XMVECTOR n = XMLoadFloat3(&nvec);
    const XMVECTOR X = XMVector3Reflect(i, n);
    XMStoreFloat3(&result, X);
}

Vector3 Vector3::Reflect(const Vector3& ivec, const Vector3& nvec) noexcept
{
    using namespace DirectX;
    const XMVECTOR i = XMLoadFloat3(&ivec);
    const XMVECTOR n = XMLoadFloat3(&nvec);
    const XMVECTOR X = XMVector3Reflect(i, n);

    Vector3 result;
    XMStoreFloat3(&result, X);
    return result;
}

void Vector3::Refract(const Vector3& ivec, const Vector3& nvec, float refractionIndex, Vector3& result) noexcept
{
    using namespace DirectX;
    const XMVECTOR i = XMLoadFloat3(&ivec);
    const XMVECTOR n = XMLoadFloat3(&nvec);
    const XMVECTOR X = XMVector3Refract(i, n, refractionIndex);
    XMStoreFloat3(&result, X);
}

Vector3 Vector3::Refract(const Vector3& ivec, const Vector3& nvec, float refractionIndex) noexcept
{
    using namespace DirectX;
    const XMVECTOR i = XMLoadFloat3(&ivec);
    const XMVECTOR n = XMLoadFloat3(&nvec);
    const XMVECTOR X = XMVector3Refract(i, n, refractionIndex);

    Vector3 result;
    XMStoreFloat3(&result, X);
    return result;
}

void Vector3::Transform(const Vector3& v, const Quaternion& quat, Vector3& result) noexcept
{
    using namespace DirectX;
    const XMVECTOR v1 = XMLoadFloat3(&v);
    const XMVECTOR q = XMLoadFloat4(&quat);
    const XMVECTOR X = XMVector3Rotate(v1, q);
    XMStoreFloat3(&result, X);
}

Vector3 Vector3::Transform(const Vector3& v, const Quaternion& quat) noexcept
{
    using namespace DirectX;
    const XMVECTOR v1 = XMLoadFloat3(&v);
    const XMVECTOR q = XMLoadFloat4(&quat);
    const XMVECTOR X = XMVector3Rotate(v1, q);

    Vector3 result;
    XMStoreFloat3(&result, X);
    return result;
}

void Vector3::Transform(const Vector3& v, const Matrix& m, Vector3& result) noexcept
{
    using namespace DirectX;
    const XMVECTOR v1 = XMLoadFloat3(&v);
    const XMMATRIX M = XMLoadFloat4x4(&m);
    const XMVECTOR X = XMVector3TransformCoord(v1, M);
    XMStoreFloat3(&result, X);
}

Vector3 Vector3::Transform(const Vector3& v, const Matrix& m) noexcept
{
    using namespace DirectX;
    const XMVECTOR v1 = XMLoadFloat3(&v);
    const XMMATRIX M = XMLoadFloat4x4(&m);
    const XMVECTOR X = XMVector3TransformCoord(v1, M);

    Vector3 result;
    XMStoreFloat3(&result, X);
    return result;
}

_Use_decl_annotations_
void Vector3::Transform(const Vector3* varray, size_t count, const Matrix& m, Vector3* resultArray) noexcept
{
    using namespace DirectX;
    const XMMATRIX M = XMLoadFloat4x4(&m);
    XMVector3TransformCoordStream(resultArray, sizeof(XMFLOAT3), varray, sizeof(XMFLOAT3), count, M);
}

void Vector3::Transform(const Vector3& v, const Matrix& m, Vector4& result) noexcept
{
    using namespace DirectX;
    const XMVECTOR v1 = XMLoadFloat3(&v);
    const XMMATRIX M = XMLoadFloat4x4(&m);
    const XMVECTOR X = XMVector3Transform(v1, M);
    XMStoreFloat4(&result, X);
}

_Use_decl_annotations_
void Vector3::Transform(const Vector3* varray, size_t count, const Matrix& m, Vector4* resultArray) noexcept
{
    using namespace DirectX;
    const XMMATRIX M = XMLoadFloat4x4(&m);
    XMVector3TransformStream(resultArray, sizeof(XMFLOAT4), varray, sizeof(XMFLOAT3), count, M);
}

void Vector3::TransformNormal(const Vector3& v, const Matrix& m, Vector3& result) noexcept
{
    using namespace DirectX;
    const XMVECTOR v1 = XMLoadFloat3(&v);
    const XMMATRIX M = XMLoadFloat4x4(&m);
    const XMVECTOR X = XMVector3TransformNormal(v1, M);
    XMStoreFloat3(&result, X);
}

Vector3 Vector3::TransformNormal(const Vector3& v, const Matrix& m) noexcept
{
    using namespace DirectX;
    const XMVECTOR v1 = XMLoadFloat3(&v);
    const XMMATRIX M = XMLoadFloat4x4(&m);
    const XMVECTOR X = XMVector3TransformNormal(v1, M);

    Vector3 result;
    XMStoreFloat3(&result, X);
    return result;
}

_Use_decl_annotations_
void Vector3::TransformNormal(const Vector3* varray, size_t count, const Matrix& m, Vector3* resultArray) noexcept
{
    using namespace DirectX;
    const XMMATRIX M = XMLoadFloat4x4(&m);
    XMVector3TransformNormalStream(resultArray, sizeof(XMFLOAT3), varray, sizeof(XMFLOAT3), count, M);
}

float Vector3::Distance(const Vector3& v1, const Vector3& v2) noexcept
{
    using namespace DirectX;
    const XMVECTOR x1 = XMLoadFloat3(&v1);
    const XMVECTOR x2 = XMLoadFloat3(&v2);
    const XMVECTOR V = XMVectorSubtract(x2, x1);
    const XMVECTOR X = XMVector3Length(V);
    return XMVectorGetX(X);
}

float Vector3::DistanceSquared(const Vector3& v1, const Vector3& v2) noexcept
{
    using namespace DirectX;
    const XMVECTOR x1 = XMLoadFloat3(&v1);
    const XMVECTOR x2 = XMLoadFloat3(&v2);
    const XMVECTOR V = XMVectorSubtract(x2, x1);
    const XMVECTOR X = XMVector3LengthSq(V);
    return XMVectorGetX(X);
}


Vector3 Vector3::MoveTowards(Vector3 from, Vector3 to, float dst) noexcept
{
    if (dst <= 0.0f) return from;
    auto delta = to - from;
    auto deltaL = delta.Length();
    return from + delta * (dst >= deltaL ? 1.0f : dst / deltaL);
}


/****************************************************************************
 *
 * Quaternion
 *
 ****************************************************************************/

void Quaternion::RotateTowards(const Quaternion& target, float maxAngle, Quaternion& result) const noexcept
{
    const XMVECTOR T = XMLoadFloat4(this);

    // We can use the conjugate here instead of inverse assuming q1 & q2 are normalized.
    const XMVECTOR R = XMQuaternionMultiply(XMQuaternionConjugate(T), target);

    const float rs = XMVectorGetW(R);
    const XMVECTOR L = XMVector3Length(R);
    const float angle = 2.f * atan2f(XMVectorGetX(L), rs);
    if (angle > maxAngle)
    {
        const XMVECTOR delta = XMQuaternionRotationAxis(R, maxAngle);
        const XMVECTOR Q = XMQuaternionMultiply(delta, T);
        XMStoreFloat4(&result, Q);
    }
    else
    {
        // Don't overshoot.
        result = target;
    }
}

void Quaternion::FromToRotation(const Vector3& fromDir, const Vector3& toDir, Quaternion& result) noexcept
{
    // Melax, "The Shortest Arc Quaternion", Game Programming Gems, Charles River Media (2000).

    const XMVECTOR F = XMVector3Normalize(fromDir);
    const XMVECTOR T = XMVector3Normalize(toDir);

    const float dot = XMVectorGetX(XMVector3Dot(F, T));
    if (dot >= 1.f)
    {
        result = Identity;
    }
    else if (dot <= -1.f)
    {
        XMVECTOR axis = XMVector3Cross(F, Vector3::Right);
        if (XMVector3NearEqual(XMVector3LengthSq(axis), g_XMZero, g_XMEpsilon))
        {
            axis = XMVector3Cross(F, Vector3::Up);
        }

        const XMVECTOR Q = XMQuaternionRotationAxis(axis, XM_PI);
        XMStoreFloat4(&result, Q);
    }
    else
    {
        const XMVECTOR C = XMVector3Cross(F, T);
        XMStoreFloat4(&result, C);

        const float s = sqrtf((1.f + dot) * 2.f);
        result.x /= s;
        result.y /= s;
        result.z /= s;
        result.w = s * 0.5f;
    }
}

void Quaternion::LookRotation(const Vector3& forward, const Vector3& up, Quaternion& result) noexcept
{
    Quaternion q1;
    FromToRotation(Vector3::Forward, forward, q1);

    const XMVECTOR C = XMVector3Cross(forward, up);
    if (XMVector3NearEqual(XMVector3LengthSq(C), g_XMZero, g_XMEpsilon))
    {
        // forward and up are co-linear
        result = q1;
        return;
    }

    const XMVECTOR U = XMQuaternionMultiply(q1, Vector3::Up);

    Quaternion q2;
    FromToRotation(U, up, q2);

    XMStoreFloat4(&result, XMQuaternionMultiply(q2, q1));
}


/****************************************************************************
*
* Viewport
*
****************************************************************************/

#if defined(__d3d11_h__) || defined(__d3d11_x_h__)
static_assert(sizeof(DirectX::SimpleMath::Viewport) == sizeof(D3D11_VIEWPORT), "Size mismatch");
static_assert(offsetof(DirectX::SimpleMath::Viewport, x) == offsetof(D3D11_VIEWPORT, TopLeftX), "Layout mismatch");
static_assert(offsetof(DirectX::SimpleMath::Viewport, y) == offsetof(D3D11_VIEWPORT, TopLeftY), "Layout mismatch");
static_assert(offsetof(DirectX::SimpleMath::Viewport, width) == offsetof(D3D11_VIEWPORT, Width), "Layout mismatch");
static_assert(offsetof(DirectX::SimpleMath::Viewport, height) == offsetof(D3D11_VIEWPORT, Height), "Layout mismatch");
static_assert(offsetof(DirectX::SimpleMath::Viewport, minDepth) == offsetof(D3D11_VIEWPORT, MinDepth), "Layout mismatch");
static_assert(offsetof(DirectX::SimpleMath::Viewport, maxDepth) == offsetof(D3D11_VIEWPORT, MaxDepth), "Layout mismatch");
#endif

#if defined(__d3d12_h__) || defined(__d3d12_x_h__) || defined(__XBOX_D3D12_X__)
static_assert(sizeof(DirectX::SimpleMath::Viewport) == sizeof(D3D12_VIEWPORT), "Size mismatch");
static_assert(offsetof(DirectX::SimpleMath::Viewport, x) == offsetof(D3D12_VIEWPORT, TopLeftX), "Layout mismatch");
static_assert(offsetof(DirectX::SimpleMath::Viewport, y) == offsetof(D3D12_VIEWPORT, TopLeftY), "Layout mismatch");
static_assert(offsetof(DirectX::SimpleMath::Viewport, width) == offsetof(D3D12_VIEWPORT, Width), "Layout mismatch");
static_assert(offsetof(DirectX::SimpleMath::Viewport, height) == offsetof(D3D12_VIEWPORT, Height), "Layout mismatch");
static_assert(offsetof(DirectX::SimpleMath::Viewport, minDepth) == offsetof(D3D12_VIEWPORT, MinDepth), "Layout mismatch");
static_assert(offsetof(DirectX::SimpleMath::Viewport, maxDepth) == offsetof(D3D12_VIEWPORT, MaxDepth), "Layout mismatch");
#endif
