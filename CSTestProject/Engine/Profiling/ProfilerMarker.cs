using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine.Jobs;

namespace Weesals.Engine.Profiling {
    public struct ProfilerMarker {

        public const bool EnableTracy = false;

        public struct Snapshot {
            public string Name;
            public string ThreadName;
            public long BeginTick;
            public uint TimerTicks;
            public int ThreadDepth;
            public long EndTick => BeginTick + TimerTicks;
            public override string ToString() { return $"{TimeSpan.FromTicks(TimerTicks).TotalMilliseconds:0.00}ms - {Name}  @{ThreadName}"; }
        }

        private static List<Snapshot> snapshots = new();
        private static Stopwatch stopwatch = new();

        public readonly string? Name;
        private readonly nuint tracyLocation;
        public static List<Snapshot> AllSnapshots => snapshots;
        private static Dictionary<string, nuint> tracyLocations = new();

        static ProfilerMarker() {
            stopwatch.Start();
        }

        public ProfilerMarker(string name, bool enable = true) {
            lock (tracyLocations) {
                if (EnableTracy && !tracyLocations.TryGetValue(name, out tracyLocation)) {
                    tracyLocation = Tracy.CreateLocation(name, name, 0);
                    tracyLocations.Add(name, tracyLocation);
                }
            }
            Name = enable ? name : null;
        }

        public Scope Auto() { return new Scope(this, Color.Clear); }
        public Scope Auto(Color color) {
            return new Scope(this, color);
        }

        public readonly struct Scope : IDisposable {
            public readonly ProfilerMarker Marker;
            private readonly nuint zone;
            public Scope(ProfilerMarker marker) : this(marker, Color.Clear) { }
            public Scope(ProfilerMarker marker, Color color) {
                Marker = marker;
                if (EnableTracy) {
                    zone = Tracy.StartScopedZone(Marker.tracyLocation);
                    if (color.A != 0) {
                        Tracy.ZoneColor(zone, color.Packed);
                    }
                }
            }
            public Scope WithText(string text) {
                Tracy.ZoneText(zone, Tracy.CreateString(text));
                return this;
            }
            public Scope WithValue(ulong value) {
                Tracy.ZoneValue(zone, value);
                return this;
            }
            public void Dispose() {
                if (EnableTracy) Tracy.EndScopedZone(zone);
            }
        }

        public static void SetValue(string name, int value) {
            if (EnableTracy) Tracy.TracyPlot(Tracy.CreateString(name), value);
        }

    }
}
