using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Game5 {
    public struct FrameTimer {

        public Stopwatch timer = new();
        private TimeSpan[] elapsed;
        private int head;
        private int count;

        public FrameTimer(int poolSize = 4) {
            timer.Start();
            elapsed = new TimeSpan[poolSize];
        }
        public TimeSpan ConsumeDeltaTicks() {
            Step();
            return GetDelta();
        }
        public void Reset() {
            count = 0;
        }
        private void Step() {
            if (count < elapsed.Length - 1) count++;
            else head = (head + 1) % elapsed.Length;
            elapsed[head] = timer.Elapsed;
        }
        private TimeSpan GetDelta() {
            if (count == 0) return TimeSpan.Zero;
            return (elapsed[head] - elapsed[(head + elapsed.Length - count) % elapsed.Length]) / count;
        }
    }

}
