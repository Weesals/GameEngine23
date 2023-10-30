#include "CanvasTransform.h"

Vector3 CanvasLayout::TransformPosition(Vector3 v) const {
	return mPosition
		+ mAxisX.xyz() * v.x
		+ mAxisY.xyz() * v.y
		+ mAxisZ * v.z;
}
Vector3 CanvasLayout::TransformPosition2D(Vector2 v) const {
	return mPosition
		+ mAxisX.xyz() * v.x
		+ mAxisY.xyz() * v.y;
}
Vector3 CanvasLayout::TransformPositionN(Vector3 v) const {
	return mPosition
		+ mAxisX.xyz() * (v.x * mAxisX.w)
		+ mAxisY.xyz() * (v.y * mAxisY.w)
		+ mAxisZ * v.z;
}
Vector3 CanvasLayout::TransformPosition2DN(Vector2 v) const {
	return mPosition
		+ mAxisX.xyz() * (v.x * mAxisX.w)
		+ mAxisY.xyz() * (v.y * mAxisY.w);
}
CanvasLayout CanvasLayout::MinMaxNormalized(float xmin, float ymin, float xmax, float ymax) const {
	return CanvasLayout{
		.mAxisX = Vector4(mAxisX.x, mAxisX.y, mAxisX.z, mAxisX.w * (xmax - xmin)),
		.mAxisY = Vector4(mAxisY.x, mAxisY.y, mAxisY.z, mAxisY.w * (ymax - ymin)),
		.mAxisZ = mAxisZ,
		.mPosition = TransformPosition2DN(Vector2(xmin, ymin))
	};
}
template<bool Horizontal> Vector4& GetAxis(CanvasLayout& layout) { return Horizontal ? layout.mAxisX : layout.mAxisY; }
template<bool Horizontal, bool Start, bool Normalized>
CanvasLayout Slice(CanvasLayout& layout, float size) {
	auto ret = layout;
	auto& retAxis = GetAxis<Horizontal>(ret);
	auto& layAxis = GetAxis<Horizontal>(layout);
	retAxis.w = Normalized ? retAxis.w * size : std::min(size, retAxis.w);
	layAxis.w -= retAxis.w;
	if (Start)
		layout.mPosition += layAxis.xyz() * retAxis.w;
	else
		ret.mPosition += retAxis.xyz() * layAxis.w;
	return ret;
}
CanvasLayout CanvasLayout::SliceTop(float height) {
	return Slice<false, true, false>(*this, height);
}
CanvasLayout CanvasLayout::SliceBottom(float height) {
	return Slice<false, false, false>(*this, height);
}
CanvasLayout CanvasLayout::SliceLeft(float width) {
	return Slice<true, true, false>(*this, width);
}
CanvasLayout CanvasLayout::SliceRight(float width) {
	return Slice<true, false, false>(*this, width);
}
CanvasLayout CanvasLayout::RotateN(float amount, Vector2 pivotN) const {
	auto sc = Vector2(std::sinf(amount), std::cosf(amount));
	auto axisX = mAxisX.xyz() * sc.y - mAxisY.xyz() * sc.x;
	auto axisY = mAxisX.xyz() * sc.x + mAxisY.xyz() * sc.y;
	auto pos = TransformPosition2DN(pivotN)
		- axisX * (pivotN.x * mAxisX.w)
		- axisY * (pivotN.y * mAxisY.w);
	return CanvasLayout{
		.mAxisX = Vector4(axisX, mAxisX.w),
		.mAxisY = Vector4(axisY, mAxisY.w),
		.mAxisZ = mAxisZ,
		.mPosition = pos,
	};
}
CanvasLayout CanvasLayout::MakeBox(Vector2 size) {
	return CanvasLayout{
		.mAxisX = Vector4(1, 0, 0, size.x),
		.mAxisY = Vector4(0, 1, 0, size.y),
		.mAxisZ = Vector3(0, 0, 1),
		.mPosition = Vector3::Zero,
		.mHash = 0,
	};
}


void CanvasTransform::Apply(const CanvasLayout& parent, CanvasLayout& layout) {
	Vector2 size = parent.GetSize();
	Vector2 invSize = 1.0f / size;
	Vector2 posMinN = AnchorMin() + OffsetMin() * invSize;
	Vector2 posMaxN = AnchorMax() + OffsetMax() * invSize;
	layout = parent.MinMaxNormalized(posMinN.x, posMinN.y, posMaxN.x, posMaxN.y);
}

CanvasTransform CanvasTransform::MakeDefault() {
	return CanvasTransform{
		.mAnchors = Vector4(0.0f, 0.0f, 1.0f, 1.0f),
		.mOffsets = Vector4(0.0f, 0.0f, 0.0f, 0.0f),
		.mScale = Vector3(1.0f, 1.0f, 1.0f),
		.mPivot = Vector2(0.5f, 0.5f),
		.mDepth = 0.0f,
	};
}
CanvasTransform CanvasTransform::MakeAnchored(Vector2 size, Vector2 anchor, Vector2 offset) {
	return CanvasTransform{
		.mAnchors = Vector4(anchor.x, anchor.y, anchor.x, anchor.y),
		.mOffsets = Vector4(
			-size.x * anchor.x + offset.x, -size.y * anchor.y + offset.y,
			size.x * (1.0f - anchor.x) + offset.x, size.y * (1.0f - anchor.y) + offset.y
		),
		.mScale = Vector3(1.0f, 1.0f, 1.0f),
		.mPivot = anchor,
		.mDepth = 0.0f,
	};
}
