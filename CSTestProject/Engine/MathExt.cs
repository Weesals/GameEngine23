using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Weesals.Engine {

    public static class Vector4Ext {
        public unsafe static Vector2 toxy(this Vector3 v) {
            return *(Vector2*)&v.X;
        }
        public unsafe static Vector2 toxz(this Vector3 v) {
            return new Vector2(v.X, v.Z);
        }
        public unsafe static Vector2 toyz(this Vector3 v) {
            return *(Vector2*)&v.Y;
        }
        public unsafe static Vector3 toxzy(this Vector3 v) {
            return new Vector3(v.X, v.Z, v.Y);
        }
        public unsafe static Vector2 toxy(this Vector4 v) {
            return *(Vector2*)&v.X;
        }
        public unsafe static Vector2 toyz(this Vector4 v) {
            return *(Vector2*)&v.Y;
        }
        public unsafe static Vector2 tozw(this Vector4 v) {
            return *(Vector2*)&v.Z;
        }
        public unsafe static Vector3 toxyz(this Vector4 v) {
            return *(Vector3*)&v.X;
        }

        public unsafe static void toxy(this ref Vector3 v, Vector2 set) {
            fixed (Vector3* w = &v) *(Vector2*)w = set;
        }
        public unsafe static void toyz(this ref Vector3 v, Vector2 set) {
            fixed (Vector3* w = &v) *(Vector2*)&w->Y = set;
        }
        public unsafe static void toxy(this ref Vector4 v, Vector2 set) {
            fixed (Vector4* w = &v) *(Vector2*)&w->X = set;
        }
        public unsafe static void toyz(this ref Vector4 v, Vector2 set) {
            fixed (Vector4* w = &v) *(Vector2*)&w->Y = set;
        }
        public unsafe static void tozw(this ref Vector4 v, Vector2 set) {
            fixed (Vector4* w = &v) *(Vector2*)&w->Z = set;
        }
        public unsafe static void toxyz(this ref Vector4 v, Vector3 set) {
            fixed (Vector4* w = &v) *(Vector3*)&w->X = set;
        }

        public static Vector4 toxzyw(this Vector4 v) {
            return new Vector4(v.X, v.Z, v.Y, v.W);
        }
        public static Vector4 tozywx(this Vector4 v) {
            return new Vector4(v.Z, v.Y, v.W, v.X);
        }
        public static Vector4 toyzwx(this Vector4 v) {
            return new Vector4(v.Y, v.Z, v.W, v.X);
        }
        public static Vector4 toxyzw(this Vector4 v) {
            return new Vector4(v.X, v.Y, v.Z, v.W);
        }
        public static float cmin(this Vector2 v) {
            return MathF.Min(v.X, v.Y);
        }
        public static float cmin(this Vector4 v) {
            return MathF.Min(MathF.Min(v.X, v.Y), MathF.Min(v.Z, v.W));
        }

        public static Vector3 AppendY(this Vector2 v, float y = 0.0f) {
            return new Vector3(v.X, y, v.Y);
        }
        public static Vector3 AppendZ(this Vector2 v, float z = 0.0f) {
            return new Vector3(v.X, v.Y, z);
        }
    }

    public struct Int2 : IEquatable<Int2> {
	    public int X, Y;
	    public Int2(int v) : this(v, v) { }
        public Int2(int _x, int _y) { X = _x; Y = _y; }
	    public Int2(Vector2 o) { X = ((int)o.X); Y = ((int)o.Y); }
        public bool Equals(Int2 o) { return X == o.X && Y == o.Y; }
        public static Int2 operator +(Int2 o1, Int2 o2) { return new Int2(o1.X + o2.X, o1.Y + o2.Y); }
	    public static Int2 operator -(Int2 o1, Int2 o2) { return new Int2(o1.X - o2.X, o1.Y - o2.Y); }
	    public static Int2 operator *(Int2 o1, Int2 o2) { return new Int2(o1.X * o2.X, o1.Y * o2.Y); }
	    public static Int2 operator /(Int2 o1, Int2 o2) { return new Int2(o1.X / o2.X, o1.Y / o2.Y); }
	    public static Int2 operator +(Int2 o1, int o) { return new Int2(o1.X + o, o1.Y + o); }
	    public static Int2 operator -(Int2 o1, int o) { return new Int2(o1.X - o, o1.Y - o); }
	    public static Int2 operator *(Int2 o1, int o) { return new Int2(o1.X * o, o1.Y * o); }
	    public static Int2 operator /(Int2 o1, int o) { return new Int2(o1.X / o, o1.Y / o); }
	    public static bool operator ==(Int2 o1, Int2 o2) { return o1.X == o2.X && o1.Y == o2.Y; }
	    public static bool operator !=(Int2 o1, Int2 o2) { return o1.X != o2.X || o1.Y != o2.Y; }
        
	    public static Int2 Min(Int2 v1, Int2 v2) { return new Int2(Math.Min(v1.X, v2.X), Math.Min(v1.Y, v2.Y)); }
	    public static Int2 Max(Int2 v1, Int2 v2) { return new Int2(Math.Max(v1.X, v2.X), Math.Max(v1.Y, v2.Y)); }
        public static Int2 Clamp(Int2 v, Int2 min, Int2 max) { return new Int2(Math.Clamp(v.X, min.X, max.X), Math.Clamp(v.Y, min.Y, max.Y)); }
        public static int Dot(Int2 v1, Int2 v2) { return v1.X * v2.X + v1.Y * v2.Y; }
        public static int CSum(Int2 v) { return v.X + v.X; }
        public static int CMul(Int2 v) { return v.X * v.Y; }

        public static Int2 FloorToInt(Vector2 v) { return new Int2((int)MathF.Floor(v.X), (int)MathF.Floor(v.Y)); }
	    public static Int2 CeilToInt(Vector2 v) { return new Int2((int)MathF.Ceiling(v.X), (int)MathF.Ceiling(v.Y)); }

        public static implicit operator Int2(Vector2 v) { return new Int2(v); }
        public static implicit operator Int2(int v) { return new Int2(v, v); }
        public static implicit operator Vector2(Int2 v) { return new Vector2((float)v.X, (float)v.Y); }
        public override bool Equals([NotNullWhen(true)] object? obj) { return obj is Int2 i2 && i2 == this; }
        public override int GetHashCode() { return (X * 887) + Y; }
        public override string ToString() { return "<" + X + "," + Y + ">"; }
        public static readonly Int2 Zero = new Int2(0);
        public static readonly Int2 One = new Int2(1);
    }
    public struct Int4 : IEquatable<Int4> {
        public int X, Y, Z, W;
	    public Int4(int v) : this(v, v, v, v) { }
	    public Int4(int _x, int _y, int _z, int _w) { X = _x; Y = _y; Z = _z; W = _w; }
	    public Int4(Vector4 o) : this((int)o.X, (int)o.Y, (int)o.Z, (int)o.W) { }
        public bool Equals(Int4 o) { return X == o.X && Y == o.Y && Z == o.Z && W == o.W; }
        public static Int4 operator +(Int4 v, Int4 o) { return new Int4(v.X + o.X, v.Y + o.Y, v.Z + o.Z, v.W + o.W); }
	    public static Int4 operator -(Int4 v, Int4 o) { return new Int4(v.X - o.X, v.Y - o.Y, v.Z - o.Z, v.W - o.W); }
	    public static Int4 operator *(Int4 v, Int4 o) { return new Int4(v.X * o.X, v.Y * o.Y, v.Z * o.Z, v.W * o.W); }
	    public static Int4 operator /(Int4 v, Int4 o) { return new Int4(v.X / o.X, v.Y / o.Y, v.Z / o.Z, v.W / o.W); }
	    public static Int4 operator +(Int4 v, int o) { return new Int4(v.X + o, v.Y + o, v.Z + o, v.W + o); }
	    public static Int4 operator -(Int4 v, int o) { return new Int4(v.X - o, v.Y - o, v.Z - o, v.W - o); }
	    public static Int4 operator *(Int4 v, int o) { return new Int4(v.X * o, v.Y * o, v.Z * o, v.W * o); }
	    public static Int4 operator /(Int4 v, int o) { return new Int4(v.X / o, v.Y / o, v.Z / o, v.W / o); }
        
	    public static Int4 Min(Int4 v1, Int4 v2) { return new Int4(Math.Min(v1.X, v2.X), Math.Min(v1.Y, v2.Y), Math.Min(v1.Z, v2.Z), Math.Min(v1.W, v2.W)); }
	    public static Int4 Max(Int4 v1, Int4 v2) { return new Int4(Math.Max(v1.X, v2.X), Math.Max(v1.Y, v2.Y), Math.Max(v1.Z, v2.Z), Math.Max(v1.W, v2.W)); }
        public static Int4 Clamp(Int4 v, Int4 min, Int4 max) {
            return new Int4(Math.Clamp(v.X, min.X, max.X), Math.Clamp(v.Y, min.Y, max.Y), Math.Clamp(v.Z, min.Z, max.Z), Math.Clamp(v.W, min.W, max.W));
        }

        public static implicit operator Int4(int v) { return new Int4(v, v, v, v); }
        public static implicit operator Vector4(Int4 v) { return new Vector4((float)v.X, (float)v.Y, (float)v.Z, (float)v.W); }
    }

    public struct Short2 : IEquatable<Short2> {
        public short X, Y;
        public Short2(short x, short y) { X = x; Y = y; }
        public Short2(Vector2 v) { X = (short)v.X; Y = (short)v.Y; }
        public Vector2 ToVector2() { return new Vector2(X, Y); }
        public bool Equals(Short2 o) { return X == o.X && Y == o.Y; }
        public override string ToString() { return $"<{X}, {Y}>"; }
    }
    public struct UShort2 : IEquatable<UShort2> {
        public ushort X, Y;
        public UShort2(ushort x, ushort y) { X = x; Y = y; }
        public UShort2(Vector2 v) { X = (ushort)v.X; Y = (ushort)v.Y; }
        public Vector2 ToVector2() { return new Vector2(X, Y); }
        public bool Equals(UShort2 o) { return X == o.X && Y == o.Y; }
        public override string ToString() { return $"<{X}, {Y}>"; }
    }

    public struct Color : IEquatable<Color> {
        public byte R;
        public byte G;
        public byte B;
        public byte A;
        public unsafe uint Packed {
            get { fixed (byte* d = &R) return *(uint*)d; }
            set { fixed (byte* d = &R) *(uint*)d = value; }
        }
        public Color(uint packed) { Packed = packed; }
        public Color(Vector4 v) {
            R = (byte)Math.Clamp(255.0f * v.X, 0.0f, 1.0f);
            G = (byte)Math.Clamp(255.0f * v.Y, 0.0f, 1.0f);
            B = (byte)Math.Clamp(255.0f * v.Z, 0.0f, 1.0f);
            A = (byte)Math.Clamp(255.0f * v.W, 0.0f, 1.0f);
        }
        public Color(Vector3 v) {
            R = (byte)Math.Clamp(255.0f * v.X, 0.0f, 1.0f);
            G = (byte)Math.Clamp(255.0f * v.Y, 0.0f, 1.0f);
            B = (byte)Math.Clamp(255.0f * v.Z, 0.0f, 1.0f);
            A = 255;
        }
        public static implicit operator Vector4(Color c) {
            return new Vector4(c.R, c.G, c.B, c.A) * (1.0f / 255.0f);
        }
        public static implicit operator Vector3(Color c) {
            return new Vector3(c.R, c.G, c.B) * (1.0f / 255.0f);
        }
        public static readonly Color White = new Color(0xffffffff);
        public static readonly Color Black = new Color(0xff000000);
        public static readonly Color Clear = new Color(0x00000000);

        public static bool operator == (Color c1, Color c2) { return c1.Equals(c2); }
        public static bool operator !=(Color c1, Color c2) { return !c1.Equals(c2); }
        public bool Equals(Color other) { return Packed == other.Packed; }
        public override bool Equals([NotNullWhen(true)] object? obj) { return obj is Color c && c == this; }
        public override int GetHashCode() { return (int)Packed; }
    }

    public struct RectF {
        public float X, Y, Width, Height;
        public Vector2 Min => new Vector2(X, Y);
        public Vector2 Max => new Vector2(X + Width, Y + Height);
        public Vector2 Size => new Vector2(Width, Height);
        public Vector2 Centre => new Vector2(X + Width / 2.0f, Y + Height / 2.0f);
        public Vector2 TopLeft => new Vector2(X, Y);
        public Vector2 TopRight => new Vector2(X + Width, Y);
        public Vector2 BottomLeft => new Vector2(X, Y + Height);
        public Vector2 BottomRight => new Vector2(X + Width, Y + Height);
        public float Top => Y;
        public float Left => X;
        public float Bottom => Y + Height;
        public float Right => X + Width;
        public RectF(float x, float y, float width, float height) { X = x; Y = y; Width = width; Height = height; }
        public static RectF operator +(RectF r, Vector2 o) { r.X += o.X; r.Y += o.Y; return r; }
        public static RectF operator -(RectF r, Vector2 o) { r.X -= o.X; r.Y -= o.Y; return r; }
        public static RectF operator *(RectF r, Vector2 o) { r.X *= o.X; r.Y *= o.Y; r.Width *= o.X; r.Height *= o.Y; return r; }
        public static RectF operator /(RectF r, Vector2 o) { r.X /= o.X; r.Y /= o.Y; r.Width /= o.X; r.Height /= o.Y; return r; }
        public Vector2 Unlerp(Vector2 pos) { return (pos - Min) / Size; }
        public static implicit operator RectI(RectF r) { return new RectI((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height); }
    }
    public struct RectI {
        public int X, Y, Width, Height;
        public Int2 Min => new Int2(X, Y);
        public Int2 Max => new Int2(X + Width, Y + Height);
        public Int2 Size => new Int2(Width, Height);
        public Int2 Centre => new Int2(X + Width / 2, Y + Height / 2);
        public Int2 TopLeft => new Int2(X, Y);
        public Int2 TopRight => new Int2(X + Width, Y);
        public Int2 BottomLeft => new Int2(X, Y + Height);
        public Int2 BottomRight => new Int2(X + Width, Y + Height);
        public int Top => Y;
        public int Left => X;
        public int Bottom => Y + Height;
        public int Right => X + Width;
        public RectI(int x, int y, int width, int height) { X = x; Y = y; Width = width; Height = height; }
        public static RectI operator +(RectI r, Int2 o) { r.X += o.X; r.Y += o.Y; return r; }
        public static RectI operator -(RectI r, Int2 o) { r.X -= o.X; r.Y -= o.Y; return r; }
        public static RectI operator *(RectI r, Int2 o) { r.X *= o.X; r.Y *= o.Y; r.Width *= o.X; r.Height *= o.Y; return r; }
        public static RectI operator /(RectI r, Int2 o) { r.X /= o.X; r.Y /= o.Y; r.Width /= o.X; r.Height /= o.Y; return r; }
        public static implicit operator RectF(RectI r) { return new RectF(r.X, r.Y, r.Width, r.Height); }
    }

}
