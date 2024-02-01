using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.ECS;
using Weesals.Engine;

namespace Weesals.Utility {
    public class ArrayList<T> {
        public T[] Buffer = Array.Empty<T>();
        public int Count;

        public ref T this[int index] => ref Buffer[index];
        public ref T this[Index index] => ref Buffer[index.GetOffset(Count)];

        public void Add(T value) {
            if (Count >= Buffer.Length) {
                Reserve(Math.Max(Buffer.Length * 2, 16));
            }
            Buffer[Count] = value;
            ++Count;
        }
        public void RemoveAt(int index) {
            var toMove = Count - index - 1;
            if (toMove > 0) Array.Copy(Buffer, index + 1, Buffer, index, toMove);
            --Count;
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

        public Span<T>.Enumerator GetEnumerator() { return AsSpan().GetEnumerator(); }
    }
    unsafe public struct MemoryBlock<T> where T : unmanaged{
        public T* Data;
        public int Length;
        public bool IsEmpty => Length == 0;
        public ref T this[int index] { get => ref Data[index]; }
        public ref T this[Index index] { get => ref Data[index.GetOffset(Length)]; }

        public MemoryBlock(T* data, int count) { Data = data; Length = count; }

        public Span<T> AsSpan() { return new Span<T>(Data, Length); }
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
        public static implicit operator CSSpan(MemoryBlock<T> block) { return new CSSpan(block.Data, block.Length); }
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
        public int IndexOf(T value) { return Array.IndexOf(Data, value, 0, Count); }
        public Span<T> AsSpan() { return Data.AsSpan(0, Count); }
        public Span<T> AsSpan(int start) { return Data.AsSpan(start, Count - start); }
        unsafe public ref PooledList<T> AsMutable() { return ref this; }
        public void Dispose() { if (Data != null) ArrayPool<T>.Shared.Return(Data); this = default; }
        public Span<T>.Enumerator GetEnumerator() { return AsSpan().GetEnumerator(); }
        public override string ToString() { return $"<count={Count}>"; }

        public static implicit operator Span<T>(PooledList<T> pool) { return pool.AsSpan(); }
    }

    public struct Array8<T> where T : unmanaged {
        public T Value1, Value2, Value3, Value4;
        public T Value5, Value6, Value7, Value8;
        unsafe public ref T this[int index] {
            get { fixed (T* arr = &Value1) return ref arr[index]; }
        }
    }
}
