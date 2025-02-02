using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using BitIndex = System.Int32;

namespace Weesals.ECS {
    public struct BitEnumerator : IEnumerator<int> {
        public readonly ulong Bits;
        public int Current { get; private set; }
        object IEnumerator.Current => Current;
        public BitEnumerator(ulong bits) { Bits = bits; Current = -1; }
        public void Dispose() { }
        public void Reset() { Current = -1; }
        public bool MoveNext() {
            if (++Current >= 64) { Current = -1; return false; }
            Current += BitOperations.TrailingZeroCount(Bits >> Current);
            return Current < 64;
        }
        public BitEnumerator GetEnumerator() { return this; }
    }
    // Represents up to 4096 bits broken up into up to 64 blocks of 64 bits.
    // BitFields are compared by pointer, so each allocated instance should be unique.
    public unsafe readonly struct BitField : IEquatable<BitField>, IEnumerable<int> {
        private readonly ulong* storage;
        private readonly ulong unsafePageIds => *storage;
        private readonly ulong pageIds => storage == null ? 0 : *storage;
        private readonly ulong* pages => storage + 1;
        public readonly bool IsEmpty => storage == null;
        public readonly int PageCount => storage == null ? 0 : BitOperations.PopCount(unsafePageIds);
        public readonly int BitCount {
            get {
                int counter = 0;
                for (int i = PageCount - 1; i >= 0; i--) counter += BitOperations.PopCount(pages[i]);
                return counter;
            }
        }
        public BitField(ulong* storage) {
            this.storage = storage;
        }
        public bool Contains(int bit) {
            if (IsEmpty) return false;
            //if (IsEmpty) return (pageIds & (1uL << bit)) != 0;    // TODO: Packed bits (if lower bit is set)
            var pageIds = this.unsafePageIds;
            var pageId = GetPageIdByBit(bit);
            if ((pageIds & (1uL << pageId)) == 0) return false;
            var page = pages[CountBitsUntil(pageIds, pageId)];
            return (page & (1uL << (bit & 63))) != 0;
        }
        public int GetFirstBit() {
            if (IsEmpty) return -1;
            var pageIds = this.unsafePageIds;
            var nextPageId = BitOperations.TrailingZeroCount(pageIds);
            if (nextPageId >= 64) return -1;
            var nextPage = pages[CountBitsUntil(pageIds, nextPageId)];
            return nextPageId * 64 + BitOperations.TrailingZeroCount(nextPage);
        }
        public int GetNextBit(int bit) {
            if (IsEmpty) return -1;
            ++bit;
            var pageIds = this.unsafePageIds;
            var pageId = GetPageIdByBit(bit);
            if ((pageIds & (1uL << pageId)) != 0) {
                var page = pages[CountBitsUntil(pageIds, pageId)];
                page &= ~0ul << bit;
                if (page != 0) return GetBitByPageId(pageId) + BitOperations.TrailingZeroCount(page);
                ++pageId;   // Move to next page
            }
            var nextPageId = GetBitFrom(pageIds, pageId);
            if (nextPageId >= 64) return -1;
            var nextPage = pages[CountBitsUntil(pageIds, nextPageId)];
            return GetBitByPageId(nextPageId) + BitOperations.TrailingZeroCount(nextPage);
        }
        public int GetBitIndex(int bit) {
            int pageId = GetPageIdByBit(bit);
            var pageIndex = CountBitsUntil(unsafePageIds, pageId);
            int counter = 0;
            for (int i = 0; i < pageIndex; i++) counter += BitOperations.PopCount(pages[i]);
            counter += BitOperations.PopCount(pages[pageIndex] & ~(~0ul << (bit & 63)));
            return counter;
        }
        public bool TryGetBitIndex(int bit, out int index) {
            if (IsEmpty) { index = -1; return false; }
            var pageIds = this.unsafePageIds;
            int pageId = GetPageIdByBit(bit);
            if ((pageIds & (1ul << pageId)) == 0) { index = -1; return false; }
            var pageIndex = CountBitsUntil(pageIds, pageId);
            if ((pages[pageIndex] & (1ul << (bit & 63))) == 0) { index = -1; return false; }
            index = 0;
            for (int i = 0; i < pageIndex; i++) index += BitOperations.PopCount(pages[i]);
            index += BitOperations.PopCount(pages[pageIndex] & ~(~0ul << (bit & 63)));
            return true;
        }
        public bool ContainsAll(BitField withTypes) {
            if (IsEmpty || withTypes.IsEmpty) return withTypes.IsEmpty;
            int withPage1I = 0;
            var pageIds = this.unsafePageIds;
            var withPageIds = withTypes.unsafePageIds;
            for (int p = BitOperations.TrailingZeroCount(withPageIds); p < 64; p = GetNextBit(withPageIds, p)) {
                var reqBits = withTypes.pages[withPage1I++];
                if ((pageIds & (1ul << p)) == 0) return false;
                var curBits = pages[CountBitsUntil(pageIds, p)];
                if ((curBits & reqBits) != reqBits) return false;
            }
            return true;
        }
        public bool ContainsAny(BitField withTypes) {
            if (IsEmpty || withTypes.IsEmpty) return false;
            int withPage1I = 0;
            var pageIds = this.unsafePageIds;
            var withPageIds = withTypes.unsafePageIds;
            for (int p = BitOperations.TrailingZeroCount(withPageIds); p < 64; p = GetNextBit(withPageIds, p)) {
                var reqBits = withTypes.pages[withPage1I++];
                if ((pageIds & (1ul << p)) == 0) continue;
                var curBits = pages[CountBitsUntil(pageIds, p)];
                if ((curBits & reqBits) != 0) return true;
            }
            return false;
        }
        public bool DeepEquals(BitField other) {
            if (storage == other.storage) return true;
            if (unsafePageIds != other.unsafePageIds) return false;
            int pageCount = PageCount;
            return new Span<ulong>(pages, pageCount).SequenceEqual(new Span<ulong>(other.pages, pageCount));
        }
        public ulong DeepHash() {
            ulong hash = 0;
            for (int i = PageCount - 1; i >= 0; i--) hash += pages[i];
            return hash;
        }
        public bool Equals(BitField other) { return storage == other.storage; }
        public override bool Equals(object? obj) { throw new NotImplementedException(); }
        public override int GetHashCode() { return (int)storage + (int)((nint)storage >> 32); }
        public override string ToString() {
            return PageCount == 0 ? "Empty"
                : this.Select(i => i.ToString()).Aggregate((i1, i2) => $"{i1},{i2}");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetPageIdByBit(int bit) { return bit >> 6; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetBitByPageId(int bit) { return bit << 6; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetLocalBit(int bit) { return bit & 63; }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CountBitsUntil(ulong pattern, int pageId) {
            return BitOperations.PopCount(pattern & ~(~0ul << pageId));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetNextBit(ulong pattern, int bit) {
            return GetBitFrom(pattern, bit + 1);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetBitFrom(ulong pattern, int bit) {
            //return BitOperations.TrailingZeroCount(pattern & ~((2uL << bit) - 1));
            //return BitOperations.TrailingZeroCount(pattern & (ulong)(-(long)(1uL << Math.Min(63, bit + 1))));
            return BitOperations.TrailingZeroCount(pattern & ((ulong.MaxValue) << bit));
        }

        public struct UnionEnumerator : IEnumerator<int>, IEnumerable<int> {
            public readonly BitField Bits1;
            public readonly BitField Bits2;
            private ulong page;
            private int bitIndex;
            public int Current => bitIndex;
            object IEnumerator.Current => Current;
            public UnionEnumerator(BitField bits1, BitField bits2) {
                Bits1 = bits1;
                Bits2 = bits2;
                bitIndex = -1;
            }
            public void Dispose() { }
            public void Reset() { bitIndex = -1; }
            public bool MoveNext() {
                var next = GetNextBit(page, bitIndex & 63);
                if (next >= 64) {
                    var pageIds = Bits1.unsafePageIds | Bits2.unsafePageIds;
                    var pageId = GetPageIdByBit(bitIndex);
                    pageId = GetNextBit(pageIds, pageId);
                    if (pageId >= 64) return false;
                    bitIndex = pageId * 64;
                    page = 0;
                    if ((Bits1.unsafePageIds & (1ul << pageId)) != 0)
                        page |= Bits1.pages[Bits1.GetBitIndex(pageId)];
                    if ((Bits2.unsafePageIds & (1ul << pageId)) != 0)
                        page |= Bits2.pages[Bits2.GetBitIndex(pageId)];
                    next = GetNextBit(page, 0);
                }
                bitIndex = (bitIndex & ~63) + next;
                return true;
            }
            public UnionEnumerator GetEnumerator() { return this; }
            IEnumerator<int> IEnumerable<int>.GetEnumerator() { return GetEnumerator(); }
            IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
        }
        public static UnionEnumerator Union(BitField bits1, BitField bits2) {
            return new UnionEnumerator(bits1, bits2);
        }
        public struct IntersectionEnumerator : IEnumerator<int>, IEnumerable<int> {
            public readonly BitField Bits1;
            public readonly BitField Bits2;
            private ulong page;
            private int bitIndex;
            public int Current => bitIndex;
            object IEnumerator.Current => Current;
            public IntersectionEnumerator(BitField bits1, BitField bits2) {
                Bits1 = bits1;
                Bits2 = bits2;
                bitIndex = -1;
            }
            public void Dispose() { }
            public void Reset() { bitIndex = -1; }
            public bool MoveNext() {
                var next = GetNextBit(page, bitIndex);
                if (next >= 64) {
                    var pageIds = Bits1.pageIds & Bits2.pageIds;
                    var pageId = GetPageIdByBit(bitIndex);
                    pageId = GetNextBit(pageIds, pageId);
                    if (pageId >= 64) return false;
                    bitIndex = pageId * 64;
                    page = ~0ul;
                    if ((Bits1.unsafePageIds & (1ul << pageId)) != 0)
                        page &= Bits1.pages[Bits1.GetBitIndex(pageId)];
                    if ((Bits2.unsafePageIds & (1ul << pageId)) != 0)
                        page &= Bits2.pages[Bits2.GetBitIndex(pageId)];
                    next = GetNextBit(page, -1);
                }
                bitIndex = (bitIndex & ~63) + next;
                return true;
            }
            public IntersectionEnumerator GetEnumerator() { return this; }
            IEnumerator<int> IEnumerable<int>.GetEnumerator() { return GetEnumerator(); }
            IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
        }
        public static IntersectionEnumerator Intersection(BitField bits1, BitField bits2) {
            return new IntersectionEnumerator(bits1, bits2);
        }
        public struct DifferenceEnumerator : IEnumerator<int>, IEnumerable<int> {
            public readonly BitField Bits1;
            public readonly BitField Bits2;
            private ulong page;
            private int bitIndex;
            public int Current => bitIndex;
            object IEnumerator.Current => Current;
            public DifferenceEnumerator(BitField bits1, BitField bits2) {
                Bits1 = bits1;
                Bits2 = bits2;
                bitIndex = -1;
            }
            public void Dispose() { }
            public void Reset() { page = 0; bitIndex = -1; }
            public bool MoveNext() {
                while (page == 0) {
                    var pageId = GetPageIdByBit(bitIndex) + 1;
                    pageId = GetBitFrom(Bits1.pageIds, pageId);
                    if (pageId >= 64) return false;
                    bitIndex = GetBitByPageId(pageId);
                    page = Bits1.pages[Bits1.GetBitIndex(pageId)];
                    if ((Bits2.pageIds & (1ul << pageId)) != 0)
                        page &= ~Bits2.pages[Bits2.GetBitIndex(pageId)];
                }
                bitIndex = (bitIndex & ~63) + BitOperations.TrailingZeroCount(page);
                page &= page - 1;
                return true;
            }
            public DifferenceEnumerator GetEnumerator() { return this; }
            IEnumerator<int> IEnumerable<int>.GetEnumerator() { return GetEnumerator(); }
            IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
        }
        public static DifferenceEnumerator Except(BitField bits1, BitField bits2) {
            return new DifferenceEnumerator(bits1, bits2);
        }

        public struct Enumerator : IEnumerator<int> {
            public readonly BitField Field;
            public int Current { get; private set; }
            object IEnumerator.Current => Current;
            public Enumerator(BitField field) {
                Field = field;
                Current = -1;
            }
            public void Dispose() { }
            public bool MoveNext() {
                Current = Field.GetNextBit(Current);
                return Current != -1;
            }
            public void Reset() { Current = -1; }
        }
        public Enumerator GetEnumerator() { return new Enumerator(this); }
        IEnumerator<int> IEnumerable<int>.GetEnumerator() { return GetEnumerator(); }
        IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
        // BitFields are readonly and must be created using this Generator
        public class Generator {
            public ulong PageIds;
            public int PageCount;
            public ulong[] Pages = new ulong[64];
            public bool IsEmpty => PageIds == 0;
            private void InsertPages(ulong toAdd) {
                Debug.Assert((PageIds & toAdd) == 0);
                var oldPageCount = PageCount;
                PageCount += BitOperations.PopCount(toAdd);
                PageIds |= toAdd;
                for (int p0 = oldPageCount - 1, p1 = PageCount - 1; toAdd != 0; p1--) {
                    var pageIndex = 63 - BitOperations.LeadingZeroCount(toAdd);
                    toAdd ^= 1ul << pageIndex;
                    var next = BitOperations.PopCount(PageIds >> pageIndex);
                    while (p1 >= next) Pages[p1--] = Pages[p0--];
                    Pages[p1] = 0;
                }
            }
            public void Clear() { PageIds = 0; PageCount = 0; }
            public void Append(BitField field) {
                if (field.IsEmpty) return;
                var allPages = field.unsafePageIds | PageIds;
                var toAdd = field.unsafePageIds & (~PageIds);
                if (toAdd != 0) InsertPages(toAdd);
                for (var bits = field.unsafePageIds; bits != 0; bits &= bits - 1) {
                    var pageId = BitOperations.TrailingZeroCount(bits);
                    var dstPageI = CountBitsUntil(PageIds, pageId);
                    var srcPageI = CountBitsUntil(field.unsafePageIds, pageId);
                    Pages[dstPageI] |= field.pages[srcPageI];
                }
                Debug.Assert(PageIds == allPages);
            }
            public void Remove(BitField field) {
                if (field.IsEmpty) return;
                var newPages = ~field.unsafePageIds & PageIds;
                var toRemove = field.unsafePageIds & PageIds;
                if (toRemove == 0) return;
                int consume = 0;
                for (var bits = field.unsafePageIds; bits != 0; bits &= bits - 1) {
                    var pageId = BitOperations.TrailingZeroCount(bits);
                    var dstPageI = CountBitsUntil(PageIds, pageId);
                    var srcPageI = CountBitsUntil(field.unsafePageIds, pageId);
                    Pages[dstPageI] &= ~field.pages[srcPageI];
                    if (Pages[dstPageI] == 0) {
                        PageIds &= ~(1ul << pageId);
                        ++consume;
                    }
                    if (consume > 0) {
                        Pages[dstPageI] = Pages[dstPageI + consume];
                    }
                }
                Debug.Assert(PageIds == newPages);
            }
            public void Add(int bit) {
                var pageId = GetPageIdByBit(bit);
                var toAdd = (1ul << pageId) & (~PageIds);
                if (toAdd != 0) InsertPages(toAdd);
                Pages[CountBitsUntil(PageIds, pageId)] |= 1ul << (bit & 63);
            }
            public bool Remove(int bit) {
                var pageId = GetPageIdByBit(bit);
                if (((1ul << pageId) & PageIds) == 0) return false;
                var pageIndex = CountBitsUntil(PageIds, pageId);
                Pages[pageIndex] &= ~(1ul << (bit & 63));
                if (Pages[pageIndex] == 0) {
                    PageIds &= ~(1ul << pageId);
                    var index = BitOperations.PopCount(PageIds & ((1ul << pageId) - 1));
                    PageCount -= 1;
                    for (int i = index; i < PageCount; ++i) Pages[i] = Pages[i + 1];
                }
                return true;
            }
            public bool Contains(int bit) {
                var pageId = GetPageIdByBit(bit);
                if (((1ul << pageId) & PageIds) == 0) return false;
                var pageIndex = CountBitsUntil(PageIds, pageId);
                return (Pages[pageIndex] & (1ul << (bit & 63))) != 0;
            }
        }
        // Allocate this BitField on the heap.
        public unsafe static BitField Allocate(BitField other) {
            int pcount = other.PageCount;
            var storage = (ulong*)Marshal.AllocHGlobal(sizeof(ulong) * (pcount + 1));
            *storage = other.unsafePageIds;
            for (int i = 0; i < pcount; i++) storage[i + 1] = other.pages[i];
            return new BitField(storage);
        }
    }

    public class DynamicBitField2 : IEnumerable<int> {
        public struct PageCluster : IComparable<PageCluster> {
            public ulong PageIds;
            public ushort ClusterId;
            public ushort PageIndexOffset;
            public uint BitIndexOffset;
            public int CompareTo(PageCluster other) { return ClusterId - other.ClusterId; }
        }
        private PageCluster[] clusters = Array.Empty<PageCluster>();
        private ulong[] pages = Array.Empty<ulong>();
        private int pageCount = 0;
        private int pageIndexOffsetValid = 1;
        private int bitIndexOffsetValid = 1;
        private int GetClusterIndex(int clusterId) {
            for (int i = 0; i < clusters.Length; i++) {
                var itemId = clusters[i].ClusterId;
                if (itemId >= clusterId) return itemId == clusterId ? i : ~i;
            }
            return ~clusters.Length;
        }
        private int RequireClusterIndex(int clusterId) {
            var clusterIndex = GetClusterIndex(clusterId);
            if (clusterIndex < 0) {
                clusterIndex = ~clusterIndex;
                var newCluster = new PageCluster() {
                    ClusterId = (ushort)clusterId,
                };
                if (clusterIndex < clusters.Length) {
                    var current = clusters[clusterIndex];
                    newCluster.PageIndexOffset = current.PageIndexOffset;
                    newCluster.BitIndexOffset = current.BitIndexOffset;
                }
                Array.Resize(ref clusters, clusters.Length + 1);
                Array.Copy(clusters, clusterIndex, clusters, clusterIndex + 1, clusters.Length - clusterIndex - 1);
                clusters[clusterIndex] = newCluster;
            }
            return clusterIndex;
        }
        private int GetPageIndex(int pageId) {
            var clusterIndex = GetClusterIndex(pageId >> 6);
            if (clusterIndex < 0) return -1;
            ValidatePageOffsets(clusterIndex);
            var cluster = clusters[clusterIndex];
            var pageMask = 1ul << (pageId & 63);
            if ((cluster.PageIds & pageMask) == 0) return -1;
            return cluster.PageIndexOffset + BitOperations.PopCount(cluster.PageIds & (pageMask - 1));
        }
        private int RequirePageIndex(int pageId) {
            var clusterIndex = RequireClusterIndex(pageId >> 6);
            ValidatePageOffsets(clusterIndex);
            var cluster = clusters[clusterIndex];
            var pageMask = 1ul << (pageId & 63);
            var pageIndex = cluster.PageIndexOffset + BitOperations.PopCount(cluster.PageIds & (pageMask - 1));
            if ((cluster.PageIds & pageMask) == 0) {
                cluster.PageIds |= pageMask;
                clusters[clusterIndex] = cluster;
                pageIndexOffsetValid = Math.Min(pageIndexOffsetValid, clusterIndex + 1);
                bitIndexOffsetValid = Math.Min(bitIndexOffsetValid, clusterIndex + 1);
                if (pageCount >= pages.Length) {
                    Array.Resize(ref pages, (int)BitOperations.RoundUpToPowerOf2((uint)pages.Length + 4));
                }
                if (pageIndex <= pageCount) {
                    Array.Copy(pages, pageIndex, pages, pageIndex + 1, pageCount - pageIndex);
                }
                pages[pageIndex] = 0;
                pageCount++;
            }
            return pageIndex;
        }

        private void ValidatePageOffsets(int clusterIndex) {
            for (; pageIndexOffsetValid <= clusterIndex; ++pageIndexOffsetValid) {
                var prevCluster = clusters[pageIndexOffsetValid - 1];
                ref var icluster = ref clusters[pageIndexOffsetValid];
                icluster.PageIndexOffset = (ushort)(prevCluster.PageIndexOffset + BitOperations.PopCount(prevCluster.PageIds));
            }
        }
        private void ValidateBitOffsets(int clusterIndex) {
            ValidatePageOffsets(clusterIndex);
            for (; bitIndexOffsetValid <= clusterIndex; ++bitIndexOffsetValid) {
                var prevCluster = clusters[bitIndexOffsetValid - 1];
                ref var icluster = ref clusters[bitIndexOffsetValid];
                var bitIndex = prevCluster.BitIndexOffset;
                var pageOffset = prevCluster.PageIndexOffset;
                var pageCount = BitOperations.PopCount(prevCluster.PageIds);
                for (int p = 0; p < pageCount; p++) bitIndex += (uint)BitOperations.PopCount(pages[pageOffset + p]);
                icluster.BitIndexOffset = bitIndex;
            }
        }

        public void Clear() {
            clusters = Array.Empty<PageCluster>();
            pageCount = 0;
        }
        public void Add(int bit) {
            var pageId = (ushort)(bit >> 6);
            var pageIndex = RequirePageIndex(pageId);
            pages[pageIndex] |= (1ul << (bit & 63));
        }
        public void Remove(int bit) {
            var pageId = (ushort)(bit >> 6);
            var pageIndex = GetPageIndex(pageId);
            if (pageIndex < 0) return;
            pages[pageIndex] &= ~(1ul << (bit & 63));
        }
        public bool Contains(int bit) {
            var pageId = (ushort)(bit >> 6);
            var pageIndex = GetPageIndex(pageId);
            if (pageIndex < 0) return false;
            return (pages[pageIndex] & (1ul << (bit & 63))) != 0;
        }
        public int GetBitIndex(int bit) {
            int clusterIndex = GetClusterIndex(bit >> 12);
            int localPageId = (bit >> 6) & 63;
            ValidateBitOffsets(clusterIndex);
            ref var cluster = ref clusters[clusterIndex];
            int pageCount = BitOperations.PopCount(cluster.PageIds & ((1ul << localPageId) - 1));
            uint bitIndex = cluster.BitIndexOffset;
            for (int p = 0; p < pageCount; ++p)
                bitIndex += (uint)BitOperations.PopCount(pages[cluster.PageIndexOffset + p]);
            if ((cluster.PageIds & (1ul << localPageId)) != 0)
                bitIndex += (uint)BitOperations.PopCount(pages[cluster.PageIndexOffset + pageCount] & ~(~0ul << (bit & 63)));
            return (int)bitIndex;
        }
        public int GetNextBit(int bit) {
            ++bit;
            int clusterIndex = GetClusterIndex(bit >> 12);
            int localPageId = (bit >> 6) & 63;
            var pageIndex = 0;
            if (clusterIndex >= 0) {
                var cluster = clusters[clusterIndex];
                var pageMask = 1ul << (localPageId);
                pageIndex = cluster.PageIndexOffset +
                    BitOperations.PopCount(cluster.PageIds & (pageMask - 1));
                if ((cluster.PageIds & pageMask) != 0) {
                    var page = pages[pageIndex] & (ulong)-(long)(1ul << (bit & 63));
                    if (page != 0) return (cluster.ClusterId * 64 + localPageId) * 64 + BitOperations.TrailingZeroCount(page);
                    ++localPageId;
                    ++pageIndex;
                }
            } else {
                clusterIndex = ~clusterIndex;
                localPageId = 0;
                if (clusterIndex < clusters.Length) pageIndex = clusters[clusterIndex].PageIndexOffset;
            }
            while (clusterIndex < clusters.Length) {
                var cluster = clusters[clusterIndex];
                for (; localPageId < 64;) {
                    var masked = cluster.PageIds & (ulong)-(long)(1ul << localPageId);
                    if (masked == 0) break;
                    localPageId = BitOperations.TrailingZeroCount(masked);
                    var page = pages[pageIndex++];
                    if (page != 0) return (cluster.ClusterId * 64 + localPageId) * 64 + BitOperations.TrailingZeroCount(page);
                }
                localPageId = 0;
                ++clusterIndex;
            }
            return -1;
        }

        public struct Enumerator : IEnumerator<int> {
            public readonly DynamicBitField2 BitField;
            private int bit;
            public int Current => bit;
            object IEnumerator.Current => Current;
            public Enumerator(DynamicBitField2 bitField) {
                BitField = bitField;
                bit = -1;
            }
            public void Dispose() { }
            public void Reset() { bit = -1; }
            public bool MoveNext() {
                bit = BitField.GetNextBit(bit);
                return bit >= 0;
            }
        }
        public Enumerator GetEnumerator() => new Enumerator(this);
        IEnumerator<int> IEnumerable<int>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public class DynamicBitField : IEnumerable<BitIndex> {
        public const int BitToPageShift = 6;
        public const BitIndex InvalidIndex = ~(BitIndex)0;
        private BitIndex[] pageOffsets = Array.Empty<BitIndex>();
        private ulong[] pages = Array.Empty<ulong>();
        private int pageCount = 0;
        private int popCount = 0;

        public bool IsEmpty => popCount == 0;
        public int Count => popCount;

        private int AllocatePageIndex(int pageIndex, int pageOffset) {
            Debug.Assert(pageIndex < 0, "Should not allocate valid page index");
            pageIndex = ~pageIndex;
            if (pageIndex >= pageCount || pages[pageIndex] != 0) {
                if (pageCount >= pages.Length) {
                    int newCount = (int)BitOperations.RoundUpToPowerOf2((uint)pageCount + 4);
                    Array.Resize(ref pages, newCount);
                    Array.Resize(ref pageOffsets, newCount);
                }
                if (pageIndex < pageCount) {
                    Array.Copy(pages, pageIndex, pages, pageIndex + 1, pageCount - pageIndex);
                    Array.Copy(pageOffsets, pageIndex, pageOffsets, pageIndex + 1, pageCount - pageIndex);
                }
                pages[pageIndex] = 0;
                ++pageCount;
            }
            pageOffsets[pageIndex] = pageOffset;
            return pageIndex;
        }

        public void Clear() {
            pageCount = 0;
            popCount = 0;
        }
        public int GetPageIndex(BitIndex pageOffset) {
            return pageOffsets.AsSpan(0, pageCount).BinarySearch(pageOffset);
        }
        public int GetBitPage(BitIndex bit) {
            var pageOffset = (bit >> BitToPageShift);
            return GetPageIndex(pageOffset);
        }
        public bool TryAdd(BitIndex bit) {
            var pageOffset = (bit >> BitToPageShift);
            var pageIndex = GetPageIndex(pageOffset);
            if (pageIndex < 0) pageIndex = AllocatePageIndex(pageIndex, pageOffset);
            var mask = 1ul << (int)(bit & 63);
            var current = pages[pageIndex];
            var masked = current | mask;
            if (current == masked) return false;
            popCount += 1;
            pages[pageIndex] = masked;
            return true;
        }
        public void Add(BitIndex bit) {
            Debug.Assert(!Contains(bit));
            TryAdd(bit);
        }
        public bool TryRemove(BitIndex bit) {
            var pageOffset = (bit >> BitToPageShift);
            var pageIndex = GetPageIndex(pageOffset);
            if ((uint)pageIndex >= pageCount) return false;
            var current = pages[pageIndex];
            var masked = current & ~(1ul << (int)(bit & 63));
            if (current == masked) return false;
            popCount -= 1;
            pages[pageIndex] = masked;
            return true;
        }
        public void Remove(BitIndex bit) {
            var pageOffset = (bit >> BitToPageShift);
            var pageIndex = GetPageIndex(pageOffset);
            var mask = (1ul << (int)(bit & 63));
            Debug.Assert((pages[pageIndex] & mask) != 0);
            pages[pageIndex] &= ~mask;
            popCount -= 1;
        }
        public bool Contains(BitIndex bit) {
            var pageOffset = (bit >> BitToPageShift);
            var pageIndex = GetPageIndex(pageOffset);
            if (pageIndex < 0) return false;
            return (pages[pageIndex] & (1ul << (int)(bit & 63))) != 0;
        }
        public BitIndex GetNextBitInclusive(BitIndex bit) {
            var pageOffset = (bit >> BitToPageShift);
            var pageIndex = GetPageIndex(pageOffset);
            if (pageIndex >= 0) {
                var mask = pages[pageIndex] & (ulong)-(long)(1ul << (int)(bit & 63));
                if (mask != 0) return (BitIndex)(pageOffset * 64 + (uint)BitOperations.TrailingZeroCount(mask));
                ++pageIndex;
            } else {
                pageIndex = ~pageIndex;
            }
            // Keep looking for a non-empty page
            for (; pageIndex < pageCount; ++pageIndex) {
                var page = pages[pageIndex];
                if (page == 0) continue;
                return (BitIndex)(pageOffsets[pageIndex] * 64 + (uint)BitOperations.TrailingZeroCount(page));
            }
            return ~((BitIndex)0);
        }

        public interface IPagedEnumerator {
            public BitIndex MoveNextPage();
            public BitIndex GetCurrentPageId();
            public ulong GetCurrentPage();
        }

        public struct Enumerator : IEnumerator<BitIndex>, IPagedEnumerator {
            public readonly DynamicBitField BitField;
            private int pageIndex;
            private int bitIndex;
            public BitIndex Current => (BitIndex)(BitField.pageOffsets[pageIndex] * 64 + bitIndex);
            object IEnumerator.Current => Current;
            public Enumerator(DynamicBitField bitField) {
                BitField = bitField;
                pageIndex = 0;
                bitIndex = -1;
            }
            public void Dispose() { }
            public void Reset() { pageIndex = 0; bitIndex = -1; }
            public bool MoveNext() {
                while (true) {
                    if (pageIndex >= BitField.pageCount) return false;
                    if (bitIndex < 64 - 1) {
                        var page = BitField.pages[pageIndex];
                        bitIndex = BitOperations.TrailingZeroCount(
                            page & (0xffffffffffffffff << (bitIndex + 1)));
                        if (bitIndex < 64) return true;
                    }
                    pageIndex++;
                    bitIndex = -1;
                }
            }

            public void RemoveCurrent() {
                BitField.pages[pageIndex] &= ~(1ul << bitIndex);
                BitField.popCount -= 1;
            }

            public BitIndex MoveNextPage() {
                ++pageIndex;
                return pageIndex < BitField.pageOffsets.Length
                    ? BitField.pageOffsets[pageIndex] : InvalidIndex;
            }
            public BitIndex GetCurrentPageId() { return BitField.pageOffsets[pageIndex]; }
            public ulong GetCurrentPage() { return BitField.pages[pageIndex]; }
            public static readonly Enumerator Invalid = new(Empty);
        }
        public Enumerator GetEnumerator() => new Enumerator(this);
        IEnumerator<BitIndex> IEnumerable<BitIndex>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public struct UnionEnumerator<BitField1, BitField2>
            : IEnumerator<BitIndex>, IEnumerable<BitIndex>, IPagedEnumerator
            where BitField1 : struct, IPagedEnumerator where BitField2 : struct, IPagedEnumerator {
            public BitField1 Bits1;
            public BitField2 Bits2;
            private ulong page;
            private BitIndex bitIndex;
            private BitIndex page1, page2;
            public BitIndex Current => bitIndex;
            object IEnumerator.Current => Current;
            public UnionEnumerator(BitField1 bits1, BitField2 bits2) {
                Bits1 = bits1;
                Bits2 = bits2;
                bitIndex = InvalidIndex;
                page1 = page2 = 0;
                //MoveNextPage();
                //Bits1.MoveNextPage();
                //Bits2.MoveNextPage();
            }
            public BitIndex MoveNextPage() {
                var minPage = (int)Math.Min((uint)page1, (uint)page2);
                if ((uint)page1 <= (uint)minPage) page1 = Bits1.MoveNextPage();
                if ((uint)page2 <= (uint)minPage) page2 = Bits2.MoveNextPage();
                page = 0;
                minPage = (int)Math.Min((uint)page1, (uint)page2);
                if (page1 == minPage) page |= Bits1.GetCurrentPage();
                if (page2 == minPage) page |= Bits2.GetCurrentPage();
                return minPage;
            }
            public BitIndex GetCurrentPageId() {
                return Math.Min(page1, page2);
            }
            public ulong GetCurrentPage() {
                return page;
            }
            public void Dispose() { }
            public void Reset() { bitIndex = InvalidIndex; }
            public bool MoveNext() {
                while (true) {
                    var next = GetNextBit(page, (int)(bitIndex & 63));
                    if (next < 64) {
                        page &= page - 1;
                        bitIndex = (BitIndex)((bitIndex & ~63) + (uint)next);
                        return true;
                    }
                    var pageId = MoveNextPage();
                    bitIndex = pageId << 6;
                    if (pageId == InvalidIndex) return false;
                }
            }
            private static int GetNextBit(ulong page, int bitIndex) {
                return bitIndex + BitOperations.TrailingZeroCount(page >> bitIndex);
            }
            public UnionEnumerator<BitField1, BitField2> GetEnumerator() => this;
            IEnumerator<BitIndex> IEnumerable<BitIndex>.GetEnumerator() => GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }

        public static UnionEnumerator<BitField1, BitField2> Union<BitField1, BitField2>(BitField1 bits1, BitField2 bits2)
            where BitField1 : struct, IPagedEnumerator where BitField2 : struct, IPagedEnumerator {
            return new UnionEnumerator<BitField1, BitField2>(bits1, bits2);
        }

        public struct PageEnumerator : IEnumerator<BitIndex> {
            public readonly DynamicBitField BitField;
            private int pageIndex;
            public int PageCount => BitField.pageCount;
            public BitIndex Current => BitField.pageOffsets[pageIndex];
            public ulong PageContent => BitField.pages[pageIndex];
            object IEnumerator.Current => Current;
            public PageEnumerator(DynamicBitField bitField) {
                BitField = bitField;
                pageIndex = -1;
            }
            public void Dispose() { }
            public void Reset() { pageIndex = -1; }
            public bool MoveNext() {
                while (true) {
                    if (pageIndex >= BitField.pageCount) return false;
                    pageIndex++;
                }
            }
            public void JumpToPage(int newPageIndex) {
                pageIndex = newPageIndex;
            }

            // Note: All of these do not rely on pageIndex
            public BitIndex GetPageOffset(BitIndex bit) {
                return bit >> BitToPageShift;
            }
            public BitIndex GetFirstPageBit(BitIndex pageOffset) {
                return pageOffset << BitToPageShift;
            }
            public bool SetBit(int pageIndex, int bit) {
                ref var page = ref BitField.pages[pageIndex];
                var mask = 1ul << (bit & 63);
                if ((page & mask) != 0) return false;
                page |= mask;
                BitField.popCount++;
                return true;
            }
            public bool ClearBit(int pageIndex, int bit) {
                ref var page = ref BitField.pages[pageIndex];
                var mask = 1ul << (bit & 63);
                if ((page & mask) == 0) return false;
                page &= ~mask;
                BitField.popCount--;
                return true;
            }
            public ulong GetPageContent(BitIndex pageOffset) {
                var pageIndex = BitField.GetPageIndex(pageOffset);
                return pageIndex >= 0 ? BitField.pages[pageIndex] : 0;
            }
            // Returns ~ of next index if doesnt exist
            public int RequirePageIndex(BitIndex pageOffset, out bool alocated) {
                var pageIndex = GetPageIndex(pageOffset);
                alocated = pageIndex < 0;
                if (alocated) pageIndex = BitField.AllocatePageIndex(pageIndex, pageOffset);
                return pageIndex;
            }
            public int GetPageIndex(BitIndex pageOffset) {
                return BitField.GetPageIndex(pageOffset);
            }
            public bool IsValidPageIndex(int pageIndex) {
                return (uint)pageIndex < BitField.pages.Length;
            }
            public int GetNextPage(int pageIndex) {
                pageIndex = pageIndex < 0 ? ~pageIndex : pageIndex + 1;
                if (pageIndex >= BitField.pages.Length) return -1;
                return pageIndex;
            }
            public BitIndex GetPageOffsetAt(int pageIndex) {
                return BitField.pageOffsets[pageIndex];
            }
            public ulong GetPageContentAt(int pageIndex) {
                return BitField.pages[pageIndex];
            }
            public int GetLocalBitIndex(int pageIndex, BitIndex bit) {
                return BitOperations.PopCount(BitField.pages[pageIndex] & ~(~0ul << (bit & 63)));
            }
        }
        public PageEnumerator GetPageEnumerator() => new PageEnumerator(this);

        public static DynamicBitField Empty = new();
    }
}
