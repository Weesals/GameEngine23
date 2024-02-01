using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Numerics;

namespace Weesals.Engine.Jobs {
    /*
     * Each thread creates page pools (cache sized)
     * If one gets filled, it gets pushed onto a global stack
     * Threads process their own stacks, then grab a global, then steal
     * 
     */
    public class JobDependencies {

        public static JobDependencies Instance = new();
        public const int MaxDepCount = 4;
        public const int DepsPerPage = 64;
        public const int PageCount = 16;

        public struct DependencyNode {
            [InlineArray(MaxDepCount)]
            public struct Depenendecy {
                public ushort Id;
                public override string ToString() {
                    return Id == 0 ? "None" : ((ushort)~Id).ToString();
                }
            }
            public byte Version;
            // How many things need to complete before this one finishes
            public int DependencyCount;
            public ushort JobId;
            public Depenendecy Dependents;
            public override string ToString() {
                return $"Job<{JobId}> @{DependencyCount}";
            }
        }
        public class DependenciesPage {
            public DependencyNode[] Dependencies = new DependencyNode[DepsPerPage];
            public ulong DependenciesUsage = 0;
            public override string ToString() {
                return $"Count={BitOperations.PopCount(DependenciesUsage)}";
            }
        }
        public DependenciesPage[] Pages = new DependenciesPage[PageCount];
        public ulong PagesUsage = 0;

        //[ThreadStatic]
        private static int lastIndex = -1;

        public JobDependencies() {
            for (int i = 0; i < Pages.Length; i++) Pages[i] = new();
        }

        private static int GetPage(int id) { return id >> 6; }
        private static int GetBit(int id) { return id & (DepsPerPage - 1); }
        private static ulong GetBitMask(int id) { return (1ul << (id & (DepsPerPage - 1))); }

        private JobHandle IntlCreateHandle() {
            return IntlCreateHandle(ushort.MaxValue);
        }
        private JobHandle IntlCreateHandle(ushort jobId) {
            lastIndex = Volatile.Read(ref lastIndex);
            for (; ; ) {
                var index = (++lastIndex) & (DepsPerPage * PageCount - 1);
                var page = Pages[GetPage(index)];
                var mask = GetBitMask(index);
                if ((page.DependenciesUsage & mask) != 0) {
                    lastIndex = (index & ~(DepsPerPage - 1)) + BitOperations.TrailingZeroCount(~page.DependenciesUsage & (ulong)-(long)mask) - 1;
                    continue;
                }
                if ((mask & Interlocked.Or(ref page.DependenciesUsage, mask)) != 0) continue;
                ref var entry = ref page.Dependencies[GetBit(index)];
                entry = new() { Version = entry.Version, JobId = jobId, };
                return new JobHandle(index, entry.Version);
            }
        }
        private void DeleteHandle(JobHandle handle) {
            var mask = GetBitMask(handle.Id);
            Trace.Assert((mask & Interlocked.And(ref Pages[GetPage(handle.Id)].DependenciesUsage, ~mask)) != 0);
        }
        private bool RegisterDependency(JobHandle handle, JobHandle dependent) {
            var depPage = Pages[GetPage(dependent.Id)];
            ref var entry = ref GetNode(handle);
            lock (depPage) {
                if ((depPage.DependenciesUsage & GetBitMask(dependent.Id)) == 0) return false;
                Interlocked.Increment(ref entry.DependencyCount);
                ref var depEntry = ref depPage.Dependencies[GetBit(dependent.Id)];
                int count = 0;
                for (; count < MaxDepCount; ++count) if (depEntry.Dependents[count] == 0) break;
                Debug.Assert(count < MaxDepCount);
                depEntry.Dependents[count] = (ushort)~handle.Id;
            }
            return true;
        }
        private ref DependencyNode GetNode(JobHandle handle) {
            return ref Pages[GetPage(handle.Id)].Dependencies[GetBit(handle.Id)];
        }

