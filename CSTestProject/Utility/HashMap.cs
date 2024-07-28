using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;
using Index = System.UInt32;

namespace Weesals.Utility {
    public unsafe struct PooledSentinel {
        public class HeapSentinel { public int Revision; }
        private int revision;
        private HeapSentinel heap = new();
        public bool IsValid => revision == heap.Revision;
        public PooledSentinel() { }
        [Conditional("DEBUG")]
        public void Validate() {
            Debug.Assert(++heap.Revision == ++revision);
        }
    }

    public struct PooledHashSet<TKey> : IDisposable, IEnumerable<TKey> where TKey : IEquatable<TKey> {
        public const Index InvalidIndex = unchecked((Index)(~0));
        public struct HashSet : IDisposable {
            public TKey[] Keys;
            public Index[] Remap;
            public ulong[] UnallocBucketMask;
            public int Capacity;
#if DEBUG
            public PooledSentinel Sentinel;
#endif
            public EqualityComparer<TKey> Comparer;
            public bool IsAllocated => Capacity > 0;
#if DEBUG
            public bool IsValid => Sentinel.IsValid;
#else
            public bool IsValid => true;
#endif
            public HashSet(int capacity = 16) {
#if DEBUG
                Sentinel = new();
#endif
                Keys = ArrayPool<TKey>.Shared.Rent(capacity);
                Remap = ArrayPool<Index>.Shared.Rent(capacity * 2);
                UnallocBucketMask = ArrayPool<ulong>.Shared.Rent((capacity + 63) >> 5);
                Capacity = capacity;
                Comparer = EqualityComparer<TKey>.Default;
                IntlClear();
            }
            [Conditional("DEBUG")]
            private void ValidateSentinel() {
#if DEBUG
                Sentinel.Validate();
#endif
            }
            private void IntlClear() {
                UnallocBucketMask.AsSpan().Fill(~0ul);
                Remap.AsSpan().Fill(InvalidIndex);
            }
            public void Clear() {
                ValidateSentinel();
                IntlClear();
            }
            public void Dispose() {
                ValidateSentinel();
                ArrayPool<TKey>.Shared.Return(Keys);
                ArrayPool<Index>.Shared.Return(Remap);
                ArrayPool<ulong>.Shared.Return(UnallocBucketMask);
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Index CreateIndex(int hash) {
                return (Index)((hash & (Capacity - 1)) | Capacity);
            }
            [MethodImpl(MethodImplOptions.AggressiveOptimization)]
            public Index FindFreeBucket() {
                for (int i = 0; i < UnallocBucketMask.Length; i++) {
                    if (UnallocBucketMask[i] == 0) continue;
                    return (Index)(i * 64 + BitOperations.TrailingZeroCount(UnallocBucketMask[i]));
                }
                return InvalidIndex;
            }
            [MethodImpl(MethodImplOptions.AggressiveOptimization)]
            public Index Insert(Index parent, TKey key) {
                ValidateSentinel();
                var index = FindFreeBucket();
                Debug.Assert(Remap[parent] != index,
                    "New item already referenced");
                Remap[index] = Remap[parent];
                Remap[parent] = index;
                Keys[index] = key;
                UnallocBucketMask[index >> 6] &= ~(1ul << ((int)index & 63));
                return index;
            }
            [MethodImpl(MethodImplOptions.AggressiveOptimization)]
            public bool Remove(Index parent, TKey key) {
                ValidateSentinel();
                var index = Remap[parent];
                while (index != InvalidIndex && !key.Equals(Keys[index])) {
                    parent = index;
                    index = Remap[index];
                }
                if (index == InvalidIndex) return false;
                RemoveChild(parent, index);
                return true;
            }
            [MethodImpl(MethodImplOptions.AggressiveOptimization)]
            public void RemoveChild(Index parent, Index index) {
                ValidateSentinel();
                if (parent != InvalidIndex) Remap[parent] = Remap[index];
                Remap[index] = InvalidIndex;
#if DEBUG
                Keys[index] = default;
#endif
                UnallocBucketMask[index >> 6] |= (1ul << ((int)index & 63));
            }
            public Index FindParent(Index parent, Index child) {
                while (parent != InvalidIndex) {
                    var next = Remap[parent];
                    if (next == child) return parent;
                    parent = next;
                }
                return InvalidIndex;
            }
            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
            public unsafe Index GetNextIndexOf(Index index, TKey key) {
                while (true) {
                    index = Remap[index];
                    if (index == InvalidIndex || key.Equals(Keys[index])) return index;
                }
            }
            [MethodImpl(MethodImplOptions.AggressiveOptimization)]
            public Index GetIndexFrom(Index index) {
                var indexInvMask = (1ul << ((int)index & 63)) - 1;
                for (var page = index >> 6; page < UnallocBucketMask.Length; ++page) {
                    var mask = UnallocBucketMask[page];
                    mask |= indexInvMask;
                    if (mask == ~0ul) { indexInvMask = 0ul; continue; }
                    return (Index)(page * 64 + BitOperations.TrailingZeroCount(~mask));
                }
                return InvalidIndex;
            }

            public void CopyTo(HashSet other) {
                other.Dispose();
                other.Capacity = Capacity;
                other.Keys = ArrayPool<TKey>.Shared.Rent(Capacity);
                other.Remap = ArrayPool<Index>.Shared.Rent(Capacity * 2);
                other.UnallocBucketMask = ArrayPool<ulong>.Shared.Rent((Capacity + 63) >> 5);
                Keys.AsSpan(0, Capacity).CopyTo(Keys.AsSpan(0, Capacity));
                Remap.AsSpan(0, Capacity).CopyTo(Remap.AsSpan(0, Capacity));
                UnallocBucketMask.AsSpan(0, Capacity).CopyTo(other.UnallocBucketMask.AsSpan(0, Capacity));
            }
        }
        HashSet set;
        int count;

