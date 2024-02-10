using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;

namespace Game5.Game.Gameplay {

    public class BuildingType {

        public Int2 Size;
        public uint[] BuildMask;

        public RectF Bounds => new RectF(-(Vector2)Size / 2.0f, (Vector2)Size);

        public bool GetCell(Int2 pnt) {
            if (BuildMask == null || pnt.Y < 0 || pnt.Y >= BuildMask.Length) return true;
            return (BuildMask[pnt.Y] & (1 << pnt.X)) != 0;
        }
        public void SetCell(Int2 pnt, bool value) {
            if (!value) BuildMask[pnt.Y] &= ~(1u << pnt.X);
            else BuildMask[pnt.Y] |= (1u << pnt.X);
        }

    }

    public interface IBuildArea {
        public Matrix4x4 Transform { get; }

        bool CanBuildType(BuildingType type);
        LocationData GetNearestValidLocation(BuildingType type, Vector3 pos, int orientation, BuildAreaContext context);
        Placement.Cell GetCellAt(Int2 pnt);
    }
    public struct BuildAreaContext {
        public IBuildArea BuildArea;
    }
    public struct Placement : IEquatable<Placement> {
        public Int2 Position;
        public Int2 Size;
        public int Orientation;
        public bool IsValid => Size.X > 0;
        public RectI Rect => new RectI(Position, Size);
        public static Placement operator +(Placement p, Int2 o) {
            p.Position += o;
            return p;
        }
        public bool Equals(Placement other) {
            return Position.Equals(other.Position) && Size.Equals(other.Size) && Orientation == other.Orientation;
        }
        public static bool operator ==(Placement left, Placement right) { return left.Equals(right); }
        public static bool operator !=(Placement left, Placement right) { return !(left == right); }
        public override bool Equals(object? other) { return other is Placement placement && Equals(placement); }
        public override int GetHashCode() { return HashCode.Combine(Position, Size, Orientation); }
        public static readonly Placement Invalid = new Placement() { Size = -1, };

        public struct Cell {
            public sbyte State;
            public bool IsEmpty { get { return State == 0; } }
            public bool IsOccupied { get { return State > 0; } }
            public bool IsValid { get { return State >= 0; } }
            public static readonly Cell Blocked = new Cell() { State = 1, };
            public static readonly Cell Empty = new Cell() { State = 0, };
            public static readonly Cell Invalid = new Cell() { State = -1, };
            public override string ToString() {
                return IsEmpty ? "Empty" : IsOccupied ? "Occupied" : "!Valid";
            }
        }

        public static Vector3 GridToLocal(Int2 pnt) {
            return new Vector3(pnt.X + 0.5f, 0, pnt.Y + 0.5f);
        }
        public static Int2 LocalToGrid(Vector2 pos) {
            return new Int2((int)MathF.Floor(pos.X), (int)MathF.Floor(pos.Y));
        }
        public static Int2 LocalToGrid(Vector3 pos) {
            return new Int2((int)MathF.Floor(pos.X), (int)MathF.Floor(pos.Z));
        }
        public static RectI LocalToGrid(RectF bounds) {
            int xMin = (int)MathF.Floor(bounds.Min.X);
            int yMin = (int)MathF.Floor(bounds.Min.Y);
            int xMax = (int)MathF.Floor(bounds.Max.X);
            int yMax = (int)MathF.Floor(bounds.Max.Y);
            return new RectI(xMin, yMin, xMax - xMin + 1, yMax - yMin + 1);
        }

        public static Int2 ApplyRotation(Int2 lpnt, Int2 size, int orientation) {
            switch (orientation) {
                case 1: return new Int2(size.Y - 1 - lpnt.Y, lpnt.X);
                case 2: return new Int2(size.X - 1 - lpnt.X, size.Y - 1 - lpnt.Y);
                case 3: return new Int2(lpnt.Y, size.X - 1 - lpnt.X);
            }
            return lpnt;
        }

        public static Vector2 GetForward(int rotation) {
            return rotation == 0 ? new Vector2(0, 1) : rotation == 2 ? new Vector2(0, -1) :
                rotation == 1 ? new Vector2(1, 0) : new Vector2(-1, 0);
        }
        public static int GetOrientation(Vector2 fwd) {
            return MathF.Abs(fwd.X) > MathF.Abs(fwd.Y)
                ? fwd.X < 0f ? 3 : 1
                : fwd.Y < 0f ? 2 : 0;
        }

