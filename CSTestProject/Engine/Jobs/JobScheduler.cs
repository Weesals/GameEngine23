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
using Weesals.Engine.Profiling;

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
            public ushort TaskId;
            public Depenendecy Dependents;
            public override string ToString() {
                return $"Job<{TaskId}> @{DependencyCount}";
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
                entry = new() { Version = entry.Version, TaskId = jobId, };
                return new JobHandle(index, entry.Version);
            }
        }
        private void DeleteHandle(JobHandle handle) {
            var mask = GetBitMask(handle.Id);
            Trace.Assert((mask & Interlocked.And(ref Pages[GetPage(handle.Id)].DependenciesUsage, ~mask)) != 0,
                "Handle was already deleted");
        }
        private bool RegisterDependency(JobHandle handle, JobHandle dependent) {
            var depPage = Pages[GetPage(dependent.Id)];
            ref var entry = ref GetNode(handle);
            lock (depPage) {
                if ((depPage.DependenciesUsage & GetBitMask(dependent.Id)) == 0) return false;
                ref var depEntry = ref depPage.Dependencies[GetBit(dependent.Id)];
                if (depEntry.Version != dependent.Version) return false;
                Interlocked.Increment(ref entry.DependencyCount);
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
        public JobHandle CreateHandle(ushort taskId) {
            var handle = IntlCreateHandle(taskId);
            GetNode(handle).DependencyCount = -1;
            JobScheduler.Instance.ScheduleTask(taskId, handle);
            return handle;
        }
        public JobHandle CreateHandle(ushort taskId, JobHandle dependency) {
            if (taskId == ushort.MaxValue) return dependency;
            // If handle is expired
            if (!dependency.IsValid || GetIsComplete(dependency)) return CreateHandle(taskId);
            // If handle has no job
            //JobScheduler.Instance.Validate();
            if (TrySetTaskId(dependency, taskId)) return dependency;
            // Otherwise we must create a new handle
            var handle = IntlCreateHandle(taskId);
            if (!RegisterDependency(handle, dependency)) {
                GetNode(handle).DependencyCount = -1;
                JobScheduler.Instance.ScheduleTask(taskId, handle);
            }
            return handle;
        }
        public bool TrySetTaskId(JobHandle handle, ushort taskId) {
            var page = Pages[GetPage(handle.Id)];
            ref var entry = ref page.Dependencies[GetBit(handle.Id)];
            if (entry.TaskId != ushort.MaxValue) return false;
            lock (page) {
                if ((page.DependenciesUsage & (1ul << GetBit(handle.Id))) == 0) return false;
                if (entry.TaskId != ushort.MaxValue) return false;
                entry.TaskId = taskId;
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
            if (result.Singleton.Id != JobHandle.Invalid.Id) {
                var page = Pages[GetPage(result.Handle.Id)];
                ref var entry = ref page.Dependencies[GetBit(result.Handle.Id)];
                var count = entry.DependencyCount;
                DeleteHandle(result.Handle);
                return result.Singleton;
            }
            DecrementDependency(result.Handle.Id);
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
                if (entry.TaskId == ushort.MaxValue) {
                    MarkComplete(depHandle);
                } else {
                    JobScheduler.Instance.ScheduleTask(entry.TaskId, depHandle);
                }
            }
            return true;
        }

        public bool GetIsComplete(JobHandle handle) {
            var page = Pages[GetPage(handle.Id)];
            return (page.DependenciesUsage & (1ul << GetBit(handle.Id))) == 0
                || page.Dependencies[GetBit(handle.Id)].Version != handle.Version;
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

        public ushort GetTaskId(JobHandle handle) {
            var page = Pages[GetPage(handle.Id)];
            ref var entry = ref page.Dependencies[GetBit(handle.Id)];
            return entry.TaskId;
        }
    }

    public class JobScheduler {

        public const int ThreadCount = -1;
        public const bool EnableThreading = true;
        public const ushort BatchBegin_MainThread = ushort.MaxValue;
        public const ushort ContextCount_HasReturn = 0x8000;

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

        public class ValuePool<T> {
            public const int Capacity = 2048;
            public ulong[] Occupied = new ulong[Capacity / 64];
            public T[] Contexts = new T[Capacity];
            private int lastIndex = 0;
            public int Borrow(int count) {
                while (true) {
                    int newIndex = Interlocked.Add(ref lastIndex, count) - count;
                    newIndex &= Capacity - 1;
                    // If count will not fit in a page
                    if ((newIndex >> 6) >= ((newIndex + count) >> 6)) continue;
                    var page = newIndex >> 6;
                    var mask = ((1ul << count) - 1) << (newIndex & 63);
                    var oldMask = Interlocked.Or(ref Occupied[page], mask);
                    if ((oldMask & mask) != 0) {
                        oldMask |= ~mask;
                        Interlocked.And(ref Occupied[page], oldMask);
                        continue;
                    }
                    return newIndex;
                }
            }
            public void Return(int begin, int count) {
                int page = begin >> 6;
                ulong mask = ((1ul << count) - 1) << (begin & 63);
                Interlocked.And(ref Occupied[page], ~mask);
            }
        }

        public struct JobTask {
            public object Callback;
            public ushort ContextBegin, ContextCount;
            public JobHandle Dependency;
            public ushort BatchBegin, BatchCount;
            public uint RunState;
            public int RunCount => (int)(RunState & 0xffff);
            public int CompleteCount => (int)((RunState >> 16) & 0xffff);
            public bool IsMainThread => BatchBegin == BatchBegin_MainThread;
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
        private ValuePool<object> contextPool = new();

        private int wakeCount;

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
            internal static int GetRunCount(int maxCount) {
                int batchBit = 31 - BitOperations.LeadingZeroCount((uint)maxCount);
                var shift = Math.Max(batchBit - 4, 0);
                return (maxCount + (1 << shift) - 1) >> shift;
            }
            internal static void GetRunIndices(int index, JobTask task, out ushort batchBegin, out ushort batchCount) {
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
                Scheduler.NotifyThreadWake(this);
                JobScheduler.currentThread = this;
                ThreadName = Name;
                int spinCount = 200;
                for (; !Scheduler.IsQuitting; ++spinCount) {
                    var run = TryTakeJobRun();
                    if (run.IsValid) {
                        Scheduler.ExecuteRun(run);
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
                            Scheduler.ExecuteRun(run);
                            found = true;
                            break;
                        }
                    }
                    if (found) {
                        spinCount = 0;
                        continue;
                    }
                    if (spinCount > 100) {
                        Sleep();
                        spinCount = 0;
                    } else if (spinCount > 10) {
                        Thread.Sleep(0);
                    }
                }
                Scheduler.NotifyThreadSleep(this);
            }

            private void Sleep() {
                Volatile.Write(ref isAsleep, true);
                Interlocked.MemoryBarrier();
                if (CurrentTasks.VolatileCount == 0 && Scheduler.globalPool.Count == 0) {
                    Scheduler.NotifyThreadSleep(this);
                    SleepEvent.WaitOne();
                    Scheduler.NotifyThreadWake(this);
                }
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
            public override string ToString() {
                var status = IsAsleep ? "Asleep" : "Awake";
                return $"{Name} Queue: {CurrentTasks.VolatileCount} {status}";
            }
        }

        private void NotifyThreadWake(JobThread jobThread) {
            Interlocked.Increment(ref wakeCount);
            ProfilerMarker.SetValue("WakeCount", wakeCount);
        }
        private void NotifyThreadSleep(JobThread jobThread) {
            Interlocked.Decrement(ref wakeCount);
            ProfilerMarker.SetValue("WakeCount", wakeCount);
        }

        private JobThread[] jobThreads = Array.Empty<JobThread>();
        [ThreadStatic] static JobThread? currentThread;
        [ThreadStatic] static string ThreadName;
        private Dictionary<JobHandle, int> resultIds = new();

        public static string CurrentThreadName => ThreadName ?? "Unknown";
        public static bool IsMainThread => ThreadName == "Main Thread";

        public JobScheduler() {
            Tracy.TracyPlotConfig(Tracy.CreateString("WakeCount"), step: true);
            ThreadName = "Main Thread";
            jobThreads = new JobThread[ThreadCount > 0 ? ThreadCount : (Environment.ProcessorCount * 2 / 4)];
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
        public ushort CreateTask(Action action) {
            if (!EnableThreading) { action(); return ushort.MaxValue; }
            return IntlPushTask(new JobTask() { Callback = action, ContextBegin = 0, ContextCount = 0, BatchBegin = 0, BatchCount = 0, });
        }
        public ushort CreateTask(Action<object?> action, object? context = null) {
            if (!EnableThreading) { action(context); return ushort.MaxValue; }
            var contextI = contextPool.Borrow(1);
            contextPool.Contexts[contextI] = context;
            return IntlPushTask(new JobTask() { Callback = action, ContextBegin = (ushort)contextI, ContextCount = 1, BatchBegin = 0, BatchCount = 0, });
        }
        public ushort CreateTask(Action<object?, object?> action, object? context1 = null, object? context2 = null) {
            if (!EnableThreading) { action(context1, context2); return ushort.MaxValue; }
            var contextI = contextPool.Borrow(2);
            contextPool.Contexts[contextI + 0] = context1;
            contextPool.Contexts[contextI + 1] = context2;
            return IntlPushTask(new JobTask() { Callback = action, ContextBegin = (ushort)contextI, ContextCount = 2, BatchBegin = 0, BatchCount = 0, });
        }

        public ushort CreateTask<TResult>(Func<TResult> action) {
            var contextI = contextPool.Borrow(1);
            contextPool.Contexts[contextI] = null;
            if (!EnableThreading) {
                contextPool.Contexts[contextI] = action();
                action = null;
            }
            return IntlPushTask(new JobTask() { Callback = action, ContextBegin = (ushort)contextI, ContextCount = ContextCount_HasReturn, BatchBegin = 0, BatchCount = 0, });
        }

        public ushort CreateBatchTask(Action<RangeInt> action, int count) {
            if (!EnableThreading) { action(new RangeInt(0, count)); return ushort.MaxValue; }
            return IntlPushTask(new JobTask() { Callback = action, ContextCount = 0, BatchBegin = 0, BatchCount = (ushort)count, });
        }
        public ushort CreateBatchTask(Action<RangeInt> action, RangeInt range) {
            if (!EnableThreading) { action(range); return ushort.MaxValue; }
            return IntlPushTask(new JobTask() { Callback = action, ContextCount = 0, BatchBegin = (ushort)range.Start, BatchCount = (ushort)range.Length, });
        }
        public void MarkRunOnMain(ushort job) {
            Debug.Assert(taskArray[job].BatchCount == 0,
                "Batch jobs cannot run on main");
            taskArray[job].BatchBegin = BatchBegin_MainThread;
        }
#pragma warning restore
        public void ScheduleTask(ushort taskId, JobHandle handle) {
            taskArray[taskId].Dependency = handle;
            if (taskArray[taskId].IsMainThread) {
                while (!mainThreadTasks.TryPush(taskId)) {
                    Thread.Sleep(0);
                }
                return;
            }
            PushJobToThread(taskId);
            int wakeThreads = Math.Min(jobThreads.Length, taskArray[taskId].BatchCount);
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
        public bool GetHasQueuedTasks() {
            if (mainThreadTasks.Count > 0) return true;
            foreach (var job in jobThreads) {
                if (job.CurrentTasks.Count > 0) return true;
            }
            return false;
        }
        public void WakeHelperThread() {
            for (int i = 0; i < jobThreads.Length; i++) {
                if (!jobThreads[i].IsAsleep) continue;
                jobThreads[i].Wake();
                break;
            }
        }
        private int FindBestThread() {
            // Add to another thread
            int bestThread = 0;
            int bestPenalty = int.MaxValue;
            for (int i = 0; i < jobThreads.Length; i++) {
                var thread = jobThreads[i];
                int penalty = thread.CurrentTasks.Count * 2 + (thread.IsAsleep ? 0 : 1);
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

        // Called by job threads on the thread
        internal void ExecuteRun(JobRun run) {
            ref var task = ref taskArray[run.TaskId];
            if (task.BatchCount > 0) {
                ((Action<RangeInt>)task.Callback)(new RangeInt(run.BatchBegin, run.BatchCount));
                var runCount = JobThread.GetRunCount(task.BatchCount);
                var newState = Interlocked.Add(ref task.RunState, 0x10000);
                if (((newState >> 16) & 0xffff) >= runCount)
                    MarkComplete(ref task);
            } else if (task.ContextCount == 0) {
                ((Action)task.Callback)();
                MarkComplete(ref task);
            } else if (task.ContextCount == 1) {
                ((Action<object?>)task.Callback)(
                    contextPool.Contexts[task.ContextBegin]
                );
                MarkComplete(ref task);
            } else if (task.ContextCount == 2) {
                ((Action<object?, object?>)task.Callback)(
                    contextPool.Contexts[task.ContextBegin],
                    contextPool.Contexts[task.ContextBegin + 1]
                );
                MarkComplete(ref task);
            } else if (task.ContextCount == ContextCount_HasReturn) {
                var resultSlot = task.ContextBegin;
                if (task.Callback != null)
                    contextPool.Contexts[resultSlot] = ((Func<object>)task.Callback)();
                lock (resultIds) {
                    resultIds.Add(task.Dependency, resultSlot);
                }
                MarkComplete(ref task);
            }
        }
        internal void MarkComplete(ref JobTask task) {
            var dep = task.Dependency;
            if (task.ContextCount > 0)
                contextPool.Return(task.ContextBegin, task.ContextCount);
            task = default;
            JobDependencies.Instance.MarkComplete(dep);
        }

        internal bool TryRunMainThreadTask() {
            if (!mainThreadTasks.TryPop(out var taskId)) return false;
            ref var task = ref taskArray[taskId];
            Debug.Assert(task.BatchCount == 0);
            ExecuteRun(new() { TaskId = taskId, BatchBegin = task.BatchBegin, BatchCount = task.BatchCount, });
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

        public TResult ConsumeResult<TResult>(JobHandle handle) {
            var resultId = -1;
            lock (resultIds) {
                resultId = resultIds[handle];
                resultIds.Remove(handle);
            }
            var result = contextPool.Contexts[resultId];
            contextPool.Return(resultId, 1);
            return (TResult)result;
        }
        public void SetResult(JobHandle handle, object result) {
            var resultId = contextPool.Borrow(1);
            contextPool.Contexts[resultId] = result;
            lock (resultIds) {
                resultIds[handle] = resultId;
            }
        }

        public static JobScheduler Instance = new();

    }
}
