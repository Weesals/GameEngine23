using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Weesals.ECS {
    public struct ColumnData {
        public readonly ComponentType Type;
        public Array Items;
        private SparseRanges allocated;
        public bool IsValid => Items != null;
        //public int Revision;
        public int MonitorRef;
        public ColumnData(ComponentType type) {
            Type = type;
            Type.Resize(ref Items!, 0);
            allocated = new();
        }
        public void Resize(int size) {
            Debug.Assert(size > Items.Length);
            Type.Resize(ref Items, (int)BitOperations.RoundUpToPowerOf2((uint)size));
        }
        public void Resize(ref Range range, int size) {
            var newStart = allocated.TryExtend(range.Start, range.Length, size);
            if (newStart == -1) {
                allocated.ClearRange(range.Start, range.Length);
                newStart = allocated.FindAndSetRange(size);
            }
            var newEnd = newStart + size;
            if (newEnd > Items.Length) {
                Resize(newEnd);
            }
            if (newStart != range.Start) {
                Array.Copy(Items, range.Start, Items, newStart, range.Length);
            }
            range = new() { Start = newStart, Length = size, };
        }

        public void CopyValue(int toRow, int fromRow) {
            CopyValue(toRow, Items, fromRow);
        }
        public void CopyValue(int toRow, Array from, int fromRow) {
            Array.Copy(from, fromRow, Items, toRow, 1);
        }

        public ref readonly T GetValueRO<T>(Range range, int row) {
            return ref ((T[])Items)[range.Start + row];
        }
        public ref T GetValueRW<T>(Range range, int row) {
            return ref ((T[])Items)[range.Start + row];
        }
        public ArrayItem GetRawItem(Range range, int row) {
            return new(Items, range.Start + row);
        }

        public override string ToString() { return Type.ToString(); }
    }
    public struct RevisionMonitor {
        public ArchetypeId ArchetypeId;
        public TypeId TypeId;
        public int Revision;
    }
    public struct RevisionStorage {
        public const int PageShift = 6;
        unsafe public struct ColumnRevision {
            public Range PageRange;
            public int Revision;
            // End this revision
            public void Flush() {
                if (Revision >= 0) Revision = ~Revision;
            }
            // Require a valid revision
            public void Realize() {
                if (Revision < 0) Revision = -Revision;
            }
        }
        public struct Page : SparsePages.IPage {
            public ulong BitMask;
            bool SparsePages.IPage.IsOccupied => BitMask != 0;
            public override string ToString() { return $"{BitMask:X}"; }
        }
        private SparsePages<Page> pages;

        public RevisionStorage() {
            pages = new();
        }

        public void SetModified(ref ColumnRevision revision, int index) {
            ref var page = ref pages.RequirePage(ref revision.PageRange, IndexToPage(index));
            page.BitMask |= IndexToBit(index);
        }
        public void ClearModified(ref ColumnRevision revision, int index) {
            var pageIndex = pages.GetPageIndex(revision.PageRange, IndexToPage(index));
            if (pageIndex < 0) return;
            ref var page = ref pages.GetPage(pageIndex);
            page.BitMask &= ~IndexToBit(index);
        }
        public bool GetIsModified(ColumnRevision revision, int index) {
            var pageIndex = pages.GetPageIndex(revision.PageRange, IndexToPage(index));
            if (pageIndex < 0) return false;
            ref var page = ref pages.GetPage(pageIndex);
            var bit = IndexToBit(index);
            return (page.BitMask & bit) != 0;
        }
        public void Clear(ref ColumnRevision revisionData) {
            pages.Clear(ref revisionData.PageRange);
            revisionData.Realize();
        }

        public struct Enumerator {
            SparsePages<Page>.Enumerator pageEnumerator;
            int pageOffset;
            ulong page;
            public int Current => pageOffset + BitOperations.TrailingZeroCount(page);
            public Enumerator(SparsePages<Page>.Enumerator pages) {
                pageEnumerator = pages;
                pageOffset = 0;
                page = 0;
            }
            public Enumerator(int componentCount) {
                pageEnumerator = new(default, 0, componentCount);
                pageOffset = 0;
                page = 0;
                pageEnumerator.MoveNext();
            }
            public bool MoveNext() {
                page &= page - 1;
                while (page == 0) {
                    if (!pageEnumerator.IsValid) {
                        var index = pageEnumerator.PageIndex;
                        int remain = Math.Min(64, pageEnumerator.End - index);
                        if (remain <= 0) return false;
                        pageEnumerator.Skip(remain);
                        pageOffset = index;
                        page = ~(~1ul << (remain - 1));
                        return true;
                    }
                    if (!pageEnumerator.MoveNext()) return false;
                    pageOffset = PageToIndex(pageEnumerator.PageOffset);
                    page = pageEnumerator.Current.BitMask;
                }
                return true;
            }
            public static readonly Enumerator Invalid = new();
            public Enumerator GetEnumerator() => this;
        }
        public Enumerator GetEnumerator(ColumnRevision revision, Range dataRange) {
            return new(pages.GetEnumerator(revision.PageRange));
        }

        public static int IndexToPage(int index) { return index >> PageShift; }
        public static ulong IndexToMask(int index) { return ~(~0ul << index); }   // All lower bits (exclusive)
        public static ulong IndexToUpperMask(int index) { return (~0ul << index); }   // All lower bits (exclusive)
        public static ulong IndexToBit(int index) { return 1ul << index; }
        public static int IndexToBitIndex(int index) { return index & ((1 << PageShift) - 1); }
        public static int PageToIndex(int pageId) { return pageId << PageShift; }
    }

    public struct ColumnStorage {
        public readonly EntityContext Context;
        public readonly SparsePages<SparseColumnStorage.Page> SparseStorage;
        public readonly RevisionStorage RevisionStorage;
        public readonly ECSSparseArray<ArchetypeColumn> ArchetypeColumns;
        private ColumnData[] columns;
        private SparseColumnStorage[] sparseColumns = Array.Empty<SparseColumnStorage>();
        public struct ColumnRange {
            public int Start;
            public int Count;
            public int End => Start + Count;
        }
        private ColumnRange denseRange;
        private ColumnRange sparseRange;
        public ColumnStorage(EntityContext context) {
            Context = context;
            columns = new ColumnData[64];
            ArchetypeColumns = new(64);
            SparseStorage = new();
            RevisionStorage = new();
        }
        public ref ColumnData GetColumn(TypeId typeId) {
            return ref GetColumnRaw(typeId.Index, typeId.IsSparse);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref ColumnData GetColumnRaw(int columnIndex, bool isSparse) {
            ref var range = ref isSparse ? ref sparseRange : ref denseRange;
            Debug.Assert(columnIndex < range.Count);
            return ref columns[range.Start + columnIndex];
        }
        public ref ColumnData RequireColumn(TypeId typeId) {
            ref var range = ref typeId.IsSparse ? ref sparseRange : ref denseRange;
            int index = typeId.Index;
            if (index >= range.Count) {
                var type = Context.GetComponentType(typeId);
                var origSparseRange = sparseRange;
                var origDenseRange = denseRange;
                range.Count = index + 1;
                if (denseRange.End >= sparseRange.Start && sparseRange.Count > 0) {
                    sparseRange.Start = (int)BitOperations.RoundUpToPowerOf2((uint)denseRange.End + 16);
                }
                int end = sparseRange.Count > 0 ? sparseRange.End : denseRange.End;
                if (end > columns.Length) {
                    Array.Resize(ref columns, (int)BitOperations.RoundUpToPowerOf2((uint)end));
                }
                if (origSparseRange.Start != sparseRange.Start) {
                    Array.Copy(columns, origSparseRange.Start, columns, sparseRange.Start, origSparseRange.Count);
                }
            }
            ref var column = ref columns[range.Start + index];
            if (!column.IsValid) {
                column = new(Context.GetComponentType(typeId));
            }
            return ref column;
        }
        public Span<T> GetColumnDataAs<T>(int columnIndex, Range range) {
            return ((T[])columns[columnIndex].Items).AsSpan(range.Start, range.Length);
        }

        public SparseColumnStorage RequireSparseColumn(TypeId typeId) {
            RequireColumn(typeId);
            Debug.Assert(typeId.IsSparse);
            if (typeId.Index >= sparseColumns.Length) {
                Array.Resize(ref sparseColumns, (int)BitOperations.RoundUpToPowerOf2((uint)typeId.Index + 8));
            }
            ref var column = ref sparseColumns[typeId.Index];
            if (column == null) column = new(SparseStorage);
            return column;
        }
        public SparseColumnStorage GetSparseColumn(TypeId typeId) {
            return sparseColumns[typeId.Index];
        }

        public RevisionMonitor CreateRevisionMonitor(TypeId typeId, Archetype archetype) {
            var archColumnIndex = archetype.GetColumnId(typeId);
            ref var archColumn = ref archetype.GetColumn(ref this, archColumnIndex);
            archColumn.RevisionData.Flush();
            archColumn.MonitorRef++;
            return new RevisionMonitor() { TypeId = typeId, Revision = archColumn.Revision, };
        }
        public void RemoveRevisionMonitor(RevisionMonitor monitor) {
            ref var column = ref GetColumn(monitor.TypeId);
            Debug.Assert(column.MonitorRef > 0);
            column.MonitorRef--;
        }
        public void Reset(ref RevisionMonitor monitor, Archetype archetype) {
            var archColumnIndex = archetype.GetColumnId(monitor.TypeId);
            ref var archColumn = ref archetype.GetColumn(ref this, archColumnIndex);
            Debug.Assert(archColumn.MonitorRef > 0);
            monitor.Revision = archColumn.Revision;
            archColumn.RevisionData.Flush();
        }
        public RevisionStorage.Enumerator GetChanges(RevisionMonitor monitor, Archetype archetype) {
            var archColumnIndex = archetype.GetColumnId(monitor.TypeId);
            ref var archColumn = ref archetype.GetColumn(ref this, archColumnIndex);
            if (monitor.Revision == archColumn.Revision) return default;
            var dataRange = monitor.TypeId.IsSparse ? archColumn.SparsePages : archColumn.DataRange;
            if (monitor.Revision == -1) {
                return new(archetype.EntityCount);
            }
            return RevisionStorage.GetEnumerator(archColumn.RevisionData, dataRange);
        }

        public void CopyRowTo(Archetype src, int srcRow, Archetype dest, int dstRow, EntityManager manager) {
            if (dest.Id == src.Id) {
                // Copying to self, special case
                for (int i = 0; i < src.ColumnCount; i++) {
                    ref var columnData = ref GetColumnRaw(src.GetColumn(ref this, i).TypeId, false);
                    columnData.CopyValue(dstRow, srcRow);
                }
                CopyColumns(src, srcRow, dest, dstRow, src.SparseTypeMask, dest.SparseTypeMask, src.ColumnCount, dest.ColumnCount);
            } else if (!src.IsNullArchetype) {
                var entities = src.GetEntities(ref this);
                entities[dstRow] = entities[srcRow];
                CopyColumns(src, srcRow, dest, dstRow, src.TypeMask, dest.TypeMask, 1, 1);
                foreach (var typeIndex in BitField.Except(src.SparseTypeMask, dest.SparseTypeMask)) {
                    dest.RequireSparseColumn(TypeId.MakeSparse(typeIndex), manager);
                }
                CopyColumns(src, srcRow, dest, dstRow, src.SparseTypeMask, dest.SparseTypeMask, src.ColumnCount, dest.ColumnCount);
            }
            ++src.Revision;
        }

        private void CopyColumns(Archetype src, int srcRow, Archetype dest, int dstRow, BitField srcTypes, BitField dstTypes, int srcColBegin, int dstColBegin) {
            var it1 = srcTypes.GetEnumerator();
            var it2 = dstTypes.GetEnumerator();
            int i1 = -1, i2 = -1;
            bool isSparse = srcColBegin >= src.ColumnCount;
            while (it1.MoveNext()) {
                ++i1;
                // Try to find the matching bit
                while (it2.Current < it1.Current) {
                    if (!it2.MoveNext()) return;
                    ++i2;
                }
                // If no matching bit was found
                if (it2.Current != it1.Current) continue;
                int denseSrcRow = srcRow, denseDstRow = dstRow;
                // Otherwise copy the value
                if (isSparse) {
                    denseSrcRow = src.TryGetSparseIndex(ref this, srcColBegin + i1, srcRow);
                    if (denseSrcRow < 0) {
                        Debug.Assert(!dest.GetHasSparseComponent(ref this, dstColBegin + i2, dstRow),
                            "Sparse component will leak! Dest already has component.");
                        continue;
                    }
                    denseDstRow = dest.RequireSparseIndex(ref this, dstColBegin + i2, dstRow);
                }
                CopyValue(src, srcColBegin + i1, denseSrcRow,
                    dest, dstColBegin + i2, denseDstRow, dstRow);
            }
        }

        public void CopyValue(Archetype src, int srcColumnId, int srcDenseRow,
            Archetype dst, int dstColumnId, int dstDenseRow, int dstRow) {
            ref var srcColumn = ref src.GetColumn(ref this, srcColumnId);
            ref var dstColumn = ref dst.GetColumn(ref this, dstColumnId);
            Debug.Assert(srcColumn.TypeId == dstColumn.TypeId);
            ref var columnData = ref GetColumn(srcColumn.TypeId);
            columnData.CopyValue(dstColumn.DataRange.Start + dstDenseRow,
                srcColumn.DataRange.Start + srcDenseRow);
            dstColumn.NotifyMutation(ref this, dstRow);
        }
        public void CopyValue(Archetype dst, int dstColumnId, int dstRow, Array srcData, int srcRow) {
            ref var column = ref dst.GetColumn(ref this, dstColumnId);
            ref var columnData = ref GetColumn(column.TypeId);
            columnData.CopyValue(column.DataRange.Start + dstRow, srcData, srcRow);
            column.NotifyMutation(ref this, dstRow);
        }
        public void ReleaseRow(Archetype archetype, int oldRow) {
            for (int i = 0; i < archetype.AllColumnCount; i++) {
                ref var column = ref archetype.GetColumn(ref this, i);
                RevisionStorage.ClearModified(ref column.RevisionData, oldRow);
            }
            ++archetype.Revision;
            if (archetype.MaxItem < 0) { Debug.Assert(archetype.TypeMask.IsEmpty, "Only null archetype has invalid count."); return; }
            Debug.Assert(archetype.MaxItem == oldRow, "Must release from end. Move entity to end first.");
            --archetype.MaxItem;
        }
    }

    public struct EntityStorage {
        public struct EntityData {
            public EntityAddress Address;
            public ArchetypeId ArchetypeId => Address.ArchetypeId;
            public int Row => Address.Row;
            public uint Version;
            public override string ToString() { return $"Archetype {ArchetypeId} Row {Row}"; }
        }
        public struct EntityMeta {
            public string Name;
        }
        private EntityData[] entities = Array.Empty<EntityData>();
        private EntityMeta[] entityMeta = Array.Empty<EntityMeta>();

        private int entityCount = 0;
        private int deletedEntity = -1;

        public EntityStorage() {
        }
        private uint AllocateEntity() {
            if (entityCount >= entities.Length) {
                int capacity = (int)BitOperations.RoundUpToPowerOf2((uint)entityCount + 32);
                Array.Resize(ref entities, capacity);
                Array.Resize(ref entityMeta, capacity);
            }
            return (uint)(entityCount++);
        }
        public int GetMaximumEntityId() {
            return entityCount;
        }
        public Entity CreateEntity(string name = "unknown") {
            lock (entities) {
                var entity = new Entity(0, 1);
                if (deletedEntity != -1) {
                    var entityData = entities[deletedEntity];
                    entity = new((uint)deletedEntity, entityData.Version);
                    deletedEntity = entityData.Address.Row;
                    entityData.Address = EntityAddress.Invalid;
                    entities[(int)entity.Index] = entityData;
                } else {
                    entity.Index = AllocateEntity();
                    entities[entity.Index] = new EntityData() {
                        Address = EntityAddress.Invalid,
                        Version = entity.Version,
                    };
                }
                entityMeta[entity.Index] = new EntityMeta() {
                    Name = name,
                };
                return entity;
            }
        }
        public bool IsValid(Entity entity) {
            return entities[(int)entity.Index].Version == entity.Version;
        }
        public void DeleteEntity(Entity entity) {
            lock (entities) {
                var entityData = entities[(int)entity.Index];
                entityData.Address.Row = deletedEntity;
                entityData.Version++;
                entities[(int)entity.Index] = entityData;
                deletedEntity = (int)entity.Index;
            }
        }
        public EntityMeta GetEntityMeta(Entity entity) {
            return entityMeta[entity.Index];
        }
        public EntityAddress RequireEntityAddress(Entity entity) {
            var entityData = entities[(int)entity.Index];
            if (entityData.Version != entity.Version) throw new Exception("Invalid entity");
            return entityData.Address;
        }

        public ref EntityData GetEntityDataRef(Entity entity)
            => ref GetEntityDataRef((int)entity.Index);
        public ref EntityData GetEntityDataRef(int index) {
            return ref entities[index];
        }
    }

    public class SparseColumnStorage {
        public const int PageShift = 5;
        public struct DataMutation {
            public int PreviousOffset, NewOffset;
            public int NewCount;
            public int Index;
            public int NewSize => NewOffset + NewCount;

            public void ApplyDeletion(Array items) {
                Debug.Assert(PreviousOffset == NewOffset);
                Array.Copy(items, Index + 1, items, Index, NewCount - Index);
            }
            public void ApplyInsertion(Array items) {
                if (PreviousOffset != NewOffset) {
                    Debug.Assert(Index == 0 || NewCount > 1);
                    Array.Copy(items, PreviousOffset,
                        items, NewOffset, Index);
                }
                if (Index < NewCount - 1) {
                    Array.Copy(items, PreviousOffset + Index,
                        items, NewOffset + Index + 1,
                        NewCount - 1 - Index);
                }
            }
        }
        public struct Page : SparsePages.IPage {
            public int Offset;
            public byte Allocated;
            public uint BitMask;
            bool SparsePages.IPage.IsOccupied => BitMask != 0;
            public override string ToString() { return $"{Offset} +{Allocated} = {BitMask:X}"; }
        }
        protected readonly SparsePages<Page> pageStorage;
        protected SparseRanges dataAllocated = new();
        public SparseColumnStorage(SparsePages<Page> _pageStorage) {
            pageStorage = _pageStorage;
        }
        public DataMutation AllocateIndex(ref Range pageRange, int index) {
            ref var page = ref pageStorage.RequirePage(ref pageRange, IndexToPage(index));
            return AllocateIndex(ref page, index);
        }
        private DataMutation AllocateIndex(ref Page page, int index) {
            Debug.Assert((page.BitMask & (1u << index)) == 0);
            page.BitMask |= 1u << index;
            var bitIndex = BitOperations.PopCount(page.BitMask & IndexToMask(index));
            int origOffset = page.Offset;
            int pageCount = BitOperations.PopCount(page.BitMask);
            if (pageCount > page.Allocated) {
                var newAllocated = (int)BitOperations.RoundUpToPowerOf2((uint)page.Allocated + 4);
                var newOffset = dataAllocated.RequireResize(page.Offset, page.Allocated, newAllocated);
                page.Offset = newOffset;
                page.Allocated = (byte)newAllocated;
            }
            return new DataMutation() {
                PreviousOffset = origOffset,
                NewOffset = page.Offset,
                NewCount = pageCount,
                Index = bitIndex,
            };
        }
        public bool GetHasIndex(Range pageRange, int index) {
            var pageIndex = pageStorage.GetPageIndex(pageRange, IndexToPage(index));
            if (pageIndex < 0) return false;
            ref var page = ref pageStorage.GetPage(pageIndex);
            return (page.BitMask & IndexToBit(index)) != 0;
        }
        public int TryGetIndex(Range pageRange, int index) {
            var pageIndex = pageStorage.GetPageIndex(pageRange, IndexToPage(index));
            if (pageIndex < 0) return -1;
            ref var page = ref pageStorage.GetPage(pageIndex);
            var bitIndex = BitOperations.PopCount(page.BitMask & IndexToMask(index));
            if ((page.BitMask & IndexToBit(index)) == 0) return -1;
            return page.Offset + bitIndex;
        }
        public int GetIndex(Range pageRange, int index) {
            var pageIndex = pageStorage.GetPageIndex(pageRange, IndexToPage(index));
            ref var page = ref pageStorage.GetPage(pageIndex);
            var bitIndex = BitOperations.PopCount(page.BitMask & IndexToMask(index));
            return page.Offset + bitIndex;
        }
        public int GetNextIndex(Range pageRange, int index) {
            var bitMask = IndexToUpperMask(index);
            var pageIndex = pageStorage.GetPageIndex(pageRange, IndexToPage(index));
            if (pageIndex < 0) {
                pageIndex = ~pageIndex;
                bitMask = IndexToUpperMask(0);
            }
            var pageEnd = pageRange.End;
            while (pageIndex < pageEnd) {
                ref var page = ref pageStorage.GetPage(pageIndex);
                var masked = page.BitMask & bitMask;
                if (masked != 0) return PageToIndex(pageStorage.GetPageId(pageIndex)) + BitOperations.TrailingZeroCount(masked);
                ++pageIndex;
            }
            return -1;
        }
        public DataMutation RequireIndex(ref Range pageRange, int index) {
            ref var page = ref pageStorage.RequirePage(ref pageRange, IndexToPage(index));
            if ((page.BitMask & IndexToBit(index)) != 0)
                return new() { NewCount = -1, NewOffset = page.Offset, Index = BitOperations.PopCount(page.BitMask & IndexToMask(index)), };
            return AllocateIndex(ref page, index);
        }
        public DataMutation RemoveIndex(ref Range pageRange, int index) {
            var pageIndex = pageStorage.GetPageIndex(pageRange, IndexToPage(index));
            ref var page = ref pageStorage.GetPage(pageIndex);
            page.BitMask &= ~IndexToBit(index);
            int pageCount = BitOperations.PopCount(page.BitMask);
            var bitIndex = BitOperations.PopCount(page.BitMask & IndexToMask(index));
            //Array.Copy(data, page.Offset + bitIndex, data, page.Offset + bitIndex + 1, pageCount - bitIndex);
            return new() { PreviousOffset = page.Offset, NewOffset = page.Offset, Index = bitIndex, NewCount = pageCount, };
        }
        public DataMutation TryRemoveIndex(ref Range pageRange, int index) {
            var pageIndex = pageStorage.GetPageIndex(pageRange, IndexToPage(index));
            if (pageIndex < 0) return new() { NewCount = -1, };
            ref var page = ref pageStorage.GetPage(pageIndex);
            if ((page.BitMask & IndexToBit(index)) == 0) return new() { NewCount = -1, };
            page.BitMask &= ~IndexToBit(index);
            int pageCount = BitOperations.PopCount(page.BitMask);
            var bitIndex = BitOperations.PopCount(page.BitMask & IndexToMask(index));
            //Array.Copy(data, page.Offset + bitIndex, data, page.Offset + bitIndex + 1, pageCount - bitIndex);
            return new() { PreviousOffset = page.Offset, NewOffset = page.Offset, Index = bitIndex, NewCount = pageCount, };
        }
        public int GetSparseIndex(Range pageRange, int index) {
            var end = pageRange.End;
            for (int pageIndex = pageRange.Start; pageIndex < end; pageIndex++) {
                var page = pageStorage.GetPage(pageIndex);
                var localIndex = index - page.Offset;
                var bitMask = page.BitMask;
                if (localIndex < BitOperations.PopCount(bitMask)) {
                    for (; bitMask != 0; --localIndex) {
                        if (localIndex == 0) {
                            return PageToIndex(pageStorage.GetPageId(pageIndex)) + BitOperations.TrailingZeroCount(bitMask);
                        }
                        bitMask &= bitMask - 1;
                    }
                }
            }
            return -1;
        }
        public uint GetPageMask(Range pageRange, int index) {
            var pageIndex = pageStorage.GetPageIndex(pageRange, IndexToPage(index));
            if (pageIndex < 0) return 0;
            return pageStorage.GetPage(pageIndex).BitMask;
        }
        public static int IndexToPage(int index) { return index >> PageShift; }
        public static uint IndexToMask(int index) { return ~(~0u << index); }   // All lower bits (exclusive)
        public static uint IndexToUpperMask(int index) { return (~0u << index); }   // All lower bits (exclusive)
        public static uint IndexToBit(int index) { return 1u << index; }
        public static int PageToIndex(int pageId) { return pageId << PageShift; }
    }
}
