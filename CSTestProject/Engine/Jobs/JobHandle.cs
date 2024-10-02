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
        const uint SentinelFlag = 0x10000;

        internal readonly uint packed;

        public readonly int Id => (int)(packed & 0x7fff);
        public readonly byte Version => (byte)(packed >> 24);
        public readonly bool IsValid => packed != 0;
        public readonly bool IsComplete => JobDependencies.Instance.GetIsComplete(this);

        // Currently unused
        public readonly byte Flags => (byte)(packed >> 16);

        internal JobHandle(uint _packed) { packed = _packed; }
        public JobHandle(int id, byte version) { packed = ((uint)id | SentinelFlag) | ((uint)version << 24); }
        public override string ToString() { return $"<{Id} V={Version}>"; }
        public override int GetHashCode() { return (int)packed; }
        public static bool operator ==(JobHandle h1, JobHandle h2) => h1.packed == h2.packed;
        public static bool operator !=(JobHandle h1, JobHandle h2) => h1.packed != h2.packed;
        public override bool Equals(object? obj) => throw new NotImplementedException();
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
        public static void ConvertDeferred(JobHandle deferred, JobHandle dependency) {
            var dependencies = JobDependencies.Instance;
            dependencies.ConvertDeferred(deferred, dependency);
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

        public static JobHandle Schedule(Action callback, JobHandle dependency = default) {
            return JobDependencies.Instance.CreateHandle(JobScheduler.Instance.CreateTask(callback), dependency);
        }
        public static JobHandle Schedule(Action<object?> callback, object context, JobHandle dependency = default) {
            return JobDependencies.Instance.CreateHandle(JobScheduler.Instance.CreateTask(callback, context), dependency);
        }
        public static JobHandle Schedule(Action<object?, object?> callback, object context1, object context2, JobHandle dependency = default) {
            return JobDependencies.Instance.CreateHandle(JobScheduler.Instance.CreateTask(callback, context1, context2), dependency);
        }
        public static JobHandle Schedule(Action<uint> callback, uint data, JobHandle dependency = default) {
            return JobDependencies.Instance.CreateHandle(JobScheduler.Instance.CreateTask(callback, data), dependency);
        }
        public static JobHandle Schedule(Action<ulong> callback, ulong data, JobHandle dependency = default) {
            return JobDependencies.Instance.CreateHandle(JobScheduler.Instance.CreateTask(callback, data), dependency);
        }
        public static JobHandle Schedule<T>(Action<T> callback, T data, JobHandle dependency = default) {
            return JobDependencies.Instance.CreateHandle(JobScheduler.Instance.CreateTask(callback, data), dependency);
        }

        public static JobHandle ScheduleBatch(Action<RangeInt> value, int count, JobHandle dependency = default) {
            if (count == 0) return JobHandle.None;
            var job = JobScheduler.Instance.CreateBatchTask(value, count);
            return JobDependencies.Instance.CreateHandle(job, dependency);
        }
        public static JobHandle ScheduleBatch(Action<RangeInt> value, RangeInt range, JobHandle dependency = default) {
            if (range.Length == 0) return JobHandle.None;
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
        public readonly JobHandle Then(Action<object?> action, object context) {
            return JobHandle.Schedule(action, context, this);
        }
        public readonly JobHandle Then(Action<uint> action, uint data) {
            return JobHandle.Schedule(action, data, this);
        }
        public readonly JobHandle Then(Action<ulong> action, ulong data) {
            return JobHandle.Schedule(action, data, this);
        }
        public readonly JobHandle Then<T>(Action<T> action, T data) {
            return JobHandle.Schedule(action, data, this);
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

    public struct JobResult<TResult> : IDisposable {
        public class Sentinel {
            public StackTrace? Stack;
            ~Sentinel() {
                if (Stack != null) {
                    throw new Exception("Complete/Dispose not called for Job at " + Stack);
                }
            }
        }
        public readonly JobHandle Handle;
#if DEBUG
        private Sentinel sentinel;
#endif
        private JobHandle thenHandles;
        public bool IsComplete => JobDependencies.Instance.GetIsComplete(Handle);
        public JobResult(JobHandle handle) {
            Handle = handle;
#if DEBUG
            sentinel = new() { Stack = new(true) };
#endif
        }
        private void Destroy() {
#if DEBUG
            sentinel.Stack = null;
#endif
            thenHandles = Handle;
        }
        public void Dispose() {
            if (thenHandles == Handle) return;  // Already complete
            if (!thenHandles.IsValid) thenHandles = Handle;
            thenHandles.Then(static (handle) => {
                JobScheduler.Instance.ConsumeResult<TResult>(new JobHandle(handle));
            }, Handle.packed);
            Destroy();
        }
        public JobHandle Then(Action<TResult> callback) {
            var task = JobScheduler.Instance.CreateTask(
                static (obj, callback) => ((Action<TResult>)callback)((TResult)obj),
                null, callback);
            var copyHandle = Handle.Then(static (packed) => {
                JobScheduler.Instance.CopyResult(new JobHandle((uint)(packed >> 32)), (ushort)packed);
            }, ((ulong)Handle.packed << 32) | task);
            thenHandles = JobHandle.CombineDependencies(thenHandles, copyHandle);
            return JobDependencies.Instance.CreateHandle(task, copyHandle);
        }
        public TResult Complete() {
            Debug.Assert(thenHandles != Handle,
                "Cannot Complete after Dispose()");
            Handle.Complete();
            TResult result;
            if (thenHandles.IsValid) {
                result = JobScheduler.Instance.GetResult<TResult>(Handle);
                Dispose();
            } else {
                result = JobScheduler.Instance.ConsumeResult<TResult>(Handle);
                Destroy();
            }
            return result;
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
        public static implicit operator JobHandle(JobResult<TResult> result) {
            return result.Handle;
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
