using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;

namespace Weesals.ECS {
    public struct Range {
        public int Start;
        public int Length;
        public int End => Start + Length;
        public Range(int start, int length) { Start = start; Length = length; }
        public override string ToString() { return $"<{Start} {Length}>"; }
    }
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
        // TODO: Allow passing a min/max newLength, to avoid requesting too
        // much and missing a window
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
            if (pos >= 2) TryMergeAt(pos - 2);
            TryMergeAt(pos);
            Validate();
            return newStart;
        }
        public int RequireResize(int start, int length, int newLength, int minBound = 0, int maxBound = int.MaxValue) {
            var newOffset = TryExtend(start, length, newLength);
            if (newOffset != -1) return newOffset;
            newOffset = FindAndSetRange(newLength);
            ClearRange(start, length);
            Validate();
            return newOffset;
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

    public class SparsePages {
        public interface IPage {
            public bool IsOccupied => true;
        }
    }
    public class SparsePages<Page> where Page : SparsePages.IPage {
        protected int[] pageOffsets = Array.Empty<int>();
        protected Page[] pages = Array.Empty<Page>();
        protected SparseRanges pagesAllocated = new();
        public int Capacity => pageOffsets.Length;
        public int MaximumPageIndex => pagesAllocated.MaximumIndex;
        public ref Page RequirePage(ref Range pageRange, int pageId) {
            var pageIndex = GetPageIndex(pageRange, pageId);
            if (pageIndex < 0) {
                pageIndex = ~pageIndex;
                if (pageIndex >= pageRange.End || pages[pageIndex].IsOccupied) {
                    InsertPage(ref pageRange, ref pageIndex);
                }
                AssignPage(pageIndex, pageId);
            }
            return ref pages[pageIndex];
        }
        public void InsertPage(ref Range pageRange, ref int pageIndex) {
            var localIndex = pageIndex - pageRange.Start;
            Debug.Assert((uint)localIndex <= pageRange.Length, "Index must be within range (or at end)");
            var newCount = pageRange.Length + 1;
            var newOffset = pagesAllocated.RequireResize(pageRange.Start, pageRange.Length, newCount);
            if (pages.Length < newOffset + newCount) {
                var size = (int)BitOperations.RoundUpToPowerOf2((uint)(newOffset + newCount));
                Array.Resize(ref pageOffsets, size);
                Array.Resize(ref pages, size);
            }
            var newEnd = newOffset + newCount;
            if (newOffset != pageRange.Start) {
                CopyPages(pageRange.Start, newOffset, localIndex);
            }
            CopyPages(pageRange.Start + localIndex, newOffset + localIndex + 1, pageRange.Length - localIndex);
            pageRange = new() { Start = newOffset, Length = newCount, };
            pageIndex = newOffset + localIndex;
            pages[pageIndex] = default;
        }
        public void AssignPage(int pageIndex, int pageId) {
            pageOffsets[pageIndex] = pageId;
        }
        public int GetPageIndex(Range pageRange, int pageId) {
            var pageIndex = Array.BinarySearch(pageOffsets, pageRange.Start, pageRange.Length, pageId);
            return pageIndex;
        }
        public ref Page GetPage(int pageIndex) { return ref pages[pageIndex]; }
        public int GetPageId(int pageIndex) { return pageOffsets[pageIndex]; }
        private void CopyPages(int from, int to, int count) {
            Array.Copy(pages, from, pages, to, count);
            Array.Copy(pageOffsets, from, pageOffsets, to, count);
        }
        public void Clear(ref Range pageRange) {
            pagesAllocated.ClearRange(pageRange.Start, pageRange.Length);
            pageRange = default;
        }

        public struct Enumerator {
            int[] pageOffsets;
            Page[] pages;
            int index, end;
            public int PageOffset => pageOffsets[index];
            public Page Current => pages[index];
            public int PageIndex => index;
            public int End => end;
            public bool IsAtEnd => index >= end;
            public bool IsValid => pages != null;
            public Enumerator(SparsePages<Page> sparsePages, int startPage, int endPage) {
                pageOffsets = sparsePages?.pageOffsets;
                pages = sparsePages?.pages;
                index = startPage - 1;
                end = endPage;
            }
            public bool MoveNext() {
                return ++index < end;
            }
            public void Skip(int count) {
                index += count;
            }
        }
        public Enumerator GetEnumerator(Range pageRange) {
            return new(this, pageRange.Start, pageRange.End);
        }
    }

    public struct ECSSparseArray<T> {
        private T[] data;
        private SparseRanges allocated;

        public ECSSparseArray(int capacity) {
            data = new T[capacity];
            allocated = new();
        }
        public Range AllocateRange(int length) {
            var start = allocated.FindAndSetRange(length);
            if (start + length > data.Length) {
                Array.Resize(ref data, (int)BitOperations.RoundUpToPowerOf2((uint)(start + length + 4)));
            }
            return new(start, length);
        }
        public void ReturnRange(ref Range range) {
            allocated.ClearRange(range.Start, range.Length);
            range = default;
        }
        public void Resize(ref Range range, int newLength) {
            var newPos = allocated.RequireResize(range.Start, range.Length, newLength);
            if (newPos != range.Start) {
                Array.Copy(data, range.Start, data, newPos, Math.Min(range.Length, newLength));
            }
            range = new(newPos, newLength);
        }
        public Span<T> GetRange(Range range) {
            return data.AsSpan(range.Start, range.Length);
        }
    }
}
