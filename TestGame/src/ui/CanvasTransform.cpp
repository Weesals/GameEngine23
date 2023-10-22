#include "CanvasTransform.h"

Vector3 CanvasLayout::TransformPosition(Vector3 v) const {
	return mPosition
		+ mAxisX.xyz() * v.x / mAxisX.w
		+ mAxisY.xyz() * v.y / mAxisY.w
		+ mAxisZ * v.z;
}
Vector3 CanvasLayout::TransformPositionN(Vector3 v) const {
	return mPosition
		+ mAxisX.xyz() * v.x
		+ mAxisY.xyz() * v.y
		+ mAxisZ * v.z;
}

void CanvasTransform::Apply(const CanvasLayout& parent, CanvasLayout& layout) {
	Vector2 size(parent.mAxisX.w, parent.mAxisY.w);
	Vector2 invSize = 1.0f / size;
	Vector2 posMinN = AnchorMin() + OffsetMin() * invSize;
	Vector2 posMaxN = AnchorMax() + OffsetMax() * invSize;
	layout.mPosition = parent.mPosition +
		parent.mAxisX.xyz() * posMinN.x +
		parent.mAxisY.xyz() * posMinN.y;
	layout.mAxisX = parent.mAxisX * (posMaxN.x - posMinN.x);
	layout.mAxisY = parent.mAxisY * (posMaxN.y - posMinN.y);
	layout.mAxisZ = parent.mAxisZ;
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