        public int Count => count;
        public int Capacity => set.Capacity;
        public bool IsEmpty => count == 0;
        public bool IsValid => set.IsValid;

        public PooledHashSet(int capacity = 16) { set = new(capacity); }
        public void Dispose() { set.Dispose(); }

        // Resize based on hard-coded loading factor 75%
        private void ResizeIfRequired() {
            if (count * 4 >= Capacity * 3) {
                Resize((int)BitOperations.RoundUpToPowerOf2((uint)Capacity + 32));
            }
        }
        // Add an item to the map
        public void Add(TKey key) {
            ResizeIfRequired();
            // Check if it already exists
            Debug.Assert(!Contains(key), "Map already contains item");
            var hash = set.CreateIndex(key.GetHashCode());
            set.Insert(hash, key);
            ++count;
            AssertValid();
        }
        // Return false if item already exists (and dont add)
        unsafe public bool AddUnique(TKey key) {
            ResizeIfRequired();
            var hash = set.CreateIndex(key.GetHashCode());
            if (set.GetNextIndexOf(hash, key) != InvalidIndex) return false;
            set.Insert(hash, key);
            ++count;
            return true;
        }
        public void AddRange(IReadOnlyCollection<TKey> values) {
            foreach (var item in values) AddUnique(item);
        }
        // If the item exists, remove it and return false
        // (used for tracking edges in navmesh adjacency)
        public bool ToggleUnique(TKey key) {
            var hash = key.GetHashCode();
            if (set.Remove(set.CreateIndex(hash), key)) {
                --count;
                return false;
            }
            ResizeIfRequired();
            set.Insert(set.CreateIndex(hash), key);
            ++count;
            return true;
        }
        private void AssertValid() {
            var index = set.GetIndexFrom(0);
            if (index == InvalidIndex) { Debug.Assert(count == 0); return; }
            var key = set.Keys[index];
            Debug.Assert(Contains(key));
        }
        public bool Remove(TKey key) {
            var hash = set.CreateIndex(key.GetHashCode());
            if (!set.Remove(hash, key)) return false;
            --count;
            return true;
        }
        public bool Contains(TKey key) {
            var hash = set.CreateIndex(key.GetHashCode());
            return set.GetNextIndexOf(hash, key) != InvalidIndex;
        }
        public bool TryPop(out TKey key) {
            AssertValid();
            key = default;
            var index = set.GetIndexFrom(0);
            if (index == InvalidIndex) return false;
            key = set.Keys[index];
            Debug.Assert(Contains(key));
            Trace.Assert(Remove(key), "Key does not exist!");
            return true;
        }
        public void Clear() {
            set.Clear();
            count = 0;
        }

        public void Resize(int newCapacity) {
            var newSet = new HashSet(newCapacity);
            if (set.IsAllocated) {
                for (int i = 0; i < set.UnallocBucketMask.Length; i++) {
                    var mask = ~set.UnallocBucketMask[i];
                    for (; mask != 0;) {
                        int b = BitOperations.TrailingZeroCount(mask);
                        mask &= ~1ul << b;
                        var index = b + i * 64;
                        var key = set.Keys[index];
                        var hash = newSet.CreateIndex(key.GetHashCode());
                        newSet.Insert(hash, key);
                    }
                }
                set.Dispose();
            }
            set = newSet;
        }

