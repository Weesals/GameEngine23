using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;

namespace Weesals.ECS {
    // Starts "Off", first index is start of first range of "On"
    // posCount should always be a multiple of 2
    public class SparseRanges {
        private int posCount;
        private int[] positions = Array.Empty<int>();
        public int MaximumIndex => posCount == 0 ? 0 : positions[posCount - 1];
        public void Clear() {
            posCount = 0;
        }
        public void Validate() {
            for (int i = 1; i < posCount; i++) {
                Debug.Assert(positions[i - 1] < positions[i]);
            }
        }
        public int GetSumCount() {
            int count = 0;
            for (int i = 1; i < posCount; i += 2) {
                count += positions[i] - positions[i - 1];
            }
            return count;
        }
        // Will force a range to be the specified value
        public void SetRange(int start, int length, bool state = true) {
            var pos = Array.BinarySearch(positions, 0, posCount, start + 1);
            if (pos < 0) pos = ~pos;
            var end = start + length;
            if (((pos & 1) != 0) == state) {     // Within same range
                // Already covered
                if (pos >= posCount || positions[pos] >= end) return;
                // Or extend range
                positions[pos] = end;
                TryMergeAt(pos);
            } else {        // Within other range
                if (pos > 0 && positions[pos - 1] == start) {
                    positions[pos - 1] = end;
                    TryMergeAt(pos - 1);
                } else if (pos < posCount && positions[pos] <= end) {
                    positions[pos] = start;
                    TryMergeAt(pos);
                } else {
                    InsertRange(pos, start, end);
                }
            }
            Validate();
        }
        public void ClearRange(int start, int length) { SetRange(start, length, false); }
        // Only succeeds if the range could be set entirely
        public bool TrySetRange(int start, int length, bool state = true) {
            Validate();
            var pos = Array.BinarySearch(positions, 0, posCount, start + 1);
            if (pos < 0) pos = ~pos;
            // Already set
            if (((pos & 1) != 0) == state) return false;

            var end = start + length;
            if (pos > 0 && positions[pos - 1] == start) {
                positions[pos - 1] = end;
                TryMergeAt(pos - 1);
            } else if (pos < posCount && positions[pos] == end) {
                positions[pos] = start;
                TryMergeAt(pos);
            } else {
                InsertRange(pos, start, end);
            }
            return true;
        }
        // Returns the new start index, or -1 if the range could not be extended
        public int TryExtend(int start, int length, int newLength, int minBound = 0, int maxBound = int.MaxValue) {
            Validate();
            var pos = Array.BinarySearch(positions, 0, posCount, start + 1);
            if (pos < 0) {
                if (posCount == 0) return -1;
                pos = ~pos;
            }

            var end = start + length;
            int potentialStart =
                pos > 0 && positions[pos - 1] != start ? start :
                pos - 2 >= 0 ? positions[pos - 2] : minBound;
            int potentialEnd =
                pos < posCount && positions[pos] != end ? end :
                pos + 1 < posCount ? positions[pos + 1] : maxBound;

            // If there is space
            if (potentialEnd - potentialStart < newLength) return -1;
            // Prefer extending the "end"
            if (potentialEnd >= start + newLength) {
                positions[pos] = start + newLength;
                TryMergeAt(pos);
                return start;
            }
            var newStart = start;
            if (potentialStart != start) {
                newStart = Math.Max(end - newLength, potentialStart);
                positions[pos - 1] = newStart;
            }
            var newEnd = newStart + newLength;
            if (potentialEnd != end) {
                positions[pos] = newEnd;
            }
            // TODO: Do this in 1 pass (update "index" with each step)
            // (or use above info - if potentialEnd == start+newLength)
            TryMergeAt(pos - 1);
            TryMergeAt(pos);
            return newStart;
        }
        // Find and set a contiguous range that can be flipped to the
        // specified state
        public int FindAndSetRange(int size, bool state = true) {
            if (state && posCount > 0 && positions[0] >= size) {
                positions[0] -= size;
                return positions[0];
            }
            for (int i = state ? 1 : 0; i < posCount - 1; i += 2) {
                int start = positions[i];
                if (positions[i + 1] - start < size) continue;
                positions[i] = start + size;
                TryMergeAt(i);
                return start;
            }
            // Can only create "true" regions (since default is false)
            if (!state) return -1;
            // No ranges? Create one at 0
            if (posCount == 0) { InsertRange(0, 0, size); return 0; }
            // Otherwise extend the end
            var last = positions[posCount - 1];
            positions[posCount - 1] += size;
            return last;
        }
        // Get the value at a specific index
        public bool GetValueAt(int index) {
            if (posCount == 0) return false;
            var pos = Array.BinarySearch(positions, 0, posCount, index + 1);
            if (pos < 0) pos = ~pos;
            return (pos & 1) != 0;
        }
        public int Compact(int index) {
            if (posCount == 0 || positions[posCount - 1] != index) return 0;
            posCount -= 2;
            return positions[posCount + 1] - positions[posCount];
        }
        private void InsertRange(int pos, int start, int end) {
            // Need to create a new "on" range
            InsertAt(pos, 2);
            positions[pos] = start;
            positions[pos + 1] = end;
        }
        private void TryMergeAt(int pos) {
            var index = positions[pos];
            int end = pos + 1;
            for (; end < posCount && index >= positions[end]; end += 2) ;
            if (end > pos + 1) {
                RemoveAt(end + 1 >= posCount || index >= positions[end + 1] ? pos + 1 : pos, end - pos - 1);
            }
        }
        private void InsertAt(int index, int count) {
            if (posCount + count > positions.Length) {
                Array.Resize(ref positions, (int)BitOperations.RoundUpToPowerOf2((uint)(posCount + count + 8)));
            }
            if (index < posCount) {
                Array.Copy(positions, index, positions, index + count, posCount - index);
            }
            posCount += count;
        }
        private void RemoveAt(int index, int count) {
            posCount -= count;
            if (index < posCount) {
                Array.Copy(positions, index + count, positions, index, posCount - index);
            }
        }
        public override string ToString() {
            string s = "";
            for (int i = 1; i < posCount; i += 2)
                s += $"[{positions[i - 1]} - {positions[i]}]";
            return s;
        }
        public struct Enumerator : IEnumerator<int>, IEnumerator {
            public SparseRanges Ranges;
            public int Count;
            private int unallocIndex;
            public int Current { get; private set; }
            object IEnumerator.Current { get { return Current; } }
            public Enumerator(SparseRanges unused, bool invert, int count) {
                Ranges = unused;
                Count = count;
                unallocIndex = invert ? 0 : 1;
                Current = unallocIndex > 0 && unallocIndex < Ranges.posCount
                    ? Ranges.positions[unallocIndex - 1] - 1 : -1;
            }
            public bool MoveNext() {
                ++Current;
                if (unallocIndex < Ranges.posCount) {
                    var limit = Ranges.positions[unallocIndex];
                    if (Current >= limit) {
                        ++unallocIndex;
                        if (unallocIndex >= Ranges.posCount) return false;
                        Current = Ranges.positions[unallocIndex];
                        ++unallocIndex;
                    }
                }
                return Current < Count;
            }
            public void Reset() { throw new NotImplementedException(); }
            public void Dispose() { }
            public void RepairIterator() {
                while (unallocIndex >= 2 && Current < Ranges.positions[unallocIndex - 1]) unallocIndex -= 2;
            }
        }
        public Enumerator GetEnumerator() { return new Enumerator(this, false, posCount == 0 ? 0 : positions[posCount - 1]); }
        public Enumerator GetInvertedEnumerator(int count) { return new Enumerator(this, true, count); }
    }

