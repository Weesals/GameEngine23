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
            public long EndTick;
            public long TimerTicks => EndTick - BeginTick;
            public override string ToString() { return $"{TimeSpan.FromTicks(TimerTicks).TotalMilliseconds:0.00}ms - {Name}  @{ThreadName}"; }
        }

        private static List<Snapshot> snapshots = new();
        private static Stopwatch stopwatch = new();

        public readonly string? Name;
        public static List<Snapshot> AllSnapshots => snapshots;

        static ProfilerMarker() {
            stopwatch.Start();
        }

        public ProfilerMarker(string name, bool enable = true) {
            Name = enable ? name : null;
        }

        public Scope Auto() {
            return new Scope(this);
        }

        public readonly struct Scope : IDisposable {
            public readonly ProfilerMarker Marker;
            private readonly long beginTicks;
            public Scope(ProfilerMarker marker) {
                Marker = marker;
                beginTicks = stopwatch.ElapsedTicks;
            }
            public void Dispose() {
                if (Marker.Name == null) return;
                lock (snapshots) {
                    if (snapshots.Count > 5000) return;
                    snapshots.Add(new() { Name = Marker.Name, BeginTick = beginTicks, EndTick = stopwatch.ElapsedTicks, ThreadName = JobScheduler.CurrentThreadName, });
                }
            }
        }

    }
}
