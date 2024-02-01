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

namespace Weesals.Utility {
    public struct PooledHashSet<TKey> : IDisposable, IEnumerable<TKey> where TKey : IEquatable<TKey> {
        public struct HashSet : IDisposable {
            public TKey[] Keys;
            public int[] Remap;
            public ulong[] UnallocBucketMask;
            public int Capacity;
            public bool IsAllocated => Capacity > 0;
            public HashSet(int capacity = 16) {
                Keys = ArrayPool<TKey>.Shared.Rent(capacity);
                Remap = ArrayPool<int>.Shared.Rent(capacity * 2);
                UnallocBucketMask = ArrayPool<ulong>.Shared.Rent((capacity + 63) >> 5);
                Capacity = capacity;
                Clear();
            }
            public void Clear() {
                UnallocBucketMask.AsSpan().Fill(unchecked((ulong)(-1)));
                Remap.AsSpan().Fill(-1);
            }
            public void Dispose() {
                ArrayPool<TKey>.Shared.Return(Keys);
                ArrayPool<int>.Shared.Return(Remap);
                ArrayPool<ulong>.Shared.Return(UnallocBucketMask);
            }
            public int FindFreeBucket() {
                for (int i = 0; i < UnallocBucketMask.Length; i++) {
                    if (UnallocBucketMask[i] == 0) continue;
                    return i * 64 + BitOperations.TrailingZeroCount(UnallocBucketMask[i]);
                }
                return -1;
            }
            public int Insert(int hash, TKey key) {
                var index = FindFreeBucket();
                Debug.Assert(Remap[hash | Capacity] != index,
                    "New item already referenced");
                Remap[index] = Remap[hash | Capacity];
                Remap[hash | Capacity] = index;
                Keys[index] = key;
                UnallocBucketMask[index >> 6] &= ~(1ul << (index & 63));
                return index;
            }
            public bool Remove(int hash, TKey key) {
                int parent = hash | Capacity;
                int index = Remap[hash | Capacity];
                while (index >= 0 && !Keys[index].Equals(key)) {
                    parent = index;
                    index = Remap[index];
                }
                if (index < 0) return false;
                RemoveChild(parent, index);
                return true;
            }
            public void RemoveChild(int parent, int index) {
                if (parent >= 0) Remap[parent] = Remap[index];
                Remap[index] = -1;
                UnallocBucketMask[index >> 6] |= (1ul << (index & 63));
            }
            public int GetIndexOf(int hash) {
                return hash | Capacity;
            }
            public int GetNextIndexOf(int index, TKey key) {
                if (index < 0) return index;
                while (true) {
                    index = Remap[index];
                    if (index < 0 || Keys[index].Equals(key)) break;
                }
                return index;
            }
            public int GetNextIndex(int index) {
                ++index;
                for (int page = index >> 6; page < UnallocBucketMask.Length; ++page) {
                    var mask = ~UnallocBucketMask[page];
                    mask &= (ulong)-(long)(1ul << (index & 63));
                    if (mask == 0) continue;
                    return page * 64 + BitOperations.TrailingZeroCount(mask);
                }
                return -1;
            }

            public void CopyTo(HashSet other) {
                other.Dispose();
                other.Capacity = Capacity;
                other.Keys = ArrayPool<TKey>.Shared.Rent(Capacity);
                other.Remap = ArrayPool<int>.Shared.Rent(Capacity * 2);
                other.UnallocBucketMask = ArrayPool<ulong>.Shared.Rent((Capacity + 63) >> 5);
                Keys.AsSpan(0, Capacity).CopyTo(Keys.AsSpan(0, Capacity));
                Remap.AsSpan(0, Capacity).CopyTo(Remap.AsSpan(0, Capacity));
                UnallocBucketMask.AsSpan(0, Capacity).CopyTo(other.UnallocBucketMask.AsSpan(0, Capacity));
            }
        }
        HashSet map;
        int count;

        public int Count => count;
        public int Capacity => map.Capacity;

        public PooledHashSet(int capacity = 16) { map = new(capacity); }
        public void Dispose() { map.Dispose(); }