    public class SparseStorage {
        public const int PageShift = 5;
        public struct Page {
            public int Offset;
            public byte Allocated;
            public int BitIndex;
            public uint BitMask;
            public override string ToString() { return $"{Offset} +{BitMask:X} @{Allocated}"; }
        }
        public struct PageRange {
            public int Offset, Count;
        }
        protected int pageCount = 0;
        protected int[] pageOffsets = Array.Empty<int>();
        protected Page[] pages = Array.Empty<Page>();
        protected SparseRanges pagesAllocated = new();
        public int PageCount => pageCount;
        public ref Page RequirePage(ref PageRange pageRange, int pageId) {
            var pageIndex = GetPageIndex(pageRange, pageId);
            if (pageIndex < 0) {
                pageIndex = ~pageIndex;
                if (pageIndex >= pageCount || pages[pageIndex].BitMask != 0) {
                    var localIndex = pageIndex - pageRange.Offset;
                    var newCount = pageRange.Count + 1;
                    var newOffset = pagesAllocated.TryExtend(pageRange.Offset, pageRange.Count, newCount);
                    if (newOffset == -1) {
                        newOffset = pagesAllocated.FindAndSetRange(newCount);
                        pagesAllocated.ClearRange(pageRange.Offset, pageRange.Count);
                    }
                    if (pages.Length < newOffset + newCount) {
                        var size = (int)BitOperations.RoundUpToPowerOf2((uint)(newOffset + newCount));
                        Array.Resize(ref pageOffsets, size);
                        Array.Resize(ref pages, size);
                    }
                    var newEnd = newOffset + newCount;
                    if (newOffset != pageRange.Offset) {
                        CopyPages(pageRange.Offset, newOffset, localIndex);
                    }
                    CopyPages(pageRange.Offset + localIndex, newOffset + localIndex + 1, pageRange.Count - localIndex);
                    pageRange = new() { Offset = newOffset, Count = newCount, };
                    pageIndex = newOffset + localIndex;
                    pages[pageIndex] = default;
                }
                pageOffsets[pageIndex] = pageId;
            }
            return ref pages[pageIndex];
        }
        public int GetPageIndex(PageRange pageRange, int pageId) {
            var pageIndex = Array.BinarySearch(pageOffsets, pageRange.Offset, pageRange.Count, pageId);
            //if (pageIndex < 0) pageIndex -= pageRange.Offset; else pageIndex += pageRange.Offset;
            return pageIndex;
        }
        public ref Page GetPage(int pageIndex) { return ref pages[pageIndex]; }
        public int GetPageId(int pageIndex) { return pageOffsets[pageIndex]; }
        private void CopyPages(int from, int to, int count) {
            Array.Copy(pages, from, pages, to, count);
            Array.Copy(pageOffsets, from, pageOffsets, to, count);
        }

