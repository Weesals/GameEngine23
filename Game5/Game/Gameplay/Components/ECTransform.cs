using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Weesals.Utility;

namespace Game5.Game {
    public struct ECTransform {
        public Int2 Position;
        public short Altitude;
        public short Orientation;

        public ECTransform(Int2 pos, short altitude = 0, short orientation = 0) {
            Position = pos;
            Altitude = altitude;
            Orientation = orientation;
        }

        public Int3 GetPosition3() { return new Int3(Position.X, Altitude, Position.Y); }
        public Vector3 GetWorldPosition() { return SimulationWorld.SimulationToWorld(GetPosition3()); }

        public void SetFacing(Int2 delta) {
            Orientation = OrientationFromFacing(delta);
        }
        public Int2 GetFacing(int magnitude = 1024) {
            return FacingFromOrientation(Orientation, magnitude);
        }

        public Matrix4x4 AsMatrix() {
            var facing = (Vector2)GetFacing() / 1024f;
            var pos = SimulationWorld.SimulationToWorld(GetPosition3());
            var mat = Matrix4x4.CreateTranslation(pos);
            mat.M11 = facing.Y;
            mat.M13 = -facing.X;
            mat.M31 = facing.X;
            mat.M33 = facing.Y;
            return mat;
        }

        public static short OrientationFromFacing(Int2 facing) {
            var ang = FixedMath.Atan2(new Fixed16_16(facing.X), new Fixed16_16(facing.Y));
            var conv = (ushort.MaxValue / 2) / Fixed16_16.PI;
            ang *= conv;
            return (short)FixedMath.RoundToInt(ang);
        }
        public static Int2 FacingFromOrientation(short orientation, int magnitude = 1024) {
            var ang = new Fixed16_16((int)((long)Fixed16_16.TwoPI.RawValue * orientation / ushort.MaxValue));
            return new Int2(
                FixedMath.RoundToInt(FixedMath.Sin(ang) * magnitude),
                FixedMath.RoundToInt(FixedMath.Cos(ang) * magnitude)
            );
        }

        public override string ToString() {
            return $"Position<{Position}>";
        }
    }
}
