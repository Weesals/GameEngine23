using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;

namespace Weesals.UI {
    public struct CanvasLayout : IEquatable<CanvasLayout> {
        // Axes are delta for anchor[0,1] (xyz) and anchor size (w)
        // mAxisZ is usually (0, 0, 1)
        public Vector4 AxisX;
        public Vector4 AxisY;
        public Vector3 AxisZ;
        public Vector3 Position;

        public Vector2 GetSize() => new Vector2(AxisX.W, AxisY.W);
        public float GetSize(CanvasAxes axis) => axis switch { CanvasAxes.Horizontal => AxisX.W, CanvasAxes.Vertical => AxisY.W, _ => throw new(), };
        public void SetSize(Vector2 size) { AxisX.W = size.X; AxisY.W = size.Y; }
        public float GetWidth() => AxisX.W;
        public void SetWidth(float width) { AxisX.W = width; }
        public float GetHeight() => AxisY.W;
        public void SetHeight(float height) { AxisY.W = height; }

        public Vector3 TransformPosition(Vector3 v) {
            return Position
                + AxisX.toxyz() * v.X
                + AxisY.toxyz() * v.Y
                + AxisZ * v.Z;
        }
        public Vector3 TransformPosition2D(Vector2 v) {
            return Position
                + AxisX.toxyz() * v.X
                + AxisY.toxyz() * v.Y;
        }
        // Normalized position (0 to 1) (faster)
        public Vector3 TransformPositionN(Vector3 v) {
            return Position
                + AxisX.toxyz() * (v.X * AxisX.W)
                + AxisY.toxyz() * (v.Y * AxisY.W)
                + AxisZ * v.Z;
        }
        public Vector3 TransformPosition2DN(Vector2 v) {
            return Position
                + AxisX.toxyz() * (v.X * AxisX.W)
                + AxisY.toxyz() * (v.Y * AxisY.W);
        }
        public Vector3 InverseTransformPosition(Vector3 v) {
            v -= Position;
            return new Vector3(
                Vector3.Dot(AxisX.toxyz(), v) / AxisX.toxyz().LengthSquared(),
                Vector3.Dot(AxisY.toxyz(), v) / AxisY.toxyz().LengthSquared(),
                Vector3.Dot(AxisZ, v) / AxisZ.LengthSquared()
            );
        }
        public Vector2 InverseTransformPosition2D(Vector2 v) {
            v -= Position.toxy();
            return new Vector2(
                Vector2.Dot(AxisX.toxy(), v) / AxisX.toxyz().LengthSquared(),
                Vector2.Dot(AxisY.toxy(), v) / AxisY.toxyz().LengthSquared()
            );
        }
        public Vector2 InverseTransformPosition2DN(Vector2 v) {
            v -= Position.toxy();
            return new Vector2(
                Vector2.Dot(AxisX.toxy(), v) / (AxisX.toxyz().LengthSquared() * AxisX.W),
                Vector2.Dot(AxisY.toxy(), v) / (AxisY.toxyz().LengthSquared() * AxisY.W)
            );
        }
        public CanvasLayout MinMaxNormalized(float xmin, float ymin, float xmax, float ymax) {
	        return new CanvasLayout {
		        AxisX = new Vector4(AxisX.X, AxisX.Y, AxisX.Z, AxisX.W * (xmax - xmin)),
		        AxisY = new Vector4(AxisY.X, AxisY.Y, AxisY.Z, AxisY.W * (ymax - ymin)),
		        AxisZ = AxisZ,
		        Position = TransformPosition2DN(new Vector2(xmin, ymin))
	        };
        }
        // Remove and return a slice from the top of this area, with the given height
        public CanvasLayout SliceTop(float height) {
            var ret = this;
            Position += AxisY.toxyz() * height;
            AxisY.W -= height;
            ret.AxisY.W = height;
            return ret;
        }
        public CanvasLayout SliceBottom(float height) {
            var ret = this;
            AxisY.W -= height;
            ret.Position += AxisY.toxyz() * AxisY.W;
            ret.AxisY.W = height;
            return ret;
        }
        public CanvasLayout SliceLeft(float width) {
            var ret = this;
            Position += AxisX.toxyz() * width;
            AxisX.W -= width;
            ret.AxisX.W = width;
            return ret;
        }
        public CanvasLayout SliceRight(float width) {
            var ret = this;
            AxisX.W -= width;
            ret.Position += AxisX.toxyz() * AxisX.W;
            ret.AxisX.W = width;
            return ret;
        }
        public CanvasLayout RotateN(float amount, Vector2 pivotN) {
	        var sc = new Vector2(MathF.Sin(amount), MathF.Cos(amount));
            var axisX = AxisX.toxyz() * sc.Y - AxisY.toxyz() * sc.X;
            var axisY = AxisX.toxyz() * sc.X + AxisY.toxyz() * sc.Y;
            var pos = TransformPosition2DN(pivotN)
		        - axisX * (pivotN.X * AxisX.W)
		        - axisY * (pivotN.Y * AxisY.W);
	        return new CanvasLayout{
		        AxisX = new Vector4(axisX, AxisX.W),
		        AxisY = new Vector4(axisY, AxisY.W),
		        AxisZ = AxisZ,
		        Position = pos,
	        };
        }
        public CanvasLayout Scale(float scale) {
            var offset = (1f - scale) / 2.0f;
            return new CanvasLayout {
                AxisX = new Vector4(AxisX.toxyz() * scale, AxisX.W),
                AxisY = new Vector4(AxisY.toxyz() * scale, AxisY.W),
                AxisZ = AxisZ,
                Position = Position
                + AxisX.toxyz() * (offset * AxisX.W)
                + AxisY.toxyz() * (offset * AxisY.W),
            };
        }
        public CanvasLayout Inset(float amount) => Inset(amount, amount, amount, amount);
        public CanvasLayout Inset(float width, float height) => Inset(width, height, width, height);
        public CanvasLayout Inset(float left, float top, float right, float bottom) {
            return new CanvasLayout {
                AxisX = new Vector4(AxisX.toxyz(), AxisX.W - (left + right)),
                AxisY = new Vector4(AxisY.toxyz(), AxisY.W - (top + bottom)),
                AxisZ = AxisZ,
                Position = TransformPosition2D(new Vector2(left, top)),
            };
        }
        public static CanvasLayout MakeBox(Vector2 size) {
            return MakeBox(size, default);
        }
        public static CanvasLayout MakeBox(Vector2 size, Vector2 offset) {
	        return new CanvasLayout{
		        AxisX = new Vector4(1, 0, 0, size.X),
		        AxisY = new Vector4(0, 1, 0, size.Y),
		        AxisZ = new Vector3(0, 0, 1),
		        Position = new Vector3(offset.X, offset.Y, 0f),
	        };
        }

