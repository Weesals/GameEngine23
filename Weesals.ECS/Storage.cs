﻿using System;
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
        public bool IsValid => Type != null;
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
        public struct RevisionChannel {
            public Range PageRange;
        }
        public enum RevisionTypes { Created, Modified, Destroyed, COUNT }
        unsafe public struct ColumnRevision {
            public const int FlushedFlag = unchecked((int)0x80000000);
            public const int TypeCount = (int)RevisionTypes.COUNT;
            private int monitorRef;
            private int revisionData;
            private Range revisionRange;
            public int Revision => revisionData & ~FlushedFlag;
            public bool HasRevision => revisionData >= 0;
            public bool IsMonitored => monitorRef > 0;
            public Range RevisionRange => revisionRange;
            public ref RevisionChannel GetChannel(RevisionStorage storage, RevisionTypes type)
                => ref GetChannel(storage, revisionRange.End - 1, type);
            public ref RevisionChannel GetChannel(RevisionStorage storage, int index, RevisionTypes type) {
                return ref storage.revisions[index * TypeCount + (int)type];
            }
            // End this revision
            public void Flush(ref RevisionStorage storage) {
                revisionData |= FlushedFlag;
            }
            // Require a valid revision
            public int Realize(ref RevisionStorage storage) {
                if (revisionRange.Length >= TypeCount) {
                    bool isEmpty = true;
                    int rev0 = (revisionRange.End - 1) * TypeCount;
                    for (int i = 0; i < 3; ++i) {
                        if (storage.revisions[rev0 - i].PageRange.Length != 0) isEmpty = false;
                    }
                    if (isEmpty) {
                        revisionData = Revision;
                        return Revision;
                    }
                }
                // TODO: If nothing was added to the previous revision, reuse it (is this safe?)
                Debug.Assert(revisionData < 0);
                revisionData = Revision + 1;
                int r = 0;
                for (; r < revisionRange.Length && storage.revisionReferences[revisionRange.Start + r] == 0; ++r) ;
                if (r < 1) {
                    var newLength = revisionRange.Length + 1;
                    int newStart = storage.revisionRanges.RequireResize(
                        revisionRange.Start, revisionRange.Length, newLength);
                    storage.RequireRevisionDataLength(newStart + newLength);
                    if (newStart != revisionRange.Start) {
                        storage.CopyRevisionData(revisionRange.Start, revisionRange.Length, newStart);
                    }
                    revisionRange = new(newStart, newLength);
                } else if (r < revisionRange.Length) {
                    storage.CopyRevisionData(revisionRange.Start + r, revisionRange.Length - r, revisionRange.Start + r - 1);
                }
                for (int i = 0; i < TypeCount; i++) {
                    GetChannel(storage, (RevisionTypes)i) = default;
                }
                storage.revisionReferences[revisionRange.End - 1] = 0;
                return Revision;
            }
            public void Reference(RevisionStorage storage, int revision) {
                monitorRef++;
                if (revision == -1) return;
                Debug.Assert(revision == Revision);
                var revisionDelta = Revision - revision;
                storage.revisionReferences[revisionRange.End - 1 - revisionDelta]++;
            }
            public void Dereference(RevisionStorage storage, int revision) {
                monitorRef--;
                if (revision == -1) return;
                Debug.Assert(revision <= Revision);
                var revisionDelta = Revision - revision;
                storage.revisionReferences[revisionRange.End - 1 - revisionDelta]--;
            }
            public static readonly ColumnRevision Default = new() { revisionData = FlushedFlag, };
        }

        private void RequireRevisionDataLength(int length) {
            if (revisionReferences.Length < length) {
                Array.Resize(ref revisions, length * 3);
                Array.Resize(ref revisionReferences, length);
            }
        }
        private void CopyRevisionData(int start, int length, int newStart) {
            revisions.AsSpan(start * 3, length * 3).CopyTo(revisions.AsSpan(newStart * 3, length * 3));
            revisionReferences.AsSpan(start, length).CopyTo(revisionReferences.AsSpan(newStart, length));
        }

        public struct Page : SparsePages.IPage {
            public ulong BitMask;
            bool SparsePages.IPage.IsOccupied => BitMask != 0;
            public override string ToString() { return $"{BitMask:X}"; }
        }
        private SparsePages<Page> pages;
        private SparseRanges revisionRanges;
        private int[] revisionReferences;
        private RevisionChannel[] revisions;

        public RevisionStorage() {
            pages = new();
            revisionRanges = new();
            revisionReferences = Array.Empty<int>();
            revisions = Array.Empty<RevisionChannel>();
        }

        public void Begin(ref ColumnRevision revisionData) {
            revisionData.Realize(ref this);
        }
        public void SetEntry(ref RevisionChannel revision, int index) {
            ref var page = ref pages.RequirePage(ref revision.PageRange, IndexToPage(index));
            page.BitMask |= IndexToBit(index);
        }
        public bool ClearEntry(ref RevisionChannel revision, int index) {
            var pageIndex = pages.GetPageIndex(revision.PageRange, IndexToPage(index));
            if (pageIndex < 0) return false;
            ref var page = ref pages.GetPage(pageIndex);
            var mask = IndexToBit(index);
            var exists = (page.BitMask & mask) != 0;
            page.BitMask &= ~mask;
            return exists;
        }
        public bool GetEntry(RevisionChannel revision, int index) {
            var pageIndex = pages.GetPageIndex(revision.PageRange, IndexToPage(index));
            if (pageIndex < 0) return false;
            ref var page = ref pages.GetPage(pageIndex);
            var bit = IndexToBit(index);
            return (page.BitMask & bit) != 0;
        }

        public struct Enumerator : DynamicBitField.IPagedEnumerator {
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
                    if (!MoveNextPageIntl()) return false;
                }
                return true;
            }
            private bool MoveNextPageIntl() {
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
                return true;
            }
            public static readonly Enumerator Invalid = new();
            public Enumerator GetEnumerator() => this;

            public int MoveNextPage() => MoveNextPageIntl() ? GetCurrentPageId() : -1;
            public int GetCurrentPageId() => pageOffset >> 6;
            public ulong GetCurrentPage() => page;
        }
        public Enumerator GetEnumerator(RevisionChannel channel) {
            return new(pages.GetEnumerator(channel.PageRange));
        }

        public static int IndexToPage(int index) { return index >> PageShift; }
        public static ulong IndexToMask(int index) { return ~(~0ul << index); }   // All lower bits (exclusive)
        public static ulong IndexToUpperMask(int index) { return (~0ul << index); }   // All lower bits (exclusive)
        public static ulong IndexToBit(int index) { return 1ul << index; }
        public static int IndexToBitIndex(int index) { return index & ((1 << PageShift) - 1); }
        public static int PageToIndex(int pageId) { return pageId << PageShift; }
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
                Array.Copy(items, NewOffset + Index + 1, items, NewOffset + Index, NewCount - Index);
