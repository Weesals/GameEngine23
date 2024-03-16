using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine.Jobs;

namespace Weesals.Engine.Profiling {
    public struct ProfilerMarker {

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
                if (!tracyLocations.TryGetValue(name, out tracyLocation)) {
                    tracyLocation = Tracy.CreateLocation(name, name, 0);
                    tracyLocations.Add(name, tracyLocation);
                }
            }
            Name = enable ? name : null;
        }

        public Scope Auto() {
            return new Scope(this);
        }

        public readonly struct Scope : IDisposable {
            public readonly ProfilerMarker Marker;
            private readonly long beginTicks;
            private readonly nuint zone;
            [ThreadStatic] private static int depth = 0;
            public Scope(ProfilerMarker marker) {
                Marker = marker;
                beginTicks = stopwatch.ElapsedTicks;
                ++depth;
                zone = Tracy.StartScopedZone(Marker.tracyLocation);
            }
            public void Dispose() {
                Tracy.EndScopedZone(zone);
                --depth;
                if (Marker.Name == null) return;
                lock (snapshots) {
                    if (snapshots.Count > 5000) return;
                    snapshots.Add(new() {
                        Name = Marker.Name,
                        BeginTick = beginTicks,
                        TimerTicks = (uint)(stopwatch.ElapsedTicks - beginTicks),
                        ThreadName = JobScheduler.CurrentThreadName,
                        ThreadDepth = depth,
                    });
                }
            }
        }

    }
}