        public static RectI GetBuildingRect(BuildingType type, Vector2 ctr, int rotation, out Vector2 offset) {
            return GetBuildingRect(type.Bounds, ctr, rotation, out offset);
        }
        public static RectI GetBuildingRect(RectF localBounds, Vector2 ctr, int rotation, out Vector2 offset) {
            var fwd = GetForward(rotation);
            var rgt = new Vector2(fwd.Y, -fwd.X);

            var boundsF = localBounds;
            var boundsFSize = Vector2.Max(Vector2.Abs(boundsF.Size.X * rgt), Vector2.Abs(boundsF.Size.Y * fwd));
            var boundsFCtr = boundsF.Centre;
            boundsF = new RectF((Vector2)boundsFCtr - boundsFSize * 0.5f, boundsFSize);

            // Is 0.5 if size is even, 0 if odd
            offset = VectorExt.Mod(VectorExt.Round(boundsF.Size - Vector2.One), 2) * 0.5f;
            var boundsIMin = Int2.CeilToInt((Vector2)boundsF.Min + offset);
            var boundsIMax = Int2.CeilToInt((Vector2)boundsF.Max + offset);
            var ctrI = Int2.RoundToInt(ctr - offset - new Vector2(0.5f));
            return new RectI(boundsIMin + ctrI, boundsIMax - boundsIMin);
        }
        public static RectF GetBuildingRectF(RectF localBounds, int orientation) {
            var axis = (orientation % 2) == 0 ? new Vector2(0f, 1f) : new Vector2(1f, 0f);
            var boundsFSize = new Vector2(Vector2.Dot(localBounds.Size, axis), Vector2.Dot(localBounds.Size, axis.YX()));
            return new RectF((Vector2)localBounds.Centre - boundsFSize / 2f, boundsFSize);
        }
        public static Placement GetBuildingPlacement(BuildingType type, Vector2 ctr, int orientation, out Vector2 offset) {
            return GetBuildingPlacement(type.Bounds, ctr, orientation, out offset);
        }
        public static Placement GetBuildingPlacement(RectF localBounds, Vector2 ctr, int orientation, out Vector2 offset) {
            var boundsF = GetBuildingRectF(localBounds, orientation);
            // Is 0.5 if size is even, 0 if odd
            offset = VectorExt.Mod(VectorExt.Round(boundsF.Size - Vector2.One), 2) * new Vector2(0.5f);
            return new Placement() {
                Position = Int2.CeilToInt(ctr + localBounds.Min),
                Size = Int2.RoundToInt(boundsF.Size),
                Orientation = orientation,
            };
        }
        public static Vector3 GetPlacementPosition(BuildingType type, Placement placement) {
            var boundsF = GetBuildingRectF(type.Bounds, placement.Orientation);
            var lpos = GridToLocal(placement.Position).toxz() - boundsF.Min - new Vector2(0.5f);
            return new Vector3(lpos.X, 0f, lpos.Y);
        }
        public static Quaternion GetPlacementRotation(int orientation) {
            return MathExt.QuaternionFromDirection(
                GetForward(orientation).AppendY(0f),
                Vector3.UnitY
            );
        }

        public static LocationData GetNearestValidLocation(IBuildArea area, BuildingType type, Int2 pnt, int orientation, BuildAreaContext context = default) {
            var iterator = new GridSpiralIterator(pnt);
            var placement = GetBuildingPlacement(type, Vector2.Zero, orientation, out _);
            var rect = placement.Rect;
            LocationData location = LocationData.Invalid;
            for (; iterator.MoveNext() && iterator.Distance < 3;) {
                bool valid = true;
                var offset = iterator.Current + rect.Min;
                for (int y = 0; y < type.Size.Y; y++) {
                    for (int x = 0; x < type.Size.X; x++) {
                        if (!type.GetCell(new Int2(x, y))) continue;
                        if (area.GetCellAt(offset + new Int2(x, y)).IsEmpty) continue;
                        valid = false; y = 100000; break;
                    }
                }
                var dst2 = iterator.GetDistanceSq(pnt);
                if (!valid || dst2 >= location.Distance2) continue;
                location = new LocationData() {
                    Placement = placement + iterator.Current,
                    Distance2 = dst2,
                };
            }
            return location;
        }
    }
    public struct LocationData {
        public bool IsValid { get { return Distance2 != float.MaxValue; } }
        public Placement Placement;
        public float Distance2;
        public static readonly LocationData Invalid = new LocationData() { Distance2 = float.MaxValue, };
    }

    public class MapBuildArea : IBuildArea {

        public Matrix4x4 Transform => Matrix4x4.Identity;

        public bool CanBuildType(BuildingType type) {
            throw new NotImplementedException();
        }
        public Placement.Cell GetCellAt(Int2 pnt) {
            throw new NotImplementedException();
        }
        public LocationData GetNearestValidLocation(BuildingType type, Vector3 pos, int orientation, BuildAreaContext context) {
            return Placement.GetNearestValidLocation(this, type, Int3.FloorToInt(pos).XZ, orientation, context);
        }

    }
}
