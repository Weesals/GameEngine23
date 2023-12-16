using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Weesals.Utility {
    public struct RangeInt {
        public int Start;
        public int Length;
        public int End => Start + Length;
        public RangeInt(int start, int length) { this.Start = start; this.Length = length; }
        public override string ToString() { return "[" + Start + "-" + End + "]"; }
        public static RangeInt FromBeginEnd(int begin, int end) { return new RangeInt(begin, end - begin); }
    }

    public struct SparseIndices {
        private List<RangeInt> ranges = new();

        public IReadOnlyList<RangeInt> Ranges => ranges;

        public SparseIndices() { }
        public int GetSumCount() {
            int count = 0;
            foreach (var range in ranges) count += range.Length;
            return count;
        }
        public int Allocate(RangeInt range) {
            int count = 0;
            for (int i = ranges.Count - 1; i >= 0; i--) {
                var block = ranges[i];
                // Entirely contained within range
                if (block.Start >= range.Start && block.End <= range.End) {
                    count += block.Length;
                    ranges.RemoveAt(i--);
                    continue;
                }
                if (block.Start < range.End && block.End > range.Start) {
                    if (block.Start < range.Start) {
                        var bstart = block;
                        bstart.Length = range.Start - bstart.Start;
                        ranges[i] = bstart;
                        count += block.End - range.Start;
                        if (block.End > range.End) {
                            ranges.Insert(i + 1, new RangeInt(range.End, block.End - range.End));
                            count -= block.End - range.End;
                        }
                    } else if (block.End > range.End) {
                        ranges[i] = new RangeInt(range.Start, block.End - range.Start);
                        count += range.Start - block.Start;
                    }
                }
            }
            return count;
        }
        public RangeInt Allocate(int pointCount) {
            for (int i = 0; i < ranges.Count; i++) {
                var block = ranges[i];
                if (block.Length >= pointCount) {
                    block.Length -= pointCount;
                    block.Start += pointCount;
                    if (block.Length <= 0) ranges.RemoveAt(i);
                    else ranges[i] = block;
                    return new RangeInt(block.Start - pointCount, pointCount);
                }
            }
            return new RangeInt(-1, -1);
        }
        public bool TryAllocateAt(int from, int count) {
            int min = 0, max = ranges.Count - 1;
            while (min < max) {
                var mid = (min + max) / 2;
                if (ranges[mid].Start < from) min = mid + 1;
                else max = mid;
            }
            if (min < ranges.Count && ranges[min].Start == from) {
                var range = ranges[min];
                if (range.Length >= count) {
                    range.Length -= count;
                    range.Start += count;
                    if (range.Length == 0) ranges.RemoveAt(min);
                    else ranges[min] = range;
                    return true;
                }
            }
            return false;
        }
        public void Return(ref RangeInt range) {
            Return(range.Start, range.Length);
            range = default;
        }
        public void Return(int start, int count = 1) {
            ReturnRange(new RangeInt(start, count));
        }
        public void Clear() {
            ranges.Clear();
        }
        public void ReturnRange(RangeInt range) {
            if (ranges.Count == 0) { ranges.Add(range); return; }
            int min = 0, max = ranges.Count;
            while (min < max) {
                var mid = (min + max) / 2;
                if (ranges[mid].Start < range.End) min = mid + 1;
                else max = mid;
            }
            // Merge at end of block
            if (min > 0 && ranges[min - 1].End == range.Start) {
                var block = ranges[min - 1];
                block.Length += range.Length;
                ranges[min - 1] = block;
                AttemptMerge(min - 1);
                return;
            }
            if (min < ranges.Count && ranges[min].Start == range.End) {
                var block = ranges[min];
                block.Start -= range.Length; block.Length += range.Length;
                ranges[min] = block;
                return;
            }
            ranges.Insert(min, range);
        }
        private bool AttemptMerge(int index) {
            if (index < 0 || index >= ranges.Count - 1) return false;
            var p0 = ranges[index];
            var p1 = ranges[index + 1];
            if (p0.End != p1.Start) return false;
            p0.Length += p1.Length;
            ranges[index] = p0;
            ranges.RemoveAt(index + 1);
            return true;
        }
        // Returns the number of removed items
        public int Compact(int from) {
            if (ranges.Count == 0) return 0;
            var back = ranges[^1];
            if (back.End != from) return 0;
            ranges.RemoveAt(ranges.Count - 1);
            return back.Length;
        }
        public bool Contains(int index) {
            for (int i = 0; i < ranges.Count; i++) {
                var range = ranges[i];
                if ((uint)(index - range.Start) < range.Length) return true;
            }
            return false;
        }
        public struct Enumerator : IEnumerator<int>, IEnumerator {
            public SparseIndices Unused;
            public int Count;
            private int unallocIndex;
            public int Current { get; private set; }
            object IEnumerator.Current { get { return Current; } }
            public Enumerator(SparseIndices unused, int count) {
                Unused = unused;
                Count = count;
                unallocIndex = 0;
                Current = -1;
            }
            public bool MoveNext() {
                ++Current;
                if (unallocIndex < Unused.ranges.Count) {
                    var unused = Unused.ranges[unallocIndex];
                    if (Current >= unused.Start) {
                        Current += unused.Length;
                        ++unallocIndex;
                    }
                }
                return Current < Count;
            }
            public void Reset() { throw new NotImplementedException(); }
            public void Dispose() { }
        }
        public Enumerator GetEnumerator(int count) { return new Enumerator(this, count); }
    }

    public class SparseArray<T> : IEnumerable<T> {
        public SparseIndices Unused = new();
        public T[] Items;
        public int Capacity => Items.Length;

        public SparseArray() {
            Items = Array.Empty<T>();
        }

        public ref T this[int index] {
            get {
                return ref Items[index];
            }
        }

        public void Splice(ref RangeInt range, int index, int delete, int insert) {
            int delta = insert - delete;
            if (delta == 0) return;
            if (delta < 0) {
                int start = range.Start + index - delta, end = range.End;
                for (int i = start; i < end; i++) Items[i + delta] = Items[i];
                range.Length += delta;
                Return(range.End, -delta);
            } else {
                Reallocate(ref range, range.Length + delta);
                int start = range.End - delta, end = range.Start + index;
                for (int i = start; i >= end; i--) {
                    Items[i + delta] = Items[i];
                }
            }
        }

        public int Allocate() {
            return Allocate(1).Start;
        }
        public RangeInt Allocate(int itemCount) {
            if (itemCount == 0) return default;
            while (true) {
                var range = Unused.Allocate(itemCount);
                if (range.Start >= 0) return range;
                int start = Items.Length;
                RequireCapacity(start + itemCount);
                //return new RangeInt(start, itemCount);
            }
        }

        public int Add(T value) {
            var id = Allocate();
            Items[id] = value;
            return id;
        }
        public void Return(ref RangeInt range) {
            Return(range.Start, range.Length);
            range = default;
        }
        public void Reallocate(ref RangeInt range, int newLength) {
            if (newLength > range.Length) {
                if (Unused.TryAllocateAt(range.End, newLength - range.Length)) {
                    range.Length = newLength;
                    return;
                } else {
                    var nrange = Allocate(newLength);
                    if (range.Length > 0) {
                        Array.Copy(Items, range.Start, Items, nrange.Start, range.Length);
                        Return(ref range);
                    }
                    range = nrange;
                }
            }
            if (newLength < range.Length) {
                Unused.Return(range.Start + newLength, range.Length - newLength);
                range.Length = newLength;
            }
        }
        public int Append(ref RangeInt range, T value) {
            Reallocate(ref range, range.Length + 1);
            int index = range.End - 1;
            Items[index] = value;
            return index;
        }
        public void Return(int start, int count = 1) {
            Unused.Return(start, count);
        }

        public void Clear() {
            Unused.Clear();
            if (Capacity > 0) Unused.Return(0, Capacity);
        }

        public ArraySegment<T> Slice(RangeInt range) {
            if (range.Length == 0) return new ArraySegment<T>(Items, 0, 0);
            return new ArraySegment<T>(Items, range.Start, range.Length);
        }
        public ArraySegment<T> Slice(int pointStart, int pointCount) {
            return new ArraySegment<T>(Items, pointStart, pointCount);
        }
        public ArraySegment<T> AsArray() {
            return Items;
        }
        public Span<T> AsSpan() {
            return Items.AsSpan();
        }

        public bool ContainsIndex(int index) {
            if ((uint)index >= (uint)Items.Length) return false;
            return !Unused.Contains(index);
        }

        private void RequireCapacity(int newCapacity) {
            if (Items.Length < newCapacity) {
                var oldSize = Items.Length;
                int newSize = Math.Max(oldSize, 32);
                while (newSize < newCapacity) newSize *= 2;
                Array.Resize(ref Items, newSize);
                Unused.Return(oldSize, newSize - oldSize);
            }
        }

        public override string ToString() {
            return $"Array<{Capacity - Unused.GetSumCount()}, Capacity={Capacity}>";
        }

        public struct Enumerator : IEnumerator<T> {
            public SparseIndices.Enumerator Indices;
            public T[] Items;
            public int Index => Indices.Current;
            public T Current { get { return Items[Index]; } set { Items[Index] = value; } }
            object IEnumerator.Current { get { return Current; } }
            public Enumerator(SparseArray<T> array) {
                Indices = array.Unused.GetEnumerator(array.Items.Length);
                Items = array.Items;
            }
            public bool MoveNext() {
                return Indices.MoveNext();
            }
            public void Reset() { throw new NotImplementedException(); }
            public void Dispose() { }
        }

        public Enumerator GetEnumerator() { return new Enumerator(this); }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
        IEnumerator<T> IEnumerable<T>.GetEnumerator() { return GetEnumerator(); }

        public SparseIndices.Enumerator GetIndexEnumerator() {
            return Unused.GetEnumerator(Items.Length);
        }
    }
}
