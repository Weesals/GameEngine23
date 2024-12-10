using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using UnityEngine;
using Weesals;
using Weesals.ECS;
using Weesals.Engine;
using Weesals.Engine.Profiling;
using Weesals.Landscape;
using Weesals.Utility;

namespace Game5.Game {
    //[UpdateOrderId(500)]
    public partial class EntityMapSystem : SystemBase
        , LifeSystem.ICreateListener
        , LifeSystem.IDestroyListener
        {

        public const int Separation = 8000;

        [SparseComponent]
        [NoCloneComponent]
        public struct EntityMapRegister {
            public ulong NamesMask;
            public RectI ChunkRect;
        }

        public interface IMovedEntitiesListener {
            void NotifyMovedEntities(HashSet<Entity> entities);
        }

        public LifeSystem LifeSystem { get; private set; }
        public LandscapeData LandscapeData { get; private set; }

        public struct EntityMap {
            public struct Bucket {
                public ulong Names;
                public RectI Range;
                public RangeInt EntityRange;
                public int ReferenceCount;
                public override string ToString() => Range.ToString();
            }
            private SparseArray<Entity> entities;
            private SparseArray<Bucket> buckets;
            private MultiHashMap<int, int> bucketByHash;
            private MultiHashMap<uint, int> map;
            private HashMap<string, int> names;

            public EntityMap() {
                entities = new(256);
                buckets = new(32);
                bucketByHash = new(64);
                map = new(128);
                names = new(32);
            }
            public void Dispose() {
                //entities.Dispose();
                //buckets.Dispose();
                bucketByHash.Dispose();
                names.Dispose();
                map.Dispose();
            }

            public int RequireNameIndex(string name) {
                if (!names.TryGetValue(name, out var index)) {
                    names.Add(name, index = names.Count());
                }
                return index;
            }

            private int FindMatchingBucket(int bucketHash, RectI range, ulong names) {
                foreach (var otherBucketIndex in bucketByHash.GetValuesForKey(bucketHash)) {
                    var otherBucket = buckets[otherBucketIndex];
                    if (otherBucket.Names == names && otherBucket.Range == range) return otherBucketIndex;
                }
                return -1;
            }

            private static int GetBucketHash(RectI range, ulong names) {
                return (range, names).GetHashCode();
            }
            private static uint GetMapHash(Int2 pnt, int nameIndex) {
                return (uint)(pnt, nameIndex).GetHashCode();
            }

            private int RequireBucket(RectI range, ulong names) {
                var bucketHash = GetBucketHash(range, names);
                var bucketIndex = FindMatchingBucket(bucketHash, range, names);
                if (bucketIndex == -1) {
                    bucketIndex = buckets.Add(new() { Range = range, Names = names, });
                    bucketByHash.Add(bucketHash, bucketIndex);
                    AddBucket(bucketIndex);
                }
                return bucketIndex;
            }

            public void Insert(RectI range, Entity entity, ulong nameSets) {
                var bucketIndex = RequireBucket(range, nameSets);
                ref var bucket = ref buckets[bucketIndex];
                entities.Reallocate(ref bucket.EntityRange, bucket.EntityRange.Length + 1);
                entities[bucket.EntityRange.End - 1] = entity;
            }
            public bool Remove(RectI range, Entity entity, ulong namesMask) {
                var bucketIndex = RequireBucket(range, namesMask);
                ref var bucket = ref buckets[bucketIndex];
                int entryIndex = bucket.EntityRange.Length - 1;
                for (; entryIndex >= 0; --entryIndex)
                    if (entities[bucket.EntityRange.Start + entryIndex].Equals(entity)) break;
                if (entryIndex < 0) return false;
                entities[bucket.EntityRange.Start + entryIndex] = entities[bucket.EntityRange.End - 1];
                entities.Reallocate(ref bucket.EntityRange, bucket.EntityRange.Length - 1);
                if (bucket.EntityRange.Length == 0) {
                    RemoveBucket(bucketIndex);
                    var bucketHash = GetBucketHash(range, namesMask);
                    bucketByHash.Remove(bucketHash, bucketIndex);
                    buckets.Return(bucketIndex);
                }
                return true;
            }

            private void AddBucket(int bucketIndex) {
                var bucket = buckets[bucketIndex];
                for (int y = 0; y < bucket.Range.Height; y++) {
                    for (int x = 0; x < bucket.Range.Width; x++) {
                        var pnt = new Int2(bucket.Range.X + x, bucket.Range.Y + y);
                        for (var bits = bucket.Names; bits != 0; bits &= bits - 1) {
                            var nameIndex = BitOperations.TrailingZeroCount(bits);
                            var hash = GetMapHash(pnt, nameIndex);
                            map.Add(hash, bucketIndex);
                        }
                    }
                }
            }
            private void RemoveBucket(int bucketIndex) {
                var bucket = buckets[bucketIndex];
                for (int y = 0; y < bucket.Range.Height; y++) {
                    for (int x = 0; x < bucket.Range.Width; x++) {
                        var pnt = new Int2(bucket.Range.X + x, bucket.Range.Y + y);
                        for (var bits = bucket.Names; bits != 0; bits &= bits - 1) {
                            var nameIndex = BitOperations.TrailingZeroCount(bits);
                            var hash = GetMapHash(pnt, nameIndex);
                            bool removed = map.Remove(hash, bucketIndex);
                            Debug.Assert(removed, "Failed to remove bucket");
                        }
                    }
                }
            }

            public struct SpiralIterator {
                public GridSpiralIterator Spiral;
                public Int2 Current => Spiral.Current;
                public SpiralIterator(Int2 cpnt) {
                    Spiral = new GridSpiralIterator(cpnt);
                }
                public bool MoveNext() { return Spiral.MoveNext(); }
                public int GetDistanceSq(Int2 from) {
                    from -= Spiral.Current * Separation + Separation / 2;
                    from = Int2.Abs(from);
                    from -= Separation / 2;
                    from = Int2.Max(from, 0);
                    return (int)Int2.Dot(from, from);
                }
            }
            public SpiralIterator CreateSpiralIterator(Int2 pnt) {
                var it = new SpiralIterator(SimToChunk(pnt));
                it.Spiral.SetIterationCorner(pnt - (it.Spiral.Current * Separation + Separation / 2));
                return it;
            }

            public struct NearestIterator {
                public Int2 Position;
                public SpiralIterator SpiralIt;
                private int nearestDst2;
                private int nameIndex;
                private MultiHashMap<uint, int>.Enumerator bucketIt;
                private int entryIndex;
                public Entity Current { get; private set; }
                public NearestIterator(Int2 pnt, int rangeDst2, int _nameIndex) {
                    Position = pnt;
                    bucketIt = default;
                    entryIndex = default;
                    Current = default;
                    nearestDst2 = rangeDst2;
                    nameIndex = _nameIndex;
                    SpiralIt = new SpiralIterator(SimToChunk(pnt));
                    SpiralIt.Spiral.SetIterationCorner(pnt - (SpiralIt.Spiral.Current * Separation + Separation / 2));
                }
                public bool MoveNext1(in EntityMap map) {
                    if (!SpiralIt.MoveNext() || SpiralIt.GetDistanceSq(Position) >= nearestDst2) return false;
                    var mapHash = GetMapHash(SpiralIt.Current, nameIndex);
                    bucketIt = map.map.GetValuesForKey(mapHash);
                    return true;
                }
                public bool MoveNext2(in EntityMap map) {
                    var pnt = SpiralIt.Current;
                    var ctr = SimToChunk(Position);
                    while (bucketIt.MoveNext()) {
                        var bucketIndex = bucketIt.Current;
                        var bucket = map.buckets[bucketIndex];
                        if ((bucket.Names & (1ul << nameIndex)) == 0) continue;
                        //if (!bucket.Range.Contains(new(pnt.x, pnt.y))) continue;
                        var nearest = Int2.Clamp(ctr, bucket.Range.Min, bucket.Range.Max - 1);
                        if (!pnt.Equals(nearest)) continue;

                        entryIndex = bucket.EntityRange.Start - 1;
                        return true;
                    }
                    return false;
                }
                public bool MoveNext3(in EntityMap map) {
                    var bucket = map.buckets[bucketIt.Current];
                    if (++entryIndex >= bucket.EntityRange.End) return false;
                    Current = map.entities[entryIndex];
                    return true;
                }
                public bool MarkNearest(int dst2) {
                    if (dst2 >= nearestDst2) return false;
                    nearestDst2 = dst2;
                    return true;
                }
            }
            public NearestIterator CreateNearestIterator(Int2 pnt, int range2, string name) {
                return new NearestIterator(pnt, range2, RequireNameIndex(name));
            }

            public override int GetHashCode() {
                int hash = 0;
                foreach (var kv in map) {
                    hash = hash * 51 + kv.Key.GetHashCode() * 3 + kv.Value.GetHashCode();
                }
                return hash;
            }

            public void CopyStateFrom(EntityMap other) {
                other.map.CopyTo(map);
            }

            public struct EntitiesEnumerator {
                public readonly EntityMap EntityMap;
                public readonly Int2 Position;
                public readonly int NameHash;
                private ArraySegment<Entity>.Enumerator entities;
                private MultiHashMap<uint, int>.Enumerator cellsAt;
                public Entity Current => entities.Current;
                public bool HasAny => GetHasAny();
                public EntitiesEnumerator(EntityMap entityMap, Int2 point, int nameHash) {
                    EntityMap = entityMap;
                    Position = point;
                    NameHash = nameHash;
                    cellsAt = EntityMap.map.GetValuesForKey(GetMapHash(point, nameHash));
                    entities = new ArraySegment<Entity>(Array.Empty<Entity>()).GetEnumerator();
                }
                private bool GetHasAny() {
                    var copy = this;
                    return copy.MoveNext();
                }
                public bool MoveNext() {
                    while (true) {
                        if (entities.MoveNext()) return true;
                        while (true) {
                            if (!cellsAt.MoveNext()) return false;
                            var bucket = EntityMap.buckets[cellsAt.Current];
                            if ((bucket.Names & (1ul << NameHash)) == 0) continue;
                            if (!bucket.Range.Contains(Position)) continue;
                            entities = EntityMap.entities.Slice(bucket.EntityRange).GetEnumerator();
                            break;
                        }
                    }
                }
                public EntitiesEnumerator GetEnumerator() => this;
            }
            public EntitiesEnumerator GetEntitiesEnumerator(Int2 pnt, int nameIndex) {
                return new(this, pnt, nameIndex);
            }
        }

        public struct MoveContract : IDisposable {
            private PooledHashMap<Entity, RectI> previousChunkId;

            public bool IsValid => previousChunkId.IsValid;

            public MoveContract(PooledHashMap<Entity, RectI> prev) {
                previousChunkId = prev;
            }
            public void Dispose() {
                previousChunkId.Dispose();
            }
            public void Clear() {
                previousChunkId.Clear();
            }
            public void MoveEntity(Entity entity, ref ECTransform tform, Int2 newPos) {
                var oldHash = SimToRect(tform.Position);
                var newHash = SimToRect(newPos);
                tform.Position = newPos;
                if (oldHash == newHash) return;
                if (previousChunkId.ContainsKey(entity)) return;
                previousChunkId[entity] = oldHash;
            }
            public PooledHashMap<Entity, RectI>.Enumerator GetEnumerator() { return previousChunkId.GetEnumerator(); }
        }

        public EntityMap AllEntities;
        private HashSet<Entity> movedEntities;
        private List<IMovedEntitiesListener> movedListeners = new();

        public MoveContract AllocateContract() {
            return new MoveContract(new(32));
        }

        private ComponentMutateListener mutations;

        protected override void OnCreate() {
            base.OnCreate();
            LifeSystem = World.GetOrCreateSystem<LifeSystem>();
            AllEntities = new();
            movedEntities = new(256);
            LifeSystem.RegisterCreateListener(this, true);
            LifeSystem.RegisterDestroyListener(this, true);

            var query = World.BeginQuery().With<ECTransform>().Build();
            World.Manager.AddListener(query, new ArchetypeListener() {
                OnCreate = RegisterEntity,
                OnDelete = UnregisterEntity,
            });
            mutations = new ComponentMutateListener(World.Manager, query, World.Context.RequireComponentTypeId<ECTransform>());
        }

        protected override void OnDestroy() {
            //movedEntities.Dispose();
            LifeSystem.RegisterDestroyListener(this, false);
            LifeSystem.RegisterCreateListener(this, false);
            AllEntities.Dispose();
            base.OnDestroy();
        }
        protected override void OnUpdate() {
            foreach (var transform in mutations) {
                var mapRegister = World.Manager.GetComponent<EntityMapRegister>(transform.EntityAddress);
                var newChunkId = SimToRect(transform.GetRO<ECTransform>().Position);
                if (newChunkId != mapRegister.ChunkRect) {
                    var remove = AllEntities.Remove(mapRegister.ChunkRect, transform.Entity, 1);
                    Debug.Assert(remove, "Failed to remove entity");
                    Debug.WriteLine($"Moving from {mapRegister.ChunkRect} to {newChunkId}");
                    mapRegister.ChunkRect = newChunkId;
                    World.Manager.GetComponentRef<EntityMapRegister>(transform.EntityAddress) = mapRegister;
                    AllEntities.Insert(mapRegister.ChunkRect, transform.Entity, 1);
                }
            }
            mutations.Clear();
            if (movedEntities.Count > 0) {
                foreach (var listener in movedListeners) listener.NotifyMovedEntities(movedEntities);
                movedEntities.Clear();
            }
        }
        private void RegisterEntity(EntityAddress entityAddr) {
            using var marker = new ProfilerMarker("Register Map").Auto();
            var entity = World.Manager.GetEntity(entityAddr);
            var tform = World.Manager.GetComponent<ECTransform>(entityAddr);
            var chunkRect = SimToRect(tform.Position);
            AllEntities.Insert(chunkRect, entity, 1);
            World.Manager.AddComponent<EntityMapRegister>(entity) = new() { ChunkRect = chunkRect };
        }
        private void UnregisterEntity(EntityAddress entityAddr) {
            var entity = World.Manager.GetEntity(entityAddr);
            var chunkId = World.Manager.GetComponent<EntityMapRegister>(entityAddr).ChunkRect;
            AllEntities.Remove(chunkId, entity, 1);
        }

        public void SetLandscape(LandscapeData landscapeData) {
            LandscapeData = landscapeData;
        }

        public void CommitContract(MoveContract contract) { }

        public static Int2 ChunkToSim(Int2 cell) => cell * Separation + Separation / 2;
        public static Int2 SimToChunk(Int2 pnt)  => pnt / Separation;
        public static RectI SimToRect(Int2 pos) => new(SimToChunk(pos), 1);

        public void NotifyCreatedEntities(Span<Entity> entities) {
            var tformLookup = GetComponentLookup<ECTransform>(true);
            foreach (var entity in entities) {
                var tform = tformLookup[entity];
                AllEntities.Insert(SimToRect(tform.Position), entity, 1);
            }
        }
        public void NotifyDestroyedEntities(HashSet<Entity> entities) {
            var tformLookup = GetComponentLookup<ECTransform>(true);
            foreach (var entity in entities) {
                var tform = tformLookup[entity];
                AllEntities.Remove(SimToRect(tform.Position), entity, 1);
            }
        }

        public void RegisterMovedListener(IMovedEntitiesListener listener, bool enable) {
            if (enable) movedListeners.Add(listener);
            else movedListeners.Remove(listener);
        }

        public override int GetHashCode() {
            var hash = AllEntities.GetHashCode();
            foreach (var item in movedEntities) hash += item.GetHashCode();
            return hash;
        }

        public void CopyStateFrom(EntityMapSystem other) {
            AllEntities.CopyStateFrom(other.AllEntities);
            Debug.Assert(movedEntities.Count == 0);
            if (GetHashCode() != other.GetHashCode()) {
                Debug.WriteLine("Hash mismatch");
            }
        }
    }
}