        // Resize based on hard-coded loading factor 75%
        private void ResizeIfRequired() {
            if (1 + count * 4 > Capacity * 3) {
                Resize((int)BitOperations.RoundUpToPowerOf2((uint)Capacity + 32));
            }
        }
        // Add an item to the map
        public void Add(TKey key) {
            ResizeIfRequired();
            var hash = key.GetHashCode() & (map.Capacity - 1);
            // Check if it already exists
            Debug.Assert(!Contains(key), "Map already contains item");
            map.Insert(hash, key);
            ++count;
        }
        public bool Remove(TKey key) {
            var hash = key.GetHashCode() & (map.Capacity - 1);
            return map.Remove(hash, key);
        }
        public bool Contains(TKey key) {
            var hash = key.GetHashCode() & (map.Capacity - 1);
            return map.GetNextIndexOf(map.GetIndexOf(hash), key) >= 0;
        }
        public void Clear() {
            map.Clear();
            count = 0;
        }

        public void Resize(int newCapacity) {
            var newMap = new HashSet(newCapacity);
            if (map.IsAllocated) {
                for (int i = 0; i < map.UnallocBucketMask.Length; i++) {
                    var mask = ~map.UnallocBucketMask[i];
                    for (; mask != 0;) {
                        int b = BitOperations.TrailingZeroCount(mask);
                        mask &= ~(1ul << b);
                        var index = b + i * 64;
                        var key = map.Keys[index];
                        var hash = key.GetHashCode() & (map.Capacity - 1);
                        newMap.Insert(hash, key);
                    }
                }
                map.Dispose();
            }
            map = newMap;
        }

        public struct Enumerator : IEnumerator<TKey> {
            private HashSet map;
            private int index;
            public Enumerator(PooledHashSet<TKey> dictionary) {
                map = dictionary.map;
                index = -1;
            }
            public TKey Current => map.Keys[index];
            object IEnumerator.Current => Current;
            public void Dispose() { }
            public void Reset() { }
            public bool MoveNext() {
                index = map.GetNextIndex(index);
                return index >= 0;
            }
        }
        public Enumerator GetEnumerator() => new(this);
        IEnumerator<TKey> IEnumerable<TKey>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    public struct PooledHashMap<TKey, TValue> : IDisposable, IEnumerable<KeyValuePair<TKey, TValue>> where TKey : IEquatable<TKey> {

        public struct ChainedMap : IDisposable {
            public PooledHashSet<TKey>.HashSet Set;
            public TKey[] Keys => Set.Keys;
            public TValue[] Values;
            public ulong[] UnallocBucketMask => Set.UnallocBucketMask;
            public int Capacity => Set.Capacity;
            public bool IsAllocated => Capacity > 0;

            public bool IsCreated => Set.IsAllocated;

            public ChainedMap(int capacity = 16) {
                Set = new(capacity);
                Values = ArrayPool<TValue>.Shared.Rent(capacity);
                Set.Capacity = Math.Min(Set.Capacity, Values.Length);
            }
            public void Clear() {
                Set.Clear();
            }
            public void Dispose() {
                Set.Dispose();
                ArrayPool<TValue>.Shared.Return(Values);
            }
            public int Insert(int hash, TKey key, TValue value) {
                int index = Set.Insert(hash, key);
                Values[index] = value;
                return index;
            }
            public bool Remove(int hash, TKey key) {
                return Set.Remove(hash, key);
            }
            public int GetIndexOf(int hash) {
                return Set.GetIndexOf(hash);
            }
            public int GetFirstIndexOf(int hash, TKey key) {
                return Set.GetNextIndexOf(Set.GetIndexOf(hash), key);
            }

            public void CopyTo(ChainedMap other) {
                Dispose();
                Set.CopyTo(other.Set);
                other.Values = ArrayPool<TValue>.Shared.Rent(Capacity);
                Values.AsSpan(0, Capacity).CopyTo(other.Values.AsSpan(0, Capacity));
            }
        }

        internal ChainedMap map;
        internal int count;

        public int Count => count;
        public int Capacity => map.Capacity;

        public PooledHashMap(int capacity = 16) {
            map = new(capacity);
        }
        public void Dispose() {
            map.Dispose();
        }
        public ref TValue this[TKey key] {
            get {
                var hash = key.GetHashCode() & (map.Capacity - 1);
                var index = map.GetFirstIndexOf(hash, key);
                // If it doesnt exist, create it
                if (index == -1) {
                    ResizeIfRequired();
                    index = map.Insert(hash, key, default!);
                }
                return ref map.Values[index];
            }
        }
        // Resize based on hard-coded loading factor 75%
        private void ResizeIfRequired() {
            if (1 + count * 4 > Capacity * 3) {
                Resize((int)BitOperations.RoundUpToPowerOf2((uint)Capacity + 16));
            }
        }
        // Add an item to the map
        public void Add(TKey key, TValue value) {
            ResizeIfRequired();
            var hash = key.GetHashCode() & (map.Capacity - 1);
            Debug.Assert(map.GetFirstIndexOf(hash, key) == -1, "Map already contains item");
            map.Insert(hash, key, value);
            ++count;
        }
        public void AddDuplicate(TKey key, TValue value) {
            ResizeIfRequired();
            var hash = key.GetHashCode() & (map.Capacity - 1);
            map.Insert(hash, key, value);
            ++count;
        }
        public bool Remove(TKey key) {
            var hash = key.GetHashCode() & (map.Capacity - 1);
            if (!map.Remove(hash, key)) return false;
            --count;
            return true;
        }
        public bool ContainsKey(TKey key) {
            var hash = key.GetHashCode() & (map.Capacity - 1);
            return map.GetFirstIndexOf(hash, key) >= 0;
        }
        public void Clear() {
            map.Clear();
            count = 0;
        }
        public bool TryGetValue(TKey key, out TValue value) {
            var hash = key.GetHashCode() & (map.Capacity - 1);
            var index = map.GetFirstIndexOf(hash, key);
            if (index < 0) { value = default; return false; }
            value = map.Values[index];
            return true;
        }

        public void Resize(int newCapacity) {
            var newMap = new ChainedMap(newCapacity);
            if (map.IsAllocated) {
                for (int i = 0; i < map.UnallocBucketMask.Length; i++) {
                    var mask = ~map.UnallocBucketMask[i];
                    for (; mask != 0; ) {
                        int b = BitOperations.TrailingZeroCount(mask);
                        mask &= ~(1ul << b);
                        var index = b + i * 64;
                        var key = map.Keys[index];
                        var hash = key.GetHashCode() & (newMap.Capacity - 1);
                        newMap.Insert(hash, key, map.Values[index]);
                    }
                }
                map.Dispose();
            }
            map = newMap;
        }

        public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>> {
            private ChainedMap map;
            private int index;
            public Enumerator(PooledHashMap<TKey, TValue> dictionary) {
                map = dictionary.map;
                index = -1;
            }
            public KeyValuePair<TKey, TValue> Current => new(map.Keys[index], map.Values[index]);
            object IEnumerator.Current => Current;
            public void Dispose() { }
            public void Reset() { }
            public bool MoveNext() {
                index = map.Set.GetNextIndex(index);
                return index >= 0;
            }
        }
        public PooledHashMap<TKey, TValue>.Enumerator GetEnumerator() => new(this);
        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void CopyTo(PooledHashMap<TKey, TValue> other) {
            map.CopyTo(other.map);
            other.count = count;
        }
    }
    public class HashMap<TKey, TValue> : IDisposable, IEnumerable<KeyValuePair<TKey, TValue>> where TKey : IEquatable<TKey> {

