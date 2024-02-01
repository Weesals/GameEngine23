using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Weesals.ECS;

namespace Weesals.Game {
    public partial class TimeSystem : SystemBase {
        public int Tick;
        public long TimePreviousMS;
        public long TimeCurrentMS;
        public long TimeDeltaMS;

        public struct TimeInterval {
            public long TimePreviousMS;
            public long TimeCurrentMS;
            public long DeltaTimeMS => TimeCurrentMS - TimePreviousMS;

            public int GetIntervalTicks(int interval, int phase) {
                phase -= interval;
                return (int)((TimeCurrentMS - phase) / interval - (TimePreviousMS - phase) / interval);
            }
        }

        public TimeInterval GetInterval() {
            return new TimeInterval() {
                TimeCurrentMS = TimeCurrentMS,
                TimePreviousMS = TimePreviousMS,
            };
        }

        public void Step(long stepMS) {
            Tick++;
            TimeDeltaMS = stepMS;
            TimePreviousMS = TimeCurrentMS;
            TimeCurrentMS += TimeDeltaMS;
        }

        protected override void OnUpdate() { }

        public void CopyStateFom(TimeSystem other) {
            TimePreviousMS = other.TimePreviousMS;
            TimeCurrentMS = other.TimeCurrentMS;
            Tick = other.Tick;
            TimeDeltaMS = other.TimeDeltaMS;
        }

    }
}
