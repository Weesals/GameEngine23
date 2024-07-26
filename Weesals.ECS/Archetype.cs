using System;
using System.Collections;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Weesals.ECS {
    public struct EntityAddress {
        public ArchetypeId ArchetypeId;
        public int Row;
        public EntityAddress(ArchetypeId archetype, int row) { ArchetypeId = archetype; Row = row; }
        public EntityAddress(EntityStorage.EntityData entity) : this(entity.ArchetypeId, entity.Row) { }
        public override string ToString() { return $"{ArchetypeId}:{Row}"; }
        public static implicit operator EntityAddress(EntityStorage.EntityData entity) { return entity.Address; }
        public static readonly EntityAddress Invalid = new(new(0), -1);
    }

    // Receive callback when a row is added / removed from a archetype
    public class ArchetypeListener {
        public QueryId QueryId;
        public struct MoveEvent {
            public EntityAddress From;
            public EntityAddress To;
        }
        public Action<EntityAddress> OnCreate;
        public Action<MoveEvent> OnMove;
        public Action<EntityAddress> OnDelete;
    }

    // Flag when a component changes on an Archetype
    public class ArchetypeMutateListener {
        public readonly Archetype Archetype;
        public RevisionMonitor RevisionMonitor;
        public ArchetypeMutateListener(Archetype archetype) {
            Archetype = archetype;
        }
        public RevisionStorage.Enumerator GetEnumerator(EntityManager manager) {
            return manager.ColumnStorage.GetChanges(RevisionMonitor, Archetype);
        }
    }
    // Flag when a component changes on any archetype
    public class ComponentMutateListener : ArchetypeListener, IDisposable {
        public readonly EntityManager Manager;
        public readonly TypeId TypeId;
        public readonly QueryId Query;
        private List<ArchetypeMutateListener> bindings = new();
        public ComponentMutateListener(EntityManager manager, QueryId query, TypeId typeId) {
            Manager = manager;
            TypeId = typeId;
            Manager.AddListener(query, this);
            OnCreate += (entityAddr) => {
                int index = 0;
                for (; index < bindings.Count; ++index) if (bindings[index].Archetype.Id == entityAddr.ArchetypeId) break;
                if (index >= bindings.Count) {
                    var archetype = Manager.GetArchetype(entityAddr.ArchetypeId);
                    if (!archetype.TryGetColumnId(TypeId, out var columnIndex)) return;
                    var listener = new ArchetypeMutateListener(archetype);
                    listener.RevisionMonitor = archetype.CreateRevisionMonitor(Manager, columnIndex);
                    bindings.Add(listener);
                }
            };
        }
        public void Dispose() {
            foreach (var binding in bindings) {
                Manager.ColumnStorage.RemoveRevisionMonitor(binding.RevisionMonitor);
                binding.RevisionMonitor = default;
            }
        }
        public void Clear() {
            foreach (var binding in bindings) {
                Manager.ColumnStorage.Reset(ref binding.RevisionMonitor, binding.Archetype);
            }
        }
        public struct Enumerator : IEnumerator<ComponentRef> {
            public readonly ComponentMutateListener Listener;
            private List<ArchetypeMutateListener>.Enumerator listenersEn;
            private RevisionStorage.Enumerator bitEnum;
            public Archetype CurrentArchetype => listenersEn.Current.Archetype;
            public ComponentRef Current => new ComponentRef(Listener.Manager, CurrentArchetype, bitEnum.Current, Listener.TypeId);
            object IEnumerator.Current => Current;
            public Enumerator(ComponentMutateListener listener) {
                Listener = listener;
                listenersEn = Listener.bindings.GetEnumerator();
                bitEnum = RevisionStorage.Enumerator.Invalid;
            }
            public void Dispose() { }
            public void Reset() { }
            public bool MoveNext() {
                while (!bitEnum.MoveNext()) {
                    if (!listenersEn.MoveNext()) return false;
                    bitEnum = listenersEn.Current.GetEnumerator(Listener.Manager);
                }
                return true;
            }
        }
        public Enumerator GetEnumerator() { return new(this); }
    }

    // A column stores a list of data for a specific component type within a archetype
    public struct ArchetypeColumn {
        public readonly TypeId TypeId;
        // The range of values for this column within ColumnData.Items
        public Range DataRange;
        // The range of pages used for sparse lookup in SparsePages
        public Range SparsePages;

        public RevisionStorage.ColumnRevision RevisionData;
        public int Revision => RevisionData.Revision < 0 ? ~RevisionData.Revision : RevisionData.Revision;
        public bool HasRevision => RevisionData.Revision >= 0;

        public int MonitorRef;

        public ArchetypeColumn(TypeId typeId) {
            TypeId = typeId;
        }

        public void NotifyMutation(scoped ref ColumnStorage columnStorage, int row) {
            if (MonitorRef > 0) {
                ref var column = ref columnStorage.RequireColumn(TypeId);
                if (!HasRevision) {
                    columnStorage.RevisionStorage.Clear(ref RevisionData);
                }
                columnStorage.RevisionStorage.SetModified(ref RevisionData, row);
            }
        }

        public override string ToString() { return $"C{TypeId}<x{DataRange.Length}>"; }
    }

    // A archetype of component columns and entity rows
    public class Archetype {
#if DEBUG
        private EntityManager debugManager;
#endif
        public readonly ArchetypeId Id;
        public readonly int ColumnCount;
        public readonly BitField TypeMask;
        public BitField SparseTypeMask;
        private Range columnRange;
        // Index of the last item
        public int MaxItem = -1;
        // To ensure structure doesnt change during iterate
        public int Revision = 0;
        // Used to track which listeners are active for this archetype
        public BitField ArchetypeListeners;

        public bool IsEmpty => MaxItem < 0;
        public bool IsNullArchetype => ColumnCount == 1;
        public int EntityCount => MaxItem + 1;
        public int AllColumnCount => columnRange.Length;

        public Archetype(ArchetypeId id, BitField field, scoped ref ColumnStorage columnStorage) {
            Id = id;
            TypeMask = field;
            ColumnCount = 1 + field.BitCount;
            columnRange = columnStorage.ArchetypeColumns.AllocateRange(ColumnCount);
            var columns = columnStorage.ArchetypeColumns.GetRange(columnRange);
            columns[0] = new ArchetypeColumn(EntityContext.EntityColumnType.TypeId);
            var it = TypeMask.GetEnumerator();
            for (int i = 1; i < columns.Length; i++) {
                Trace.Assert(it.MoveNext());
                columns[i] = new ArchetypeColumn(new TypeId(it.Current));
            }
            Trace.Assert(!it.MoveNext());
        }
        [Conditional("DEBUG")]
        public void SetDebugManager(EntityManager manager) {
            debugManager = manager;
        }
        public int AllocateRow(scoped ref ColumnStorage columnStorage, Entity entity) {
            ++Revision;
            ++MaxItem;
            if (MaxItem >= GetEntities(ref columnStorage).Length) {
                RequireSize(ref columnStorage, (int)BitOperations.RoundUpToPowerOf2((uint)MaxItem + 512));
            }
            GetEntities(ref columnStorage)[MaxItem] = entity;
            return MaxItem;
        }

        public Span<Entity> GetEntities(scoped ref ColumnStorage columnStorage) {
            return columnStorage.GetColumnDataAs<Entity>(0, GetColumn(ref columnStorage, 0).DataRange);
        }

        public void RequireSize(scoped ref ColumnStorage columnStorage, int size) {
            for (int i = 0; i < ColumnCount; i++) {
                ref var column = ref GetColumn(ref columnStorage, i);
                ref var columnData = ref columnStorage.GetColumn(column.TypeId);
                columnData.Resize(ref column.DataRange, size);
            }
        }

        public ref ArchetypeColumn GetColumn(scoped ref ColumnStorage columnStorage, int id) {
            var columns = columnStorage.ArchetypeColumns.GetRange(columnRange);
            return ref columns[id];
        }
        public bool GetHasColumn(TypeId typeId) {
            if (!typeId.IsSparse) return TypeMask.Contains(typeId.Packed);
            return SparseTypeMask.Contains(typeId.Index);
        }
        public int GetColumnId(TypeId typeId, EntityContext? context = default) {
            if (typeId.IsSparse) {
                return SparseTypeMask.TryGetBitIndex(typeId.Index, out var index) ? ColumnCount + index : -1;
            } else {
                if (!TypeMask.TryGetBitIndex(typeId, out var index)) {
                    AssertComponentId(typeId, context);
                    return -1;
                }
                return 1 + index;
            }
        }
        public bool TryGetColumnId(TypeId typeId, out int index) {
            if (typeId.IsSparse) {
                if (!SparseTypeMask.TryGetBitIndex(typeId.Index, out index)) return false;
                index += ColumnCount;
            } else {
                if (!TypeMask.TryGetBitIndex(typeId, out index)) return false;
                index += 1;
            }
            return true;
        }

        public ref T GetValueRW<T>(scoped ref ColumnStorage columnStorage, int columnIndex, int row, int entityArchetypeRow) {
            ref var column = ref GetColumn(ref columnStorage, columnIndex);
            ref var columnData = ref columnStorage.GetColumn(column.TypeId);
            column.NotifyMutation(ref columnStorage, entityArchetypeRow);
            return ref columnData.GetValueRW<T>(column.DataRange, row);
        }
        public ref readonly T GetValueRO<T>(scoped ref ColumnStorage columnStorage, int columnIndex, int row) {
            ref var column = ref GetColumn(ref columnStorage, columnIndex);
            ref var columnData = ref columnStorage.GetColumn(column.TypeId);
            return ref columnData.GetValueRO<T>(column.DataRange, row);
        }
        public void NotifyMutation(scoped ref ColumnStorage columnStorage, int columnIndex, int row) {
            GetColumn(ref columnStorage, columnIndex).NotifyMutation(ref columnStorage, row);
        }

        public int AddSparseColumn(TypeId typeId, EntityManager manager) {
            ref var columnStorage = ref manager.ColumnStorage;
            var context = manager.Context;
            Debug.Assert(typeId.IsSparse, "Component must be marked as sparse");
            Debug.Assert(!SparseTypeMask.Contains(typeId.Index), "Component already added");
            var index = SparseTypeMask.IsEmpty ? 0 : SparseTypeMask.GetBitIndex(typeId.Index);
            var builder = new EntityContext.TypeInfoBuilder(context, SparseTypeMask);
            builder.AddComponent(new TypeId(typeId.Index));
            SparseTypeMask = builder.Build();
            var sparseColumn = columnStorage.RequireSparseColumn(typeId);
            columnStorage.ArchetypeColumns.Resize(ref columnRange, columnRange.Length + 1);
            var columns = columnStorage.ArchetypeColumns.GetRange(columnRange);
            index += ColumnCount;
            for (int i = columns.Length - 1; i > index; --i)
                columns[i] = columns[i - 1];
            columns[index] = new(typeId);
            return index;
        }
        public bool TryGetSparseColumn(TypeId componentTypeId, out int column, EntityContext? context = default) {
            if (!SparseTypeMask.IsEmpty && SparseTypeMask.TryGetBitIndex(componentTypeId.Index, out column)) {
                column += ColumnCount;
                return true;
            }
            column = -1;
            return false;
        }
        public int RequireSparseColumn(TypeId componentTypeId, EntityManager manager) {
            if (!SparseTypeMask.IsEmpty && SparseTypeMask.TryGetBitIndex(componentTypeId.Index, out var column)) {
                return ColumnCount + column;
            }
            return AddSparseColumn(componentTypeId, manager);
        }
        public int TryGetSparseIndex(ref ColumnStorage columnStorage, int columnId, int row) {
            ref var column = ref GetColumn(ref columnStorage, columnId);
            var sparseColumn = columnStorage.GetSparseColumn(column.TypeId);
            return sparseColumn.TryGetIndex(column.SparsePages, row);
        }
        public int RequireSparseIndex(ref ColumnStorage columnStorage, int columnId, int row) {
            ref var column = ref GetColumn(ref columnStorage, columnId);
            var sparseColumn = columnStorage.GetSparseColumn(column.TypeId);
            var mutation = sparseColumn.RequireIndex(ref column.SparsePages, row);
            if (mutation.NewCount >= 0) {
                ref var columnData = ref columnStorage.GetColumn(column.TypeId);
                var newSize = mutation.NewSize;
                if (columnData.Items.Length <= newSize) {
                    newSize = (int)BitOperations.RoundUpToPowerOf2((uint)newSize + 4);
                    columnData.Resize(newSize);
                }
                mutation.ApplyInsertion(columnData.Items);
            }
            Debug.Assert(column.DataRange.Start == 0);
            row = mutation.NewOffset + mutation.Index;
            return row;
        }
        public bool GetHasSparseComponent(ref ColumnStorage columnStorage, TypeId typeId, int row) {
            return TryGetSparseColumn(typeId, out var columnId)
                && GetHasSparseComponent(ref columnStorage, columnId, row);
        }
        public bool GetHasSparseComponent(ref ColumnStorage columnStorage, int columnId, int row) {
            ref var column = ref GetColumn(ref columnStorage, columnId);
            var sparseColumn = columnStorage.RequireSparseColumn(column.TypeId);
            return sparseColumn.GetHasIndex(column.SparsePages, row);
        }
        public int GetNextSparseRowInclusive(ref ColumnStorage columnStorage, int columnId, int row) {
            ref var column = ref GetColumn(ref columnStorage, columnId);
            var sparseColumn = columnStorage.RequireSparseColumn(column.TypeId);
            return sparseColumn.GetNextIndex(column.SparsePages, row);
        }
        public void ClearSparseComponent(ref ColumnStorage columnStorage, int columnId, int row) {
            ref var column = ref GetColumn(ref columnStorage, columnId);
            var sparseColumn = columnStorage.RequireSparseColumn(column.TypeId);
            var mutation = sparseColumn.RemoveIndex(ref column.SparsePages, row);
            ApplyDeleteMutation(ref columnStorage, columnId, mutation);
        }
        public void ClearSparseRow(ref ColumnStorage columnStorage, int row) {
            for (int c = ColumnCount; c < AllColumnCount; ++c) {
                ref var column = ref GetColumn(ref columnStorage, c);
                var sparseColumn = columnStorage.RequireSparseColumn(column.TypeId);
                var mutation = sparseColumn.TryRemoveIndex(ref column.SparsePages, row);
                ApplyDeleteMutation(ref columnStorage, ColumnCount + c, mutation);
            }
        }

        private void ApplyDeleteMutation(ref ColumnStorage columnStorage, int columnId, SparseColumnStorage.DataMutation mutation) {
            if (mutation.NewCount < 0) return;
            ref var column = ref GetColumn(ref columnStorage, columnId);
            ref var columnData = ref columnStorage.GetColumn(column.TypeId);
            mutation.ApplyDeletion(columnData.Items);
        }

        [Conditional("DEBUG")]
        public void AssertComponentId(TypeId id, EntityContext? context = default) {
            if (!GetHasColumn(id)) {
                var name = context == null ? "???" : context.GetComponentType(id).Type.Name;
                throw new Exception($"Missing component {name}");
            }
        }
        public RevisionMonitor CreateRevisionMonitor(EntityManager manager, int columnIndex) {
            ref var column = ref GetColumn(ref manager.ColumnStorage, columnIndex);
            column.MonitorRef++;
            return manager.ColumnStorage.CreateRevisionMonitor(column.TypeId, this);
        }
        public override string ToString() {
            if (debugManager != null) {
                var columns = debugManager.ColumnStorage.ArchetypeColumns.GetRange(columnRange);
                var columnNames = new string[columnRange.Length];
                for (int i = 0; i < columnNames.Length; i++) {
                    columnNames[i] = $"{debugManager.Context.GetComponentType(columns[i].TypeId)}<x{columns[i].DataRange.Length}>";
                }
                return string.Join(",", columnNames);
            }
            return columnRange.ToString();
        }
    }
    public struct ArchetypeComponentLookup<T> {
        public readonly ArchetypeId Id;
        public int ColumnId;
        public readonly bool IsValid => ColumnId != -1;
        public ArchetypeComponentLookup(EntityManager manager, Archetype archetype) {
            Id = archetype.Id;
            var typeId = manager.Context.RequireComponentTypeId<T>();
            if (ComponentType<T>.IsSparse) ColumnId = archetype.RequireSparseColumn(typeId, manager);
            else archetype.TryGetColumnId(typeId, out ColumnId);
        }

        [Conditional("DEBUG")]
        private void ValidateArchetype(Archetype archetype) {
            Debug.Assert(archetype.Id == Id, "Archetype mismatch");
        }

        public RevisionMonitor CreateRevisionMonitor(EntityManager manager, bool prewarm = false) {
            var archetype = manager.GetArchetype(Id);
            var typeId = manager.Context.RequireComponentTypeId<T>();
            var monitor = manager.ColumnStorage.CreateRevisionMonitor(typeId, archetype);
            if (prewarm) {
                monitor.Revision = -1;
                Debug.Assert(!typeId.IsSparse, "Prewarm is not supported with sparse components");
            }
            return monitor;
        }
        public void RemoveRevisionMonitor(EntityManager manager, ref RevisionMonitor monitor) {
            Debug.Assert(monitor.ArchetypeId == Id);
            manager.ColumnStorage.RemoveRevisionMonitor(monitor);
            monitor = default;
        }
        public ref readonly T GetValueRO(EntityManager manager, EntityAddress entityAddr) {
            var archetype = manager.GetArchetype(entityAddr.ArchetypeId);
            var row = entityAddr.Row;
            if (ComponentType<T>.IsSparse) row = archetype.RequireSparseIndex(ref manager.ColumnStorage, ColumnId, row);
            return ref GetValueRO(ref manager.ColumnStorage, archetype, row);
        }
        public ref readonly T GetValueRO(ref ColumnStorage columnStorage, Archetype archetype, int row) {
            ValidateArchetype(archetype);
            return ref archetype.GetValueRO<T>(ref columnStorage, ColumnId, row);
        }
        public ref T GetValueRW(EntityManager manager, EntityAddress entityAddr) {
            var archetype = manager.GetArchetype(entityAddr.ArchetypeId);
            var row = entityAddr.Row;
            if (ComponentType<T>.IsSparse) row = archetype.RequireSparseIndex(ref manager.ColumnStorage, ColumnId, row);
            return ref GetValueRW(ref manager.ColumnStorage, manager.GetArchetype(entityAddr.ArchetypeId), row, entityAddr.Row);
        }
        public ref T GetValueRW(ref ColumnStorage columnStorage, Archetype archetype, int row, int entityArchetypeRow) {
            ValidateArchetype(archetype);
            return ref archetype.GetValueRW<T>(ref columnStorage, ColumnId, row, entityArchetypeRow);
        }

        public bool GetHasSparseComponent(EntityManager manager, EntityAddress entityAddr) {
            if (!IsValid) return false;
            var archetype = manager.GetArchetype(entityAddr.ArchetypeId);
            ValidateArchetype(archetype);
            return archetype.GetHasSparseComponent(ref manager.ColumnStorage, ColumnId, entityAddr.Row);
        }
    }
    // Same as above, but only typecast once.
    // (should the above do the same?)
    // Currently does not mark dirty (but should!)
    public struct ArchetypeComponentGetter<T> {
        public int Offset;
        public T[] Data;
        public ArchetypeComponentGetter(ref ColumnStorage columnStorage, Archetype archetype, int columnId) {
            var column = archetype.GetColumn(ref columnStorage, columnId);
            ref var columnData = ref columnStorage.GetColumn(column.TypeId);
            Data = (T[])columnData.Items;
            Offset = column.DataRange.Start;
        }
        public ref T this[int index] => ref Data[index];
    }

    public struct ArchetypeId {
        public readonly int Index;
        public ArchetypeId(int index) { Index = index; }
        public override string ToString() { return Index.ToString(); }
        public static implicit operator int(ArchetypeId id) { return id.Index; }
        public static readonly ArchetypeId Invalid = new(-1);
    }

    public readonly struct ArrayItem {
        public readonly Array Array;
        public readonly int Index;
        public bool IsValid => Array != null;
        public ArrayItem(Array arr, int index) { Array = arr; Index = index; }
        public static readonly ArrayItem Null = new();
        public void CopyFrom(ArrayItem item) {
            Array.Copy(item.Array, item.Index, Array, Index, 1);
        }
    }

    public readonly struct ComponentRef {
        public readonly EntityManager Manager;
        public readonly EntityContext Context => Manager.Context;
        public readonly Archetype Archetype;
        public readonly int Row;
        public readonly int DenseRow;
        public readonly TypeId TypeId;
        public readonly int ColumnId => Archetype.GetColumnId(TypeId);
        public readonly Entity Entity => Archetype.GetEntities(ref Manager.ColumnStorage)[Row];
        public readonly EntityAddress EntityAddress => new(Archetype.Id, Row);
        public readonly ArrayItem RawItem {
            get {
                ref var column = ref Archetype.GetColumn(ref Manager.ColumnStorage, ColumnId);
                ref var columnData = ref Manager.ColumnStorage.GetColumn(TypeId);
                return columnData.GetRawItem(column.DataRange, DenseRow);
            }
        }
        public ComponentRef(EntityManager entityManager, Archetype archetype, int row, TypeId typeId) {
            Manager = entityManager;
            Archetype = archetype;
            Row = row;
            DenseRow = Row;
            TypeId = typeId;
            if (TypeId.IsSparse) {
                DenseRow = Archetype.TryGetSparseIndex(ref Manager.ColumnStorage, Archetype.GetColumnId(typeId), row);
                Debug.Assert(DenseRow >= 0, "Need to preallocate before creating ComponentRef");
            }
        }
        private Array itemsArray => Manager.ColumnStorage.GetColumn(TypeId).Items;
        private ref ArchetypeColumn Column => ref Archetype.GetColumn(ref Manager.ColumnStorage, ColumnId);
        public ComponentType GetComponentType() { return Context.GetComponentType(TypeId); }
        public Type GetRawType() { return Context.GetComponentType(TypeId).Type; }
        public bool GetIs<T>() { return itemsArray is T[]; }
        public object? GetValue() { return itemsArray.GetValue(Column.DataRange.Start + DenseRow); }
        public ref readonly T GetRO<T>() { return ref Archetype.GetValueRO<T>(ref Manager.ColumnStorage, ColumnId, DenseRow); }
        public ref T GetRef<T>() { return ref Archetype.GetValueRW<T>(ref Manager.ColumnStorage, ColumnId, DenseRow, Row); }
        public void CopyTo(ComponentRef dest) {
            Manager.ColumnStorage.CopyValue(Archetype, ColumnId, DenseRow,
                dest.Archetype, dest.ColumnId, dest.DenseRow, dest.Row);
        }
        public void NotifyMutation() { Archetype.NotifyMutation(ref Manager.ColumnStorage, ColumnId, Row); }
        public override string ToString() { return Context.GetComponentType(TypeId).Type.Name; }
    }

    // A specific set of filters
    public class Query {
        public struct Key : IEquatable<Key> {
            public readonly BitField WithTypes;
            public readonly BitField WithoutTypes;
            public readonly BitField WithSparseType;
            public Key(BitField with, BitField without, BitField withSparse) {
                WithTypes = with;
                WithoutTypes = without;
                WithSparseType = withSparse;
            }
            public bool Equals(Key other) {
                return WithTypes.Equals(other.WithTypes)
                    && WithoutTypes.Equals(other.WithoutTypes)
                    && WithSparseType.Equals(other.WithSparseType);
            }
            public override bool Equals(object? obj) { return obj is Key key && Equals(key); }
            public override int GetHashCode() { return HashCode.Combine(WithTypes, WithoutTypes); }
            public override string ToString() { return $"With {WithTypes} Without {WithoutTypes}"; }
        }
        public struct Cache {
            public struct MatchedArchetype {
                public ArchetypeId ArchetypeIndex;
                public int[] ComponentOffsets;
            }
            public List<MatchedArchetype> MatchingArchetypes = new();
            public Cache() { }
            public override string ToString() {
                return MatchingArchetypes.Count == 0 ? "Empty"
                    : MatchingArchetypes.Select(m => $"Archetype{m.ArchetypeIndex}").Aggregate((i1, i2) => $"{i1},{i2}");
            }
        }
        public struct Builder {
            public readonly EntityManager Manager;
            public readonly EntityContext Context => Manager.Context;
            [ThreadStatic] public BitField.Generator WithTypes;
            [ThreadStatic] public BitField.Generator WithoutTypes;
            [ThreadStatic] public BitField.Generator WithSparseTypes;
            public Builder(EntityManager manager) {
                Manager = manager;
                if (WithTypes == null) {
                    WithTypes = new();
                    WithoutTypes = new();
                    WithSparseTypes = new();
                }
            }
            public Builder With(TypeId typeId) {
                (typeId.IsSparse ? WithSparseTypes : WithTypes).Add(typeId);
                return this;
            }
            public Builder With<C>() {
                With(Context.RequireComponentTypeId<C>());
                return this;
            }
            public Builder With<C1, C2>() {
                With(Context.RequireComponentTypeId<C1>());
                With(Context.RequireComponentTypeId<C2>());
                return this;
            }
            public Builder With<C1, C2, C3>() {
                With(Context.RequireComponentTypeId<C1>());
                With(Context.RequireComponentTypeId<C2>());
                With(Context.RequireComponentTypeId<C3>());
                return this;
            }
            public Builder Without<C>() {
                WithoutTypes.Add(Context.RequireComponentTypeId<C>());
                return this;
            }
            public Builder Without<C1, C2>() {
                WithoutTypes.Add(Context.RequireComponentTypeId<C1>());
                WithoutTypes.Add(Context.RequireComponentTypeId<C2>());
                return this;
            }
            public Builder Without<C1, C2, C3>() {
                WithoutTypes.Add(Context.RequireComponentTypeId<C1>());
                WithoutTypes.Add(Context.RequireComponentTypeId<C2>());
                WithoutTypes.Add(Context.RequireComponentTypeId<C3>());
                return this;
            }
            public QueryId Build() {
                var withField = Context.RequireTypeMask(WithTypes);
                var withoutField = Context.RequireTypeMask(WithoutTypes);
                var withSparseField = Context.RequireTypeMask(WithSparseTypes);
                WithTypes.Clear();
                WithoutTypes.Clear();
                WithSparseTypes.Clear();
                return Manager.RequireQueryIndex(withField, withoutField, withSparseField);
            }
        }
        public readonly BitField WithTypes;
        public readonly BitField WithoutTypes;
        public readonly BitField WithSparseTypes;
        public Query(BitField with, BitField without, BitField withSparse) {
            WithTypes = with;
            WithoutTypes = without;
            WithSparseTypes = withSparse;
        }
        public override string ToString() { return $"With {WithTypes} Without {WithoutTypes}"; }

        public bool Matches(BitField typeMask) {
            return typeMask.ContainsAll(WithTypes) && !typeMask.ContainsAny(WithoutTypes);
        }

        public int GetNextSparseRow(ref ColumnStorage columnStorage, Archetype archetype, int row) {
            // TODO: Store page iterators and iterate sequentially instead of binary search
            for (var pageId = SparseColumnStorage.IndexToPage(++row); row < archetype.EntityCount;) {
                uint mask = (~0u << row);
                foreach (var typeId in WithSparseTypes) {
                    var columnId = archetype.RequireSparseColumn(TypeId.MakeSparse(typeId), default!);
                    ref var column = ref archetype.GetColumn(ref columnStorage, columnId);
                    var sparseColumn = columnStorage.GetSparseColumn(column.TypeId);
                    mask &= sparseColumn.GetPageMask(column.SparsePages, pageId);
                    if (mask == 0) break;
                }
                if (mask != 0) {
                    return SparseColumnStorage.PageToIndex(pageId) + BitOperations.TrailingZeroCount(mask);
                }
                row = SparseColumnStorage.PageToIndex(++pageId);
            }
            return -1;
        }
    }
    public struct QueryId {
        public readonly int Index;
        public bool IsValid => Index >= 0;
        public QueryId(int index) { Index = index; }
        public override string ToString() { return Index.ToString(); }
        public override int GetHashCode() { return Index; }
        public static implicit operator int(QueryId id) { return id.Index; }
        public static readonly QueryId Invalid = new(-1);
    }

    // TODO: Implement per-row filtering (ie. for disabled components)
    public struct Filter {
        public bool IncludeEntity(Archetype archetype, int row) {
            return true;
        }
        public static readonly Filter None = default;
    }
}
