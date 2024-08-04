using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using Weesals.Engine;

/// <summary>
/// Iterates outward from the specified location in a square spiral pattern 
/// </summary>
public struct GridSpiralIterator : IEnumerator<Int2> {
    public Int2 Centre;
    public int Iterator;
    public ushort Leg;
    public ushort LegOffset;
    public Int2 Current { get; private set; }
    public int Distance { get { return Leg / 4 + 1; } }
    object IEnumerator.Current { get { return Current; } }
    public GridSpiralIterator(Int2 ctr) { Centre = ctr; Iterator = -1; Leg = 0; LegOffset = 0; Current = ctr; }
    // Cells touching this corner will be iterated first
    public void SetIterationCorner(Int2 corner) {
        LegOffset = (ushort)(Math.Abs(corner.X) < Math.Abs(corner.Y) ? corner.X < 0 ? 2 : 0 : corner.Y < 0 ? 3 : 1);
        var dir = (Math.Abs(corner.X) >= Math.Abs(corner.Y)) == ((corner.X ^ corner.Y) >= 0);
        if (dir) { LegOffset |= 0x00f0; LegOffset += 1; }
    }
    public float GetDistanceSq(Vector2 from) {
        from -= (Vector2)Current + new Vector2(0.5f);
        from = Vector2.Abs(from);
        from -= new Vector2(0.5f);
        from = Vector2.Max(from, default);
        return Vector2.Dot(from, from);
    }
    public bool MoveNext() {
        if (Iterator == -1) { Iterator = 0; return true; }
        var current = Current;
        switch (((Leg ^ (LegOffset >> 4)) + LegOffset) & 0x03) {
            case 0: current.X++; break;
            case 1: current.Y++; break;
            case 2: current.X--; break;
            case 3: current.Y--; break;
        }
        Current = current;
        ++Iterator;
        if (Iterator > Leg / 2) { ++Leg; Iterator = 0; }
        return true;
    }
    public void Reset() {
        Current = Centre;
        Iterator = 0;
        Leg = 0;
    }
    public void Dispose() { }
}

/// <summary>
/// Iterates through a grid based on the initial ray From+Delta
/// </summary>
struct GridRayIterator {
    public Vector2 From;
    public Vector2 Delta;
    public Int2 Position;
    public float Interpolation;
    public GridRayIterator(Vector2 from, Vector2 to) {
        From = from;
        Delta = to - from;
        Position = Int2.FloorToInt(From);
        Interpolation = -1f;
    }
    public bool Next() {
        if (Interpolation == -1f) { Interpolation = 0f; return true; }
        Vector2 end = Position;
        if (Delta.X > 0f) end.X += 1f;
        if (Delta.Y > 0f) end.Y += 1f;
        var pdelta = end - From;
        pdelta /= Delta;
        if (pdelta.X < pdelta.Y) {
            Position.X += Delta.X < 0f ? -1 : 1;
            Interpolation = pdelta.X;
        } else {
            Position.Y += Delta.Y < 0f ? -1 : 1;
            Interpolation = pdelta.Y;
        }
        return Interpolation < 1f;
    }
}

// Iterates a ray through a grid, but with a given thickness (axial)
public struct GridThickRayIterator : IEnumerator<Int2> {
    public Int2 From;
    public Int2 Direction;  // Magnitude is unimportant
    public int Thickness;
    public int GridSize;
    public bool IsSwizzled;
    public bool IsNegated;
    public Int2 Step;
    public int ExtentEnd;
    public Int2 Current => GetOffset();
    object IEnumerator.Current => Current;
    public bool IsEnded => GetIsEnded();
    public GridThickRayIterator(Int2 from, Int2 direction, int thickness, int gridSize = 1) {
        From = from;
        Direction = direction;
        Thickness = thickness;
        GridSize = gridSize;
        IsSwizzled = Math.Abs(direction.X) < Math.Abs(direction.Y);
        IsNegated = (IsSwizzled ? direction.Y : direction.X) < 0;
        var sfrom = IsSwizzled ? From.YX : From;
        if (IsNegated) sfrom.X *= -1;
        Step = new Int2(FloorDiv(sfrom.X, GridSize), 0);
        ExtentEnd = int.MinValue;
    }
    public void Dispose() { }
    public void Reset() { }
    public Int2 GetOffset() { var step = Step; if (IsNegated) step.X *= -1; return IsSwizzled ? step.YX : step; }
    private bool GetIsEnded() {
        var sfrom = IsSwizzled ? From.YX : From;
        var sdir = IsSwizzled ? Direction.YX : Direction;
        var sstep = Step.X * GridSize;
        var send = sfrom.X + sdir.X;
        if (IsNegated) send = -send;
        return sstep > send;
    }
    public Int2 GetExtents() {
        var sfrom = IsSwizzled ? From.YX : From;
        var sdir = IsSwizzled ? Direction.YX : Direction;
        if (sdir.X == 0) return new Int2(sfrom.Y, sfrom.Y);
        if (IsNegated) { sfrom.X *= -1; sdir.X *= -1; }
        int x0 = Step.X, x1 = x0 + 1;
        var y0 = (sfrom.Y + FloorDiv(sdir.Y * (x0 * GridSize - sfrom.X), sdir.X));
        var y1 = (sfrom.Y + FloorDiv(sdir.Y * (x1 * GridSize - sfrom.X), sdir.X));
        if (y1 < y0) { var t = y0; y0 = y1; y1 = t; }
        if (sdir.Y > 0) y0 = Math.Max(y0, sfrom.Y);
        else y1 = Math.Min(y1, sfrom.Y);
        y0 -= Thickness; y1 += Thickness;
        return new Int2(FloorDiv(y0, GridSize), FloorDiv(y1, GridSize));
    }
    public bool MoveNext() {
        if (++Step.Y <= ExtentEnd) return true;
        if (ExtentEnd != int.MinValue) Step.X++;
        var extents = GetExtents();
        ExtentEnd = extents.Y;
        Step.Y = extents.X;
        return !IsEnded;
    }
    private static int RoundDiv(int value, int div) {
        return (value + (value < 0 ? -div / 2 : div / 2)) / div;
    }
    private static int FloorDiv(int value, int div) {
        return (value + (value < 0 ? -(div - 1) : 0)) / div;
    }
    public GridThickRayIterator GetEnumerator() => this;
}
