using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Weesals.ECS {
    // Represents up to 4096 bits broken up into up to 64 blocks of 64 bits.
    // BitFields are compared by pointer, so each allocated instance should be unique.
    public unsafe readonly struct BitField : IEquatable<BitField>, IEnumerable<int> {
        private readonly ulong pageIds;
        private readonly ulong* pages;
        public readonly bool IsEmpty => pageIds == 0;
        public readonly int PageCount => BitOperations.PopCount(pageIds);
        public readonly int BitCount {
            get {
                int counter = 0;
                for (int i = PageCount - 1; i >= 0; i--) counter += BitOperations.PopCount(pages[i]);
                return counter;
            }
        }
        public BitField(ulong pageIds, ulong* pages) {
            this.pageIds = pageIds;
            this.pages = pages;
        }
        public bool Contains(int bit) {
            if (pages == null) return (pageIds & (1uL << bit)) != 0;
            var pageId = GetPageIdByBit(bit);
            if ((pageIds & (1uL << pageId)) == 0) return false;
            var page = pages[CountBitsUntil(pageIds, pageId)];
            return (page & (1uL << (bit & 63))) != 0;
        }
        public int GetFirstBit() {
            var nextPageId = BitOperations.TrailingZeroCount(pageIds);
            if (nextPageId >= 64) return -1;
            var nextPage = pages[CountBitsUntil(pageIds, nextPageId)];
            return nextPageId * 64 + BitOperations.TrailingZeroCount(nextPage);
        }
        public int GetNextBit(int bit) {
            ++bit;
            var pageId = GetPageIdByBit(bit);
            if ((pageIds & (1uL << pageId)) != 0) {
                var page = pages[CountBitsUntil(pageIds, pageId)];
                page &= (ulong)(-(1L << (bit & 63)));
                if (page != 0) return pageId * 64 + BitOperations.TrailingZeroCount(page);
            }
            var nextPageId = GetNextBit(pageIds, pageId);
            if (nextPageId >= 64) return -1;
            var nextPage = pages[CountBitsUntil(pageIds, nextPageId)];
            return nextPageId * 64 + BitOperations.TrailingZeroCount(nextPage);
        }
        public int GetBitIndex(int bit) {
            int pageId = GetPageIdByBit(bit);
            var pageIndex = CountBitsUntil(pageIds, pageId);
            int counter = 0;
            for (int i = 0; i < pageIndex; i++) counter += BitOperations.PopCount(pages[i]);
            counter += BitOperations.PopCount(pages[pageIndex] & ((1ul << (bit & 63)) - 1));
            return counter;
        }
        public bool TryGetBitIndex(int bit, out int index) {
            int pageId = GetPageIdByBit(bit);
            if ((pageIds & 1ul << pageId) == 0) { index = -1; return false; }
            var pageIndex = CountBitsUntil(pageIds, pageId);
            if ((pages[pageIndex] & (1ul << (bit & 63))) == 0) { index = -1; return false; }
            index = 0;
            for (int i = 0; i < pageIndex; i++) index += BitOperations.PopCount(pages[i]);
            index += BitOperations.PopCount(pages[pageIndex] & ((1ul << (bit & 63)) - 1));
            return true;
        }
        public bool ContainsAll(BitField withTypes) {
            int withPage1I = 0;
            for (int p = BitOperations.TrailingZeroCount(withTypes.pageIds); p < 64; p = GetNextBit(withTypes.pageIds, p)) {
                var reqBits = withTypes.pages[withPage1I++];
                if ((pageIds & (1ul << p)) == 0) return false;
                var curBits = pages[CountBitsUntil(pageIds, p)];
                if ((curBits & reqBits) != reqBits) return false;
            }
            return true;
        }
        public bool ContainsAny(BitField withTypes) {
            int withPage1I = 0;
            for (int p = BitOperations.TrailingZeroCount(withTypes.pageIds); p < 64; p = GetNextBit(withTypes.pageIds, p)) {
                var reqBits = withTypes.pages[withPage1I++];
                if ((pageIds & (1ul << p)) == 0) continue;
                var curBits = pages[CountBitsUntil(pageIds, p)];
                if ((curBits & reqBits) != 0) return true;
            }
            return false;
        }
        public bool DeepEquals(BitField other) {
            if (pageIds != other.pageIds) return false;
            int pageCount = PageCount;
            return new Span<ulong>(pages, pageCount).SequenceEqual(new Span<ulong>(other.pages, pageCount));
        }
        public ulong DeepHash() {
            ulong hash = 0;
            for (int i = PageCount - 1; i >= 0; i--) hash += pages[i];
            return hash;
        }
        public bool Equals(BitField other) { return pages == other.pages; }
        public override bool Equals(object? obj) { throw new NotImplementedException(); }
        public override int GetHashCode() { return (int)pages + (int)((nint)pages >> 32); }
        public override string ToString() {
            return PageCount == 0 ? "Empty"
                : this.Select(i => i.ToString()).Aggregate((i1, i2) => $"{i1},{i2}");
        }

        private static int GetPageIdByBit(int bit) { return bit >> 6; }
        private static int GetLocalBit(int bit) { return bit & 63; }
        private static int CountBitsUntil(ulong pattern, int pageId) {
            return BitOperations.PopCount(pattern & ((1ul << pageId) - 1));
        }
        private static int GetNextBit(ulong pattern, int bit) {
            return GetBitFrom(pattern, bit + 1);
        }
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
                    var pageIds = Bits1.pageIds | Bits2.pageIds;
                    var pageId = GetPageIdByBit(bitIndex);
                    pageId = GetNextBit(pageIds, pageId);
                    if (pageId >= 64) return false;
                    bitIndex = pageId * 64;
                    page = 0;
                    if ((Bits1.pageIds & (1ul << pageId)) != 0)
                        page |= Bits1.pages[Bits1.GetBitIndex(pageId)];
                    if ((Bits2.pageIds & (1ul << pageId)) != 0)
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
                    page = ~(page = 0);
                    if ((Bits1.pageIds & (1ul << pageId)) != 0)
                        page &= Bits1.pages[Bits1.GetBitIndex(pageId)];
                    if ((Bits2.pageIds & (1ul << pageId)) != 0)
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
            public void Reset() { bitIndex = -1; }
            public bool MoveNext() {
                var next = GetNextBit(page, bitIndex);
                if (next >= 64) {
                    var pageIds = Bits1.pageIds;
                    var pageId = GetPageIdByBit(bitIndex);
                    pageId = GetNextBit(pageIds, pageId);
                    if (pageId >= 64) return false;
                    bitIndex = pageId * 64;
                    page = Bits1.pages[Bits1.GetBitIndex(pageId)];
                    if ((Bits2.pageIds & (1ul << pageId)) != 0)
                        page &= Bits2.pages[Bits2.GetBitIndex(pageId)];
                    next = GetNextBit(page, -1);
                }
                bitIndex = (bitIndex & ~63) + next;
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
            public List<ulong> Pages = new();
            public bool IsEmpty => PageIds == 0;
            private void InsertPages(ulong toAdd) {
                Debug.Assert((PageIds & toAdd) == 0);
                PageIds |= toAdd;
                var prune = PageIds;
                int offset = BitOperations.PopCount(toAdd);
                for (int i = 0; i < offset; i++) Pages.Add(0);
                for (int p = Pages.Count - 1; offset > 0; --offset) {
                    int pageId;
                    while (true) {
                        pageId = 63 - BitOperations.LeadingZeroCount(prune);
                        prune ^= 1ul << pageId;
                        if ((toAdd & (1ul << pageId)) != 0) break;
                    }
                    // Shuffle pages down
                    for (; p - offset >= pageId;) Pages[p--] = Pages[p - offset];
                    // Insert slot for new page
                    Pages[p--] = 0;
                }
            }
            public void Clear() { PageIds = 0; Pages.Clear(); }
            public void Append(BitField field) {
                var allPages = field.pageIds | PageIds;
                var toAdd = field.pageIds & (~PageIds);
                if (toAdd != 0) InsertPages(toAdd);
                var prune = field.pageIds;
                while (prune != 0) {
                    var pageId = BitOperations.TrailingZeroCount(prune);
                    prune ^= 1ul << pageId;
                    var dstPageI = CountBitsUntil(PageIds, pageId);
                    var srcPageI = CountBitsUntil(field.pageIds, pageId);
                    Pages[dstPageI] |= field.pages[srcPageI];
                }
                Debug.Assert(PageIds == allPages);
            }
            public void Remove(BitField field) {
                var newPages = ~field.pageIds & PageIds;
                var toRemove = field.pageIds & PageIds;
                if (toRemove == 0) return;
                var prune = field.pageIds;
                while (prune != 0) {
                    var pageId = BitOperations.TrailingZeroCount(prune);
                    prune ^= 1ul << pageId;
                    var dstPageI = CountBitsUntil(PageIds, pageId);
                    var srcPageI = CountBitsUntil(field.pageIds, pageId);
                    Pages[dstPageI] &= ~field.pages[srcPageI];
                    if (Pages[dstPageI] == 0) {
                        PageIds &= ~(1ul << pageId);
                        Pages.RemoveAt(dstPageI);
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
                    Pages.RemoveAt(pageIndex);
                }
                return true;
            }
        }
        // Allocate this BitField on the heap.
        public unsafe static BitField Allocate(BitField other) {
            int pcount = other.PageCount;
            var pages = (ulong*)Marshal.AllocHGlobal(sizeof(ulong) * pcount);
            for (int i = 0; i < pcount; i++) pages[i] = other.pages[i];
            return new BitField(other.pageIds, pages);
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
                bitIndex += (uint)BitOperations.PopCount(pages[cluster.PageIndexOffset + pageCount] & ((1ul << (bit & 63)) - 1));
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

    public class DynamicBitField : IEnumerable<int> {
        private ushort[] pageOffsets = Array.Empty<ushort>();
        private ulong[] pages = Array.Empty<ulong>();
        int pageCount = 0;
        public void Clear() {
            pageCount = 0;
        }
        public void Add(int bit) {
            var pageOffset = (ushort)((uint)bit / 64);
            var pageIndex = pageOffsets.AsSpan(0, pageCount).BinarySearch(pageOffset);
            if (pageIndex < 0) {
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
                    pageOffsets[pageIndex] = pageOffset;
                    pages[pageIndex] = 0;
                    ++pageCount;
                }
            }
            pages[pageIndex] |= 1ul << (bit & 63);
        }
        public bool TryRemove(int bit) {
            var pageOffset = (ushort)((uint)bit / 64);
            var pageIndex = pageOffsets.AsSpan(0, pageCount).BinarySearch(pageOffset);
            if ((uint)pageIndex >= pageCount) return false;
            var current = pages[pageIndex];
            var masked = current & ~(1ul << (bit & 63));
            pages[pageIndex] = masked;
            return (current != masked);
        }
        public void Remove(int bit) {
            var pageOffset = (ushort)((uint)bit / 64);
            var pageIndex = pageOffsets.AsSpan(0, pageCount).BinarySearch(pageOffset);
            pages[pageIndex] &= ~(1ul << (bit & 63));
        }
        public bool Contains(int bit) {
            var pageOffset = (ushort)((uint)bit / 64);
            var pageIndex = pageOffsets.AsSpan(0, pageCount).BinarySearch(pageOffset);
            if (pageIndex < 0) return false;
            return (pages[pageIndex] & (1ul << (bit & 63))) != 0;
        }
        public int GetNextBitInclusive(int bit) {
            var pageOffset = (ushort)((uint)bit / 64);
            var pageIndex = pageOffsets.AsSpan(0, pageCount).BinarySearch(pageOffset);
            if (pageIndex >= 0) {
                var mask = pages[pageIndex] & (ulong)-(long)(1ul << (bit & 63));
                if (mask != 0) return pageOffset * 64 + BitOperations.TrailingZeroCount(mask);
                ++pageIndex;
            } else {
                pageIndex = ~pageIndex;
            }
            // Keep looking for a non-empty page
            for (; pageIndex < pageCount; ++pageIndex) {
                var page = pages[pageIndex];
                if (page == 0) continue;
                return pageOffsets[pageIndex] * 64 + BitOperations.TrailingZeroCount(page);
            }
            return -1;
        }

        public struct Enumerator : IEnumerator<int> {
            public readonly DynamicBitField BitField;
            private int pageIndex;
            private int bitIndex;
            public int Current => BitField.pageOffsets[pageIndex] * 64 + bitIndex;
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
            public static readonly Enumerator Invalid = new(None);
        }
        public Enumerator GetEnumerator() => new Enumerator(this);
        IEnumerator<int> IEnumerable<int>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        private static readonly DynamicBitField None = new();
    }
}
