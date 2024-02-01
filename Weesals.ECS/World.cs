using System;
using System.Linq;
using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Weesals.ECS {

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

    // Management object for a collection of entities and components
    [DebuggerTypeProxy(typeof(Stage.DebugStageView))]
    public class Stage {
        public struct EntityData {
            public EntityAddress Address;
            public ArchetypeId ArchetypeId => Address.ArchetypeId;
            public int Row => Address.Row;
            public uint Version;
            public override string ToString() { return $"Archetype {ArchetypeId} Row {Row}"; }
        }

        public StageContext Context = new();

        private List<EntityData> entities = new();
        private List<Archetype> archetypes = new();
        private LambdaCache lambdaCache = new();
        //private List<SystemLambda.Cache> lambdaCaches = new();
        private List<Query> queries = new();
        private List<Query.Cache> queryCaches = new();
        private List<Listener> listeners = new();
        private List<ArchetypeListener> archetypeListeners = new();

        // Archetypes and queries must be unique, these allow efficient lookup
        // of existing instances
        private Dictionary<BitField, ArchetypeId> archetypesByTypes = new();
        private Dictionary<Query.Key, QueryId> queriesByTypes = new();

        private Archetype zeroArchetype;
        private int deletedEntity = -1;

        public Stage() {
            var entityTypeId = Context.RequireComponentTypeId<Entity>();
            Debug.Assert(entityTypeId.Packed == -1, "Entity must be invalid type id");
            zeroArchetype = new(new ArchetypeId(0), Context, default);
            archetypes.Add(zeroArchetype);
            archetypesByTypes.Add(default, new ArchetypeId(0));
            entities.Add(default);
        }

        public Entity CreateEntity() {
            lock (entities) {
                var entity = new Entity() {
                    Index = (uint)entities.Count,
                    Version = 1,
                };
                if (deletedEntity != -1) {
                    var entityData = entities[deletedEntity];
                    entity.Index = (uint)deletedEntity;
                    entity.Version = entityData.Version;
                    deletedEntity = entityData.Address.Row;
                    entityData.Address = EntityAddress.Invalid;
                    entities[(int)entity.Index] = entityData;
                } else {
                    entities.Add(new EntityData() {
                        Address = EntityAddress.Invalid,
                        Version = entity.Version,
                    });
                }
                return entity;
            }
        }
        public bool IsValid(Entity entity) {
            return entities[(int)entity.Index].Version == entity.Version;
        }
        public void DeleteEntity(Entity entity) {
            MoveEntity(entity, EntityAddress.Invalid);
            lock (entities) {
                var entityData = entities[(int)entity.Index];
                entityData.Address.Row = deletedEntity;
                entityData.Version++;
                entities[(int)entity.Index] = entityData;
                deletedEntity = (int)entity.Index;
            }
        }

        public EntityAddress RequireEntityAddress(Entity entity) {
            var entityData = entities[(int)entity.Index];
            if (entityData.Version != entity.Version) throw new Exception("Invalid entity");
            return entityData.Address;
        }
        public unsafe ref T AddComponent<T>(Entity entity) {
            return ref RequireComponent<T>(entity);
        }
        public unsafe ref T RequireComponent<T>(Entity entity, bool markDirty = true) {
            var componentTypeId = Context.RequireComponentTypeId<T>();
            var entityAddress = RequireEntityAddress(entity);
            var archetype = archetypes[entityAddress.ArchetypeId];

            var index = -1;
            if (componentTypeId.IsSparse) {
                index = archetype.RequireSparseComponent(componentTypeId, Context);
                archetype.RequireSparseIndex(index, entityAddress.Row);
            } else {
                if (archetype.TypeMask.Contains(componentTypeId))
                    throw new Exception($"Entity already has component {typeof(T).Name}");

                var builder = new StageContext.TypeInfoBuilder(Context);
                builder.Append(archetype.TypeMask);
                builder.AddComponent(componentTypeId);
                entityAddress = MoveEntity(entity,
                    RequireArchetypeIndex(builder.Build()));
                archetype = archetypes[entityAddress.ArchetypeId];
                index = archetype.RequireTypeIndex(componentTypeId);
            }
            ref var column = ref archetype.GetColumn(index);
            if (markDirty) column.NotifyMutation(entityAddress.Row);
            return ref column.GetValueRef<T>(entityAddress.Row);
        }
        public bool HasComponent<T>(Entity entity) {
            var entityAddr = RequireEntityAddress(entity);
            var componentTypeId = Context.RequireComponentTypeId<T>();
            var archetype = archetypes[entityAddr.ArchetypeId];
            if (ComponentType<T>.IsSparse) {
                if (!archetype.TryGetSparseComponent(componentTypeId, out var column, Context)) return false;
                if (!archetype.GetHasSparseComponent(column, entityAddr.Row)) return false;
            } else {
                if (!archetype.GetContainsType(componentTypeId, Context)) return false;
            }
            return true;
        }
        public ref readonly T GetComponent<T>(Entity entity) {
            return ref GetComponent<T>(RequireEntityAddress(entity));
        }
        public ref readonly T GetComponent<T>(EntityAddress entityData) {
            var componentTypeId = Context.RequireComponentTypeId<T>();
            var archetype = archetypes[entityData.ArchetypeId];
            var index = archetype.RequireTypeIndex(componentTypeId, Context);
            return ref archetype.GetValueAs<T>(index, entityData.Row);
        }
        public ref T GetComponentRef<T>(Entity entity) {
            return ref GetComponentRef<T>(RequireEntityAddress(entity));
        }
        public ref T GetComponentRef<T>(EntityAddress entityData, bool markDirty = true) {
            var componentTypeId = Context.RequireComponentTypeId<T>();
            var archetype = archetypes[entityData.ArchetypeId];
            var index = archetype.RequireTypeIndex(componentTypeId, Context);
            if (markDirty) archetype.NotifyMutation(index, entityData.Row);
            return ref archetype.GetValueAs<T>(index, entityData.Row);
        }
        public NullableRef<T> TryGetComponentRef<T>(Entity entity, bool markDirty = true) {
            return TryGetComponentRef<T>(RequireEntityAddress(entity), markDirty);
        }
        public NullableRef<T> TryGetComponentRef<T>(EntityAddress entityData, bool markDirty = true) {
            var componentTypeId = Context.RequireComponentTypeId<T>();
            var archetype = archetypes[entityData.ArchetypeId];
            if (!archetype.TryGetTypeIndex(componentTypeId, out var index, Context)) return default;
            if (ComponentType<T>.IsSparse && !archetype.GetHasSparseComponent(index, entityData.Row)) return default;
            if (markDirty) archetype.NotifyMutation(index, entityData.Row);
            return new NullableRef<T>(ref archetype.GetValueAs<T>(index, entityData.Row));
        }
        unsafe public bool RemoveComponent<T>(Entity entity) {
            var entityData = RequireEntityAddress(entity);
            var componentTypeId = Context.RequireComponentTypeId<T>();
            var archetype = archetypes[entityData.ArchetypeId];
            if (ComponentType<T>.IsSparse) {
                var column = archetype.RequireSparseComponent(componentTypeId, Context);
                Debug.Assert(archetype.GetHasSparseComponent(column, entityData.Row));
                archetype.ClearSparseIndex(column, entityData.Row);
                return true;
            }
            if (!archetype.TypeMask.Contains(componentTypeId))
                throw new Exception($"Entity doesnt have component {typeof(T).Name}");
            var builder = new StageContext.TypeInfoBuilder(Context);
            builder.Append(archetype.TypeMask);
            builder.RemoveComponent(componentTypeId);
            MoveEntity(entity,
                RequireArchetypeIndex(builder.Build()));
            return true;
        }

        public QueryId RequireQueryIndex(BitField withTypes, BitField withoutTypes, BitField withSparseTypes) {
            var key = new Query.Key(withTypes, withoutTypes, withSparseTypes);
            if (queriesByTypes.TryGetValue(key, out var queryI)) return queryI;
            lock (queries) {
                if (queriesByTypes.TryGetValue(key, out queryI)) return queryI;
                queryI = new QueryId(queries.Count);
                queries.Add(new(withTypes, withoutTypes, withSparseTypes));
                var cache = new Query.Cache();
                queryCaches.Add(cache);
                queriesByTypes.Add(key, queryI);
                lock (archetypes) {
                    foreach (var archetypeI in QueryArchetypes(queryI))
                        cache.MatchingArchetypes.Add(CreateArchetypeQueryCache(archetypeI, queryI));
                }
            }
            return queryI;
        }
        public bool QueryMightHaveMatches(QueryId query) {
            return queryCaches[query].MatchingArchetypes.Count > 0;
        }

        private Query.Cache.MatchedArchetype CreateArchetypeQueryCache(ArchetypeId archetypeI, QueryId queryI) {
            var archetype = archetypes[archetypeI];
            var query = queries[queryI];
            var componentOffsets = new int[query.WithTypes.BitCount];
            var it = query.WithTypes.GetEnumerator();
            for (int i = 0; i < componentOffsets.Length; i++) {
                Debug.Assert(it.MoveNext());
                componentOffsets[i] = archetype.RequireTypeIndex(new TypeId(it.Current), Context);
            }
            Debug.Assert(!it.MoveNext());
            var match = new Query.Cache.MatchedArchetype() {
                ArchetypeIndex = archetypeI,
                ComponentOffsets = componentOffsets,
            };
            return match;
        }
        public ArchetypeId RequireArchetypeIndex(BitField field) {
            if (archetypesByTypes.TryGetValue(field, out var archetypeI)) return archetypeI;
            lock (archetypes) {
                if (archetypesByTypes.TryGetValue(field, out archetypeI)) return archetypeI;
                archetypeI = new ArchetypeId(archetypes.Count);
                var archetype = new Archetype(archetypeI, Context, field);
                archetype.RequireSize(4);
                archetypes.Add(archetype);
                archetypesByTypes[field] = archetypeI;
                lock (queries) {
                    foreach (var listener in listeners) {
                        if (queries[listener.QueryId].Matches(archetype.TypeMask))
                            archetype.AddListener(listener);
                    }
                    var builder = new StageContext.TypeInfoBuilder(Context, archetype.ArchetypeListeners);
                    for (int i = 0; i < archetypeListeners.Count; i++) {
                        var listener = archetypeListeners[i];
                        if (queries[listener.QueryId].Matches(archetype.TypeMask))
                            builder.AddComponent(new TypeId(i));
                    }
                    archetype.ArchetypeListeners = builder.Build();
                    for (int q = 0; q < queries.Count; q++) {
                        var query = queries[q];
                        if (!query.Matches(archetype.TypeMask)) continue;
                        queryCaches[q].MatchingArchetypes.Add(CreateArchetypeQueryCache(archetypeI, new QueryId(q)));
                    }
                }
            }
            return archetypeI;
        }

        public EntityData MoveEntity(Entity entity, ArchetypeId newArchetypeI) {
            var newRow = archetypes[newArchetypeI].AllocateRow(entity);
            return MoveEntity(entity, new EntityAddress(newArchetypeI, newRow));
        }
        private EntityData MoveEntity(Entity entity, EntityAddress newAddr) {
            var oldData = entities[(int)entity.Index];
            var newData = oldData;
            newData.Address = newAddr;
            entities[(int)entity.Index] = newData;
            archetypes[oldData.ArchetypeId].CopyRowTo(oldData.Row,
                archetypes[newData.ArchetypeId], newData.Row);
            NotifyEntityChange(entity, oldData, newData);
            RemoveRow(oldData);
            return newData;
        }

        private void NotifyEntityChange(Entity entity, EntityAddress oldData, EntityAddress newData) {
            var oldArchetype = oldData.ArchetypeId >= 0 ? archetypes[oldData.ArchetypeId] : default;
            var newArchetype = newData.ArchetypeId >= 0 ? archetypes[newData.ArchetypeId] : default;
            var oldArchetypeMask = oldArchetype != null ? oldArchetype.TypeMask : default;
            var newArchetypeMask = newArchetype != null ? newArchetype.TypeMask : default;
            var oldArchetypeListeners = oldArchetype != null ? oldArchetype.ArchetypeListeners : default;
            var newArchetypeListeners = newArchetype != null ? newArchetype.ArchetypeListeners : default;
            if (oldArchetype != null) {
                foreach (var listener in oldArchetype.Listeners)
                    if (!queries[listener.QueryId].Matches(newArchetypeMask))
                        listener.OnRegister(entity, false);
                foreach (var listenerI in oldArchetypeListeners.Except(newArchetypeListeners))
                    archetypeListeners[listenerI].OnDelete?.Invoke(oldData);
            }
            if (oldArchetype != null && newArchetype != null) {
                foreach (var listenerI in oldArchetypeListeners.Intersect(newArchetypeListeners)) {
                    archetypeListeners[listenerI].OnMove?.Invoke(new ArchetypeListener.MoveEvent() {
                        From = oldData,
                        To = newData,
                    });
                }
            }
            if (newArchetype != null) {
                foreach (var listener in newArchetype.Listeners)
                    if (!queries[listener.QueryId].Matches(oldArchetypeMask))
                        listener.OnRegister(entity, true);
                foreach (var listenerI in newArchetypeListeners.Except(oldArchetypeListeners))
                    archetypeListeners[listenerI].OnCreate?.Invoke(newData);
            }
        }
        private void RemoveRow(EntityAddress entityData) {
            var archetype = archetypes[entityData.ArchetypeId];
            if (archetype.MaxItem == entityData.Row) {
                archetype.ReleaseRow(entityData.Row);
            } else {
                MoveEntity(archetype.Entities[archetype.MaxItem], new EntityAddress(archetype.Id, entityData.Row));
            }
        }

        public Entity UnsafeGetEntityByIndex(int entityIndex) {
            return new Entity() { Index = (uint)entityIndex, Version = entities[entityIndex].Version };
        }
        public Entity GetEntity(EntityAddress addr) {
            return GetArchetype(addr.ArchetypeId).Entities[addr.Row];
        }
        public Archetype GetArchetype(Entity entity) {
            return archetypes[RequireEntityAddress(entity).ArchetypeId];
        }
        public Archetype GetArchetype(ArchetypeId archId) {
            return archetypes[archId];
        }
        public Query GetQuery(QueryId query) {
            return queries[query];
        }

        public void AddListener(Listener listener) {
            listeners.Add(listener);
            foreach (var archI in queryCaches[listener.QueryId].MatchingArchetypes) {
                var archetype = archetypes[archI.ArchetypeIndex];
                archetype.AddListener(listener);
            }
        }
        public void AddListener(QueryId query, ArchetypeListener listener) {
            var listenerId = archetypeListeners.Count;
            archetypeListeners.Add(listener);
            foreach (var archI in GetArchetypes(query)) {
                var archetype = archetypes[archI];
                var builder = new StageContext.TypeInfoBuilder(Context, archetype.ArchetypeListeners);
                builder.AddComponent(new TypeId(listenerId));
                archetype.ArchetypeListeners = builder.Build();
                listener.NotifyCreate(archetype);
            }
        }

        public LambdaId RequireLambda<C1>(SystemLambda.Callback<C1> callback) {
            return lambdaCache.RequireLambda(this, callback);
        }
        public LambdaId RequireLambda<C1, C2>(SystemLambda.Callback<C1, C2> callback) {
            return lambdaCache.RequireLambda(this, callback);
        }
        public LambdaId RequireLambda<C1, C2, C3>(SystemLambda.Callback<C1, C2, C3> callback) {
            return lambdaCache.RequireLambda(this, callback);
        }
        public LambdaId RequireLambda<C1, C2, C3, C4>(SystemLambda.Callback<C1, C2, C3, C4> callback) {
            return lambdaCache.RequireLambda(this, callback);
        }
        public LambdaId RequireLambda<C1, C2, C3, C4, C5>(SystemLambda.Callback<C1, C2, C3, C4, C5> callback) {
            return lambdaCache.RequireLambda(this, callback);
        }
        public void InvokeLambda(SystemLambda lambda) {
            var sysCache = lambdaCache.GetLambda(lambda.LambdaId);
            Span<int> columnIs = stackalloc int[sysCache.ComponentIds.Length];
            int beginI = 0;
            // Entity must appear first (or not at all)
            if (sysCache.ComponentIds[0] == -1) columnIs[beginI++] = 0;
            foreach(var match in GetArchetypes(sysCache.QueryIndex)) {
                var archetype = archetypes[match.Index];
                if (archetype.IsEmpty) continue;
                for (int i = beginI; i < columnIs.Length; i++)
                    columnIs[i] = archetype.GetColumnId(sysCache.ComponentIds[i]);
                var archetypeRevision = archetype.Revision;
                lambda.InvokeForArchetype(archetype, columnIs, Filter.None);
                Debug.Assert(archetypeRevision == archetype.Revision,
                    "Archetype was mutated during system invocation");
            }
        }

        // Iterate all entities in the world
        public struct EntityEnumerator : IEnumerator<Entity> {
            public readonly Stage Stage;
            public int Archetype;
            public int Entity;
            public Entity Current => Stage.archetypes[Archetype].Entities[Entity];
            object IEnumerator.Current => Current;
            public EntityEnumerator(Stage stage) {
                Stage = stage;
                Archetype = 0;
                Entity = -1;
            }
            public void Dispose() { }
            public void Reset() { Archetype = -1; Entity = -1; }
            public bool MoveNext() {
                while (Archetype < Stage.archetypes.Count) {
                    if (++Entity < Stage.archetypes[Archetype].EntityCount) return true;
                    ++Archetype;
                    Entity = -1;
                }
                return false;
            }
            public EntityEnumerator GetEnumerator() { return this; }
        }
        public EntityEnumerator GetEntities() {
            return new EntityEnumerator(this);
        }

        // Iterate all components of an entity
        public struct EntityComponentEnumerator : IEnumerator<ComponentRef>, IEnumerable<ComponentRef> {
            public readonly StageContext Context;
            public readonly Archetype Archetype;
            public readonly int Row;
            public TypeId TypeId;
            public ComponentRef Current => new ComponentRef(Context, Archetype, Row, new TypeId(TypeId));
            object IEnumerator.Current => Current;
            public EntityComponentEnumerator(StageContext context, Archetype archetype, int row) {
                Context = context;
                Archetype = archetype;
                Row = row;
                TypeId = TypeId.Invalid;
            }
            public void Dispose() { }
            public void Reset() { TypeId = TypeId.Invalid; }
            public bool MoveNext() {
                var index = TypeId.Packed;
                if (index < TypeId.SparseHeader) {
                    index = Archetype.TypeMask.GetNextBit(index);
                    if (index != -1) { TypeId = new TypeId(index); return true; }
                    index = Archetype.SparseTypeMask.GetFirstBit();
                } else {
                    index = Archetype.SparseTypeMask.GetNextBit(index & TypeId.Tail);
                }
                while (index != -1 && !Archetype.GetHasSparseComponent(Archetype.RequireSparseComponent(TypeId.MakeSparse(index), Context), Row))
                    index = Archetype.SparseTypeMask.GetNextBit(index & TypeId.Tail);
                if (index == -1) return false;
                TypeId = TypeId.MakeSparse(index);
                return true;
            }
            public EntityComponentEnumerator GetEnumerator() { return this; }
            IEnumerator<ComponentRef> IEnumerable<ComponentRef>.GetEnumerator() => GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
        public EntityComponentEnumerator GetEntityComponents(Entity entity) {
            var entityData = entities[(int)entity.Index];
            if (entityData.Version != entity.Version) return default;
            return new EntityComponentEnumerator(Context, archetypes[entityData.ArchetypeId], entityData.Row);
        }

        // Iterate all archetypes with all of the specified types
        public struct ArchetypeEnumerator : IEnumerator<ArchetypeId> {
            public readonly Stage Stage;
            public readonly BitField WithTypes;
            public ArchetypeId Current { get; private set; }
            object IEnumerator.Current => Current;
            public ArchetypeEnumerator(Stage stage, BitField withTypes) {
                Stage = stage;
                WithTypes = withTypes;
                Current = new ArchetypeId(-1);
            }
            public void Dispose() { }
            public void Reset() { Current = new ArchetypeId(-1); }
            public bool MoveNext() {
                Current = new ArchetypeId(Current + 1);
                for (; Current < Stage.archetypes.Count; Current = new ArchetypeId(Current + 1)) {
                    var archetype = Stage.archetypes[Current];
                    if (archetype.TypeMask.ContainsAll(WithTypes)) return true;
                }
                return false;
            }
            public ArchetypeEnumerator GetEnumerator() { return this; }
        }
        public ArchetypeEnumerator GetArchetypesWith(BitField field) {
            return new ArchetypeEnumerator(this, field);
        }
        // Iterate all archetypes matching all with and none of without
        public struct ArchetypeWithWithoutEnumerator : IEnumerator<ArchetypeId>, IEnumerable<ArchetypeId> {
            public readonly Stage Stage;
            public readonly BitField WithTypes;
            public readonly BitField WithoutTypes;
            public ArchetypeId Current { get; private set; }
            object IEnumerator.Current => Current;
            public ArchetypeWithWithoutEnumerator(Stage stage, BitField withTypes, BitField withoutTypes) {
                Stage = stage;
                WithTypes = withTypes;
                WithoutTypes = withoutTypes;
                Current = new ArchetypeId(-1);
            }
            public void Dispose() { }
            public void Reset() { Current = new ArchetypeId(-1); }
            public bool MoveNext() {
                Current = new ArchetypeId(Current + 1);
                for (; Current < Stage.archetypes.Count; Current = new ArchetypeId(Current + 1)) {
                    var archetype = Stage.archetypes[Current];
                    if (!archetype.TypeMask.ContainsAll(WithTypes)) continue;
                    if (archetype.TypeMask.ContainsAny(WithoutTypes)) continue;
                    return true;
                }
                return false;
            }
            public ArchetypeWithWithoutEnumerator GetEnumerator() { return this; }
            IEnumerator<ArchetypeId> IEnumerable<ArchetypeId>.GetEnumerator() => this;
            IEnumerator IEnumerable.GetEnumerator() => this;
        }
        public ArchetypeWithWithoutEnumerator QueryArchetypes(QueryId queryId) {
            var query = queries[queryId];
            return new ArchetypeWithWithoutEnumerator(this, query.WithTypes, query.WithoutTypes);
        }
        // Iterate cached archetypes matching a query
        public struct ArchetypeQueryEnumerator : IEnumerator<ArchetypeId>, IEnumerable<ArchetypeId> {
            public readonly Stage Stage;
            public readonly QueryId QueryId;
            public readonly Query Query => Stage.queries[QueryId];
            public readonly Query.Cache QueryCache => Stage.queryCaches[QueryId];
            public ArchetypeId Current => QueryCache.MatchingArchetypes[archetypeIndex].ArchetypeIndex;
            public Archetype CurrentArchetype => Stage.archetypes[Current];
            object IEnumerator.Current => Current;
            private int archetypeIndex;
            public ArchetypeQueryEnumerator(Stage stage, QueryId queryId) {
                Stage = stage;
                QueryId = queryId;
                archetypeIndex = -1;
            }
            public void Dispose() { }
            public void Reset() { archetypeIndex = -1; }
            public bool MoveNext() {
                while (++archetypeIndex < QueryCache.MatchingArchetypes.Count) {
                    var query = Stage.queries[QueryId];
                    var archetype = CurrentArchetype;
                    if (archetype.SparseTypeMask.ContainsAll(query.WithSparseTypes))
                        return true;
                }
                return false;
            }
            public ArchetypeQueryEnumerator GetEnumerator() { return this; }
            IEnumerator<ArchetypeId> IEnumerable<ArchetypeId>.GetEnumerator() => this;
            IEnumerator IEnumerable.GetEnumerator() => this;
        }
        public ArchetypeQueryEnumerator GetArchetypes(QueryId query) {
            return new ArchetypeQueryEnumerator(this, query);
        }
        // Iterate entities of cached archetypes matching a query
        public struct EntityQueryEnumerator : IEnumerator<Entity>, IEnumerable<Entity> {
            public ArchetypeQueryEnumerator ArchetypeEnumerator;
            public Stage Stage => ArchetypeEnumerator.Stage;
            public int RowIndex;
            public Entity Current => ArchetypeEnumerator.CurrentArchetype.Entities[RowIndex];
            object IEnumerator.Current => Current;
            public EntityQueryEnumerator(ArchetypeQueryEnumerator archetypeEn) {
                ArchetypeEnumerator = archetypeEn;
                RowIndex = -1;
                PrimeFirst();
            }
            public void Reset() { ArchetypeEnumerator.Reset(); RowIndex = -1; PrimeFirst(); }
            public void Dispose() { }
            private void PrimeFirst() {
                if (!ArchetypeEnumerator.MoveNext()) RowIndex = -2;
            }
            public bool MoveNext() {
                if (RowIndex == -2) return false;
                var query = ArchetypeEnumerator.Query;
                while (true) {
                    var archetype = ArchetypeEnumerator.CurrentArchetype;
                    ++RowIndex;
                    while (true) {
                        if (RowIndex >= archetype.EntityCount) break;
                        var oldRow = RowIndex;
                        foreach (var typeId in query.WithSparseTypes) {
                            var column = archetype.RequireSparseComponent(TypeId.MakeSparse(typeId), Stage.Context);
                            RowIndex = archetype.GetNextSparseRowInclusive(column, RowIndex);
                            if (RowIndex == -1) break;
                            //if (oldRow != RowIndex) break;
                        }
                        if (oldRow == RowIndex) return true;
                        if (RowIndex == -1) break;
                    }
                    if (!ArchetypeEnumerator.MoveNext()) break;
                    RowIndex = -1;
                }
                return false;
            }
            public EntityQueryEnumerator GetEnumerator() { return this; }
            IEnumerator<Entity> IEnumerable<Entity>.GetEnumerator() => this;
            IEnumerator IEnumerable.GetEnumerator() => this;
        }
        public EntityQueryEnumerator GetEntities(QueryId query) {
            return new EntityQueryEnumerator(GetArchetypes(query));
        }

        public struct DebugEntity {
            public readonly Stage Stage;
            public readonly Entity Entity;
            public EntityComponentEnumerator Components => Stage.GetEntityComponents(Entity);
            public DebugEntity(Stage stage, Entity entity) {
                Stage = stage;
                Entity = entity;
            }
            public override string ToString() {
                return Components.Select(c => $"{c.GetRawType().Name}:{c.GetValue()}").Aggregate((i1, i2) => $"{i1},{i2}");
            }
        }
        public struct DebugEntityEnumerator : IEnumerator, IEnumerable {
            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            EntityEnumerator entityEn;
            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            object IEnumerator.Current => new DebugEntity(entityEn.Stage, entityEn.Current);
            public DebugEntityEnumerator(Stage stage) { entityEn = new(stage); }
            public void Dispose() { entityEn.Dispose(); }
            public void Reset() { entityEn.Reset(); }
            public bool MoveNext() { return entityEn.MoveNext(); }
            IEnumerator IEnumerable.GetEnumerator() => this;
        }
        public struct DebugStageView {
            [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
            public DebugEntityEnumerator View;
            public DebugStageView(Stage stage) { View = new DebugEntityEnumerator(stage); }
        }
    }

    public class World {
        public Stage Stage = new();
        public StageContext Context => Stage.Context;

        private List<SystemBase> systems = new();

        public Entity CreateEntity() { return Stage.CreateEntity(); }
        public Entity CreateEntity(Entity prefab) {
            var instance = Stage.CreateEntity();
            var prefabAddr = Stage.RequireEntityAddress(prefab);
            var instanceAddr = Stage.MoveEntity(instance, prefabAddr.ArchetypeId);
            var instanceArchetype = Stage.GetArchetype(instanceAddr.ArchetypeId);
            foreach (var prefabCmp in Stage.GetEntityComponents(prefab)) {
                var instanceCmp = new ComponentRef(Context, instanceArchetype, instanceAddr.Row, prefabCmp.TypeId);
                if (instanceCmp.GetComponentType().IsNoCopy) continue;
                prefabCmp.CopyTo(instanceCmp);
                instanceCmp.NotifyMutation();
            }
            return instance;
        }
        public bool IsValid(Entity entity) { return Stage.IsValid(entity); }
        public void DeleteEntity(Entity entity) { Stage.DeleteEntity(entity); }

        public Query.Builder BeginQuery() {
            return new Query.Builder(Stage);
        }

        public ref T AddComponent<T>(Entity entity) {
            return ref Stage.AddComponent<T>(entity);
        }
        public ref T AddComponent<T>(Entity entity, T value) {
            ref var component = ref Stage.AddComponent<T>(entity);
            component = value;
            return ref component;
        }
        public bool HasComponent<T>(Entity entity) {
            return Stage.HasComponent<T>(entity);
        }
        public ref readonly T GetComponent<T>(Entity entity) {
            return ref Stage.GetComponent<T>(entity);
        }
        public ref T GetComponentRef<T>(Entity entity) {
            return ref Stage.GetComponentRef<T>(entity);
        }
        public NullableRef<T> TryGetComponentRef<T>(Entity entity) {
            return Stage.TryGetComponentRef<T>(Stage.RequireEntityAddress(entity));
        }
        public bool RemoveComponent<T>(Entity entity) {
            return Stage.RemoveComponent<T>(entity);
        }

        public void OnRegister<C1>(Action<Entity, bool> callback) {
            var builder = new Query.Builder(Stage);
            builder.With<C1>();
            var query = builder.Build();
            Stage.AddListener(new Listener(query, callback));
        }
        public void OnRegister<C1, C2>(Action<Entity, bool> callback) {
            var builder = new Query.Builder(Stage);
            builder.With<C1>();
            builder.With<C2>();
            var query = builder.Build();
            Stage.AddListener(new Listener(query, callback));
        }
        public void OnRegister<C1, C2, C3>(Action<Entity, bool> callback) {
            var builder = new Query.Builder(Stage);
            builder.With<C1>();
            builder.With<C2>();
            builder.With<C3>();
            var query = builder.Build();
            Stage.AddListener(new Listener(query, callback));
        }


        public void AddSystem(SystemBase system) {
            systems.Add(system);
            system.Initialise(this);
        }
        public T GetSystem<T>() where T : SystemBase {
            foreach (var tsystem in systems) if (tsystem is T titem) return titem;
            return default;
        }
        public T GetOrCreateSystem<T>() where T : SystemBase, new() {
            foreach (var tsystem in systems) if (tsystem is T titem) return titem;
            var system = new T();
            AddSystem(system);
            return system;
        }

        public void Step() {
            for (int i = 0; i < systems.Count; i++) {
                var system = systems[i];
                system.Update();
            }
            for (int i = 0; i < systems.Count; i++) {
                if (systems[i] is not ILateUpdateSystem system) continue;
                system.OnLateUpdate();
            }
        }

        public void AddSystem(SystemLambda system, TypeId[] componentTypes = default, QueryId? queryI = null) {
            //AddSystem(new SystemInvokeLambda(system));
        }
        public void AddSystem<C1>(SystemLambda.Callback<C1> callback, QueryId? queryI = null) {
            AddSystem(new SystemLambda<C1>(callback, Stage), new[] {
                Context.RequireComponentTypeId<C1>()
            }, queryI);
        }
        public void AddSystem<C1, C2>(SystemLambda.Callback<C1, C2> callback, QueryId? queryI = null) {
            AddSystem(new SystemLambda<C1, C2>(callback, Stage), new[] {
                Context.RequireComponentTypeId<C1>(),
                Context.RequireComponentTypeId<C2>(),
            }, queryI);
        }
        public void AddSystem<C1, C2, C3>(SystemLambda.Callback<C1, C2, C3> callback, QueryId? queryI = null) {
            AddSystem(new SystemLambda<C1, C2, C3>(callback, Stage), new[] {
                Context.RequireComponentTypeId<C1>(),
                Context.RequireComponentTypeId<C2>(),
                Context.RequireComponentTypeId<C3>(),
            }, queryI);
        }
        public void AddSystem<C1, C2, C3, C4>(SystemLambda.Callback<C1, C2, C3, C4> callback, QueryId? queryI = null) {
            AddSystem(new SystemLambda<C1, C2, C3, C4>(callback, Stage), new[] {
                Context.RequireComponentTypeId<C1>(),
                Context.RequireComponentTypeId<C2>(),
                Context.RequireComponentTypeId<C3>(),
                Context.RequireComponentTypeId<C4>(),
            }, queryI);
        }
        public void AddSystem<C1, C2, C3, C4, C5>(SystemLambda.Callback<C1, C2, C3, C4, C5> callback, QueryId? queryI = null) {
            AddSystem(new SystemLambda<C1, C2, C3, C4, C5>(callback, Stage), new[] {
                Context.RequireComponentTypeId<C1>(),
                Context.RequireComponentTypeId<C2>(),
                Context.RequireComponentTypeId<C3>(),
                Context.RequireComponentTypeId<C4>(),
                Context.RequireComponentTypeId<C5>(),
            }, queryI);
        }

        public Stage.EntityEnumerator GetEntities() {
            return Stage.GetEntities();
        }
        public Stage.EntityComponentEnumerator GetEntityComponents(Entity entity) {
            return Stage.GetEntityComponents(entity);
        }
        public Stage.ArchetypeEnumerator GetArchetypesWith(BitField field) {
            return Stage.GetArchetypesWith(field);
        }
        public Stage.EntityQueryEnumerator GetEntities(QueryId query) {
            return Stage.GetEntities(query);
        }

        public TypedQueryIterator<C1> QueryAll<C1>() {
            var query = BeginQuery().With<C1>().Build();
            return new TypedQueryIterator<C1>(Stage.GetArchetypes(query));
        }
        public TypedQueryIterator<C1> QueryAll<C1>(QueryId query) {
            return new TypedQueryIterator<C1>(Stage.GetArchetypes(query));
        }
        public TypedQueryIterator<C1, C2> QueryAll<C1, C2>() {
            var query = BeginQuery().With<C1, C2>().Build();
            return new TypedQueryIterator<C1, C2>(Stage.GetArchetypes(query));
        }
        public TypedQueryIterator<C1, C2> QueryAll<C1, C2>(QueryId query) {
            return new TypedQueryIterator<C1, C2>(Stage.GetArchetypes(query));
        }
        public TypedQueryIterator<C1, C2, C3> QueryAll<C1, C2, C3>() {
            var query = BeginQuery().With<C1, C2, C3>().Build();
            return new TypedQueryIterator<C1, C2, C3>(Stage.GetArchetypes(query));
        }
        public TypedQueryIterator<C1, C2, C3> QueryAll<C1, C2, C3>(QueryId query) {
            return new TypedQueryIterator<C1, C2, C3>(Stage.GetArchetypes(query));
        }

    }

}