        public override string ToString() { return $"HashSet<Count={Count}>"; }

        public struct Enumerator : IEnumerator<TKey> {
            private HashSet map;
            private Index index;
            public Enumerator(PooledHashSet<TKey> dictionary) {
                map = dictionary.set;
                index = unchecked((Index)0 - 1);
            }
            public TKey Current => map.Keys[index];
            object IEnumerator.Current => Current;
            public void Dispose() { }
            public void Reset() { }
            public bool MoveNext() {
                index = map.GetIndexFrom(index + 1);
                return index != InvalidIndex;
            }
            public void RemoveSelf(ref PooledHashSet<TKey> set) {
                var hash = set.set.CreateIndex(set.set.Keys[index].GetHashCode());
                var parent = set.set.FindParent(hash, index);
                set.set.RemoveChild(parent, index);
                //index = parent;
                --set.count;
            }
        }
        public Enumerator GetEnumerator() => new(this);
        IEnumerator<TKey> IEnumerable<TKey>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        unsafe public ref PooledHashSet<TKey> AsMutable() { return ref this; }
    }
    public struct PooledHashMap<TKey, TValue> : IDisposable, IEnumerable<KeyValuePair<TKey, TValue>> where TKey : IEquatable<TKey> {
        public const Index InvalidIndex = PooledHashSet<TKey>.InvalidIndex;

        public struct ChainedMap : IDisposable {
            public PooledHashSet<TKey>.HashSet Set;
            public TKey[] Keys => Set.Keys;
            public TValue[] Values;
            public ulong[] UnallocBucketMask => Set.UnallocBucketMask;
            public int Capacity => Set.Capacity;
            public bool IsAllocated => Capacity > 0;

