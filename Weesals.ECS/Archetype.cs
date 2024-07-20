﻿using System;
using System.Collections;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Weesals.ECS {
    public struct EntityAddress {
        public ArchetypeId ArchetypeId;
        public int Row;
        public EntityAddress(ArchetypeId archetype, int row) { ArchetypeId = archetype; Row = row; }
        public EntityAddress(Stage.EntityData entity) : this(entity.ArchetypeId, entity.Row) { }
        public override string ToString() { return $"{ArchetypeId}:{Row}"; }
        public static implicit operator EntityAddress(Stage.EntityData entity) { return entity.Address; }
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

        public void NotifyCreate(Archetype archetype) {
            for (int i = 0; i < archetype.EntityCount; i++) {
                OnCreate?.Invoke(new EntityAddress() {
                    ArchetypeId = archetype.Id,
                    Row = i,
                });
            }
        }
    }

    // Flag when a component changes on an Archetype
    public class ArchetypeMutateListener : DynamicBitField, IDisposable {
        public readonly Archetype Archetype;
        public readonly int ColumnIndex;
        public ComponentType ComponentType => Archetype.GetColumn(ColumnIndex).Type;
        public ArchetypeMutateListener(Archetype archetype, int columnIndex) {
            Archetype = archetype;
            ColumnIndex = columnIndex;
            Archetype.GetColumn(ColumnIndex).AddModificationListener(this);
        }
        public void Dispose() {
            Archetype.GetColumn(ColumnIndex).RemoveModificationListener(this);
        }
    }
    // Flag when a component changes on any archetype
    public class ComponentMutateListener : ArchetypeListener, IDisposable {
        public readonly Stage Stage;
        public readonly TypeId TypeId;
        public readonly QueryId Query;
        private List<ArchetypeMutateListener> bindings = new();
        public ComponentMutateListener(Stage stage, QueryId query, TypeId typeId) {
            Stage = stage;
            TypeId = typeId;
            Stage.AddListener(query, this);
            OnCreate += (entityAddr) => {
                var archetype = Stage.GetArchetype(entityAddr.ArchetypeId);
                int index = 0;
                for (; index < bindings.Count; ++index) if (bindings[index].Archetype == archetype) break;
                if (index >= bindings.Count) {
                    if (!archetype.TryGetColumnId(TypeId, out var typeIndex)) return;
                    bindings.Add(new ArchetypeMutateListener(archetype, typeIndex));
                }
            };
        }
        public void Dispose() {
            Debug.WriteLine("TODO: Implement");
        }
        public struct Enumerator : IEnumerator<ComponentRef> {
            public readonly ComponentMutateListener Listener;
            private List<ArchetypeMutateListener>.Enumerator listenersEn;
            private DynamicBitField.Enumerator bitEnum;
            public Archetype CurrentArchetype => listenersEn.Current.Archetype;
            public ComponentRef Current => new ComponentRef(CurrentArchetype, bitEnum.Current, Listener.TypeId);
            object IEnumerator.Current => Current;
            public Enumerator(ComponentMutateListener listener) {
                Listener = listener;
                listenersEn = Listener.bindings.GetEnumerator();
                bitEnum = listenersEn.MoveNext() ? listenersEn.Current.GetEnumerator()
                    : DynamicBitField.Enumerator.Invalid;
            }
            public void Dispose() { }
            public void Reset() { }
            public bool MoveNext() {
                while (!bitEnum.MoveNext()) {
                    if (!listenersEn.MoveNext()) return false;
                    bitEnum = listenersEn.Current.GetEnumerator();
                }
                return true;
            }
        }
        public Enumerator GetEnumerator() { return new(this); }

        public void Clear() {
            foreach (var binding in bindings) binding.Clear();
        }
    }

    // A column stores a list of data for a specific component type within a archetype
    public struct ArchetypeColumn {
        public Array Items;
        public ComponentType Type;
        public List<DynamicBitField>? RowModificationFlags;
        public ArchetypeColumn(ComponentType type) {
            Type = type;
            Type.Resize(ref Items!, 0);
        }
        public void Resize(int size) {
            Type.Resize(ref Items, size);
        }

        public void CopyValue(int toRow, ArchetypeColumn from, int fromRow) {
            CopyValue(toRow, from.Items, fromRow);
        }
        public void CopyValue(int toRow, Array from, int fromRow) {
            Array.Copy(from, fromRow, Items, toRow, 1);
        }

        public ref readonly T GetValueRO<T>(int row) {
            return ref ((T[])Items)[row];
        }
        public ref T GetValueRW<T>(int row) {
            return ref ((T[])Items)[row];
        }

        public void NotifyMutation(int row) {
            if (RowModificationFlags == null) return;
            foreach (var field in RowModificationFlags) field.TryAdd(row);
        }

        public void AddModificationListener(DynamicBitField movedRows) {
            if (RowModificationFlags == null) RowModificationFlags = new();
            RowModificationFlags.Add(movedRows);
        }
        public void RemoveModificationListener(DynamicBitField movedRows) {
            RowModificationFlags!.Remove(movedRows);
        }

        public override string ToString() { return Type.ToString(); }

        public ArrayItem GetRawItem(int row) {
            return new(Items, row);
        }
    }

    public class SparseColumnMeta {
        public SparseColumnArchetype SparseData;
    }

    // A archetype of component columns and entity rows
    public class Archetype {
        private static ComponentType<Entity> EntityColumnType = new(new TypeId(0));
        public readonly Stage Stage;
        public readonly ArchetypeId Id;
        public readonly BitField TypeMask;
        public readonly int ColumnCount;
        private ArchetypeColumn[] columns;
        public BitField SparseTypeMask;
        public SparseColumnMeta[] SparseColumns = Array.Empty<SparseColumnMeta>();
        // Index of the last item
        public int MaxItem = -1;
        public int EntityCount => MaxItem + 1;
        // To ensure structure doesnt change during iterate
        public int Revision = 0;
        // Used to track which listeners are active for this archetype
        public BitField ArchetypeListeners;

        public SparseStorage SparseStorage => Stage.SparseStorage;
        public StageContext Context => Stage.Context;
        public Entity[] Entities => (Entity[])columns[0].Items;
        public bool IsEmpty => MaxItem < 0;
        public bool IsNullArchetype => ColumnCount == 1;

        public Archetype(ArchetypeId id, Stage stage, BitField field) {
            Stage = stage;
            Id = id;
            TypeMask = field;
            columns = new ArchetypeColumn[1 + field.BitCount];
            columns[0] = new ArchetypeColumn(EntityColumnType);
            var it = TypeMask.GetEnumerator();
            for (int i = 1; i < columns.Length; i++) {
                Trace.Assert(it.MoveNext());
                columns[i] = new ArchetypeColumn(Context.GetComponentType(it.Current));
            }
            ColumnCount = columns.Length;
            Trace.Assert(!it.MoveNext());
        }
        public int AllocateRow(Entity entity) {
            ++Revision;
            ++MaxItem;
            if (MaxItem >= Entities.Length) RequireSize(Math.Max(Entities.Length * 2, 1024));
            Entities[MaxItem] = entity;
            return MaxItem;
        }
        public void ReleaseRow(int oldRow) {
            ++Revision;
            if (MaxItem < 0) { Debug.Assert(TypeMask.IsEmpty, "Only null archetype has invalid count."); return; }
            Debug.Assert(MaxItem == oldRow, "Must release from end. Move entity to end first.");
            --MaxItem;
        }

        public void CopyRowTo(int srcRow, Archetype dest, int dstRow, StageContext context) {
            if (dest == this) {
                // Copying to self, special case
                for (int i = 0; i < ColumnCount; i++) {
                    columns[i].CopyValue(dstRow, columns[i], srcRow);
                }
                CopyColumns(srcRow, dest, dstRow, SparseTypeMask, dest.SparseTypeMask, ColumnCount, dest.ColumnCount);
            } else if (!IsNullArchetype) {
                dest.Entities[dstRow] = Entities[srcRow];
                CopyColumns(srcRow, dest, dstRow, TypeMask, dest.TypeMask, 1, 1);
                foreach (var typeIndex in BitField.Except(SparseTypeMask, dest.SparseTypeMask)) {
                    dest.RequireSparseComponent(TypeId.MakeSparse(typeIndex), context);
                }
                CopyColumns(srcRow, dest, dstRow, SparseTypeMask, dest.SparseTypeMask, ColumnCount, dest.ColumnCount);
            }
            ++Revision;
        }

        private void CopyColumns(int srcRow, Archetype dest, int dstRow, BitField srcTypes, BitField dstTypes, int srcColBegin, int dstColBegin) {
            var it1 = srcTypes.GetEnumerator();
            var it2 = dstTypes.GetEnumerator();
            int i1 = -1, i2 = -1;
            bool isSparse = srcColBegin >= ColumnCount;
            while (it1.MoveNext()) {
                ++i1;
                // Try to find the matching bit
                while (it2.Current < it1.Current) {
                    if (!it2.MoveNext()) return;
                    ++i2;
                }
                // If no matching bit was found
                if (it2.Current != it1.Current) continue;
                var denseDstRow = dstRow;
                // Otherwise copy the value
                if (isSparse) {
                    if (!GetHasSparseComponent(srcColBegin + i1, srcRow)) {
                        // TODO: Probably need to remove sparse components on entity delete
                        // (otherwise they could leak into this moved entity)
                        Debug.Assert(!dest.GetHasSparseComponent(dstColBegin + i2, dstRow));
                        continue;
                    }
                    denseDstRow = dest.RequireSparseIndex(dstColBegin + i2, dstRow);
                }
                dest.columns[dstColBegin + i2].CopyValue(denseDstRow, columns[srcColBegin + i1], srcRow);
            }
        }

        public void CopyValue(int dstColumnId, int dstRow, Array srcData, int srcRow) {
            ref var column = ref columns[dstColumnId];
            column.CopyValue(dstRow, srcData, srcRow);
            column.NotifyMutation(dstRow);
        }

        public void RequireSize(int size) {
            for (int i = 0; i < columns.Length; i++) {
                columns[i].Resize(size);
            }
        }

        public ref ArchetypeColumn GetColumn(int id) {
            return ref columns[id];
        }
        public bool GetHasColumn(TypeId typeId, StageContext? context = default) {
            if (!typeId.IsSparse) return TypeMask.Contains(typeId.Packed);
            return SparseTypeMask.Contains(typeId.Index);
        }
        public int GetColumnId(TypeId typeId, StageContext? context = default) {
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
        public bool TryGetColumnId(TypeId typeId, out int index, StageContext? context = default) {
            if (typeId.IsSparse) {
                if (!SparseTypeMask.TryGetBitIndex(typeId.Index, out index)) return false;
                index += ColumnCount;
            } else {
                if (!TypeMask.TryGetBitIndex(typeId, out index)) return false;
                index += 1;
            }
            return true;
        }

        public ref T GetValueRW<T>(int columnIndex, int row, int entityArchetypeRow) {
            ref var column = ref columns[columnIndex];
            column.NotifyMutation(entityArchetypeRow);
            return ref column.GetValueRW<T>(row);
        }
        public ref readonly T GetValueRO<T>(int columnIndex, int row) {
            return ref columns[columnIndex].GetValueRO<T>(row);
        }
        public void NotifyMutation(int columnIndex, int row) {
            columns[columnIndex].NotifyMutation(row);
        }

        public int AddSparseComponent(TypeId typeId, StageContext context) {
            Debug.Assert(typeId.IsSparse, "Component must be marked as sparse");
            Debug.Assert(!SparseTypeMask.Contains(typeId.Index), "Component already added");
            var index = SparseTypeMask.IsEmpty ? 0 : SparseTypeMask.GetBitIndex(typeId.Index);
            var builder = new StageContext.TypeInfoBuilder(context, SparseTypeMask);
            builder.AddComponent(new TypeId(typeId.Index));
            SparseTypeMask = builder.Build();
            Array.Resize(ref SparseColumns, (SparseColumns != null ? SparseColumns.Length : 0) + 1);
            for (int i = SparseColumns.Length - 1; i > index; --i)
                SparseColumns[i] = SparseColumns[i - 1];
            SparseColumns[index] = new();
            SparseColumns[index].SparseData = new(new(SparseStorage, context.GetComponentType(typeId)));
            Array.Resize(ref columns, columns.Length + 1);
            index += ColumnCount;
            for (int i = columns.Length - 1; i > index; --i)
                columns[i] = columns[i - 1];
            columns[index] = new(context.GetComponentType(typeId));
            return index;
        }
        public bool TryGetSparseComponent(TypeId componentTypeId, out int column, StageContext? context = default) {
            if (!SparseTypeMask.IsEmpty && SparseTypeMask.TryGetBitIndex(componentTypeId.Index, out column)) {
                column += ColumnCount;
                return true;
            }
            column = -1;
            return false;
        }
        public int RequireSparseComponent(TypeId componentTypeId, StageContext context) {
            if (!SparseTypeMask.IsEmpty && SparseTypeMask.TryGetBitIndex(componentTypeId.Index, out var column)) {
                return ColumnCount + column;
            }
            return AddSparseComponent(componentTypeId, context);
        }
        public int TryGetSparseIndex(int column, int row) {
            var sparseColumn = SparseColumns[column - ColumnCount];
            row = sparseColumn.SparseData.GetIndex(row);
            return row;
        }
        public int RequireSparseIndex(int columnId, int row) {
            ref var column = ref columns[columnId];
            var sparseColumn = SparseColumns[columnId - ColumnCount];
            var mutation = sparseColumn.SparseData.RequireIndex(row);
            if (mutation.NewCount >= 0) {
                var dataEnd = mutation.NewOffset + mutation.NewCount + 1;
                if (column.Items.Length <= dataEnd) {
                    column.Resize((int)BitOperations.RoundUpToPowerOf2((uint)dataEnd + 4));
                }
                if (mutation.PreviousOffset != mutation.NewOffset) {
                    Array.Copy(column.Items, mutation.PreviousOffset, column.Items, mutation.NewOffset, mutation.Index);
                }
                if (mutation.Index < mutation.NewCount - 1) {
                    Array.Copy(column.Items, mutation.PreviousOffset + mutation.Index,
                        column.Items, mutation.NewOffset + mutation.Index + 1,
                        mutation.NewCount - 1 - mutation.Index);
                }
            }
            row = mutation.NewOffset + mutation.Index;
            return row;
        }
        public bool GetHasSparseComponent(int column, int row) {
            return SparseColumns[column - ColumnCount].SparseData.GetHasIndex(row);
        }
        public int GetNextSparseRowInclusive(int column, int row) {
            return SparseColumns[column - ColumnCount].SparseData.GetNextIndexInclusive(row);
        }
        public void ClearSparseIndex(int column, int row) {
            var mutation = SparseColumns[column - ColumnCount].SparseData.RemoveIndex(row);
            ApplyDeleteMutation(column, mutation);
        }
        public void ClearSparseRow(int row) {
            for (int c = 0; c < SparseColumns.Length; ++c) {
                var mutation = SparseColumns[c].SparseData.TryRemoveIndex(row);
                ApplyDeleteMutation(ColumnCount + c, mutation);
            }
        }

        private void ApplyDeleteMutation(int columnId, SparseColumnStorage.DataMutation mutation) {
            if (mutation.NewCount < 0) return;
            Debug.Assert(mutation.PreviousOffset == mutation.NewOffset);
            ref var column = ref columns[columnId];
            Array.Copy(column.Items, mutation.Index + 1, column.Items, mutation.Index, mutation.NewCount - mutation.Index);
        }

        [Conditional("DEBUG")]
        public void AssertComponentId(TypeId id, StageContext? context = default) {
            if (!GetHasColumn(id, context)) {
                var name = context == null ? "???" : context.GetComponentType(id).Type.Name;
                throw new Exception($"Missing component {name}");
            }
        }
        public override string ToString() {
            return columns.Select(c => c.ToString()).Aggregate((i1, i2) => $"{i1},{i2}");
        }
    }
    public struct ArchetypeComponentLookup<T> {
        public readonly ArchetypeId Id;
        public int ColumnId;
        public readonly bool IsValid => ColumnId != -1;
        public ArchetypeComponentLookup(StageContext context, Archetype archetype) {
            Id = archetype.Id;
            var typeId = context.RequireComponentTypeId<T>();
            if (ComponentType<T>.IsSparse) ColumnId = archetype.RequireSparseComponent(typeId, context);
            else archetype.TryGetColumnId(typeId, out ColumnId, context);
        }

        [Conditional("DEBUG")]
        private void ValidateArchetype(Archetype archetype) {
            Debug.Assert(archetype.Id == Id, "Archetype mismatch");
        }

        public void AddModificationListener(Archetype archetype, DynamicBitField movedRows) {
            ValidateArchetype(archetype);
            archetype.GetColumn(ColumnId).AddModificationListener(movedRows);
        }
        public void RemoveModificationListener(Archetype archetype, DynamicBitField movedRows) {
            ValidateArchetype(archetype);
            archetype.GetColumn(ColumnId).RemoveModificationListener(movedRows);
        }
        public ref readonly T GetValueRO(Stage stage, EntityAddress entityAddr) {
            var archetype = stage.GetArchetype(entityAddr.ArchetypeId);
            var row = entityAddr.Row;
            if (ComponentType<T>.IsSparse) row = archetype.RequireSparseIndex(ColumnId, row);
            return ref GetValueRO(archetype, row);
        }
        public ref readonly T GetValueRO(Archetype archetype, int row) {
            ValidateArchetype(archetype);
            return ref archetype.GetValueRO<T>(ColumnId, row);
        }
        public ref T GetValueRW(Stage stage, EntityAddress entityAddr) {
            var archetype = stage.GetArchetype(entityAddr.ArchetypeId);
            var row = entityAddr.Row;
            if (ComponentType<T>.IsSparse) row = archetype.RequireSparseIndex(ColumnId, row);
            return ref GetValueRW(stage.GetArchetype(entityAddr.ArchetypeId), row, entityAddr.Row);
        }
        public ref T GetValueRW(Archetype archetype, int row, int entityArchetypeRow) {
            ValidateArchetype(archetype);
            return ref archetype.GetValueRW<T>(ColumnId, row, entityArchetypeRow);
        }

        public bool GetHasSparseComponent(Stage stage, EntityAddress entityAddr) {
            if (!IsValid) return false;
            var archetype = stage.GetArchetype(entityAddr.ArchetypeId);
            ValidateArchetype(archetype);
            return archetype.GetHasSparseComponent(ColumnId, entityAddr.Row);
        }
    }
    // Same as above, but only typecast once.
    // (should the above do the same?)
    // Currently does not mark dirty (but should!)
    public struct ArchetypeComponentGetter<T> {
        public T[] Data;
        public ArchetypeComponentGetter(Archetype archetype, int columnId) {
            Data = (T[])archetype.GetColumn(columnId).Items;
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
        public readonly StageContext Context => Archetype.Context;
        public readonly Archetype Archetype;
        public readonly int Row;
        public readonly int DenseRow;
        public readonly TypeId TypeId;
        public readonly int ColumnId => Archetype.GetColumnId(TypeId);
        public readonly Entity Entity => Archetype.Entities[Row];
        public readonly EntityAddress EntityAddress => new(Archetype.Id, Row);
        public readonly ArrayItem RawItem => new(Archetype.GetColumn(ColumnId).Items, DenseRow);
        public ComponentRef(Archetype archetype, int row, TypeId typeId) {
            Archetype = archetype;
            Row = row;
            DenseRow = Row;
            TypeId = typeId;
            if (TypeId.IsSparse) {
                DenseRow = archetype.RequireSparseIndex(archetype.GetColumnId(typeId), row);
                Debug.Assert(DenseRow >= 0, "Need to preallocate before creating ComponentRef");
            }
        }
        public ComponentType GetComponentType() { return Context.GetComponentType(TypeId); }
        public Type GetRawType() { return Context.GetComponentType(TypeId).Type; }
        public bool GetIs<T>() { return Archetype.GetColumn(ColumnId).Items is T[]; }
        public object? GetValue() { return Archetype.GetColumn(ColumnId).Items.GetValue(DenseRow); }
        public ref readonly T GetRO<T>() { return ref Archetype.GetValueRO<T>(ColumnId, DenseRow); }
        public ref T GetRef<T>() { return ref Archetype.GetValueRW<T>(ColumnId, DenseRow, Row); }
        public void CopyTo(ComponentRef dest) {
            Debug.Assert(TypeId == dest.TypeId);
            ref var srcColumn = ref Archetype.GetColumn(ColumnId);
            ref var dstColumn = ref dest.Archetype.GetColumn(dest.ColumnId);
            dstColumn.CopyValue(dest.DenseRow, srcColumn, DenseRow);
            dstColumn.NotifyMutation(dest.Row);
        }
        public void NotifyMutation() { Archetype.NotifyMutation(ColumnId, Row); }
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
            public readonly Stage Stage;
            public readonly StageContext Context => Stage.Context;
            [ThreadStatic] public BitField.Generator WithTypes;
            [ThreadStatic] public BitField.Generator WithoutTypes;
            [ThreadStatic] public BitField.Generator WithSparseTypes;
            public Builder(Stage stage) {
                Stage = stage;
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
                return Stage.RequireQueryIndex(withField, withoutField, withSparseField);
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

        public int GetNextSparseRow(Archetype archetype, int row) {
            // TODO: Store page iterators and iterate sequentially instead of binary search
            for (var pageId = SparseStorage.IndexToPage(++row); row < archetype.EntityCount;) {
                uint mask = (~0u << row);
                foreach (var typeId in WithSparseTypes) {
                    var columnId = archetype.RequireSparseComponent(TypeId.MakeSparse(typeId), default!);
                    var sparseColumn = archetype.SparseColumns[columnId - archetype.ColumnCount];
                    mask &= sparseColumn.SparseData.GetPageMask(pageId);
                    if (mask == 0) break;
                }
                if (mask != 0) {
                    return SparseStorage.PageToIndex(pageId) + BitOperations.TrailingZeroCount(mask);
                }
                row = SparseStorage.PageToIndex(++pageId);
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
