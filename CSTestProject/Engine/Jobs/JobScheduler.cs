using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Diagnostics;
using System.Numerics;
using Weesals.Utility;

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
            // How many things need to complete before this one finishes
            public int DependencyCount;
            public byte Version;
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

        public JobHandle CreateDeferredHandle() {
            var handle = IntlCreateHandle(ushort.MaxValue - 1);
            GetNode(handle).DependencyCount = -1;
            return handle;
        }
        public JobHandle CreateHandle(ushort jobId) {
            var handle = IntlCreateHandle(jobId);
            GetNode(handle).DependencyCount = -1;
            JobScheduler.Instance.ScheduleJob(jobId, handle);
            return handle;
        }
        public JobHandle CreateHandle(ushort jobId, JobHandle dependency) {
            if (jobId == ushort.MaxValue) return dependency;
            // If handle is expired
            if (!dependency.IsValid || GetIsComplete(dependency)) return CreateHandle(jobId);
            // If handle has no job
            //JobScheduler.Instance.Validate();
            if (TrySetJobId(dependency, jobId)) return dependency;
            // Otherwise we must create a new handle
            var handle = IntlCreateHandle(jobId);
            if (!RegisterDependency(handle, dependency)) {
                GetNode(handle).DependencyCount = -1;
                JobScheduler.Instance.ScheduleJob(jobId, handle);
            }
            return handle;
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
        public struct JoinCreator {
            public JobHandle Handle;
            public JobHandle Singleton;
        }
        public JoinCreator CreateJoined() {
            var result = IntlCreateHandle();
            var page = Pages[GetPage(result.Id)];
            ref var entry = ref page.Dependencies[GetBit(result.Id)];
            Interlocked.Increment(ref entry.DependencyCount);
            return new JoinCreator() { Handle = result, Singleton = JobHandle.None, };
        }
        // Same as join, but mutates the existing handle
        public void AppendJoined(ref JoinCreator join, JobHandle handle2) {
            if (RegisterDependency(join.Handle, handle2)) {
                join.Singleton = join.Singleton.Id == JobHandle.None.Id ? handle2
                    : JobHandle.Invalid;
            }
        }
        public JobHandle EndJoined(ref JoinCreator result) {
            DecrementDependency(result.Handle.Id);
            if (result.Singleton.Id != JobHandle.Invalid.Id) {
                var page = Pages[GetPage(result.Handle.Id)];
                ref var entry = ref page.Dependencies[GetBit(result.Handle.Id)];
                var count = entry.DependencyCount;
                DeleteHandle(result.Handle);
                return result.Singleton;
            }
            return result.Handle;
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

        public const int ThreadCount = -1;
        public const bool EnableThreading = true;

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
                    // Something else got here first
                    if (Interlocked.CompareExchange(ref State, 0x20000 | state, state) != state) continue;
                    state |= 0x20000;
                    count++;
                    Pool[(begin + count - 1) % Capacity] = taskId;
                    Trace.Assert(Interlocked.CompareExchange(ref State, (begin << 8) | (count), state) == state);
                    return true;
                }
            }
            public bool TryPeek(out int taskId) {
                taskId = default;
                while (true) {
                    var state = Volatile.Read(ref State);
                    int count = (byte)(state >> 0);
                    int begin = (byte)(state >> 8);
                    // No items
                    if (count <= 0) return false;
                    // Currently busy
                    if ((state & OpMask) != 0) continue;
                    // Something else got here first
                    if (Interlocked.CompareExchange(ref State, 0x20000 | state, state) != state) continue;
                    state |= 0x20000;
                    taskId = Pool[begin];
                    Trace.Assert(Interlocked.CompareExchange(ref State, (begin << 8) | (count), state) == state);
                    return true;
                }
                throw new Exception("Failed to pop an item");
            }
            public bool TryBeginPop(out int taskId, out nint state) {
                taskId = default;
                while (true) {
                    state = Volatile.Read(ref State);
                    int count = (byte)(state >> 0);
                    int begin = (byte)(state >> 8);
                    // No items
                    if (count <= 0) return false;
                    // Currently busy
                    if ((state & OpMask) != 0) continue;
                    // Something else got here first
                    if (Interlocked.CompareExchange(ref State, 0x20000 | state, state) != state) continue;
                    state |= 0x20000;
                    taskId = Pool[begin];
                    return true;
                }
            }
            public bool YieldPop(nint state) {
                Trace.Assert(Interlocked.CompareExchange(ref State, state & 0xffff, state) == state);
                return true;
            }
            public bool EndPop(nint state) {
                int count = (byte)(state >> 0);
                int begin = (byte)(state >> 8);
                if (++begin >= Capacity) begin -= Capacity;
                --count;
                Trace.Assert(Interlocked.CompareExchange(ref State, (begin << 8) | (count), state) == state);
                return true;
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
                    // Something else got here first
                    if (Interlocked.CompareExchange(ref State, 0x20000 | state, state) != state) continue;
                    state |= 0x20000;

                    taskId = Pool[begin];
                    if (++begin >= Capacity) begin -= Capacity;
                    --count;
                    Trace.Assert(Interlocked.CompareExchange(ref State, (begin << 8) | (count), state) == state);
                    return true;
                }
                throw new Exception("Failed to pop an item");
            }
        }

        public struct JobTask {
            public object Callback;
            public object? Context;
            public JobHandle Dependency;
            public ushort BatchBegin;
            public ushort BatchCount;
            public uint RunState;
            public int RunCount => (int)(RunState & 0xffff);
            public int CompleteCount => (int)((RunState >> 16) & 0xffff);
            public bool IsMainThread => BatchBegin == ushort.MaxValue;
        }
        public struct JobRun {
            public int TaskId;
            public ushort BatchBegin;
            public ushort BatchCount;
            public bool IsValid => TaskId >= 0;
            public static readonly JobRun Invalid = new() { TaskId = -1, };
        }
        private JobTask[] taskArray = new JobTask[1024];
        private int lastTaskIndex = -1;
        private Queue<TaskPool> globalPool = new();
        private TaskPool mainThreadTasks = new();

        public class JobThread {
            public readonly string Name;
            public readonly JobScheduler Scheduler;
            public readonly Thread Thread;
            public readonly AutoResetEvent SleepEvent = new(false);
            private bool isAsleep;
            public bool IsAsleep => Volatile.Read(ref isAsleep);
            public TaskPool CurrentTasks = new();
            public JobThread(JobScheduler scheduler, string name) {
                Name = name;
                Scheduler = scheduler;
                Thread = new Thread(Invoke) { Name = name };
                Thread.Start();
            }
            private int GetRunCount(int maxCount) {
                int batchBit = 31 - BitOperations.LeadingZeroCount((uint)maxCount);
                return maxCount >> Math.Max(batchBit - 4, 0);
            }
            private void GetRunIndices(int index, JobTask task, out ushort batchBegin, out ushort batchCount) {
                int batchBit = 31 - BitOperations.LeadingZeroCount((uint)task.BatchCount);
                batchBegin = (ushort)(index << Math.Max(batchBit - 4, 0));
                var batchEnd = Math.Min(((index + 1) << Math.Max(batchBit - 4, 0)), task.BatchCount);
                batchCount = (ushort)(batchEnd - batchBegin);
                batchBegin += task.BatchBegin;
            }
            private JobRun TryTakeJobRun() {
                if (!CurrentTasks.TryBeginPop(out var taskId, out var state)) return JobRun.Invalid;
                ref var task = ref Scheduler.taskArray[taskId];
                JobRun run = new() { TaskId = taskId, };
                if (task.BatchCount == 0) {
                    CurrentTasks.EndPop(state);
                } else {
                    var index = (int)(Interlocked.Increment(ref task.RunState) & 0xffff);
                    if (index >= GetRunCount(task.BatchCount)) {
                        CurrentTasks.EndPop(state);
                    } else {
                        CurrentTasks.YieldPop(state);
                    }
                    GetRunIndices(index - 1, task, out run.BatchBegin, out run.BatchCount);
                }
                return run;
            }
            private void Invoke() {
                JobScheduler.currentThread = this;
                ThreadName = Name;
                int spinCount = 0;
                for (; !Scheduler.IsQuitting; ++spinCount) {
                    var run = TryTakeJobRun();
                    if (run.IsValid) {
                        ExecuteRun(run);
                        spinCount = 0;
                        continue;
                    }
                    if (CurrentTasks.IsEmpty && Scheduler.globalPool.Count != 0) {
                        lock (Scheduler.globalPool) {
                            lock (CurrentTasks) {
                                if (CurrentTasks.IsEmpty && Scheduler.globalPool.Count != 0) {
                                    CurrentTasks = Scheduler.globalPool.Dequeue();
                                    continue;
                                }
                            }
                        }
                    }
                    bool found = false;
                    foreach (var thread in Scheduler.jobThreads) {
                        run = thread.TryTakeJobRun();
                        if (run.IsValid) {
                            ExecuteRun(run);
                            found = true;
                            break;
                        }
                    }
                    if (found) {
                        spinCount = 0;
                        continue;
                    }
                    if (spinCount > 200) {
                        Sleep();
                        spinCount = 0;
                    } else if (spinCount > 50) {
                        Thread.Sleep(0);
                    }
                }
            }

            private void ExecuteRun(JobRun run) {
                ref var task = ref Scheduler.taskArray[run.TaskId];
                if (task.BatchCount == 0) {
                    ((Action<object?>)task.Callback)(task.Context);
                    MarkComplete(ref task);
                } else {
                    ((Action<RangeInt>)task.Callback)(new RangeInt(run.BatchBegin, run.BatchCount));
                    var runCount = GetRunCount(task.BatchCount);
                    var newState = Interlocked.Add(ref task.RunState, 0x10000);
                    if (((newState >> 16) & 0xffff) >= runCount)
                        MarkComplete(ref task);
                }
            }
            public static void MarkComplete(ref JobTask task) {
                var dep = task.Dependency;
                task = default;
                JobDependencies.Instance.MarkComplete(dep);
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
        [ThreadStatic] static string ThreadName;

        public static string CurrentThreadName => ThreadName ?? "Unknown";
        public static bool IsMainThread => ThreadName == "Main Thread";

        public JobScheduler() {
            ThreadName = "Main Thread";
            jobThreads = new JobThread[ThreadCount > 0 ? ThreadCount : (Environment.ProcessorCount * 2 / 3)];
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

#pragma warning disable
        public ushort CreateJob(Action<object?> action, object? context = null) {
            if (!EnableThreading) { action(context); return ushort.MaxValue; }
            return IntlPushTask(new JobTask() { Callback = action, Context = context, BatchBegin = 0, BatchCount = 0, });
        }
        public ushort CreateBatchJob(Action<RangeInt> action, int count) {
            if (!EnableThreading) { action(new RangeInt(0, count)); return ushort.MaxValue; }
            return IntlPushTask(new JobTask() { Callback = action, Context = null, BatchBegin = 0, BatchCount = (ushort)count, });
        }
        public ushort CreateBatchJob(Action<RangeInt> action, RangeInt range) {
            if (!EnableThreading) { action(range); return ushort.MaxValue; }
            return IntlPushTask(new JobTask() { Callback = action, Context = null, BatchBegin = (ushort)range.Start, BatchCount = (ushort)range.Length, });
        }
        public void MarkRunOnMain(ushort job) {
            Debug.Assert(taskArray[job].BatchCount == 0,
                "Batch jobs cannot run on main");
            taskArray[job].BatchBegin = ushort.MaxValue;
        }
#pragma warning restore
        public void ScheduleJob(ushort jobId, JobHandle handle) {
            taskArray[jobId].Dependency = handle;
            if (taskArray[jobId].IsMainThread) {
                while (!mainThreadTasks.TryPush(jobId)) {
                    Thread.Sleep(0);
                }
                return;
            }
            PushJobToThread(jobId);
            int wakeThreads = Math.Min(jobThreads.Length, taskArray[jobId].BatchCount);
            for (int c = 0; c < wakeThreads; ++c) jobThreads[c].RequireWake();
        }
        private bool PushJobToThread(int jobId) {
            // Try to add to local stack (is running in local thread)
            if (currentThread != null) {
                Debug.Assert(!currentThread.IsAsleep);
                if (currentThread.CurrentTasks.Count == 0 && currentThread.TryPushTask(jobId)) return true;
            }
            int threadId = FindBestThread();
            if (jobThreads[threadId].TryPushTask(jobId)) { jobThreads[threadId].RequireWake(); return true; }
            return false;
        }
        private int FindBestThread() {
            // Add to another thread
            int bestThread = 0;
            int bestPenalty = int.MaxValue;
            for (int i = 0; i < jobThreads.Length; i++) {
                var thread = jobThreads[i];
                int penalty = thread.CurrentTasks.Count + (thread.IsAsleep ? 1 : 0);
                if (penalty < bestPenalty) {
                    bestThread = i;
                    bestPenalty = penalty;
                }
            }
            return bestThread;
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

        internal bool TryRunMainThreadTask() {
            if (!mainThreadTasks.TryPop(out var taskId)) return false;
            ref var task = ref taskArray[taskId];
            Debug.Assert(task.BatchCount == 0);
            ((Action<object?>)task.Callback)(task.Context);
            JobThread.MarkComplete(ref task);
            return true;
        }
        public void RunMainThreadTasks() {
            while (TryRunMainThreadTask()) ;
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