#if DEBUG
                Array.Copy(items, items.Length - 1, items, NewOffset + NewCount, 1);
#endif
            }
            public void ApplyInsertion(Array items) {
                if (PreviousOffset != NewOffset) {
                    Debug.Assert(Index == 0 || NewCount > 1);
                    Array.Copy(items, PreviousOffset,
                        items, NewOffset, Index);
                }
                if (Index < NewCount - 1) {
                    var moveCount = NewCount - 1 - Index;
                    Array.Copy(items, PreviousOffset + Index,
                        items, NewOffset + Index + 1,
                        moveCount);
                }
#if DEBUG
                for (int i = 0; i < NewCount - 1; i++) {
                    int index = PreviousOffset + i;
                    if (index >= NewOffset && index < NewOffset + NewCount && i != Index) continue;
                    Array.Copy(items, items.Length - 1, items, index, 1);
                }
                Array.Copy(items, items.Length - 1, items, NewOffset + Index, 1);
#endif
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
        private DataMutation AllocateIndex(ref Page pageRange, int index) {
            Debug.Assert((pageRange.BitMask & (1u << index)) == 0);
            pageRange.BitMask |= 1u << index;
            var bitIndex = BitOperations.PopCount(pageRange.BitMask & IndexToMask(index));
            int origOffset = pageRange.Offset;
            int pageCount = BitOperations.PopCount(pageRange.BitMask);
            if (pageCount > pageRange.Allocated) {
                var newAllocated = (int)BitOperations.RoundUpToPowerOf2((uint)pageRange.Allocated + 4);
                var newOffset = dataAllocated.RequireResize(pageRange.Offset, pageRange.Allocated, newAllocated);
                pageRange.Offset = newOffset;
                pageRange.Allocated = (byte)newAllocated;
            }
            return new DataMutation() {
                PreviousOffset = origOffset,
                NewOffset = pageRange.Offset,
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
        public uint GetPageMask(Range pageRange, int pageId) {
            var pageIndex = pageStorage.GetPageIndex(pageRange, pageId);
            if (pageIndex < 0) return 0;
            return pageStorage.GetPage(pageIndex).BitMask;
        }
        public static int IndexToPage(int index) { return index >> PageShift; }
        public static uint IndexToMask(int index) { return ~(~0u << index); }   // All lower bits (exclusive)
        public static uint IndexToUpperMask(int index) { return (~0u << index); }   // All lower bits (exclusive)
        public static uint IndexToBit(int index) { return 1u << index; }
        public static int PageToIndex(int pageId) { return pageId << PageShift; }
    }

    public struct ColumnStorage {
        public Action Validate;
        public readonly EntityContext Context;
        public readonly SparsePages<SparseColumnStorage.Page> SparseStorage;
        public readonly ECSSparseArray<ArchetypeColumn> ArchetypeColumns;
        public RevisionStorage RevisionStorage;
        public int ColumnRevision;
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

        public RevisionMonitor CreateRevisionMonitor(TypeId typeId, scoped ref Archetype archetype) {
            var archColumnIndex = archetype.GetColumnId(typeId);
            ref var archColumn = ref archetype.GetColumn(ref this, archColumnIndex);
            archColumn.Revision.Flush(ref RevisionStorage);
            archColumn.Revision.Realize(ref RevisionStorage);
            var monitor = new RevisionMonitor() { TypeId = typeId, Revision = -1, ArchetypeId = archetype.Id, };
            archColumn.Revision.Reference(RevisionStorage, monitor.Revision);
            return monitor;
        }
        public void RemoveRevisionMonitor(ref RevisionMonitor monitor, scoped ref Archetype archetype) {
            var archColumnIndex = archetype.GetColumnId(monitor.TypeId);
            ref var archColumn = ref archetype.GetColumn(ref this, archColumnIndex);
            Debug.Assert(archColumn.Revision.IsMonitored);
            archColumn.Revision.Dereference(RevisionStorage, monitor.Revision);
            monitor = default;
        }
        public void Reset(ref RevisionMonitor monitor, scoped ref Archetype archetype) {
            var archColumnIndex = archetype.GetColumnId(monitor.TypeId);
            ref var archColumn = ref archetype.GetColumn(ref this, archColumnIndex);
            Debug.Assert(archColumn.Revision.IsMonitored);
            archColumn.Revision.Dereference(RevisionStorage, monitor.Revision);
            archColumn.Revision.Flush(ref RevisionStorage);
            monitor.Revision = archColumn.Revision.Realize(ref RevisionStorage);
            archColumn.Revision.Reference(RevisionStorage, monitor.Revision);
        }
        private RevisionStorage.ColumnRevision GetRevision(RevisionMonitor monitor, scoped ref Archetype archetype) {
            var archColumnIndex = archetype.GetColumnId(monitor.TypeId);
            ref var archColumn = ref archetype.GetColumn(ref this, archColumnIndex);
            if (monitor.Revision == archColumn.Revision.Revision) return RevisionStorage.ColumnRevision.Default;
            return archColumn.Revision;
        }
        public RevisionStorage.Enumerator GetRevisionChannel(RevisionMonitor monitor, scoped ref Archetype archetype, RevisionStorage.RevisionTypes type) {
            if (monitor.Revision == -1) {
                return type switch { RevisionStorage.RevisionTypes.Created => new(archetype.EntityCount), _ => default };
            }
            var revision = GetRevision(monitor, ref archetype);
            if (revision.Revision == 0) return default;
            return RevisionStorage.GetEnumerator(revision.GetChannel(RevisionStorage,
                revision.RevisionRange.End - 1 - (revision.Revision - monitor.Revision),
                type));
        }
        public RevisionStorage.Enumerator GetCreated(RevisionMonitor monitor, ref Archetype archetype) {
            return GetRevisionChannel(monitor, ref archetype, RevisionStorage.RevisionTypes.Created);
        }
        public RevisionStorage.Enumerator GetChanges(RevisionMonitor monitor, ref Archetype archetype) {
            return GetRevisionChannel(monitor, ref archetype, RevisionStorage.RevisionTypes.Modified);
        }
        public RevisionStorage.Enumerator GetDestroyed(RevisionMonitor monitor, ref Archetype archetype) {
            return GetRevisionChannel(monitor, ref archetype, RevisionStorage.RevisionTypes.Destroyed);
        }

        public void CopyRowTo(scoped ref Archetype src, int srcRow, scoped ref Archetype dest, int dstRow, EntityManager manager) {
            if (dest.Id == src.Id) {
                // Copying to self, special case
                for (int i = 0; i < src.ColumnCount; i++) {
                    ref var column = ref src.GetColumn(ref this, i);
                    ref var columnData = ref GetColumnRaw(column.TypeId, false);
                    columnData.CopyValue(column.DataRange.Start + dstRow, column.DataRange.Start + srcRow);
                }
                CopySparseColumns(ref src, srcRow, ref dest, dstRow, src.SparseTypeMask, dest.SparseTypeMask, src.ColumnCount, dest.ColumnCount);
            } else if (!src.IsNullArchetype) {
                var entities = src.GetEntities(ref this);
                entities[dstRow] = entities[srcRow];
                CopyDenseColumns(ref src, srcRow, ref dest, dstRow, src.TypeMask, dest.TypeMask, 1, 1);
                foreach (var typeIndex in BitField.Except(src.SparseTypeMask, dest.SparseTypeMask)) {
                    if (!src.GetHasSparseComponent(ref this, TypeId.MakeSparse(typeIndex), srcRow)) continue;
                    dest.RequireSparseColumn(TypeId.MakeSparse(typeIndex), manager);
                }
                CopySparseColumns(ref src, srcRow, ref dest, dstRow, src.SparseTypeMask, dest.SparseTypeMask, src.ColumnCount, dest.ColumnCount);
            }
            ++src.Revision;
        }

        private void CopyDenseColumns(scoped ref Archetype src, int srcRow, scoped ref Archetype dest, int dstRow, BitField srcTypes, BitField dstTypes, int srcColBegin, int dstColBegin) {
            var it1 = dstTypes.GetEnumerator();
            var it2 = srcTypes.GetEnumerator();
            int i1 = -1, i2 = -1;
            while (it1.MoveNext()) {
                ++i1;
                // Try to find the matching bit
                while (it2.Current < it1.Current) {
                    if (!it2.MoveNext()) return;
                    ++i2;
                }
                // If no matching bit was found
                if (it2.Current != it1.Current) continue;
                CopyValue(ref src, srcColBegin + i2, srcRow,
                    ref dest, dstColBegin + i1, dstRow, dstRow);
            }
        }
        private void CopySparseColumns(scoped ref Archetype src, int srcRow, scoped ref Archetype dest, int dstRow, BitField srcTypes, BitField dstTypes, int srcColBegin, int dstColBegin) {
            Validate?.Invoke();
            var it1 = srcTypes.GetEnumerator();
            var it2 = dstTypes.GetEnumerator();
            int i1 = -1, i2 = -1;
            while (it1.MoveNext()) {
                ++i1;
                // Try to find the matching bit
                while (it2.Current < it1.Current) {
                    if (!it2.MoveNext()) return;
                    ++i2;
                }
                // If no matching bit was found
                if (it2.Current != it1.Current) continue;
                // Otherwise copy the value
                if (!src.GetHasSparseComponent(ref this, srcColBegin + i1, srcRow)) {
                    Debug.Assert(!dest.GetHasSparseComponent(ref this, dstColBegin + i2, dstRow),
                        "Sparse component will leak! Dest already has component.");
                    continue;
                }
                var denseDstRow = dest.RequireSparseIndex(ref this, dstColBegin + i2, dstRow);
                // This must happen AFTER the above because the index might shift
                var denseSrcRow = src.TryGetSparseIndex(ref this, srcColBegin + i1, srcRow);
                CopyValue(ref src, srcColBegin + i1, denseSrcRow,
                    ref dest, dstColBegin + i2, denseDstRow, dstRow);
            }
        }

        public void CopyValue(scoped ref Archetype src, int srcColumnId, int srcDenseRow,
            ref Archetype dst, int dstColumnId, int dstDenseRow, int dstRow) {
            ref var srcColumn = ref src.GetColumn(ref this, srcColumnId);
            ref var dstColumn = ref dst.GetColumn(ref this, dstColumnId);
            Debug.Assert(srcColumn.TypeId == dstColumn.TypeId);
            ref var columnData = ref GetColumn(srcColumn.TypeId);
            columnData.CopyValue(dstColumn.DataRange.Start + dstDenseRow,
                srcColumn.DataRange.Start + srcDenseRow);
            dstColumn.NotifyMutation(ref this, dstRow);
        }
        public void CopyValue(scoped ref Archetype dst, int dstColumnId, int dstRow, Array srcData, int srcRow) {
            ref var column = ref dst.GetColumn(ref this, dstColumnId);
            ref var columnData = ref GetColumn(column.TypeId);
            columnData.CopyValue(column.DataRange.Start + dstRow, srcData, srcRow);
            column.NotifyMutation(ref this, dstRow);
        }
        public void ClearRowModifiedFlags(scoped ref Archetype archetype, int row) {
            // Not needed anymore? Handled by ArchetypeColumn
            /*for (int i = 0; i < archetype.AllColumnCount; i++) {
                ref var column = ref archetype.GetColumn(ref this, i);
                if (!column.Revision.HasRevision) continue;
                foreach (var r in column.Revision.RevisionRange) {
                    RevisionStorage.ClearEntry(ref column.Revision.GetChannel(RevisionStorage, r, RevisionStorage.RevisionTypes.Created), row);
                    RevisionStorage.ClearEntry(ref column.Revision.GetChannel(RevisionStorage, r, RevisionStorage.RevisionTypes.Modified), row);
                }
            }*/
        }
        public void ReleaseRow(scoped ref Archetype archetype, int row) {
            ++archetype.Revision;
            if (archetype.MaxItem < 0) { Debug.Assert(archetype.TypeMask.IsEmpty, "Only null archetype has invalid count."); return; }
            Debug.Assert(archetype.MaxItem == row, "Must release from end. Move entity to end first.");
            --archetype.MaxItem;
            Validate?.Invoke();
        }

        public void NotifyCreated(scoped ref Archetype archetype, EntityAddress addr) {
            for (int c = 0; c < archetype.ColumnCount; c++) {
                ref var column = ref archetype.GetColumn(ref this, c);
                column.NotifyCreated(ref this, addr.Row);
            }
        }
        public void NotifyDestroyed(scoped ref Archetype archetype, EntityAddress addr) {
            for (int c = 0; c < archetype.ColumnCount; c++) {
                ref var column = ref archetype.GetColumn(ref this, c);
                column.NotifyDestroy(ref this, addr.Row);
            }
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

}
