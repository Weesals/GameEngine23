using GameEngine23.Interop;
using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Weesals.Utility {
    public class ArrayList<T> {
        public T[] Buffer = Array.Empty<T>();
        public int Count;

        public ref T this[int index] => ref Buffer[index];

        public void Add(T value) {
            if (Count >= Buffer.Length) {
                Reserve(Math.Max(Buffer.Length * 2, 16));
            }
            Buffer[Count] = value;
            ++Count;
        }
        public Span<T> Consume(int count) {
            if (Count + count > Buffer.Length) {
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

        public void Reserve(int count) {
            if (Buffer.Length < count) Array.Resize(ref Buffer, count);
        }
    }
    unsafe public struct MemoryBlock<T> : IEnumerable<T> where T : unmanaged{
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

        public IEnumerator<T> GetEnumerator() { return new Enumerator(this); }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }

        public struct Enumerator : IEnumerator<T> {
            public T* Data;
            public T* End;
            public T Current => *Data;
            object IEnumerator.Current => *Data;
            public Enumerator(MemoryBlock<T> array) {
                Data = array.Data - 1;
                End = array.Data + array.Length;
            }
            public void Dispose() { }
            public bool MoveNext() {
                Data++;
                if (Data >= End) return false;
                return true;
            }
            public void Reset() {
                throw new NotImplementedException();
            }
        }

        public static implicit operator Span<T>(MemoryBlock<T> block) { return block.AsSpan(); }
        public static implicit operator CSSpan(MemoryBlock<T> block) { return new CSSpan(block.Data, block.Length); }
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
        public static implicit operator Span<T>(PooledArray<T> pool) { return pool.AsSpan(); }
    }
    public struct PooledList<T> : IDisposable {
        public T[] Data;
        public int Count;
        public PooledList(int capacity = 4) {
            Data = ArrayPool<T>.Shared.Rent(capacity);
            Count = 0;
        }
        public ref T this[Index index] { get => ref Data[index.GetOffset(Count)]; }
        public ref T this[int index] { get => ref Data[index]; }
        public void Clear() { Count = 0; }
        public void Add(T value) { Reserve(Count + 1); Data[Count++] = value; }
        public void RemoveAt(int index) { for (--Count; index < Count; ++index) Data[index] = Data[index]; }
        private void Reserve(int capacity) {
            if (Data != null && Data.Length >= capacity) return;
            var oldData = Data;
            Data = ArrayPool<T>.Shared.Rent(capacity);
            if (oldData == null) return;
            Array.Copy(oldData, Data, Count);
            ArrayPool<T>.Shared.Return(oldData);
        }
        public Span<T> AsSpan() { return Data.AsSpan(0, Count); }
        public Span<T> AsSpan(int start) { return Data.AsSpan(start, Count - start); }
        public void Dispose() { ArrayPool<T>.Shared.Return(Data); }

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
