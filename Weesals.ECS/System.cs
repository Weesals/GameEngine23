using System;
using System.Collections;
using System.Diagnostics;

namespace Weesals.ECS {
    public class UpdateAfterAttribute : Attribute {
        public Type Other;
        public UpdateAfterAttribute(Type type) { Other = type; }
    }
    public class UpdateBeforeAttribute : Attribute {
        public Type Other;
        public UpdateBeforeAttribute(Type type) { Other = type; }
    }
    public class UpdateInGroupAttribute : Attribute {
        public Type Other;
        public UpdateInGroupAttribute(Type type) { Other = type; }
    }

    public struct ComponentLookup<T> {
        public readonly TypeId TypeId;
        public readonly Stage Stage;
        public ComponentLookup(TypeId id, Stage stage) { TypeId = id; Stage = stage; }
        public ref T GetRefRW(Entity entity) {
            return ref Stage.GetComponentRef<T>(entity);
        }
        public NullableRef<T> GetRefOptional(Entity entity, bool markDirty = true) {
            return Stage.TryGetComponentRef<T>(entity, markDirty);
        }
        public bool HasComponent(Entity entity) {
            return Stage.HasComponent<T>(entity);
        }

        public bool TryGetComponent(Entity entity, out T component) {
            if (!HasComponent(entity)) { component = default; return false; }
            component = this[entity];
            return true;
        }

        public T this[Entity entity] {
            get => Stage.GetComponent<T>(entity);
            set => GetRefRW(entity) = value;
        }
    }
    public interface ILateUpdateSystem {
        void OnLateUpdate();
    }
    public interface IPostUpdateVerificationSystem {
        void DebugOnPostUpdate();
    }
    public interface IRollbackSystem {
        void CopyStateFrom(World other);
    }
    public abstract class SystemBase {
        public World World { get; private set; }
        public Stage Stage => World.Stage;
        public StageContext Context => Stage.Context;
        public void Initialise(World world) {
            World = world;
            OnCreate();
        }
        public void Dispose() {
            if (World == null) return;
            OnDestroy();
            World = null!;
        }

        public ComponentLookup<T> GetComponentLookup<T>() {
            return GetComponentLookup<T>(false);
        }
        public ComponentLookup<T> GetComponentLookup<T>(bool _) {
            return new ComponentLookup<T>(Context.RequireComponentTypeId<T>(), Stage);
        }

        protected virtual void OnCreate() { }
        protected virtual void OnDestroy() { }
        protected virtual void OnUpdate() { }

        public void Update() {
            OnUpdate();
        }
    }