        public JobHandle CreateHandle(ushort jobId) {
            var handle = IntlCreateHandle(jobId);
            GetNode(handle).DependencyCount = -1;
            JobScheduler.Instance.ScheduleJob(jobId, handle);
            return handle;
        }
        public JobHandle CreateHandle(ushort jobId, JobHandle dependency) {
            // If handle is expired
            if (!dependency.IsValid || GetIsComplete(dependency)) return CreateHandle(jobId);
            // If handle has no job
            //JobScheduler.Instance.Validate();
            if (TrySetJobId(dependency, jobId)) return dependency;
            // Otherwise we must create a new handle
            var job = IntlCreateHandle(jobId);
            if (!RegisterDependency(job, dependency)) {
                GetNode(job).DependencyCount = -1;
                JobScheduler.Instance.ScheduleJob(jobId, job);
            }
            return job;
        }
        public bool TrySetJobId(JobHandle handle, ushort jobId) {
            var page = Pages[GetPage(handle.Id)];
            ref var entry = ref page.Dependencies[GetBit(handle.Id)];
            if (entry.JobId != ushort.MaxValue) return false;
            lock (page) {
                if ((page.DependenciesUsage & (1ul << GetBit(handle.Id))) == 0) return false;
                if (entry.JobId != ushort.MaxValue) return false;
                entry.JobId = jobId;
            }
            return true;
        }
        public JobHandle JoinHandles(JobHandle handle1) { return handle1; }
        public JobHandle JoinHandles(JobHandle handle1, JobHandle handle2) {
            var result = IntlCreateHandle();
            var page = Pages[GetPage(result.Id)];
            ref var entry = ref page.Dependencies[GetBit(result.Id)];
            Interlocked.Increment(ref entry.DependencyCount);
            if (!RegisterDependency(result, handle1)) {
                DeleteHandle(result);
                return handle2;
            }
            try {
                if (!RegisterDependency(result, handle2)) {
                    // Dont delete, handle1 references it
                    return handle1;
                }
            } finally {
                DecrementDependency(result.Id);
            }
            return result;
        }
        public void MarkComplete(JobHandle handle) {
            var page = Pages[GetPage(handle.Id)];
            ref var entry = ref page.Dependencies[GetBit(handle.Id)];
            //Debug.WriteLine($"Completing handle {handle}");
            lock (page) {
                Debug.Assert(entry.Version == handle.Version);
                Debug.Assert(entry.DependencyCount == -1, "Should be running");
                Debug.Assert(!GetIsComplete(handle), "Can only complete self once");
                //Debug.WriteLine($"Dep<{handle}>");
                ++entry.Version;
                entry.DependencyCount = -2;
                for (int d = 0; d < MaxDepCount; ++d) {
                    var depId = entry.Dependents[d];
                    if (depId == 0) break;
                    DecrementDependency((ushort)~depId);
                }
                var mask = GetBitMask(handle.Id);
                Trace.Assert((mask & Interlocked.And(ref page.DependenciesUsage, ~mask)) != 0);
            }
        }

        private bool DecrementDependency(int id) {
            var page = Pages[GetPage(id)];
            lock (page) {
                ref var entry = ref page.Dependencies[GetBit(id)];
                if (Interlocked.Decrement(ref entry.DependencyCount) != 0) return false;
                entry.DependencyCount = -1;
                var depHandle = new JobHandle(id, entry.Version);
                //Debug.WriteLine($"Propagate {handle} to {depHandle}");
                if (entry.JobId == ushort.MaxValue) {
                    MarkComplete(depHandle);
                } else {
                    JobScheduler.Instance.ScheduleJob(entry.JobId, depHandle);
                }
            }
            return true;
        }