        public static int IndexToPage(int index) { return index >> PageShift; }
        public static uint IndexToMask(int index) { return ~(~0u << index); }   // All lower bits (exclusive)
        public static uint IndexToUpperMask(int index) { return (~0u << index); }   // All lower bits (exclusive)
        public static uint IndexToBit(int index) { return 1u << index; }
        public static int PageToIndex(int pageId) { return pageId << PageShift; }
    }
    public class SparseColumnStorage {
        public struct DataMutation {
            public int PreviousOffset, NewOffset;
            public int NewCount;
            public int Index;
        }
        protected readonly SparseStorage pageStorage;
        protected SparseRanges allocated = new();
        public SparseColumnStorage(SparseStorage _pageStorage, ComponentType _componentType) {
            pageStorage = _pageStorage;
        }
        public DataMutation AllocateIndex(ref SparseStorage.PageRange pageRange, int index) {
            ref var page = ref pageStorage.RequirePage(ref pageRange, SparseStorage.IndexToPage(index));
            return AllocateIndex(ref page, index);
        }
        private DataMutation AllocateIndex(ref SparseStorage.Page page, int index) {
            page.BitMask |= 1u << index;
            var bitIndex = BitOperations.PopCount(page.BitMask & SparseStorage.IndexToMask(index));
            int origOffset = page.Offset;
            int pageCount = BitOperations.PopCount(page.BitMask);
            if (pageCount > page.Allocated) {
                var newAllocated = (int)BitOperations.RoundUpToPowerOf2((uint)page.Allocated + 4);
                var newOffset = allocated.TryExtend(page.Offset, page.Allocated, newAllocated);
                if (newOffset == -1) {
                    newOffset = allocated.FindAndSetRange(newAllocated);
                }
                /*if (newOffset + newAllocated >= data.Length) {
                    Resize((int)BitOperations.RoundUpToPowerOf2((uint)(newOffset + newAllocated)));
                }
                if (newOffset != page.Offset) {
                    Array.Copy(data, page.Offset, data, newOffset, bitIndex);
                }*/
                page.Offset = newOffset;
                page.Allocated = (byte)newAllocated;
            }
            //Array.Copy(data, origOffset + bitIndex, data, page.Offset + bitIndex + 1, pageCount - 1 - bitIndex);
            return new DataMutation() {
                PreviousOffset = origOffset,
                NewOffset = page.Offset,
                NewCount = pageCount,
                Index = bitIndex,
            };
        }
        public bool GetHasIndex(SparseStorage.PageRange pageRange, int index) {
            var pageIndex = pageStorage.GetPageIndex(pageRange, SparseStorage.IndexToPage(index));
            if (pageIndex < 0) return false;
            ref var page = ref pageStorage.GetPage(pageIndex);
            return (page.BitMask & SparseStorage.IndexToBit(index)) != 0;
        }
        public int TryGetIndex(SparseStorage.PageRange pageRange, int index) {
            var pageIndex = pageStorage.GetPageIndex(pageRange, SparseStorage.IndexToPage(index));
            if (pageIndex < 0) return -1;
            ref var page = ref pageStorage.GetPage(pageIndex);
            var bitIndex = BitOperations.PopCount(page.BitMask & SparseStorage.IndexToMask(index));
            if ((page.BitMask & SparseStorage.IndexToBit(index)) == 0) return -1;
            return page.Offset + bitIndex;
        }
        public int GetIndex(SparseStorage.PageRange pageRange, int index) {
            var pageIndex = pageStorage.GetPageIndex(pageRange, SparseStorage.IndexToPage(index));
            ref var page = ref pageStorage.GetPage(pageIndex);
            var bitIndex = BitOperations.PopCount(page.BitMask & SparseStorage.IndexToMask(index));
            return page.Offset + bitIndex;
        }
        public int GetNextIndex(SparseStorage.PageRange pageRange, int index) {
            var bitMask = SparseStorage.IndexToUpperMask(index);
            var pageIndex = pageStorage.GetPageIndex(pageRange, SparseStorage.IndexToPage(index));
            if (pageIndex < 0) {
                pageIndex = ~pageIndex;
                bitMask = SparseStorage.IndexToUpperMask(0);
            }
            var pageEnd = pageRange.Offset + pageRange.Count;
            while (pageIndex < pageEnd) {
                ref var page = ref pageStorage.GetPage(pageIndex);
                var masked = page.BitMask & bitMask;
                if (masked != 0) return SparseStorage.PageToIndex(pageStorage.GetPageId(pageIndex)) + BitOperations.TrailingZeroCount(masked);
                ++pageIndex;
            }
            return -1;
        }
        public DataMutation RequireIndex(ref SparseStorage.PageRange pageRange, int index) {
            ref var page = ref pageStorage.RequirePage(ref pageRange, SparseStorage.IndexToPage(index));
            if ((page.BitMask & SparseStorage.IndexToBit(index)) != 0)
                return new() { NewCount = -1, NewOffset = page.Offset, Index = BitOperations.PopCount(page.BitMask & SparseStorage.IndexToMask(index)), };
            return AllocateIndex(ref page, index);
        }
        public DataMutation RemoveIndex(ref SparseStorage.PageRange pageRange, int index) {
            var pageIndex = pageStorage.GetPageIndex(pageRange, SparseStorage.IndexToPage(index));
            ref var page = ref pageStorage.GetPage(pageIndex);
            page.BitMask &= ~SparseStorage.IndexToBit(index);
            int pageCount = BitOperations.PopCount(page.BitMask);
            var bitIndex = BitOperations.PopCount(page.BitMask & SparseStorage.IndexToMask(index));
            //Array.Copy(data, page.Offset + bitIndex, data, page.Offset + bitIndex + 1, pageCount - bitIndex);
            return new() { PreviousOffset = page.Offset, NewOffset = page.Offset, Index = bitIndex, NewCount = pageCount, };
        }
        public DataMutation TryRemoveIndex(ref SparseStorage.PageRange pageRange, int index) {
            var pageIndex = pageStorage.GetPageIndex(pageRange, SparseStorage.IndexToPage(index));
            if (pageIndex < 0) return new() { NewCount = -1, };
            ref var page = ref pageStorage.GetPage(pageIndex);
            if ((page.BitMask & SparseStorage.IndexToBit(index)) == 0) return new() { NewCount = -1, };
            page.BitMask &= ~SparseStorage.IndexToBit(index);
            int pageCount = BitOperations.PopCount(page.BitMask);
            var bitIndex = BitOperations.PopCount(page.BitMask & SparseStorage.IndexToMask(index));
            //Array.Copy(data, page.Offset + bitIndex, data, page.Offset + bitIndex + 1, pageCount - bitIndex);
            return new() { PreviousOffset = page.Offset, NewOffset = page.Offset, Index = bitIndex, NewCount = pageCount, };
        }
        public int GetSparseIndex(SparseStorage.PageRange pageRange, int index) {
            for (int i = 0; i < pageRange.Count; i++) {
                var pageIndex = pageRange.Offset + i;
                var page = pageStorage.GetPage(pageIndex);
                var localIndex = index - page.Offset;
                var bitMask = page.BitMask;
                if (localIndex < BitOperations.PopCount(bitMask)) {
                    for (; bitMask != 0; --localIndex) {
                        if (localIndex == 0) {
                            return SparseStorage.PageToIndex(pageStorage.GetPageId(pageIndex)) + BitOperations.TrailingZeroCount(bitMask);
                        }
                        bitMask &= bitMask - 1;
                    }
                }
            }
            return -1;
        }
        public uint GetPageMask(SparseStorage.PageRange pageRange, int index) {
            var pageIndex = pageStorage.GetPageIndex(pageRange, SparseStorage.IndexToPage(index));
            if (pageIndex < 0) return 0;
            return pageStorage.GetPage(pageIndex).BitMask;
        }
        /*protected virtual void Resize(int size) {
            componentType.Resize(ref data, size);
        }*/
    }
    public struct SparseColumnArchetype {
        private SparseColumnStorage storage;
        private SparseStorage.PageRange pageRange;
        public SparseColumnArchetype(SparseColumnStorage _storage) { storage = _storage; pageRange = default; }
        public SparseColumnStorage.DataMutation AllocateIndex(ref SparseStorage.PageRange pageRange, int row) => storage.AllocateIndex(ref pageRange, row);
        public bool GetHasIndex(int row) => storage.GetHasIndex(pageRange, row);
        public int TryGetIndex(int row) => storage.TryGetIndex(pageRange, row);
        public int GetIndex(int row) => storage.GetIndex(pageRange, row);
        public int GetNextIndexInclusive(int row) => storage.GetNextIndex(pageRange, row);  // Not dense!
        public SparseColumnStorage.DataMutation RequireIndex(int row) => storage.RequireIndex(ref pageRange, row);
        public SparseColumnStorage.DataMutation RemoveIndex(int row) => storage.RemoveIndex(ref pageRange, row);
        public SparseColumnStorage.DataMutation TryRemoveIndex(int row) => storage.TryRemoveIndex(ref pageRange, row);
        public int GetSparseIndex(int denseRow) => storage.GetSparseIndex(pageRange, denseRow);
        public uint GetPageMask(int pageId) => storage.GetPageMask(pageRange, pageId);
    }
#if false
    public abstract class SparseColumnMapBase {
        public struct Page {
            public int Offset;
            public byte Count;
            public byte Allocated;
            public override string ToString() { return $"{Offset} +{Count} @{Allocated}"; }
        }
        protected int pageCount = 0;
        protected Page[] pages = Array.Empty<Page>();
        protected DynamicBitField occupied = new();
        protected SparseRanges allocated = new();
        protected Array data;

