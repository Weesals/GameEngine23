using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.ECS;
using Weesals.Engine;

namespace Game5.Game {
    public class SimulationWorld {

        public World World;
        public TimeSystem TimeSystem;

        // Scale used to convert from simulation space to world space
        // TODO: Move this outside Simulation. Nothing inside simulation
        // should know about the scale.
        public const float WorldScale = 1f / InvWorldScale;
        public const float InvWorldScale = 1024f;
        public const int AltitudeGranularity = 1024;

        // Convert from simulation space to world(rendering) space
        public static Vector3 SimulationToWorld(Int2 vec) {
            return new Vector3(vec.X, 0f, vec.Y) * WorldScale;
        }
        public static Vector3 SimulationToWorld(Int3 vec) {
            Debug.Assert(AltitudeGranularity == InvWorldScale);
            return (Vector3)vec * WorldScale;
        }
        public static Int3 WorldToSimulation(Vector3 pos) {
            return (Int3)(pos * InvWorldScale + new Vector3(0.5f));
        }
        public static Int3 WorldToSimulationVector(Vector3 direction, int magnitude = 1000) {
            return (Int3)(direction * magnitude);
        }

        public static float AltitudeToWorld(int altitude) {
            return altitude / (float)AltitudeGranularity;
        }

        public static Int2 SimulationToLandscape(Int2 pnt) {
            return pnt >> 10;
        }
        public static Int2 LandscapeToSimulation(Int2 pnt) {
            return pnt << 10;
        }

    }
}