        public bool GetIsComplete(JobHandle handle) {
            var page = Pages[GetPage(handle.Id)];
            return (page.DependenciesUsage & (1ul << GetBit(handle.Id))) == 0;
        }
        public void Validate() {
            foreach (var page in Pages) {
                lock (page) {
                    Interlocked.MemoryBarrierProcessWide();
                    for (int i = 0; i < page.Dependencies.Length; i++) {
                        if ((page.DependenciesUsage & (1ul << i)) == 0) continue;
                        var item = page.Dependencies[i];
                        Trace.Assert(item.DependencyCount != 0);
                    }
                }
            }
        }
    }

    public class JobScheduler {

        public const int ThreadCount = 8;

        public bool IsQuitting { get; private set; }


        public class TaskPool {
            public int OpMask = 0xff0000;
            public nint State;
            public bool HasOp => Op != 0;
            public byte Op => (byte)(State >> 16);
            public int Begin => (byte)(State >> 8);
            public int Count => (byte)(State >> 0);
            public int VolatileCount => (byte)(Volatile.Read(ref State) >> 0);
            public const int Capacity = 4;
            public bool IsFull => Count >= Capacity;
            public bool IsEmpty => Count == 0;
            [InlineArray(Capacity)]
            public struct TaskEntry {
                public int TaskId;
            }
            public TaskEntry Pool;
            public bool TryPush(int taskId) {
                while (true) {
                    var state = Volatile.Read(ref State);
                    int count = (byte)(state >> 0);
                    int begin = (byte)(state >> 8);
                    // No room
                    if (count >= Capacity) return false;
                    // Currently busy
                    if ((state & OpMask) != 0) continue;
                    count++;
                    var tmpState = 0x20000 | (begin << 8) | (count);
                    // Something else got here first
                    if (Interlocked.CompareExchange(ref State, tmpState, state) != state) continue;
                    // Should be safe to do anything now
                    Pool[(begin + count - 1) % Capacity] = taskId;
                    state = tmpState & 0xffff;
                    Trace.Assert(Interlocked.CompareExchange(ref State, state, tmpState) == tmpState);
                    return true;
                }
            }
            public bool TryPop(out int taskId) {
                taskId = default;
                while (true) {
                    var state = Volatile.Read(ref State);
                    int count = (byte)(state >> 0);
                    int begin = (byte)(state >> 8);
                    // No items
                    if (count <= 0) return false;
                    // Currently busy
                    if ((state & OpMask) != 0) continue;
                    int prevBegin = begin;
                    if (++begin >= Capacity) begin -= Capacity;
                    --count;
                    var tmpState = 0x20000 | (begin << 8) | (count);
                    // Something else got here first
                    if (Interlocked.CompareExchange(ref State, tmpState, state) != state) continue;
                    // Should be safe to do anything now
                    taskId = Pool[prevBegin];
                    state = tmpState & 0xffff;
                    Trace.Assert(Interlocked.CompareExchange(ref State, state, tmpState) == tmpState);
                    return true;
                }
                throw new Exception("Failed to pop an item");
            }
        }

        public struct JobTask {
            public Action<object?> Callback;
            public object? Context;
            public JobHandle Dependency;
        }
        private JobTask[] taskArray = new JobTask[1024];
        private int lastTaskIndex = -1;
        private Queue<TaskPool> globalPool = new();

        public class JobThread {
            public readonly JobScheduler Scheduler;
            public readonly Thread Thread;
            public readonly AutoResetEvent SleepEvent = new(false);
            private bool isAsleep;
            public bool IsAsleep => Volatile.Read(ref isAsleep);
            public TaskPool CurrentTasks = new();
            public JobThread(JobScheduler scheduler, string name) {
                Scheduler = scheduler;
                Thread = new Thread(Invoke) { Name = name };
                Thread.Start();
            }
            private void Invoke() {
                JobScheduler.currentThread = this;
                while (!Scheduler.IsQuitting) {
                    if (CurrentTasks.TryPop(out var taskId)) {
                        ref var task = ref Scheduler.taskArray[taskId];
                        var dep = task.Dependency;
                        task.Callback(task.Context);
                        task = default;
                        JobDependencies.Instance.MarkComplete(dep);
                        continue;
                    }
                    lock (Scheduler.globalPool) {
                        lock (CurrentTasks) {
                            if (CurrentTasks.IsEmpty && Scheduler.globalPool.Count != 0) {
                                CurrentTasks = Scheduler.globalPool.Dequeue();
                                continue;
                            }
                        }
                    }
                    Sleep();
                }
            }
            private void Sleep() {
                Volatile.Write(ref isAsleep, true);
                Interlocked.MemoryBarrier();
                if (CurrentTasks.VolatileCount == 0 && Scheduler.globalPool.Count == 0) SleepEvent.WaitOne();
                Volatile.Write(ref isAsleep, false);
            }
            public void Wake() {
                Volatile.Write(ref isAsleep, false);
                SleepEvent.Set();
                Interlocked.MemoryBarrier();
            }
            public void RequireWake() {
                if (Volatile.Read(ref isAsleep)) Wake();
            }

