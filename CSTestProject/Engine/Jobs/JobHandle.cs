using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Weesals.Utility;

namespace Weesals.Engine.Jobs {
    public readonly struct JobHandle {

        // This value only exists to stop packed ever being 0
        const int SentinelFlag = 0x10000;

        private readonly int packed;

        public readonly int Id => packed & 0x7fff;
        public readonly byte Version => (byte)(packed >> 24);
        public readonly bool IsValid => packed != 0;

        // Currently unused
        public readonly byte Flags => (byte)(packed >> 16);

        public JobHandle(int id, byte version) { packed = (id | SentinelFlag) | (version << 24); }
        public override string ToString() { return $"<{Id} V={Version}>"; }

        private static int deferCount = 0;
        private static int deferCmplt = 0;
        public static JobHandle CreateDeferred() {
            ++deferCount;
            var handle = JobDependencies.Instance.CreateDeferredHandle();
            Debug.Assert(!JobDependencies.Instance.GetIsComplete(handle));
            return handle;
        }
        public static void MarkDeferredComplete(JobHandle handle) {
            deferCmplt++;
            JobDependencies.Instance.MarkComplete(handle);
        }

        public static JobHandle CombineDependencies(JobHandle job1) { return job1; }
        public static JobHandle CombineDependencies(JobHandle job1, JobHandle job2) {
            if (!job1.IsValid) return job2;
            if (!job2.IsValid) return job1;
            var dependencies = JobDependencies.Instance;
            bool j1Complete = dependencies.GetIsComplete(job1);
            bool j2Complete = dependencies.GetIsComplete(job2);
            if (j1Complete && j2Complete) return JobHandle.None;
            if (j1Complete) return job2; else if (j2Complete) return job1;
            return dependencies.JoinHandles(job1, job2);
        }
        public static JobHandle CombineDependencies(JobHandle job1, JobHandle job2, JobHandle job3) {
            var dependencies = JobDependencies.Instance;
            var joined = dependencies.CreateJoined();
            dependencies.AppendJoined(ref joined, job1);
            dependencies.AppendJoined(ref joined, job2);
            dependencies.AppendJoined(ref joined, job3);
            return dependencies.EndJoined(ref joined);
        }
        public static JobHandle CombineDependencies(JobHandle job1, JobHandle job2, JobHandle job3, JobHandle job4) {
            var dependencies = JobDependencies.Instance;
            var joined = dependencies.CreateJoined();
            dependencies.AppendJoined(ref joined, job1);
            dependencies.AppendJoined(ref joined, job2);
            dependencies.AppendJoined(ref joined, job3);
            dependencies.AppendJoined(ref joined, job4);
            return dependencies.EndJoined(ref joined);
        }
        public static JobHandle CombineDependencies(JobHandle job1, JobHandle job2, JobHandle job3, JobHandle job4, JobHandle job5) {
            var dependencies = JobDependencies.Instance;
            var joined = dependencies.CreateJoined();
            dependencies.AppendJoined(ref joined, job1);
            dependencies.AppendJoined(ref joined, job2);
            dependencies.AppendJoined(ref joined, job3);
            dependencies.AppendJoined(ref joined, job4);
            dependencies.AppendJoined(ref joined, job5);
            return dependencies.EndJoined(ref joined);
        }

        public static JobHandle Schedule(Action<object?> value, JobHandle dependency = default) {
            return JobDependencies.Instance.CreateHandle(JobScheduler.Instance.CreateJob(value), dependency);
        }
        public static JobHandle Schedule(Action<object?> value, object context, JobHandle dependency) {
            return JobDependencies.Instance.CreateHandle(JobScheduler.Instance.CreateJob(value, context), dependency);
        }

        public static JobHandle ScheduleBatch(Action<RangeInt> value, int count, JobHandle dependency) {
            var job = JobScheduler.Instance.CreateBatchJob(value, count);
            return JobDependencies.Instance.CreateHandle(job, dependency);
        }
        public static JobHandle ScheduleBatch(Action<RangeInt> value, RangeInt range, JobHandle dependency) {
            var job = JobScheduler.Instance.CreateBatchJob(value, range);
            return JobDependencies.Instance.CreateHandle(job, dependency);
        }

        public static JobHandle RunOnMain(Action<object?> value, JobHandle dependency = default) {
            var job = JobScheduler.Instance.CreateJob(value);
            JobScheduler.Instance.MarkRunOnMain(job);
            return JobDependencies.Instance.CreateHandle(job, dependency);
        }

        public void Complete() {
            while (!JobDependencies.Instance.GetIsComplete(this)) {
                if (JobScheduler.IsMainThread && JobScheduler.Instance.TryRunMainThreadTask()) continue;
                Thread.Sleep(0);
            }
        }

        public static readonly JobHandle None = new();
        internal static readonly JobHandle Invalid = new(-1, 0);
    }
}