        public override bool Equals(object? obj) {
            return obj is CanvasLayout layout && Equals(layout);
        }
        public bool Equals(CanvasLayout other) {
            return AxisX.Equals(other.AxisX) &&
                   AxisY.Equals(other.AxisY) &&
                   AxisZ.Equals(other.AxisZ) &&
                   Position.Equals(other.Position);
        }
        public override int GetHashCode() {
            return HashCode.Combine(AxisX, AxisY, AxisZ, Position);
        }
    }

    public struct CanvasTransform : IEquatable<CanvasTransform> {
	    public Vector4 Anchors;
        public Vector4 Offsets;
        public Vector3 Scale;
        public Vector2 Pivot;
        public float Depth;
        public Vector2 AnchorMin { get => Anchors.toxy(); set => Anchors.toxy(value); }
        public Vector2 AnchorMax { get => Anchors.tozw(); set => Anchors.tozw(value); }
        public Vector2 OffsetMin { get => Offsets.toxy(); set => Offsets.toxy(value); }
        public Vector2 OffsetMax { get => Offsets.tozw(); set => Offsets.tozw(value); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Apply(in CanvasLayout parent, out CanvasLayout layout) {
            layout = parent;
            ApplyAnchorOffset(ref layout);
            if (Scale.X != 1f || Scale.Y != 1f || Scale.Z != 1f) {
                ApplyScale(ref layout);
            }
        }
        public void ApplyAnchorOffset(ref CanvasLayout layout) {
            layout.Position = layout.TransformPosition2D(AnchorMin * layout.GetSize() + OffsetMin);
            layout.AxisX.W = layout.AxisX.W * (AnchorMax.X - AnchorMin.X) + (OffsetMax.X - OffsetMin.X);
            layout.AxisY.W = layout.AxisY.W * (AnchorMax.Y - AnchorMin.Y) + (OffsetMax.Y - OffsetMin.Y);
        }
        public void ApplyScale(ref CanvasLayout layout) {
            layout.Position = layout.TransformPosition2D(layout.GetSize() * (new Vector2(0.5f, 0.5f) - 0.5f * new Vector2(Scale.X, Scale.Y)));
            layout.AxisX.toxyz(layout.AxisX.toxyz() * Scale.X);
            layout.AxisY.toxyz(layout.AxisY.toxyz() * Scale.Y);
        }

        public static CanvasTransform MakeDefault() {
	        return new CanvasTransform {
		        Anchors = new Vector4(0.0f, 0.0f, 1.0f, 1.0f),
		        Offsets = new Vector4(0.0f, 0.0f, 0.0f, 0.0f),
		        Scale = new Vector3(1.0f, 1.0f, 1.0f),
		        Pivot = new Vector2(0.5f, 0.5f),
		        Depth = 0.0f,
	        };
        }
        public static CanvasTransform MakeAnchored(Vector2 size) {
            return MakeAnchored(size, new Vector2(0.5f, 0.5f), new Vector2(0.0f, 0.0f));
        }
        public static CanvasTransform MakeAnchored(Vector2 size, Vector2 anchor, Vector2 offset = default) {
	        return new CanvasTransform {
		        Anchors = new Vector4(anchor.X, anchor.Y, anchor.X, anchor.Y),
		        Offsets = new Vector4(
			        -size.X * anchor.X + offset.X, -size.Y * anchor.Y + offset.Y,
			        size.X * (1.0f - anchor.X) + offset.X, size.Y * (1.0f - anchor.Y) + offset.Y
		        ),
		        Scale = new Vector3(1.0f, 1.0f, 1.0f),
		        Pivot = anchor,
		        Depth = 0.0f,
	        };
        }

        public CanvasTransform Inset(float px) {
            Inset(px, px, px, px);
            return this;
        }
        public CanvasTransform Inset(float horizontal, float vertical) {
            Inset(horizontal, vertical, horizontal, vertical);
            return this;
        }
        public CanvasTransform Inset(float left, float top, float right, float bot) {
            Offsets.X += left;
            Offsets.Y += top;
            Offsets.Z -= right;
            Offsets.W -= bot;
            return this;
        }
        public CanvasTransform WithOffsets(Vector2 offMin, Vector2 offMax) {
            Offsets = new Vector4(offMin.X, offMin.Y, offMax.X, offMax.Y);
            return this;
        }
        public CanvasTransform WithAnchors(Vector2 anchorMin, Vector2 anchorMax) {
            Anchors = new Vector4(anchorMin.X, anchorMin.Y, anchorMax.X, anchorMax.Y);
            return this;
        }
        public CanvasTransform WithOffsets(float xmin, float ymin, float xmax, float ymax) {
            Offsets = new Vector4(xmin, ymin, xmax, ymax);
            return this;
        }
        public CanvasTransform WithAnchors(float xmin, float ymin, float xmax, float ymax) {
            Anchors = new Vector4(xmin, ymin, xmax, ymax);
            return this;
        }
        public CanvasTransform WithPivot(float x, float y) {
            Pivot = new Vector2(x, y);
            return this;
        }

        public override bool Equals(object? obj) {
            return obj is CanvasTransform transform && Equals(transform);
        }
        public bool Equals(CanvasTransform other) {
            return Anchors.Equals(other.Anchors) &&
                   Offsets.Equals(other.Offsets) &&
                   Scale.Equals(other.Scale) &&
                   Pivot.Equals(other.Pivot) &&
                   Depth == other.Depth &&
                   AnchorMin.Equals(other.AnchorMin) &&
                   AnchorMax.Equals(other.AnchorMax) &&
                   OffsetMin.Equals(other.OffsetMin) &&
                   OffsetMax.Equals(other.OffsetMax);
        }
        public override int GetHashCode() {
            HashCode hash = new HashCode();
            hash.Add(Anchors);
            hash.Add(Offsets);
            hash.Add(Scale);
            hash.Add(Pivot);
            hash.Add(Depth);
            hash.Add(AnchorMin);
            hash.Add(AnchorMax);
            hash.Add(OffsetMin);
            hash.Add(OffsetMax);
            return hash.ToHashCode();
        }

        public static bool operator ==(CanvasTransform left, CanvasTransform right) { return left.Equals(right); }
        public static bool operator !=(CanvasTransform left, CanvasTransform right) { return !(left == right); }
    }
}
