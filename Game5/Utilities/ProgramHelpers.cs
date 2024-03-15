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
            //var now = timer.Elapsed;
            //var delta = now - elapsed;
            //elapsed = now;
            //return delta;
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

    public struct FrameThrottler {

        float timeSinceRender = 0f;
        int renderHash = 0;
        bool onBattery = false;

        public bool IsThrottled { get; private set; }

        // Define constants and structures required for P/Invoke
        const int SYSTEM_POWER_STATUS = 0x0015;
        [StructLayout(LayoutKind.Sequential)]
        public struct SystemPowerStatus {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte Reserved1;
            public uint BatteryLifeTime;
            public uint BatteryFullLifeTime;
        }
        [DllImport("Kernel32.dll")]
        public static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

        public FrameThrottler() {
            UpdateBatteryStatus();
        }

        public void UpdateBatteryStatus() {
            SystemPowerStatus status;
            if (!GetSystemPowerStatus(out status)) return;
            onBattery = (status.ACLineStatus == 0);
        }

        public void Update(float dt) {
            timeSinceRender += dt;
        }

        public float Step(int newRenderHash, bool forceChange = false) {
            float elapsed = timeSinceRender;
            IsThrottled = renderHash == newRenderHash && timeSinceRender <= (onBattery ? 1f : 0.1f) && !forceChange;
            renderHash = newRenderHash;
            if (!IsThrottled) NotifyRendered();
            return elapsed;
        }
        private void NotifyRendered() {
            timeSinceRender = 0f;
        }
    }

}
