using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Weesals.Engine.Profiling {
    public struct ProfilerMarker {

        public readonly string Name;

        public ProfilerMarker(string name) {
            Name = name;
        }

        private void BeginSample() {

        }
        private void EndSample() {
        }

        public Scope Auto() {
            return new Scope(this);
        }

        public readonly struct Scope : IDisposable {
            public readonly ProfilerMarker Marker;
            public Scope(ProfilerMarker marker) {
                Marker = marker;
                Marker.BeginSample();
            }
            public void Dispose() {
                Marker.EndSample();
            }
        }

    }
}
