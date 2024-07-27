using System;
using System.Linq;
using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Weesals.ECS {
    // Management object for a collection of entities and components
    [DebuggerTypeProxy(typeof(EntityManager.DebugManagerView))]
    public class EntityManager {

        public readonly EntityContext Context;
        public ColumnStorage ColumnStorage;
        public EntityStorage EntityStorage;

        private Archetype[] archetypes = new Archetype[32];
        private int archetypeCount;
        private LambdaCache lambdaCache = new();
        //private List<SystemLambda.Cache> lambdaCaches = new();
        private List<Query> queries = new(16);
        private List<Query.Cache> queryCaches = new(16);
        private List<ArchetypeListener> archetypeListeners = new(16);

        // Archetypes and queries must be unique, these allow efficient lookup
        // of existing instances
        private Dictionary<BitField, ArchetypeId> archetypesByTypes = new(64);
        private Dictionary<Query.Key, QueryId> queriesByTypes = new(64);
        public EntityCommandBuffer SharedCommandBuffer;

        public EntityManager(EntityContext context) {
            Context = context;
            ColumnStorage = new(context);
            EntityStorage = new();
            // Entity must be first "type"
            var entityTypeId = Context.RequireComponentTypeId<Entity>();
            ColumnStorage.RequireColumn(entityTypeId);
            //Debug.Assert(entityTypeId.Packed == -1, "Entity must be invalid type id");
            // Add the zero archetype (when entities are newly created)
            ref var zeroArchetype = ref archetypes[archetypeCount++];
            zeroArchetype = new Archetype(new ArchetypeId(0), default, ref ColumnStorage);
            zeroArchetype.SetDebugManager(this);
            archetypesByTypes.Add(default, new ArchetypeId(0));
            // Allocate the zero entity (reserve the index as its the "null" handle)
            EntityStorage.CreateEntity("None");
            SharedCommandBuffer = new(this);
        }

        public Entity CreateEntity(string name = "unknown") {
            return EntityStorage.CreateEntity(name);
        }
        public bool IsValid(Entity entity) {
            return EntityStorage.IsValid(entity);
        }
        public void DeleteEntity(Entity entity) {
            MoveEntity(entity, EntityAddress.Invalid);
            EntityStorage.DeleteEntity(entity);
        }
        public EntityStorage.EntityMeta GetEntityMeta(Entity entity) => EntityStorage.GetEntityMeta(entity);
        public EntityAddress RequireEntityAddress(Entity entity) => EntityStorage.RequireEntityAddress(entity);

        public unsafe ref T AddComponent<T>(Entity entity) {
            return ref RequireComponent<T>(entity);
        }
        public unsafe ref T RequireComponent<T>(Entity entity) {
            var componentTypeId = Context.RequireComponentTypeId<T>();
            var entityAddress = RequireEntityAddress(entity);
            ref var archetype = ref archetypes[entityAddress.ArchetypeId];

            var columnId = -1;
            var denseRow = entityAddress.Row;
            if (ComponentType<T>.IsSparse) {
                columnId = archetype.RequireSparseColumn(componentTypeId, this);
                denseRow = archetype.RequireSparseIndex(ref ColumnStorage, columnId, entityAddress.Row);
            } else {
                if (archetype.TypeMask.Contains(componentTypeId))
                    throw new Exception($"Entity already has component {typeof(T).Name}");

                var builder = new EntityContext.TypeInfoBuilder(Context);
                builder.Append(archetype.TypeMask);
                builder.AddComponent(componentTypeId);
                entityAddress = MoveEntity(entity, RequireArchetypeIndex(builder.Build()));
                archetype = ref archetypes[entityAddress.ArchetypeId];
                columnId = archetype.GetColumnId(componentTypeId);
                denseRow = entityAddress.Row;
            }
            return ref archetype.GetValueRW<T>(ref ColumnStorage, columnId, denseRow, entityAddress.Row);
        }
        public bool HasComponent<T>(Entity entity) { return HasComponent<T>(RequireEntityAddress(entity)); }
        public bool HasComponent<T>(EntityAddress entityAddr) {
            var componentTypeId = Context.RequireComponentTypeId<T>();
            ref var archetype = ref archetypes[entityAddr.ArchetypeId];
            if (ComponentType<T>.IsSparse) {
                if (!archetype.TryGetSparseColumn(componentTypeId, out var column)) return false;
                if (!archetype.GetHasSparseComponent(ref ColumnStorage, column, entityAddr.Row)) return false;
            } else {
                if (!archetype.GetHasColumn(componentTypeId)) return false;
            }
            return true;
        }
        public ref readonly T GetComponent<T>(Entity entity) {
            return ref GetComponent<T>(RequireEntityAddress(entity));
        }
        public ref readonly T GetComponent<T>(EntityAddress entityData) {
            var componentTypeId = Context.RequireComponentTypeId<T>();
            ref var archetype = ref archetypes[entityData.ArchetypeId];
            var columnId = archetype.GetColumnId(componentTypeId, Context);
            var row = entityData.Row;
            if (ComponentType<T>.IsSparse) row = archetype.TryGetSparseIndex(ref ColumnStorage, columnId, entityData.Row);
            return ref archetype.GetValueRO<T>(ref ColumnStorage, columnId, row);
        }
        public bool TryGetComponent<T>(Entity entity, out T component) {
            return TryGetComponent(RequireEntityAddress(entity), out component);
        }
        public bool TryGetComponent<T>(EntityAddress entityData, out T component) {
            var componentTypeId = Context.RequireComponentTypeId<T>();
            ref var archetype = ref archetypes[entityData.ArchetypeId];
            if (!archetype.TryGetColumnId(componentTypeId, out var columnId)) { component = default; return false; }
            var row = entityData.Row;
            if (ComponentType<T>.IsSparse) row = archetype.TryGetSparseIndex(ref ColumnStorage, columnId, entityData.Row);
            component = archetype.GetValueRO<T>(ref ColumnStorage, columnId, row);
            return true;
        }
        public ref T GetComponentRef<T>(Entity entity) {
            return ref GetComponentRef<T>(RequireEntityAddress(entity));
        }
        public ref T GetComponentRef<T>(EntityAddress entityData) {
            var componentTypeId = Context.RequireComponentTypeId<T>();
            ref var archetype = ref archetypes[entityData.ArchetypeId];
            var columnId = archetype.GetColumnId(componentTypeId, Context);
            archetype.NotifyMutation(ref ColumnStorage, columnId, entityData.Row);
            var row = entityData.Row;
            if (ComponentType<T>.IsSparse) row = archetype.TryGetSparseIndex(ref ColumnStorage, columnId, entityData.Row);
            return ref archetype.GetValueRW<T>(ref ColumnStorage, columnId, row, entityData.Row);
        }
        public NullableRef<T> TryGetComponentRef<T>(Entity entity, bool markDirty = true) {
            return TryGetComponentRef<T>(RequireEntityAddress(entity), markDirty);
        }
        public NullableRef<T> TryGetComponentRef<T>(EntityAddress entityData, bool markDirty = true) {
            var componentTypeId = Context.RequireComponentTypeId<T>();
            ref var archetype = ref archetypes[entityData.ArchetypeId];
            if (!archetype.TryGetColumnId(componentTypeId, out var columnId)) return default;
            var row = entityData.Row;
            if (ComponentType<T>.IsSparse) {
                row = archetype.TryGetSparseIndex(ref ColumnStorage, columnId, entityData.Row);
                if (row < 0) return default;
            }
            if (markDirty) archetype.NotifyMutation(ref ColumnStorage, columnId, entityData.Row);
            return new NullableRef<T>(ref archetype.GetValueRW<T>(ref ColumnStorage, columnId, row, entityData.Row));
        }
        unsafe public bool TryRemoveComponent<T>(Entity entity) {
            var entityData = RequireEntityAddress(entity);
            var componentTypeId = Context.RequireComponentTypeId<T>();
            ref var archetype = ref archetypes[entityData.ArchetypeId];
            if (ComponentType<T>.IsSparse) {
                var column = archetype.RequireSparseColumn(componentTypeId, this);
                if (!archetype.GetHasSparseComponent(ref ColumnStorage, column, entityData.Row)) return false;
                archetype.ClearSparseComponent(ref ColumnStorage, column, entityData.Row);
                return true;
            }
            if (!archetype.TypeMask.Contains(componentTypeId))
                return false;//throw new Exception($"Entity doesnt have component {typeof(T).Name}");
            var builder = new EntityContext.TypeInfoBuilder(Context);
            builder.Append(archetype.TypeMask);
            builder.RemoveComponent(componentTypeId);
            MoveEntity(entity,
                RequireArchetypeIndex(builder.Build()));
            return true;
        }
        unsafe public bool RemoveComponent<T>(Entity entity) {
            bool result = TryRemoveComponent<T>(entity);
            Trace.Assert(result);
            return result;
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
            ref var archetype = ref archetypes[archetypeI];
            var query = queries[queryI];
            var componentOffsets = new int[query.WithTypes.BitCount];
            var it = query.WithTypes.GetEnumerator();
            for (int i = 0; i < componentOffsets.Length; i++) {
                Debug.Assert(it.MoveNext());
                componentOffsets[i] = archetype.GetColumnId(new TypeId(it.Current), Context);
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
                foreach (var bit in field) {
                    ColumnStorage.RequireColumn(new(bit));
                }
                archetypeI = new ArchetypeId(archetypeCount);
                ref var archetype = ref archetypes[archetypeCount++];
                archetype = new Archetype(archetypeI, field, ref ColumnStorage);
                archetype.SetDebugManager(this);
                archetype.RequireSize(ref ColumnStorage, 4);
                archetypesByTypes[field] = archetypeI;
                lock (queries) {
                    var builder = new EntityContext.TypeInfoBuilder(Context, archetype.ArchetypeListeners);
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

        public EntityStorage.EntityData MoveEntity(Entity entity, ArchetypeId newArchetypeI) {
            var newRow = archetypes[newArchetypeI].AllocateRow(ref ColumnStorage, entity);
            return MoveEntity(entity, new EntityAddress(newArchetypeI, newRow));
        }
        private EntityStorage.EntityData MoveEntity(Entity entity, EntityAddress newAddr) {
            using var mover = new EntityMover(this, entity, newAddr);
            return mover.Commit();
        }
        public EntityMover BeginMoveEntity(Entity entity, ArchetypeId newArchetypeI) {
            var newRow = archetypes[newArchetypeI].AllocateRow(ref ColumnStorage, entity);
            return new EntityMover(this, entity, new EntityAddress(newArchetypeI, newRow));
        }
        public struct EntityMover : IDisposable {
            public readonly EntityManager Manager;
            public readonly Entity Entity;
            public readonly EntityStorage.EntityData From;
            public readonly EntityAddress To;
            public EntityMover(EntityManager manager, Entity entity, EntityAddress to) {
                Manager = manager;
                Entity = entity;
                From = Manager.EntityStorage.GetEntityDataRef(entity);
                To = to;
                var newData = From;
                newData.Address = to;
                Manager.EntityStorage.GetEntityDataRef(entity) = newData;
                if (newData.Row >= 0) {
                    Manager.ColumnStorage.CopyRowTo(
                        ref Manager.archetypes[From.ArchetypeId], From.Row,
                        ref Manager.archetypes[newData.ArchetypeId], newData.Row,
                        Manager
                    );
                }
            }
            public ref TComponent GetComponentRef<TComponent>() {
                return ref Manager.GetComponentRef<TComponent>(To);
            }
            public void CopyFrom(ComponentRef other) {
                var item = other.RawItem;
                CopyFrom(other.TypeId, item.Array, item.Index);
            }
            public void CopyFrom(TypeId typeId, Array data, int row) {
                ref var archetype = ref Manager.GetArchetype(To.ArchetypeId);
                var columnId = archetype.GetColumnId(typeId, Manager.Context);
                Manager.ColumnStorage.CopyValue(ref archetype, columnId, To.Row, data, row);
                archetype.NotifyMutation(ref Manager.ColumnStorage, columnId, To.Row);
            }
            public EntityStorage.EntityData Commit() {
                var newData = Manager.EntityStorage.GetEntityDataRef(Entity);
                Manager.NotifyEntityChange(Entity, From, To);
                Manager.RemoveRow(From);
                return newData;
            }
            public void Dispose() {
                // TODO: Verify that was committed
            }
        }

        private void NotifyEntityChange(Entity entity, EntityAddress oldData, EntityAddress newData) {
            var oldArchetype = oldData.ArchetypeId >= 0 ? archetypes[oldData.ArchetypeId] : default;
            var newArchetype = newData.ArchetypeId >= 0 ? archetypes[newData.ArchetypeId] : default;
            var oldArchetypeListeners = oldData.ArchetypeId >= 0 ? oldArchetype.ArchetypeListeners : default;
            var newArchetypeListeners = newData.ArchetypeId >= 0 ? newArchetype.ArchetypeListeners : default;
            if (oldData.ArchetypeId >= 0) {
                foreach (var listenerI in BitField.Except(oldArchetypeListeners, newArchetypeListeners))
                    archetypeListeners[listenerI].OnDelete?.Invoke(oldData);
            }
            if (oldData.ArchetypeId >= 0 && newData.ArchetypeId >= 0) {
                foreach (var listenerI in BitField.Intersection(oldArchetypeListeners, newArchetypeListeners)) {
                    archetypeListeners[listenerI].OnMove?.Invoke(new ArchetypeListener.MoveEvent() {
                        From = oldData,
                        To = newData,
                    });
                }
            }
            if (newData.ArchetypeId >= 0) {
                foreach (var listenerI in BitField.Except(newArchetypeListeners, oldArchetypeListeners))
                    archetypeListeners[listenerI].OnCreate?.Invoke(newData);
            }
        }
        private void RemoveRow(EntityAddress entityData) {
            ref var archetype = ref archetypes[entityData.ArchetypeId];
            archetype.ClearSparseRow(ref ColumnStorage, entityData.Row);
            if (archetype.MaxItem == entityData.Row) {
                ColumnStorage.ReleaseRow(ref archetype, entityData.Row);
            } else {
                MoveEntity(archetype.GetEntities(ref ColumnStorage)[archetype.MaxItem], new EntityAddress(archetype.Id, entityData.Row));
            }
        }

        public Entity UnsafeGetEntityByIndex(int entityIndex) {
            return new Entity((uint)entityIndex, EntityStorage.GetEntityDataRef(entityIndex).Version);
        }
        public Entity GetEntity(EntityAddress addr) {
            return GetArchetype(addr.ArchetypeId).GetEntities(ref ColumnStorage)[addr.Row];
        }
        public ref Archetype GetArchetype(Entity entity) {
            return ref archetypes[RequireEntityAddress(entity).ArchetypeId];
        }
        public ref Archetype GetArchetype(ArchetypeId archId) {
            return ref archetypes[archId];
        }
        public Query GetQuery(QueryId query) {
            return queries[query];
        }

        public void AddListener(QueryId query, ArchetypeListener listener) {
            var listenerId = archetypeListeners.Count;
            listener.QueryId = query;
            archetypeListeners.Add(listener);
            foreach (var archI in GetArchetypes(query)) {
                ref var archetype = ref archetypes[archI];
                var builder = new EntityContext.TypeInfoBuilder(Context, archetype.ArchetypeListeners);
                builder.AddComponent(new TypeId(listenerId));
                archetype.ArchetypeListeners = builder.Build();
                for (int i = 0; i < archetype.EntityCount; i++) {
                    listener.OnCreate?.Invoke(new EntityAddress() {
                        ArchetypeId = archetype.Id,
                        Row = i,
                    });
                }
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
                ref var archetype = ref archetypes[match.Index];
                if (archetype.IsEmpty) continue;
                for (int i = beginI; i < columnIs.Length; i++)
                    columnIs[i] = archetype.GetColumnId(sysCache.ComponentIds[i]);
                var archetypeRevision = archetype.Revision;
                lambda.InvokeForArchetype(ref ColumnStorage, archetype, columnIs, Filter.None);
                Debug.Assert(archetypeRevision == archetype.Revision,
                    "Archetype was mutated during system invocation");
            }
        }

        // Iterate all entities in the world
        public struct EntityEnumerator : IEnumerator<Entity> {
            public readonly EntityManager Manager;
            public int Archetype;
            public int Entity;
            public Entity Current => Manager.archetypes[Archetype].GetEntities(ref Manager.ColumnStorage)[Entity];
            object IEnumerator.Current => Current;
            public EntityEnumerator(EntityManager manager) {
                Manager = manager;
                Archetype = 0;
                Entity = -1;
            }
            public void Dispose() { }
            public void Reset() { Archetype = -1; Entity = -1; }
            public bool MoveNext() {
                while (Archetype < Manager.archetypeCount) {
                    if (++Entity < Manager.archetypes[Archetype].EntityCount) return true;
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
            public readonly EntityManager Manager;
            public readonly ArchetypeId ArchetypeId;
            public readonly EntityContext Context => Manager.Context;
            public readonly int Row;
            public TypeId TypeId;
            public ref Archetype Archetype => ref Manager.GetArchetype(ArchetypeId);
            public ComponentRef Current => new ComponentRef(Manager, ArchetypeId, Row, TypeId);
            object IEnumerator.Current => Current;
            public EntityComponentEnumerator(EntityManager manager, ArchetypeId archetypeId, int row) {
                Manager = manager;
                ArchetypeId = archetypeId;
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
                while (index != -1 && !Archetype.GetHasSparseComponent(ref Manager.ColumnStorage, Archetype.RequireSparseColumn(TypeId.MakeSparse(index), Manager), Row))
                    index = Archetype.SparseTypeMask.GetNextBit(index & TypeId.Tail);
                if (index == -1) return false;
                TypeId = TypeId.MakeSparse(index);
                return true;
            }
            private int GetRowIndex() {
                var row = Row;
                if (TypeId.IsSparse) {
                    row = Archetype.TryGetSparseIndex(ref Manager.ColumnStorage, Archetype.GetColumnId(TypeId), row);
                }
                return row;
            }
            public EntityComponentEnumerator GetEnumerator() { return this; }
            IEnumerator<ComponentRef> IEnumerable<ComponentRef>.GetEnumerator() => GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
        public EntityComponentEnumerator GetEntityComponents(Entity entity) {
            var entityData = EntityStorage.GetEntityDataRef(entity);
            if (entityData.Version != entity.Version) return default;
            return new EntityComponentEnumerator(this, entityData.ArchetypeId, entityData.Row);
        }

        // Iterate all archetypes with all of the specified types
        public struct ArchetypeEnumerator : IEnumerator<ArchetypeId> {
            public readonly EntityManager Manager;
            public readonly BitField WithTypes;
            public ArchetypeId Current { get; private set; }
            object IEnumerator.Current => Current;
            public ArchetypeEnumerator(EntityManager manager, BitField withTypes) {
                Manager = manager;
                WithTypes = withTypes;
                Current = new ArchetypeId(-1);
            }
            public void Dispose() { }
            public void Reset() { Current = new ArchetypeId(-1); }
            public bool MoveNext() {
                Current = new ArchetypeId(Current + 1);
                for (; Current < Manager.archetypeCount; Current = new ArchetypeId(Current + 1)) {
                    var archetype = Manager.archetypes[Current];
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
            public readonly EntityManager Manager;
            public readonly BitField WithTypes;
            public readonly BitField WithoutTypes;
            public ArchetypeId Current { get; private set; }
            object IEnumerator.Current => Current;
            public ArchetypeWithWithoutEnumerator(EntityManager manager, BitField withTypes, BitField withoutTypes) {
                Manager = manager;
                WithTypes = withTypes;
                WithoutTypes = withoutTypes;
                Current = new ArchetypeId(-1);
            }
            public void Dispose() { }
            public void Reset() { Current = new ArchetypeId(-1); }
            public bool MoveNext() {
                Current = new ArchetypeId(Current + 1);
                for (; Current < Manager.archetypeCount; Current = new ArchetypeId(Current + 1)) {
                    var archetype = Manager.archetypes[Current];
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
            public readonly EntityManager Manager;
            public readonly QueryId QueryId;
            public readonly Query Query => Manager.queries[QueryId];
            public readonly Query.Cache QueryCache => Manager.queryCaches[QueryId];
            public ArchetypeId Current => QueryCache.MatchingArchetypes[archetypeIndex].ArchetypeIndex;
            public Archetype CurrentArchetype => Manager.archetypes[Current];
            object IEnumerator.Current => Current;
            private int archetypeIndex;
            public ArchetypeQueryEnumerator(EntityManager manager, QueryId queryId) {
                Manager = manager;
                QueryId = queryId;
                archetypeIndex = -1;
            }
            public void Dispose() { }
            public void Reset() { archetypeIndex = -1; }
            public bool MoveNext() {
                while (++archetypeIndex < QueryCache.MatchingArchetypes.Count) {
                    var query = Manager.queries[QueryId];
                    var archetype = CurrentArchetype;
                    if (archetype.SparseTypeMask.ContainsAll(query.WithSparseTypes))
                        return true;
                }
                return false;
            }
            public ArchetypeQueryEnumerator GetEnumerator() { return this; }
            IEnumerator<ArchetypeId> IEnumerable<ArchetypeId>.GetEnumerator() => this;
            IEnumerator IEnumerable.GetEnumerator() => this;

            public ComponentAccessor CreateAccessor() {
                return new(Manager, Current);
            }
        }
        public ArchetypeQueryEnumerator GetArchetypes(QueryId query) {
            return new ArchetypeQueryEnumerator(this, query);
        }
        // Iterate entities of cached archetypes matching a query
        public struct EntityQueryEnumerator : IEnumerator<Entity>, IEnumerable<Entity> {
            public ArchetypeQueryEnumerator ArchetypeEnumerator;
            public EntityManager Manager => ArchetypeEnumerator.Manager;
            public int RowIndex;
            public Entity Current => ArchetypeEnumerator.CurrentArchetype.GetEntities(ref Manager.ColumnStorage)[RowIndex];
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
                            var column = archetype.RequireSparseColumn(TypeId.MakeSparse(typeId), Manager);
                            RowIndex = archetype.GetNextSparseRowInclusive(ref Manager.ColumnStorage, column, RowIndex);
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
            public readonly EntityManager Manager;
            public readonly Entity Entity;
            public EntityComponentEnumerator Components => Manager.GetEntityComponents(Entity);
            public DebugEntity(EntityManager manager, Entity entity) {
                Manager = manager;
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
            int count;
            [DebuggerBrowsable(DebuggerBrowsableState.Never)]
            object IEnumerator.Current => new DebugEntity(entityEn.Manager, entityEn.Current);
            public DebugEntityEnumerator(EntityManager manger) { entityEn = new(manger); }
            public void Dispose() { entityEn.Dispose(); }
            public void Reset() { entityEn.Reset(); }
            public bool MoveNext() { return ++count < 1000 && entityEn.MoveNext(); }
            IEnumerator IEnumerable.GetEnumerator() => this;
        }
        public struct DebugManagerView {
            [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
            public DebugEntityEnumerator View;
            public DebugManagerView(EntityManager manager) { View = new DebugEntityEnumerator(manager); }
        }
    }

    public class World {
        public readonly EntityContext Context = new();
        public readonly EntityManager Manager;

        private List<SystemBase> systems = new(8);

        public World() {
            Manager = new(Context);
            Context.OnTypeIdCreated += Context_OnTypeIdCreated;
        }

        private void Context_OnTypeIdCreated(TypeId typeId) {
            
        }

        public Entity CreateEntity() { return Manager.EntityStorage.CreateEntity(); }
        public Entity CreateEntity(string name) { return Manager.EntityStorage.CreateEntity(name); }
        public Entity CreateEntity(Entity prefab) {
            var instance = Manager.EntityStorage.CreateEntity();
            var prefabAddr = Manager.RequireEntityAddress(prefab);
            using var mover = Manager.BeginMoveEntity(instance, prefabAddr.ArchetypeId);
            foreach (var prefabCmp in Manager.GetEntityComponents(prefab)) {
                if (prefabCmp.GetComponentType().IsNoClone) continue;
                mover.CopyFrom(prefabCmp);
            }
            mover.Commit();
            return instance;
        }
        public bool IsValid(Entity entity) { return Manager.IsValid(entity); }
        public void DeleteEntity(Entity entity) { Manager.DeleteEntity(entity); }
        public string GetEntityName(Entity entity) {
            return Manager.GetEntityMeta(entity).Name;
        }

        public Query.Builder BeginQuery() {
            return new Query.Builder(Manager);
        }

        private void PrimeComponent<T>() {
            var systemTypes = ComponentType<T>.RequiredSystems;
            if (systemTypes == null) return;
            foreach (var type in systemTypes) {
                bool found = false;
                foreach (var system in systems) {
                    if (system.GetType() == type) { found = true; break; }
                }
                if (!found) systems.Add((SystemBase)Activator.CreateInstance(type));
            }
        }
        public ref T AddComponent<T>(Entity entity) {
            PrimeComponent<T>();
            return ref Manager.AddComponent<T>(entity);
        }
        public ref T AddComponent<T>(Entity entity, T value) {
            PrimeComponent<T>();
            ref var component = ref Manager.AddComponent<T>(entity);
            component = value;
            return ref component;
        }
        public bool HasComponent<T>(Entity entity) {
            return Manager.HasComponent<T>(entity);
        }
        public ref readonly T GetComponent<T>(Entity entity) {
            return ref Manager.GetComponent<T>(entity);
        }
        public bool TryGetComponent<T>(Entity entity, out T component) {
            return Manager.TryGetComponent<T>(Manager.RequireEntityAddress(entity), out component);
        }
        public ref T GetComponentRef<T>(Entity entity) {
            return ref Manager.GetComponentRef<T>(entity);
        }
        public NullableRef<T> TryGetComponentRef<T>(Entity entity) {
            return Manager.TryGetComponentRef<T>(Manager.RequireEntityAddress(entity));
        }
        public bool RemoveComponent<T>(Entity entity) {
            return Manager.RemoveComponent<T>(entity);
        }
        public bool TryRemoveComponent<T>(Entity entity) {
            return Manager.TryRemoveComponent<T>(entity);
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
            T system;
            if (Context.SystemBootstrap != null) {
                system = Context.SystemBootstrap.CreateSystem<T>(Context);
            } else {
                system = new T();
            }
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
            AddSystem(new SystemLambda<C1>(callback, Manager), new[] {
                Context.RequireComponentTypeId<C1>()
            }, queryI);
        }
        public void AddSystem<C1, C2>(SystemLambda.Callback<C1, C2> callback, QueryId? queryI = null) {
            AddSystem(new SystemLambda<C1, C2>(callback, Manager), new[] {
                Context.RequireComponentTypeId<C1>(),
                Context.RequireComponentTypeId<C2>(),
            }, queryI);
        }
        public void AddSystem<C1, C2, C3>(SystemLambda.Callback<C1, C2, C3> callback, QueryId? queryI = null) {
            AddSystem(new SystemLambda<C1, C2, C3>(callback, Manager), new[] {
                Context.RequireComponentTypeId<C1>(),
                Context.RequireComponentTypeId<C2>(),
                Context.RequireComponentTypeId<C3>(),
            }, queryI);
        }
        public void AddSystem<C1, C2, C3, C4>(SystemLambda.Callback<C1, C2, C3, C4> callback, QueryId? queryI = null) {
            AddSystem(new SystemLambda<C1, C2, C3, C4>(callback, Manager), new[] {
                Context.RequireComponentTypeId<C1>(),
                Context.RequireComponentTypeId<C2>(),
                Context.RequireComponentTypeId<C3>(),
                Context.RequireComponentTypeId<C4>(),
            }, queryI);
        }
        public void AddSystem<C1, C2, C3, C4, C5>(SystemLambda.Callback<C1, C2, C3, C4, C5> callback, QueryId? queryI = null) {
            AddSystem(new SystemLambda<C1, C2, C3, C4, C5>(callback, Manager), new[] {
                Context.RequireComponentTypeId<C1>(),
                Context.RequireComponentTypeId<C2>(),
                Context.RequireComponentTypeId<C3>(),
                Context.RequireComponentTypeId<C4>(),
                Context.RequireComponentTypeId<C5>(),
            }, queryI);
        }

        public EntityManager.EntityEnumerator GetEntities() {
            return Manager.GetEntities();
        }
        public EntityManager.EntityComponentEnumerator GetEntityComponents(Entity entity) {
            return Manager.GetEntityComponents(entity);
        }
        public EntityManager.ArchetypeEnumerator GetArchetypesWith(BitField field) {
            return Manager.GetArchetypesWith(field);
        }
        public EntityManager.EntityQueryEnumerator GetEntities(QueryId query) {
            return Manager.GetEntities(query);
        }

        public TypedQueryIterator<C1> QueryAll<C1>() {
            var query = BeginQuery().With<C1>().Build();
            return new TypedQueryIterator<C1>(Manager.GetArchetypes(query));
        }
        public TypedQueryIterator<C1> QueryAll<C1>(QueryId query) {
            return new TypedQueryIterator<C1>(Manager.GetArchetypes(query));
        }
        public TypedQueryIterator<C1, C2> QueryAll<C1, C2>() {
            var query = BeginQuery().With<C1, C2>().Build();
            return new TypedQueryIterator<C1, C2>(Manager.GetArchetypes(query));
        }
        public TypedQueryIterator<C1, C2> QueryAll<C1, C2>(QueryId query) {
            return new TypedQueryIterator<C1, C2>(Manager.GetArchetypes(query));
        }
        public TypedQueryIterator<C1, C2, C3> QueryAll<C1, C2, C3>() {
            var query = BeginQuery().With<C1, C2, C3>().Build();
            return new TypedQueryIterator<C1, C2, C3>(Manager.GetArchetypes(query));
        }
        public TypedQueryIterator<C1, C2, C3> QueryAll<C1, C2, C3>(QueryId query) {
            return new TypedQueryIterator<C1, C2, C3>(Manager.GetArchetypes(query));
        }

    }

}
