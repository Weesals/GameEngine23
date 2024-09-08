using System;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Weesals {
    public class ArrayList<T> {
        public T[] Buffer = Array.Empty<T>();
        public int Count;

        public ref T this[int index] => ref Buffer[index];
        public ref T this[Index index] => ref Buffer[index.GetOffset(Count)];

        public int Add(T value) {
            if (Count >= Buffer.Length) {
                Reserve(Math.Max(Buffer.Length * 2, 16));
            }
            var index = Count++;
            Buffer[index] = value;
            return index;
        }
        public void RemoveAt(int index) {
            Debug.Assert(index <= Count);
            var toMove = Count - index - 1;
            if (toMove > 0) Array.Copy(Buffer, index + 1, Buffer, index, toMove);
            --Count;
        }
        public void RemoveRange(int start, int length) {
            Debug.Assert(start + length <= Count);
            Count -= length;
            if (start > Count) Array.Copy(Buffer, start + length, Buffer, start, length);
        }
        public void Reserve(int count) {
            if (Buffer.Length < count) Array.Resize(ref Buffer, count);
        }
        public bool CanConsume(int count) {
            return Count + count <= Buffer.Length;
        }
        public Span<T> Consume(int count) {
            if (!CanConsume(count)) {
                Reserve(Math.Max(Buffer.Length * 2, Math.Max(16, Count + count)));
            }
            Count += count;
            return Buffer.AsSpan(Count - count, count);
        }
        public void Clear() {
            Count = 0;
        }

        public Span<T> AsSpan() {
            return new Span<T>(Buffer, 0, Count);
        }
        public Span<T> AsSpan(int start, int count) {
            return new Span<T>(Buffer, start, count);
        }
        public void CopyTo(T[] destination) {
            Array.Copy(Buffer, destination, Count);
        }

        public static implicit operator Span<T>(ArrayList<T> arr) => arr.AsSpan();

        public Span<T>.Enumerator GetEnumerator() { return AsSpan().GetEnumerator(); }
    }
    unsafe public struct MemoryBlock<T> where T : unmanaged{
        public T* Data;
        public int Length;
        public bool IsEmpty => Length == 0;
        public ref T this[int index] { get { Debug.Assert((uint)index < Length); return ref Data[index]; } }
        public ref T this[Index index] => ref this[index.GetOffset(Length)];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public MemoryBlock(T* data, int count) { Data = data; Length = count; }

        public Span<T> AsSpan() { return new Span<T>(Data, Length); }
        public Span<T> AsSpan(int offset) { return new Span<T>(Data + offset, Length - offset); }
        public Span<T> AsSpan(int offset, int length) { Debug.Assert(offset + length < Length); return new Span<T>(Data + offset, length); }
        public ref T GetPinnableReference() { return ref *Data; }
        public MemoryBlock<T> Slice(int start) {
            return new MemoryBlock<T>(Data + start, Length - start);
        }
        public MemoryBlock<T> Slice(int start, int length) {
            return new MemoryBlock<T>(Data + start, Math.Min(length, Length - start));
        }
        public MemoryBlock<O> Reinterpret<O>() where O : unmanaged {
            return new MemoryBlock<O>((O*)Data, Length * sizeof(T) / sizeof(O));
        }
        public void CopyTo(Span<T> dest) { AsSpan().CopyTo(dest); }
        public Span<T>.Enumerator GetEnumerator() { return AsSpan().GetEnumerator(); }
        public override string ToString() { return $"<count={Length}>"; }
        public static implicit operator Span<T>(MemoryBlock<T> block) { return block.AsSpan(); }
        public static implicit operator ReadOnlySpan<T>(MemoryBlock<T> block) { return block.AsSpan(); }
        public int GetContentsHash() {
            int hash = 0;
            foreach (var item in AsSpan()) hash = hash * 668265263 + item.GetHashCode();
            return hash;
        }
    }
    public struct PooledArray<T> : IDisposable {
        public T[] Data;
        public int Count;
        public PooledArray(int count) {
            Data = ArrayPool<T>.Shared.Rent(count);
            Count = count;
        }
        public PooledArray(Span<T> other, int count) : this(count) {
            int copy = Math.Min(count, other.Length);
            for (int i = 0; i < copy; ++i) Data[i] = other[i];
        }
        public ref T this[Index index] { get => ref Data[index.GetOffset(Count)]; }
        public ref T this[int index] { get => ref Data[index]; }
        public Span<T> AsSpan() { return Data.AsSpan(0, Count); }
        public Span<T> AsSpan(int start) { return Data.AsSpan(start, Count - start); }
        public void Dispose() { ArrayPool<T>.Shared.Return(Data); }
        public ArraySegment<T>.Enumerator GetEnumerator() {
            return new ArraySegment<T>(Data, 0, Count).GetEnumerator();
        }
        public override string ToString() { return $"<count={Count}>"; }

        public static implicit operator Span<T>(PooledArray<T> pool) { return pool.AsSpan(); }
        public static PooledArray<T> FromEnumerator<En>(En en) where En : IEnumerable<T> {
            var arr = new PooledArray<T>(en.Count());
            int index = 0;
            foreach (var item in en) arr[index++] = item;
            return arr;
        }

        public static void Resize(ref T[] array, int size) {
            if (array == null || array.Length < size) {
                var pool = ArrayPool<T>.Shared;
                if (array != null && array.Length > 0) pool.Return(array);
                array = pool.Rent(size);
            }
        }
        public static void Return(ref T[] array) {
            if (array == null) return;
            ArrayPool<T>.Shared.Return(array);
            array = Array.Empty<T>();
        }
    }
    public struct PooledList<T> : IDisposable {
        public T[] Data;
        public int Count;
        public bool IsCreated => Data != null;
        public bool IsEmpty => Count == 0;
        public PooledList() : this(4) { }
        public PooledList(int capacity) {
            Data = ArrayPool<T>.Shared.Rent(capacity);
            Count = 0;
        }
        public ref T this[Index index] { get => ref Data[index.GetOffset(Count)]; }
        public ref T this[int index] { get => ref Data[index]; }
        public void Clear() { Count = 0; }
        public void Add(T value) { Reserve(Count + 1); Data[Count++] = value; }
        public bool Contains(T value) { return Array.IndexOf(Data, value, 0, Count) >= 0; }
        public void Insert(int index, T item) {
            Reserve(Count + 1);
            Array.Copy(Data, index, Data, index + 1, Count - index);
            Data[index] = item;
            ++Count;
        }
        public void RemoveRange(int index, int count) {
            Count -= count;
            if (index < Count) Array.Copy(Data, index + count, Data, index, Count - index);
        }
        public void RemoveAt(int index) { Array.Copy(Data, index + 1, Data, index, Count - index - 1); --Count; }
        public void Remove(T value) {
            var index = IndexOf(value);
            if (index >= 0) RemoveAt(index);
        }
        private void Reserve(int capacity) {
            if (Data != null && Data.Length >= capacity) return;
            var oldData = Data;
            Data = ArrayPool<T>.Shared.Rent(capacity);
            if (oldData == null) return;
            Array.Copy(oldData, Data, Count);
            ArrayPool<T>.Shared.Return(oldData);
        }
        public RangeInt Add(T value, int count) {
            Reserve(Count + count);
            Array.Fill(Data, value, Count, count);
            Count += count;
            return new RangeInt(Count - count, count);
        }
        public RangeInt AddCount(int count) {
            var index = Count;
            Reserve(Count + count);
            Count += count;
            return new RangeInt(index, count);
        }
        public RangeInt AddRange(Span<T> values) {
            if (values.Length == 0) return new RangeInt(Count, 0);
            Reserve(Count + values.Length);
            values.CopyTo(Data.AsSpan(Count, values.Length));
            Count += values.Length;
            return new RangeInt(Count - values.Length, values.Length);
        }
        public RangeInt AddRange<C>(C values) where C : IReadOnlyCollection<T> {
            var valueCount = values.Count;
            if (valueCount == 0) return new RangeInt(Count, 0);
            Reserve(Count + valueCount);
            foreach (var item in values) Data[Count++] = item;
            return new RangeInt(Count - valueCount, valueCount);
        }
        public int IndexOf(T value) { return Array.IndexOf(Data, value, 0, Count); }
        public Span<T> AsSpan() { return Data.AsSpan(0, Count); }
        public Span<T> AsSpan(int start) { return Data.AsSpan(start, Count - start); }
        public Span<T> AsSpan(int start, int length) { Debug.Assert(length <= Count - start); return Data.AsSpan(start, length); }
        unsafe public ref PooledList<T> AsMutable() { return ref this; }
        public T[] ToArray() { return AsSpan().ToArray(); }
        public void Dispose() { if (Data != null) ArrayPool<T>.Shared.Return(Data); this = default; }
        public Span<T>.Enumerator GetEnumerator() { return AsSpan().GetEnumerator(); }
        public override string ToString() { return $"<count={Count}>"; }

        public void Swap(int index1, int index2) {
            var t = Data[index1];
            Data[index1] = Data[index2];
            Data[index2] = t;
        }

        public void CopyFrom<En>(En items) where En : ICollection<T> {
            int newCount = items.Count;
            Count = 0;
            Reserve(newCount);
            Count = newCount;
            items.CopyTo(Data, 0);
        }

        public static implicit operator Span<T>(PooledList<T> pool) { return pool.AsSpan(); }
    }
}