        public Array Data => data;

        public SparseColumnMapBase(Array zeroArray) {
            data = zeroArray;
        }
        public int Allocate(Entity entity) {
            var pageEnum = occupied.GetPageEnumerator();
            var pageCount = pageEnum.PageCount;
            var pageOffset = pageEnum.GetPageOffset((int)entity.Index);
            var pageIndex = pageEnum.RequirePageIndex(pageOffset, out _);
            if (!pageEnum.SetBit(pageIndex, (int)entity.Index)) return -1;
            if (pageEnum.PageCount != pageCount) {
                InsertPage(pageIndex);
            }
            var localIndex = pageEnum.GetLocalBitIndex(pageIndex, (int)entity.Index);
            var denseIndex = InsertItem(pageIndex, localIndex);
            Debug.Assert(pages[pageIndex].Count == BitOperations.PopCount(pageEnum.GetPageContentAt(pageIndex)));
            return denseIndex;
        }
        public int GetDenseIndex(Entity entity) {
            var pageEnum = occupied.GetPageEnumerator();
            var pageOffset = pageEnum.GetPageOffset((int)entity.Index);
            var pageIndex = pageEnum.GetPageIndex(pageOffset);
            var localIndex = pageEnum.GetLocalBitIndex(pageIndex, (int)entity.Index);
            ref var page = ref pages[pageIndex];
            return page.Offset + localIndex;
        }
        public ArrayItem GetAsArrayItem(Entity entity) {
            return new(data, GetDenseIndex(entity));
        }
        public void Remove(Entity entity) {
            var pageEnum = occupied.GetPageEnumerator();
            var pageOffset = pageEnum.GetPageOffset((int)entity.Index);
            var pageIndex = pageEnum.GetPageIndex(pageOffset);
            var localIndex = pageEnum.GetLocalBitIndex(pageIndex, (int)entity.Index);
            ref var page = ref pages[pageIndex];
            --page.Count;
            if (localIndex < page.Count) {
                Array.Copy(data, localIndex + 1, data, localIndex, page.Count - localIndex);
            }
            occupied.Remove((int)entity.Index);
            Debug.Assert(pages[pageIndex].Count == BitOperations.PopCount(pageEnum.GetPageContentAt(pageIndex)));
        }

