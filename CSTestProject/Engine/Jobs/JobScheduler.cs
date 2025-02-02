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
using TBatchIndex = System.UInt32;

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
            var handle = IntlCreateHandle(ushort.MaxValue);
            GetNode(handle).DependencyCount = -1;
            return handle;
        }
        public void ConvertDeferred(JobHandle deferred, JobHandle dependency) {
            ref var entry = ref GetNode(deferred);
            Debug.Assert(entry.DependencyCount == -1);
            entry.DependencyCount = 1;
            if (RegisterDependency(deferred, dependency)) {
                Interlocked.Decrement(ref entry.DependencyCount);
            } else {
                DeleteHandle(deferred);
            }
        }
        public JobHandle CreateHandle(ushort taskId) {
            var handle = IntlCreateHandle(taskId);
            GetNode(handle).DependencyCount = -1;
            JobScheduler.Instance.ScheduleJob(taskId, handle);
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
                JobScheduler.Instance.ScheduleJob(taskId, handle);
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
                    JobScheduler.Instance.ScheduleJob(entry.TaskId, depHandle);
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
        public const uint RunState_MainThread = TBatchIndex.MaxValue;
        public const uint RunState_Basic = TBatchIndex.MaxValue - 1;
        public const uint RunState_HasReturn = TBatchIndex.MaxValue - 2;
        public const uint RunState_HasUserData32 = TBatchIndex.MaxValue - 3;
        public const uint RunState_HasUserData64 = TBatchIndex.MaxValue - 4;
        public const uint RunState_HasUnmanaged = TBatchIndex.MaxValue - 5;
        public const uint RunState_SpecialIdBegin = TBatchIndex.MaxValue - 6;
        //public const TBatchIndex BatchBegin_MainThread = TBatchIndex.MaxValue;
        //public const ushort ContextCount_HasReturn = 0x8000;
        //public const ushort ContextCount_ContBegAsUData = 0x8001;
        //public const ushort ContextCount_RunStateAsUData = 0x8002;

        public bool IsQuitting { get; private set; }


        public class TaskPool {
            public const int OpMask = 0xff0000;
            private int taskCount = 0;
            public nint State;
            public bool HasOp => Op != 0;
            public byte Op => (byte)(State >> 16);
            public int Begin => (byte)(State >> 8);
            public int Count => (byte)(State >> 0);
            public int TaskCount => taskCount;
            public int VolatileCount => (byte)(Volatile.Read(ref State) >> 0);
            public const int Capacity = 4;
            public bool IsFull => Count >= Capacity;
            public bool IsEmpty => Count == 0;
            unsafe public struct TaskIdPool {
                public fixed int Id[4];
                public ref int this[int id] => ref Id[id];
            }
            public TaskIdPool Pool;
            public bool TryPush(int jobId) {
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
                    Pool[(begin + count - 1) % Capacity] = jobId;
                    Trace.Assert(Interlocked.CompareExchange(ref State, (begin << 8) | (count), state) == state);
                    var runCount = JobThread.GetRunCount(JobScheduler.Instance.jobArray[jobId]);
                    Interlocked.Add(ref taskCount, runCount);
                    return true;
                }
            }
            public bool TryPeek(out int jobId) {
                jobId = default;
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
                    jobId = Pool[begin];
                    var oldState = Interlocked.CompareExchange(ref State, (begin << 8) | (count), state);
                    Trace.Assert(oldState == state);
                    return true;
                }
                throw new Exception("Failed to pop an item");
            }
            public bool TryBeginPop(out int jobId, out nint state) {
                jobId = default;
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
                    jobId = Pool[begin];
                    return true;
                }
            }
            public bool YieldPop(nint state) {
                var oldState = Interlocked.CompareExchange(ref State, state & 0xffff, state);
                Trace.Assert(oldState == state);
                return true;
            }
            public bool EndPop(int jobId, nint state) {
                int count = (byte)(state >> 0);
                int begin = (byte)(state >> 8);
                if (++begin >= Capacity) begin -= Capacity;
                --count;
                Trace.Assert(Interlocked.CompareExchange(ref State, (begin << 8) | (count), state) == state);
                var runCount = JobThread.GetRunCount(JobScheduler.Instance.jobArray[jobId]);
                Interlocked.Add(ref taskCount, runCount);
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
            public T?[] Contexts = new T[Capacity];
            private int lastIndex = 0;
            public int Borrow(int count) {
                while (true) {
                    int newIndex = Interlocked.Add(ref lastIndex, count) - count;
                    newIndex &= Capacity - 1;
                    // If count will not fit in a page
                    if ((newIndex >> 6) != ((newIndex + count) >> 6)) continue;
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
            // How many batches this job can execute
            public TBatchIndex DataBegin, DataCount;
            public uint RunState;
            public JobHandle Dependency;
            public int RunCount => (int)(RunState & 0xff);
            public int CompleteCount => (int)((RunState >> 8) & 0xff);
            public bool IsMainThread => RunState == RunState_MainThread;
            public bool IsBasic => RunState == RunState_Basic;
            public bool DataIsContext => RunState >= RunState_Basic;
            public bool IsReturn => RunState == RunState_HasReturn;
        }
        public struct JobRun {
            public int TaskId;
            public TBatchIndex BatchBegin, BatchCount;
            public bool IsValid => TaskId >= 0;
            public static readonly JobRun Invalid = new() { TaskId = -1, };
        }
        private JobTask[] jobArray = new JobTask[1024];
        private int lastTaskIndex = -1;
        private int globalPoolTaskCount = 0;
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
            internal static TBatchIndex GetMinBatchSize(JobTask task) => (task.RunState >> 16) & 0xff;
            internal static int GetRunCount(JobTask task) {
                if (task.RunState >= RunState_SpecialIdBegin) return 1;
                return GetRunCount(GetMinBatchSize(task), task.DataCount);
            }
            internal static int GetRunCount(TBatchIndex minBatchCount, TBatchIndex maxCount) {
                int batchBit = 31 - BitOperations.LeadingZeroCount((uint)maxCount) - 4;
                batchBit = Math.Max(batchBit, 31 - BitOperations.LeadingZeroCount((uint)minBatchCount));
                var shift = Math.Max(batchBit, 0);
                return (int)((maxCount + (1 << shift) - 1) >> shift);
            }
            internal static void GetRunIndices(int index, JobTask task, TBatchIndex minBatchCount, out TBatchIndex batchBegin, out TBatchIndex batchCount) {
                int batchBit = 31 - BitOperations.LeadingZeroCount((uint)task.DataCount) - 4;
                batchBit = Math.Max(batchBit, 31 - BitOperations.LeadingZeroCount((uint)minBatchCount));
                batchBegin = (TBatchIndex)(index << Math.Max(batchBit, 0));
                var batchEnd = Math.Min(((index + 1) << Math.Max(batchBit, 0)), task.DataCount);
                batchCount = (TBatchIndex)(batchEnd - batchBegin);
                batchBegin += task.DataBegin;
            }
            private JobRun TryTakeJobRun() {
                if (!CurrentTasks.TryBeginPop(out var jobId, out var state)) return JobRun.Invalid;
                ref var task = ref Scheduler.jobArray[jobId];
                JobRun run = new() { TaskId = jobId, };
                if (task.RunState >= uint.MaxValue - 10) {
                    CurrentTasks.EndPop(jobId, state);
                } else {
                    var index = (int)(Interlocked.Increment(ref task.RunState) & 0xff);
                    if (index >= GetRunCount(task)) {
                        CurrentTasks.EndPop(jobId, state);
                    } else {
                        CurrentTasks.YieldPop(state);
                    }
                    GetRunIndices(index - 1, task, GetMinBatchSize(task), out run.BatchBegin, out run.BatchCount);
                }
                return run;
            }
            public bool TryRunWork() {
                var run = TryTakeJobRun();
                if (run.IsValid) {
                    Scheduler.ExecuteRun(run);
                    return true;
                }
                if (CurrentTasks.IsEmpty && Scheduler.globalPool.Count != 0) {
                    lock (Scheduler.globalPool) {
                        lock (CurrentTasks) {
                            if (CurrentTasks.IsEmpty && Scheduler.globalPool.Count != 0) {
                                CurrentTasks = Scheduler.globalPool.Dequeue();
                                Interlocked.Add(ref Scheduler.globalPoolTaskCount, CurrentTasks.TaskCount);
                                return false;
                            }
                        }
                    }
                }
                foreach (var thread in Scheduler.jobThreads) {
                    run = thread.TryTakeJobRun();
                    if (run.IsValid) {
                        Scheduler.ExecuteRun(run);
                        return true;
                    }
                }
                return false;
            }
            private void Invoke() {
                Scheduler.NotifyThreadWake(this);
                JobScheduler.currentThread = this;
                ThreadName = Name;
                int spinCount = 0;
                for (; !Scheduler.IsQuitting; ++spinCount) {
                    if (TryRunWork()) {
                        spinCount = 0;
                        continue;
                    }
                    if (spinCount > 200) {
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
                            Interlocked.Add(ref Scheduler.globalPoolTaskCount, CurrentTasks.TaskCount);
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
        public static JobThread? CurrentThread => currentThread;
        public static bool IsMainThread => ThreadName == "Main Thread";

        public JobScheduler() {
            if (ProfilerMarker.EnableTracy)
                Tracy.TracyPlotConfig(Tracy.CreateString("WakeCount"), step: true);
            ThreadName = "Main Thread";
            int coreCount = Platform.GetCoreCount();
            jobThreads = new JobThread[ThreadCount > 0 ? ThreadCount : coreCount];
            for (int i = 0; i < jobThreads.Length; i++) {
                jobThreads[i] = new(this, $"Job Thread {i}");
            }
        }

        private ushort IntlPushTask(JobTask task) {
            for (; ; ) {
                var index = (++lastTaskIndex) & (jobArray.Length - 1);
                ref var slot = ref jobArray[index];
                if (slot.Callback != null) continue;
                if (Interlocked.CompareExchange(ref slot.Callback, task.Callback, null) != null) continue;
                slot = task;
                return (ushort)index;
            }
        }

#pragma warning disable
        public ushort CreateTask(Action action) {
            if (!EnableThreading) { action(); return ushort.MaxValue; }
            return IntlPushTask(new JobTask() { Callback = action, DataBegin = 0, DataCount = 0, RunState = RunState_Basic, });
        }
        public ushort CreateTask(Action<object?> action, object? context = null) {
            if (!EnableThreading) { action(context); return ushort.MaxValue; }
            var contextI = contextPool.Borrow(1);
            contextPool.Contexts[contextI] = context;
            return IntlPushTask(new JobTask() { Callback = action, DataBegin = (ushort)contextI, DataCount = 1, RunState = RunState_Basic, });
        }
        public ushort CreateTask(Action<object?, object?> action, object? context1 = null, object? context2 = null) {
            if (!EnableThreading) { action(context1, context2); return ushort.MaxValue; }
            var contextI = contextPool.Borrow(2);
            contextPool.Contexts[contextI + 0] = context1;
            contextPool.Contexts[contextI + 1] = context2;
            return IntlPushTask(new JobTask() { Callback = action, DataBegin = (ushort)contextI, DataCount = 2, RunState = RunState_Basic, });
        }
        public ushort CreateTask(Action<uint> action, uint value) {
            if (!EnableThreading) { action(value); return ushort.MaxValue; }
            return IntlPushTask(new JobTask() { Callback = action, DataBegin = value, DataCount = 0, RunState = RunState_HasUserData32, });
        }
        public ushort CreateTask(Action<ulong> action, ulong value) {
            if (!EnableThreading) { action(value); return ushort.MaxValue; }
            return IntlPushTask(new JobTask() { Callback = action, DataBegin = (uint)value, DataCount = (uint)(value >> 32), RunState = RunState_HasUserData64, });
        }
        public ushort CreateTask<TResult>(Func<TResult> action) {
            var contextI = contextPool.Borrow(1);
            contextPool.Contexts[contextI] = null;
            if (!EnableThreading) {
                contextPool.Contexts[contextI] = action();
                action = null;
            }
            return IntlPushTask(new JobTask() { Callback = action, DataBegin = (ushort)contextI, DataCount = 0, RunState = RunState_HasReturn, });
        }
        private class Boxed<T> { public T Value; public static Stack<Boxed<T>> Pool = new(); }
        public ushort CreateTask<T>(Action<T> action, T value) {
            if (!EnableThreading) { action(value); return ushort.MaxValue; }
            if (typeof(T).IsValueType) {
                return CreateTask(static (callback, bundle) => {
                    var boxed = ((Boxed<T>)bundle);
                    ((Action<T>)callback)(boxed.Value);
                    ReturnBoxed<T>(boxed);
                }, AllocateBoxed(value));
            } else {
                return CreateTask(static (callback, boxed) => { ((Action<T>)callback)((T)boxed); }, (object)value);
            }
        }
        private static Boxed<T> AllocateBoxed<T>(T data) {
            var pool = Boxed<T>.Pool;
            Boxed<T>? boxed = default;
            lock (pool) { if (pool.Count > 0) boxed = pool.Pop(); }
            boxed ??= new();
            boxed.Value = data;
            return boxed;
        }
        private static void ReturnBoxed<T>(Boxed<T> boxed) {
            var pool = Boxed<T>.Pool;
            lock (pool) { pool.Push(boxed); }
        }

        public ushort CreateBatchTask(Action<RangeInt> action, int count, int minBatchSize) {
            return CreateBatchTask(action, new RangeInt(0, count), minBatchSize);
        }
        public ushort CreateBatchTask(Action<RangeInt> action, RangeInt range, int minBatchSize) {
            if (!EnableThreading) { action(range); return ushort.MaxValue; }
            return IntlPushTask(new JobTask() {
                Callback = action,
                DataBegin = (TBatchIndex)range.Start, DataCount = (TBatchIndex)range.Length,
                RunState = (uint)(minBatchSize << 16),
            });
        }
        public void MarkRunOnMain(ushort job) {
            Debug.Assert(jobArray[job].IsBasic,
                "Batch jobs cannot run on main");
            jobArray[job].RunState = RunState_MainThread;
        }
#pragma warning restore
        public void ScheduleJob(ushort jobId, JobHandle handle) {
            jobArray[jobId].Dependency = handle;
            if (jobArray[jobId].IsMainThread) {
                while (!mainThreadTasks.TryPush(jobId)) {
                    Thread.Sleep(0);
                }
                return;
            }
            PushJobToThread(jobId);
            ref var task = ref jobArray[jobId];
            if (task.RunState <= RunState_SpecialIdBegin) {
                int taskCount = 0;
                foreach (var thread in jobThreads) taskCount += thread.CurrentTasks?.TaskCount ?? 0;
                taskCount += globalPoolTaskCount;
                //var runCount = Math.Max(1, JobThread.GetRunCount(task));
                int wakeThreads = Math.Min(jobThreads.Length, taskCount);
                for (int c = 0; c < wakeThreads; ++c) jobThreads[c].RequireWake();
            }
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
                if (job.CurrentTasks.Count == 0) continue;
                if (job.CurrentTasks.Count > 1) return true;
                if (job.CurrentTasks.TryPeek(out var taskId)) {
                    ref var task = ref jobArray[taskId];
                    if (task.RunCount < JobThread.GetRunCount(task)) return true;
                }
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
            ref var task = ref jobArray[run.TaskId];
            if (task.DataIsContext) {
                if (task.DataCount == 0) {
                    ((Action)task.Callback)();
                    MarkComplete(ref task);
                } else if (task.DataCount == 1) {
                    ((Action<object?>)task.Callback)(
                        contextPool.Contexts[task.DataBegin]
                    );
                    MarkComplete(ref task);
                } else if (task.DataCount == 2) {
                    ((Action<object?, object?>)task.Callback)(
                        contextPool.Contexts[task.DataBegin],
                        contextPool.Contexts[task.DataBegin + 1]
                    );
                    MarkComplete(ref task);
                }
            } else if (task.IsReturn) {
                var resultSlot = task.DataBegin;
                if (task.Callback != null)
                    contextPool.Contexts[resultSlot] = ((Func<object>)task.Callback)();
                lock (resultIds) {
                    resultIds.Add(task.Dependency, (int)resultSlot);
                }
                MarkComplete(ref task);
            } else if (task.RunState == RunState_HasUserData32) {
                ((Action<uint>)task.Callback)(task.DataBegin);
                MarkComplete(ref task);
            } else if (task.RunState == RunState_HasUserData64) {
                ((Action<ulong>)task.Callback)(task.DataBegin | ((ulong)task.DataCount << 32));
                MarkComplete(ref task);
            } else {
                Debug.Assert(task.DataCount > 0);
                ((Action<RangeInt>)task.Callback)(new RangeInt((int)run.BatchBegin, (int)run.BatchCount));
                var runCount = JobThread.GetRunCount(task);
                var newState = Interlocked.Add(ref task.RunState, 0x100);
                if (((newState >> 8) & 0xff) >= runCount)
                    MarkComplete(ref task);
            }
        }
        internal void MarkComplete(ref JobTask task) {
            var dep = task.Dependency;
            if (task.DataIsContext) {
                if (task.DataCount > 0)
                    contextPool.Return((int)task.DataBegin, (int)task.DataCount);
            }
            task = default;
            JobDependencies.Instance.MarkComplete(dep);
        }

        internal bool TryRunMainThreadTask() {
            if (!mainThreadTasks.TryPop(out var taskId)) return false;
            ref var task = ref jobArray[taskId];
            Debug.Assert(task.IsMainThread);
            ExecuteRun(new() { TaskId = taskId, BatchBegin = task.DataBegin, BatchCount = task.DataCount, });
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

        public void CopyResult(JobHandle fromHandle, ushort toTask) {
            contextPool.Contexts[jobArray[toTask].DataBegin] = GetResult<object>(fromHandle);
        }
        public TResult GetResult<TResult>(JobHandle handle) {
            var resultId = -1;
            lock (resultIds) {
                resultId = resultIds[handle];
            }
            var result = contextPool.Contexts[resultId];
            return (TResult)result;
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