            private TaskPool RequireNonFullTasks() {
                var tasks = CurrentTasks;
                if (tasks != null && !tasks.IsFull) return tasks;
                lock (this) {
                    if (tasks != null && CurrentTasks == tasks && tasks.IsFull) {
                        lock (Scheduler.globalPool) {
                            Scheduler.globalPool.Enqueue(tasks);
                            CurrentTasks = null;
                        }
                    }
                    if (CurrentTasks == null) CurrentTasks = new();
                    return CurrentTasks;
                }
            }
            public bool TryPushTask(int taskId) {
                while (true) {
                    var tasks = RequireNonFullTasks();
                    if (!tasks.TryPush(taskId)) continue;
                    return true;
                }
            }
            public void Validate() {
                Interlocked.MemoryBarrier();
                //Debug.Assert(!IsAsleep || CurrentTasks.VolatileCount == 0);
            }
        }

        private JobThread[] jobThreads = Array.Empty<JobThread>();
        [ThreadStatic] static JobThread? currentThread;

        public JobScheduler() {
            jobThreads = new JobThread[ThreadCount];
            for (int i = 0; i < jobThreads.Length; i++) {
                jobThreads[i] = new(this, $"Job Thread {i}");
            }
        }

        private ushort IntlPushTask(JobTask task) {
            for (; ; ) {
                var index = (++lastTaskIndex) & (taskArray.Length - 1);
                ref var slot = ref taskArray[index];
                if (slot.Callback != null) continue;
                if (Interlocked.CompareExchange(ref slot.Callback, task.Callback, null) != null) continue;
                slot = task;
                return (ushort)index;
            }
        }

        public ushort CreateJob(Action<object?> action, object? context = null) {
            return IntlPushTask(new JobTask() { Callback = action, Context = context, });
        }
        public void ScheduleJob(ushort jobId, JobHandle handle) {
            taskArray[jobId].Dependency = handle;
            // Try to add to local stack (is running in local thread)
            if (currentThread != null) {
                Debug.Assert(!currentThread.IsAsleep);
                if (currentThread.CurrentTasks.Count == 0 && currentThread.TryPushTask(jobId)) return;
            }
            // Add to another thread
            int bestThread = 0;
            for (int i = 0; i < jobThreads.Length; i++) {
                var thread = jobThreads[i];
                if (thread.CurrentTasks.Count < jobThreads[bestThread].CurrentTasks.Count)
                    bestThread = i;
            }
            if (jobThreads[bestThread].TryPushTask(jobId)) { jobThreads[bestThread].RequireWake(); return; }
            throw new NotImplementedException();
        }
        public void Validate() {
            jobThreads[0].Validate();
        }

        public void Dispose() {
            IsQuitting = true;
            for (int i = 0; i < jobThreads.Length; i++) {
                jobThreads[i].Wake();
            }
        }

        public void WaitForAll() {
            while (globalPool.Count > 0) Thread.Sleep(1);
            foreach (var thread in jobThreads) {
                while (!thread.IsAsleep) Thread.Sleep(1);
            }
        }

        public static JobScheduler Instance = new();

    }
}