        protected void InsertPage(int pageIndex) {
            if (pageCount + 1 > pages.Length) {
                Array.Resize(ref pages, (int)BitOperations.RoundUpToPowerOf2((uint)pages.Length + 8));
            }
            pageCount++;
            if (pageIndex == pageCount - 1) return;
            Array.Copy(pages, pageIndex, pages, pageIndex + 1, pageCount - pageIndex);
            pages[pageIndex] = default;
        }
        protected int InsertItem(int pageIndex, int localIndex) {
            ref var page = ref pages[pageIndex];
            if (page.Count == page.Allocated) {
                int newAllocated = Math.Clamp((int)BitOperations.RoundUpToPowerOf2((uint)page.Allocated + 4), 4, 64);
                var newOffset = -1;
                if (page.Allocated != 0) {
                    newOffset = allocated.TryExtend(page.Offset, page.Allocated, newAllocated);
                }
                if (newOffset == -1) {
                    newOffset = allocated.FindAndSetRange(newAllocated);
                    allocated.SetRange(page.Offset, page.Allocated, false);
                }
                if (newOffset != page.Offset) {
                    Array.Copy(data, page.Offset, data, newOffset, page.Count);
                }
                page.Offset = newOffset;
                page.Allocated = (byte)newAllocated;
                if (page.Offset + page.Allocated > data.Length) {
                    var allocSize = (int)BitOperations.RoundUpToPowerOf2((uint)(page.Offset + page.Allocated + 64));
                    ResizeData(allocSize);
                }
            }
            if (localIndex < page.Count) {
                Array.Copy(data, page.Offset + localIndex,
                    data, page.Offset + localIndex + 1,
                    page.Count - localIndex);
            }
            page.Count++;
            Debug.Assert(page.Allocated >= page.Count);
            return page.Offset + localIndex;
        }
        protected abstract void ResizeData(int allocSize);
    }
    public class SparseColumnMap<T> : SparseColumnMapBase {
        public new T[] Data => (T[])data;

        public SparseColumnMap() : base(Array.Empty<T>()) {
        }
        public ref T Require(Entity entity) {
            var itemIndex = Allocate(entity);
            Data[itemIndex] = default;
            return ref Data[itemIndex];
        }
        public ref T Get(Entity entity) {
            return ref Data[GetDenseIndex(entity)];
        }
        protected override void ResizeData(int allocSize) {
            var typedData = Data;
            Array.Resize(ref typedData, allocSize);
            data = typedData;
        }

    }
#endif
}
