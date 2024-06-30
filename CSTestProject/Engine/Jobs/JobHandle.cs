using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Weesals.Utility;

namespace Weesals.Engine.Jobs {
    public readonly struct JobHandle : IEquatable<JobHandle>, IComparable<JobHandle> {

        // This value only exists to stop packed ever being 0
        const int SentinelFlag = 0x10000;

        private readonly int packed;

        public readonly int Id => packed & 0x7fff;
        public readonly byte Version => (byte)(packed >> 24);
        public readonly bool IsValid => packed != 0;
        public readonly bool IsComplete => JobDependencies.Instance.GetIsComplete(this);

        // Currently unused
        public readonly byte Flags => (byte)(packed >> 16);

        public JobHandle(int id, byte version) { packed = (id | SentinelFlag) | (version << 24); }
        public override string ToString() { return $"<{Id} V={Version}>"; }
        public override int GetHashCode() { return packed; }
        public bool Equals(JobHandle other) { return packed == other.packed; }
        public int CompareTo(JobHandle other) { return packed.CompareTo(other.packed); }

        private static int deferCount = 0;
        private static int deferCmplt = 0;
        // A handle that can be marked complete externally
        public static JobHandle CreateDeferred() {
            ++deferCount;
            var handle = JobDependencies.Instance.CreateDeferredHandle();
            Debug.Assert(!JobDependencies.Instance.GetIsComplete(handle));
            return handle;
        }
        // Mark a deferred handle as complete
        public static void MarkDeferredComplete(JobHandle handle) {
            deferCmplt++;
            JobDependencies.Instance.MarkComplete(handle);
        }
        // A handle that is already complete
        public static JobHandle CreateDummy() {
            var handle = CreateDeferred();
            MarkDeferredComplete(handle);
            return handle;
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

        public static JobHandle Schedule(Action value, JobHandle dependency = default) {
            return JobDependencies.Instance.CreateHandle(JobScheduler.Instance.CreateTask(value), dependency);
        }
        public static JobHandle Schedule(Action<object?> value, object context, JobHandle dependency = default) {
            return JobDependencies.Instance.CreateHandle(JobScheduler.Instance.CreateTask(value, context), dependency);
        }
        public static JobHandle Schedule(Action<object?, object?> value, object context1, object context2, JobHandle dependency = default) {
            return JobDependencies.Instance.CreateHandle(JobScheduler.Instance.CreateTask(value, context1, context2), dependency);
        }

        public static JobHandle ScheduleBatch(Action<RangeInt> value, int count, JobHandle dependency) {
            var job = JobScheduler.Instance.CreateBatchTask(value, count);
            return JobDependencies.Instance.CreateHandle(job, dependency);
        }
        public static JobHandle ScheduleBatch(Action<RangeInt> value, RangeInt range, JobHandle dependency) {
            var job = JobScheduler.Instance.CreateBatchTask(value, range);
            return JobDependencies.Instance.CreateHandle(job, dependency);
        }

        public static JobHandle RunOnMain(Action<object?> value, JobHandle dependency = default) {
            var job = JobScheduler.Instance.CreateTask(value);
            if (job != ushort.MaxValue) JobScheduler.Instance.MarkRunOnMain(job);
            return JobDependencies.Instance.CreateHandle(job, dependency);
        }

        public readonly void Complete() {
            if (!IsValid) return;
            while (!JobDependencies.Instance.GetIsComplete(this)) {
                if (JobScheduler.IsMainThread && JobScheduler.Instance.TryRunMainThreadTask()) continue;
                if (JobScheduler.Instance.GetHasQueuedTasks()) {
                    JobScheduler.Instance.WakeHelperThread();
                }
                Thread.Sleep(0);
            }
        }

        public readonly JobHandle Join(JobHandle other) {
            return JobHandle.CombineDependencies(this, other);
        }
        public readonly JobHandle Then(Action action) {
            return JobHandle.Schedule(action, this);
        }

        public static readonly JobHandle None = new();
        internal static readonly JobHandle Invalid = new(-1, 0);

        public struct Awaiter : INotifyCompletion {
            public readonly JobHandle Handle;
            public bool IsCompleted => Handle.IsComplete;
            public Awaiter(JobHandle handle) { Handle = handle; }
            public void GetResult() { }
            public void OnCompleted(Action continuation) {
                Schedule(continuation, Handle);
            }
        }
        public Awaiter GetAwaiter() { return new(this); }
    }

    public struct JobResult<TResult> {
        public readonly JobHandle Handle;
        public bool IsComplete => JobDependencies.Instance.GetIsComplete(Handle);
        public JobResult(JobHandle handle) {
            Handle = handle;
        }
        public JobHandle Then(Action<TResult> callback) {
            var handle = Handle;
            return JobHandle.Schedule((callback) => {
                var result = JobScheduler.Instance.ConsumeResult<TResult>(handle);
                ((Action<TResult>)callback)(result);
            }, callback, Handle);
        }
        public TResult Complete() {
            Handle.Complete();
            return JobScheduler.Instance.ConsumeResult<TResult>(Handle);
        }
        public JobHandle Join(JobHandle other) { return Handle.Join(other); }

        public static JobResult<TResult> Schedule(Func<object> value, JobHandle dependency = default) {
            var handle = JobDependencies.Instance.CreateHandle(JobScheduler.Instance.CreateTask(value), dependency);
            return new(handle);
        }
        public static implicit operator JobResult<TResult>(TResult result) {
            var handle = JobHandle.CreateDummy();
            JobScheduler.Instance.SetResult(handle, result);
            return new(handle);
        }

        public struct Awaiter : INotifyCompletion {
            public readonly JobResult<TResult> Handle;
            public bool IsCompleted => Handle.IsComplete;
            public Awaiter(JobResult<TResult> handle) { Handle = handle; }
            public TResult GetResult() {
                return JobScheduler.Instance.ConsumeResult<TResult>(Handle.Handle);
            }
            public void OnCompleted(Action continuation) {
                JobHandle.Schedule(continuation, Handle.Handle);
            }
        }
        public Awaiter GetAwaiter() { return new(this); }
    }
}
