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
            var col1 = new ArchetypeComponentGetter<C1>(table, columnIs[0]);
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
            var col1 = new ArchetypeComponentGetter<C1>(table, columnIs[0]);
            var col2 = new ArchetypeComponentGetter<C2>(table, columnIs[1]);
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
            var col1 = new ArchetypeComponentGetter<C1>(table, columnIs[0]);
            var col2 = new ArchetypeComponentGetter<C2>(table, columnIs[1]);
            var col3 = new ArchetypeComponentGetter<C3>(table, columnIs[2]);
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
            var col1 = new ArchetypeComponentGetter<C1>(table, columnIs[0]);
            var col2 = new ArchetypeComponentGetter<C2>(table, columnIs[1]);
            var col3 = new ArchetypeComponentGetter<C3>(table, columnIs[2]);
            var col4 = new ArchetypeComponentGetter<C4>(table, columnIs[3]);
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
            var col1 = new ArchetypeComponentGetter<C1>(table, columnIs[0]);
            var col2 = new ArchetypeComponentGetter<C2>(table, columnIs[1]);
            var col3 = new ArchetypeComponentGetter<C3>(table, columnIs[2]);
            var col4 = new ArchetypeComponentGetter<C4>(table, columnIs[3]);
            var col5 = new ArchetypeComponentGetter<C5>(table, columnIs[4]);
            for (int i = 0; i <= table.MaxItem; i++) {
                if (!filter.IncludeEntity(table, i)) continue;
                Callback(ref col1[i], ref col2[i], ref col3[i], ref col4[i], ref col5[i]);
            }
        }
    }



    public struct LambdaId {
        public readonly int Index;
        public LambdaId(int index) { Index = index; }
        public override string ToString() { return Index.ToString(); }
        public override int GetHashCode() { return Index; }
        public static implicit operator int(LambdaId id) { return id.Index; }
        public static readonly LambdaId Invalid = new(-1);
    }
    struct LambdaCache {
        private struct LambdaKey : IEquatable<LambdaKey> {
            public QueryId RequestQuery;
            public QueryId Query;
            public TypeId[] Types;

            public bool Equals(LambdaKey other) {
                return RequestQuery == other.RequestQuery && Types.SequenceEqual(other.Types);
            }
            public override int GetHashCode() {
                var hash = RequestQuery.GetHashCode();
                foreach (var item in Types) hash = hash * 1253 + item.GetHashCode();
                return hash;
            }
        }
        private Dictionary<LambdaKey, LambdaId> lambdasByKey = new();
        private List<SystemLambda.Cache> lambdaCaches = new();
        public LambdaId RequireLambda(Stage stage, TypeId[] types, QueryId query) {
            var key = new LambdaKey() { RequestQuery = query, Types = types, };
            if (!lambdasByKey.TryGetValue(key, out var lambda)) {
                key.Query = key.RequestQuery;
                if (!key.Query.IsValid) {
                    var builder = new Query.Builder(stage);
                    foreach (var typeId in types) builder.With(typeId);
                    key.Query = builder.Build();
                }
                lambda = new LambdaId(lambdaCaches.Count);
                lambdaCaches.Add(new SystemLambda.Cache() {
                    QueryIndex = query,
                    ComponentIds = types,
                });
                lambdasByKey.Add(key, lambda);
            }
            return lambda;
        }
        public LambdaCache() { }

        public LambdaId RequireLambda<C1>(Stage stage, SystemLambda.Callback<C1> callback) {
            return RequireLambda(stage, new[] { stage.Context.RequireComponentTypeId<C1>(), }, QueryId.Invalid);
        }
        public LambdaId RequireLambda<C1, C2>(Stage stage, SystemLambda.Callback<C1, C2> callback) {
            return RequireLambda(stage, new[] {
                stage.Context.RequireComponentTypeId<C1>(), stage.Context.RequireComponentTypeId<C2>(),
            }, QueryId.Invalid);
        }
        public LambdaId RequireLambda<C1, C2, C3>(Stage stage, SystemLambda.Callback<C1, C2, C3> callback) {
            return RequireLambda(stage, new[] {
                stage.Context.RequireComponentTypeId<C1>(), stage.Context.RequireComponentTypeId<C2>(),
                stage.Context.RequireComponentTypeId<C3>(),
            }, QueryId.Invalid);
        }
        public LambdaId RequireLambda<C1, C2, C3, C4>(Stage stage, SystemLambda.Callback<C1, C2, C3, C4> callback) {
            return RequireLambda(stage, new[] {
                stage.Context.RequireComponentTypeId<C1>(), stage.Context.RequireComponentTypeId<C2>(),
                stage.Context.RequireComponentTypeId<C3>(), stage.Context.RequireComponentTypeId<C4>(),
            }, QueryId.Invalid);
        }
        public LambdaId RequireLambda<C1, C2, C3, C4, C5>(Stage stage, SystemLambda.Callback<C1, C2, C3, C4, C5> callback) {
            return RequireLambda(stage, new[] {
                stage.Context.RequireComponentTypeId<C1>(), stage.Context.RequireComponentTypeId<C2>(),
                stage.Context.RequireComponentTypeId<C3>(), stage.Context.RequireComponentTypeId<C4>(),
                stage.Context.RequireComponentTypeId<C5>(),
            }, QueryId.Invalid);
        }

        public SystemLambda.Cache GetLambda(LambdaId lambdaId) {
            return lambdaCaches[lambdaId];
        }
    }

}
