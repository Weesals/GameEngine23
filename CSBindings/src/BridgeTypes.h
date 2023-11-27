#pragma once

#include <stdint.h>

#include "MathTypes.h"

/*
namespace DirectX::SimpleMath { struct Vector2; struct Vector3; struct Vector4; }
typedef DirectX::SimpleMath::Vector2 Vector2;
typedef DirectX::SimpleMath::Vector3 Vector3;
typedef DirectX::SimpleMath::Vector4 Vector4;
*/
struct Int2C { int x, y; };
struct Int4C { int x, y, z, w; };

inline Int2C ToC(const Int2& v) { return (const Int2C&)v; }
inline Int4C ToC(const Int4& v) { return (const Int4C&)v; }
