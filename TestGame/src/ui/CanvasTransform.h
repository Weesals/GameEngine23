#pragma once

#include <MathTypes.h>

struct CanvasLayout {
	// Axes are delta for anchor[0,1] (xyz) and anchor size (w)
	// mAxisZ is usually (0, 0, 1)
	Vector4 mAxisX;
	Vector4 mAxisY;
	Vector3 mAxisZ;
	Vector3 mPosition;
	int mRevision;
	Vector2 GetSize() const { return Vector2(mAxisX.w, mAxisY.w); }
	Vector3 TransformPosition(Vector3 v) const;
	// Normalized position (0 to 1) (faster)
	Vector3 TransformPositionN(Vector3 v) const;
};

struct CanvasTransform {
	Vector4 mAnchors;
	Vector4 mOffsets;
	Vector3 mScale;
	Vector2 mPivot;
	float mDepth;
	Vector2& AnchorMin() { return mAnchors.xy(); }
	Vector2& AnchorMax() { return mAnchors.zw(); }
	const Vector2& AnchorMin() const { return mAnchors.xy(); }
	const Vector2& AnchorMax() const { return mAnchors.zw(); }
	Vector2& OffsetMin() { return mOffsets.xy(); }
	Vector2& OffsetMax() { return mOffsets.zw(); }
	const Vector2& OffsetMin() const { return mOffsets.xy(); }
	const Vector2& OffsetMax() const { return mOffsets.zw(); }

	void Apply(const CanvasLayout& parent, CanvasLayout& layout);

	static CanvasTransform MakeDefault();
	static CanvasTransform MakeAnchored(Vector2 size, Vector2 anchor = Vector2(0.5f, 0.5f), Vector2 offset = Vector2(0.0f, 0.0f));
};