    public class SystemInvokeLambda : SystemBase {
        public SystemLambda Lambda;
        public SystemInvokeLambda(SystemLambda lambda) { Lambda = lambda; }
        protected override void OnUpdate() {
            Stage.InvokeLambda(Lambda);
        }
    }
    public abstract class SystemLambda {
        public struct Cache {
            public QueryId QueryIndex;
            public TypeId[] ComponentIds;
        }
        public LambdaId LambdaId;
        public Cache LambdaCache;
        public delegate void Callback<C1>(ref C1 c1);
        public delegate void Callback<C1, C2>(ref C1 c1, ref C2 c2);
        public delegate void Callback<C1, C2, C3>(ref C1 c1, ref C2 c2, ref C3 c3);
        public delegate void Callback<C1, C2, C3, C4>(ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4);
        public delegate void Callback<C1, C2, C3, C4, C5>(ref C1 c1, ref C2 c2, ref C3 c3, ref C4 c4, ref C5 c5);
        public abstract void InvokeForArchetype(Archetype table, Span<int> columnIs, Filter filter);
    }
    public class SystemLambda<C1> : SystemLambda {
        public Callback<C1> Callback;
        public SystemLambda(Callback<C1> callback, Stage stage) {
            Callback = callback;
            LambdaId = stage.RequireLambda(Callback);
        }
        public override void InvokeForArchetype(Archetype table, Span<int> columnIs, Filter filter) {
            var col1 = (C1[])table.Columns[columnIs[0]].Items;
            for (int i = 0; i <= table.MaxItem; i++) {
                if (!filter.IncludeEntity(table, i)) continue;
                Callback(ref col1[i]);
            }
        }
    }
    public class SystemLambda<C1, C2> : SystemLambda {
        public Callback<C1, C2> Callback;
        public SystemLambda(Callback<C1, C2> callback, Stage stage) {
            Callback = callback;
            LambdaId = stage.RequireLambda(Callback);
        }
        public override void InvokeForArchetype(Archetype table, Span<int> columnIs, Filter filter) {
            var col1 = (C1[])table.Columns[columnIs[0]].Items;
            var col2 = (C2[])table.Columns[columnIs[1]].Items;
            for (int i = 0; i <= table.MaxItem; i++) {
                if (!filter.IncludeEntity(table, i)) continue;
                Callback(ref col1[i], ref col2[i]);
            }
        }
    }
    public class SystemLambda<C1, C2, C3> : SystemLambda {
        public Callback<C1, C2, C3> Callback;
        public SystemLambda(Callback<C1, C2, C3> callback, Stage stage) {
            Callback = callback;
            LambdaId = stage.RequireLambda(Callback);
        }
        public override void InvokeForArchetype(Archetype table, Span<int> columnIs, Filter filter) {
            var col1 = (C1[])table.Columns[columnIs[0]].Items;
            var col2 = (C2[])table.Columns[columnIs[1]].Items;
            var col3 = (C3[])table.Columns[columnIs[2]].Items;
            for (int i = 0; i <= table.MaxItem; i++) {
                if (!filter.IncludeEntity(table, i)) continue;
                Callback(ref col1[i], ref col2[i], ref col3[i]);
            }
        }
    }
    public class SystemLambda<C1, C2, C3, C4> : SystemLambda {
        public Callback<C1, C2, C3, C4> Callback;
        public SystemLambda(Callback<C1, C2, C3, C4> callback, Stage stage) {
            Callback = callback;
            LambdaId = stage.RequireLambda(Callback);
        }
        public override void InvokeForArchetype(Archetype table, Span<int> columnIs, Filter filter) {
            var col1 = (C1[])table.Columns[columnIs[0]].Items;
            var col2 = (C2[])table.Columns[columnIs[1]].Items;
            var col3 = (C3[])table.Columns[columnIs[2]].Items;
            var col4 = (C4[])table.Columns[columnIs[3]].Items;
            for (int i = 0; i <= table.MaxItem; i++) {
                if (!filter.IncludeEntity(table, i)) continue;
                Callback(ref col1[i], ref col2[i], ref col3[i], ref col4[i]);
            }
        }
    }
    public class SystemLambda<C1, C2, C3, C4, C5> : SystemLambda {
        public Callback<C1, C2, C3, C4, C5> Callback;
        public SystemLambda(Callback<C1, C2, C3, C4, C5> callback, Stage stage) {
            Callback = callback;
            LambdaId = stage.RequireLambda(Callback);
        }
        public override void InvokeForArchetype(Archetype table, Span<int> columnIs, Filter filter) {
            var col1 = (C1[])table.Columns[columnIs[0]].Items;
            var col2 = (C2[])table.Columns[columnIs[1]].Items;
            var col3 = (C3[])table.Columns[columnIs[2]].Items;
            var col4 = (C4[])table.Columns[columnIs[3]].Items;
            var col5 = (C5[])table.Columns[columnIs[3]].Items;
            for (int i = 0; i <= table.MaxItem; i++) {
                if (!filter.IncludeEntity(table, i)) continue;
                Callback(ref col1[i], ref col2[i], ref col3[i], ref col4[i], ref col5[i]);
            }
        }
    }
}