            public bool IsCreated => Set.IsAllocated;
            public bool IsValid => Set.IsValid;

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
            public Index Insert(Index index, TKey key, TValue value) {
                index = Set.Insert(index, key);
                Values[index] = value;
                return index;
            }
            public bool Remove(Index hash, TKey key) {
                return Set.Remove(hash, key);
            }
            public Index CreateIndex(int hash) {
                return Set.CreateIndex(hash);
            }
            public Index GetFirstIndexOf(Index hash, TKey key) {
                return Set.GetNextIndexOf(hash, key);
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
        public bool IsValid => map.IsValid;
        public bool IsCreated => map.IsCreated;

        public PooledHashMap(int capacity = 16) {
            map = new(capacity);
        }
        public void Dispose() {
            map.Dispose();
        }
        public ref TValue this[TKey key] {
            get {
                var hash = map.CreateIndex(key.GetHashCode());
                var index = map.GetFirstIndexOf(hash, key);
                // If it doesnt exist, create it
                if (index == InvalidIndex) {
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
            var hash = map.CreateIndex(key.GetHashCode());
            Debug.Assert(map.GetFirstIndexOf(hash, key) == InvalidIndex, "Map already contains item");
            map.Insert(hash, key, value);
            ++count;
        }
        public void AddDuplicate(TKey key, TValue value) {
            ResizeIfRequired();
            var hash = map.CreateIndex(key.GetHashCode());
            map.Insert(hash, key, value);
            ++count;
        }
        public bool Remove(TKey key) {
            var hash = map.CreateIndex(key.GetHashCode());
            if (!map.Remove(hash, key)) return false;
            --count;
            return true;
        }
        public bool ContainsKey(TKey key) {
            var hash = map.CreateIndex(key.GetHashCode());
            return map.GetFirstIndexOf(hash, key) != InvalidIndex;
        }
        public void Clear() {
            map.Clear();
            count = 0;
        }
        public bool TryGetValue(TKey key, out TValue value) {
            var hash = map.CreateIndex(key.GetHashCode());
            var index = map.GetFirstIndexOf(hash, key);
            if (index == InvalidIndex) { value = default; return false; }
            value = map.Values[index];
            return true;
        }

        public void Resize(int newCapacity) {
            var newMap = new ChainedMap(newCapacity);
            if (map.IsAllocated) {
                for (int i = 0; i < map.UnallocBucketMask.Length; i++) {
                    var mask = ~map.UnallocBucketMask[i];
                    for (; mask != 0;) {
                        int b = BitOperations.TrailingZeroCount(mask);
                        mask &= ~1ul << b;
                        var index = b + i * 64;
                        var key = map.Keys[index];
                        var hash = newMap.CreateIndex(key.GetHashCode());
                        newMap.Insert(hash, key, map.Values[index]);
                    }
                }
                map.Dispose();
            }
            map = newMap;
        }

        public override string ToString() { return $"HashMap<Count={Count}>"; }

        public struct Enumerator : IEnumerator<KeyValuePair<TKey, TValue>> {
            private ChainedMap map;
            private Index index;
            public Index Index => index;
            public Enumerator(PooledHashMap<TKey, TValue> dictionary) {
                map = dictionary.map;
                index = unchecked((Index)0 - 1);
            }
            public KeyValuePair<TKey, TValue> Current => new(map.Keys[index], map.Values[index]);
            object IEnumerator.Current => Current;
            public void RemoveSelf(ref PooledHashMap<TKey, TValue> map) {
                var hash = map.map.CreateIndex(map.map.Set.Keys[index].GetHashCode());
                var parent = map.map.Set.FindParent(hash, index);
                map.map.Set.RemoveChild(parent, index);
                //index = parent;
                --map.count;
            }
            public void Dispose() { }
            public void Reset() { }
            public bool MoveNext() {
                index = map.Set.GetIndexFrom(index + 1);
                return index != InvalidIndex;
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
        public const Index InvalidIndex = PooledHashSet<TKey>.InvalidIndex;

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
        public bool Remove<VValue>(TKey key, VValue value) where VValue : TValue, IEquatable<TValue> {
            var parent = map.map.CreateIndex(key.GetHashCode());
            var index = map.map.Set.Remap[parent];
            int c = 0;
            while (index != InvalidIndex) {
                if (key.Equals(map.map.Set.Keys[index])
                    && value.Equals(map.map.Values[index])) {
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
            public Index Parent;
            public Index Index;
            public TKey Key;
        }

        public bool TryGetFirstValue(TKey key, out TValue value, out KeyIterator it) {
            var hash = map.map.CreateIndex(key.GetHashCode());
            it = new() { Key = key, Parent = hash, };
            it.Index = map.map.Set.GetNextIndexOf(it.Parent, key);
            var hasValue = it.Index != InvalidIndex;
            value = hasValue ? map.map.Values[it.Index] : default!;
            return hasValue;
        }
        public bool TryGetNextValue(out TValue value, ref KeyIterator it) {
            it.Index = map.map.Set.GetNextIndexOf(it.Index, it.Key);
            var hasValue = it.Index != InvalidIndex;
            value = hasValue ? map.map.Values[it.Index] : default!;
            return hasValue;
        }
        public void Remove(ref KeyIterator it) {
            map.map.Set.RemoveChild(it.Parent, it.Index);
            it.Index = it.Parent;
            map.count--;
        }
        public void Resize(int newCapacity) => map.Resize(newCapacity);

        public struct Enumerator : IEnumerator<TValue> {
            MultiHashMap<TKey, TValue> hashMap;
            Index parent;
            Index index;
            TKey key;
            public TValue Current => hashMap.map.map.Values[index];
            object IEnumerator.Current => Current;
            public Enumerator(MultiHashMap<TKey, TValue> _hashMap, Index _index, TKey _key) {
                hashMap = _hashMap;
                parent = InvalidIndex;
                index = _index;
                key = _key;
            }
            public void Dispose() { }
            public void Reset() { throw new NotImplementedException(); }
            public bool MoveNext() {
                var remap = hashMap.map.map.Set.Remap;
                var keys = hashMap.map.map.Set.Keys;
                while (true) {
                    parent = index;
                    index = remap[index];
                    if (index == InvalidIndex) return false;
                    if (keys[index].Equals(key)) return true;
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
            var hash = map.map.CreateIndex(key.GetHashCode());
            return new(this, hash, key);
        }

        public struct UniqueKeyEnumerator : IEnumerator<TKey>, IEnumerable<TKey> {
            MultiHashMap<TKey, TValue> hashMap;
            Index index;
            public UniqueKeyEnumerator(MultiHashMap<TKey, TValue> _hashMap) {
                hashMap = _hashMap;
                index = unchecked((Index)0 - 1);
            }
            public TKey Current => hashMap.map.map.Set.Keys[index];
            object IEnumerator.Current => Current;
            public void Dispose() { }
            public void Reset() { throw new NotImplementedException(); }
            public bool MoveNext() {
                var keys = hashMap.map.map.Keys;
                while (true) {
                    index = hashMap.map.map.Set.GetIndexFrom(index + 1);
                    if (index == InvalidIndex) return false;
                    var key = keys[index];
                    var hash = hashMap.map.map.CreateIndex(key.GetHashCode());
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
        public void Assign<TTValue>(TTValue value, TPriority priority) where TTValue : TValue, IEquatable<TValue> {
            int i = count;
            for (; i >= 1; i--) {
                if (value.Equals(heap[i].Value)) break;
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