        PooledHashMap<TKey, TValue> map;

        public int Count => map.Count;
        public int Capacity => map.Capacity;

        public HashMap(int capacity = 16) {
            map = new(capacity);
        }
        public void Dispose() {
            map.Dispose();
        }
        public ref TValue this[TKey key] => ref map[key];
        public void Add(TKey key, TValue value) => map.Add(key, value);
        public bool Remove(TKey key) => map.Remove(key);
        public bool ContainsKey(TKey key) => map.ContainsKey(key);
        public void Clear() => map.Clear();
        public bool TryGetValue(TKey key, out TValue value) => map.TryGetValue(key, out value);
        public void Resize(int newCapacity) => map.Resize(newCapacity);

        public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>> {
            PooledHashMap<TKey, TValue>.Enumerator enumerator;
            public Enumerator(PooledHashMap<TKey, TValue> map) { enumerator = new(map); }
            public KeyValuePair<TKey, TValue> Current => enumerator.Current;
            object IEnumerator.Current => Current;
            public void Dispose() { }
            public void Reset() => enumerator.Reset();
            public bool MoveNext() => enumerator.MoveNext();
        }
        public PooledHashMap<TKey, TValue>.Enumerator GetEnumerator() => new(map);
        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public class MultiHashMap<TKey, TValue> : IDisposable, IEnumerable<KeyValuePair<TKey, TValue>> where TKey : IEquatable<TKey> {

        PooledHashMap<TKey, TValue> map;

        public int Count => map.Count;
        public int Capacity => map.Capacity;
        public bool IsCreated => map.map.IsCreated;

        public MultiHashMap(int capacity = 16) {
            map = new(capacity);
        }
        public void Dispose() {
            map.Dispose();
        }
        public ref TValue this[TKey key] => ref map[key];
        public void Add(TKey key, TValue value) {
            map.AddDuplicate(key, value);
            Debug.Assert(Contains(key, value));
        }
        public bool Remove(TKey key) {
            int c = 0;
            for (; map.Remove(key); ++c) ;
            Debug.Assert(!ContainsKey(key));
            return c > 0;
        }
        public bool Remove(TKey key, TValue value) {
            var hash = key.GetHashCode() & (map.Capacity - 1);
            var parent = hash | Capacity;
            var index = map.map.Set.Remap[parent];
            int c = 0;
            while (index >= 0) {
                if (map.map.Set.Keys[index].Equals(key)
                    && EqualityComparer<TValue>.Default.Equals(map.map.Values[index], value)) {
                    map.map.Set.RemoveChild(parent, index);
                    index = parent;
                    map.count--;
                    ++c;
                }
                parent = index;
                index = map.map.Set.Remap[index];
            }
            Debug.Assert(!Contains(key, value));
            return c > 0;
        }
        public bool ContainsKey(TKey key) => map.ContainsKey(key);
        public bool Contains(TKey key, TValue value) {
            var comparer = EqualityComparer<TValue>.Default;
            foreach (var item in GetValuesForKey(key)) {
                if (comparer != null ? comparer.Equals(item, value) : item.Equals(value)) return true;
            }
            return false;
        }
        public void Clear() => map.Clear();

        public struct KeyIterator {
            public int Parent;
            public int Index;
            public TKey Key;
        }

        public bool TryGetFirstValue(TKey key, out TValue value, out KeyIterator it) {
            var hash = key.GetHashCode() & (map.Capacity - 1);
            it = new() { Key = key, Parent = map.map.GetIndexOf(hash), };
            it.Index = map.map.Set.GetNextIndexOf(it.Parent, key);
            value = it.Index >= 0 ? map.map.Values[it.Index] : default!;
            return it.Index >= 0;
        }
        public bool TryGetNextValue(out TValue value, ref KeyIterator it) {
            it.Index = map.map.Set.GetNextIndexOf(it.Index, it.Key);
            value = it.Index >= 0 ? map.map.Values[it.Index] : default!;
            return it.Index >= 0;
        }
        public void Remove(ref KeyIterator it) {
            map.map.Set.RemoveChild(it.Parent, it.Index);
            it.Index = it.Parent;
            map.count--;
        }
        public void Resize(int newCapacity) => map.Resize(newCapacity);

        public struct Enumerator : IEnumerator<TValue> {
            MultiHashMap<TKey, TValue> hashMap;
            int parent;
            int index;
            TKey key;
            public TValue Current => hashMap.map.map.Values[index];
            object IEnumerator.Current => Current;
            public Enumerator(MultiHashMap<TKey, TValue> _hashMap, int _index, TKey _key) {
                hashMap = _hashMap;
                parent = -1;
                index = _index | hashMap.Capacity;
                key = _key;
            }
            public void Dispose() { }
            public void Reset() { throw new NotImplementedException(); }
            public bool MoveNext() {
                while (true) {
                    parent = index;
                    index = hashMap.map.map.Set.Remap[index];
                    if (index < 0) return false;
                    if (hashMap.map.map.Set.Keys[index].Equals(key)) return true;
                }
            }
            public void RemoveSelf() {
                hashMap.map.map.Set.RemoveChild(parent, index);
                index = parent;
                --hashMap.map.count;
            }
            public Enumerator GetEnumerator() => this;
        }
        public Enumerator GetValuesForKey(TKey key) {
            var hash = key.GetHashCode() & (map.Capacity - 1);
            return new(this, hash, key);
        }

        public struct UniqueKeyEnumerator : IEnumerator<TKey>, IEnumerable<TKey> {
            MultiHashMap<TKey, TValue> hashMap;
            int index;
            public UniqueKeyEnumerator(MultiHashMap<TKey, TValue> _hashMap) {
                hashMap = _hashMap;
                index = -1;
            }
            public TKey Current => hashMap.map.map.Set.Keys[index];
            object IEnumerator.Current => Current;
            public void Dispose() { }
            public void Reset() { throw new NotImplementedException(); }
            public bool MoveNext() {
                while (true) {
                    index = hashMap.map.map.Set.GetNextIndex(index);
                    if (index < 0) return false;
                    var key = hashMap.map.map.Keys[index];
                    var hash = key.GetHashCode() & (hashMap.Capacity - 1);
                    if (hashMap.map.map.GetFirstIndexOf(hash, key) == index) return true;
                }
            }
            public UniqueKeyEnumerator GetEnumerator() => this;
            IEnumerator<TKey> IEnumerable<TKey>.GetEnumerator() => GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
        public UniqueKeyEnumerator Keys => new(this);

        public struct KeyValueEnumerator : IEnumerator<KeyValuePair<TKey, TValue>> {
            PooledHashMap<TKey, TValue>.Enumerator enumerator;
            public KeyValueEnumerator(PooledHashMap<TKey, TValue> map) { enumerator = new(map); }
            public KeyValuePair<TKey, TValue> Current => enumerator.Current;
            object IEnumerator.Current => Current;
            public void Dispose() { }
            public void Reset() => enumerator.Reset();
            public bool MoveNext() => enumerator.MoveNext();
        }
        public KeyValueEnumerator GetEnumerator() => new(map);
        IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void CopyTo(MultiHashMap<TKey, TValue> other) {
            map.CopyTo(other.map);
        }
    }


    public struct PooledPriorityQueue<TValue, TPriority> : IDisposable where TPriority : IComparable<TPriority> {
        struct Element {
            public TPriority Key;
            public TValue Value;
            public Element(TPriority key, TValue value) { Key = key; Value = value; }
            public override string ToString() { return Key + ": " + Value; }
        }
        private int count = 0;
        private Element[] heap;

        public int Count => count;
        public bool IsCreated => heap != null;

        public PooledPriorityQueue(int capacity = 16) {
            heap = ArrayPool<Element>.Shared.Rent(capacity);
        }
        public void Dispose() {
            if (heap != null) ArrayPool<Element>.Shared.Return(heap);
        }
        public bool IsEmpty() { return count == 0; }
        public TPriority PeekKey() { return heap[1].Key; }
        public TValue Peek() { return heap[1].Value; }
        public TValue Dequeue() {
            var value = heap[1].Value;
            heap[1] = heap[count];
            count--;
            MoveDown(1);
            return value;
        }
        public void Enqueue(TValue value, TPriority priority) {
            var e = new Element(priority, value);
            count++;
            if (count == heap.Length) IncreaseCapacity();
            heap[count] = e;
            MoveUp(count);
        }
        // Either change key, or insert if it doesnt exist
        public void Assign(TValue value, TPriority priority) {
            int i = count;
            for (; i >= 1; i--) {
                if (heap[i].Value.Equals(value)) break;
            }
            if (i > 0) {
                var e = heap[i];
                var compare = priority.CompareTo(e.Key);
                e.Key = priority;
                heap[i] = e;
                if (compare < 0) MoveDown(i); else MoveUp(i);
                return;
            }
            Enqueue(value, priority);
        }
        public void Clear() {
            count = 0;
        }
        private void MoveDown(int i) {
            int childL = i << 1;
            if (childL > count) return;
            int childR = childL + 1;
            int smallerChild;
            if (childR <= count && heap[childR].Key.CompareTo(heap[childL].Key) < 0) {
                smallerChild = childR;
            } else {
                smallerChild = childL;
            }
            if (heap[i].Key.CompareTo(heap[smallerChild].Key) > 0) {
                Swap(i, smallerChild);
                MoveDown(smallerChild);
            }
        }

        private void MoveUp(int i) {
            while (i > 1) {
                int parent = i >> 1;
                if (heap[parent].Key.CompareTo(heap[i].Key) <= 0) break;
                Swap(parent, i);
                i = parent;
            }
        }

        private void Swap(int i, int j) {
            Element temp = heap[i];
            heap[i] = heap[j];
            heap[j] = temp;
        }

        private void IncreaseCapacity() {
            var pool = ArrayPool<Element>.Shared;
            var newHeap = pool.Rent((int)BitOperations.RoundUpToPowerOf2((uint)heap.Length + 16));
            if (heap != null) {
                for (int i = 0; i < count; i++) newHeap[i] = heap[i];
                pool.Return(heap);
            }
            heap = newHeap;
        }

        public int GetCount() { return count; }
        public void SetCount(int v) { count = v; }

    }
}
