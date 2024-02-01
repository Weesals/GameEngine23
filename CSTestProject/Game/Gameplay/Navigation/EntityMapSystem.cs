using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Weesals.ECS;
using Weesals.Engine;
using Weesals.Landscape;
using Weesals.Utility;

namespace Weesals.Game {
    //[UpdateOrderId(500)]
    public partial class EntityMapSystem : SystemBase
        , LifeSystem.ICreateListener
        , LifeSystem.IDestroyListener
        {

        public const int Separation = 8000;

        public interface IMovedEntitiesListener {
            void NotifyMovedEntities(HashSet<Entity> entities);
        }

        public LifeSystem LifeSystem { get; private set; }
        public LandscapeData LandscapeData { get; private set; }

        public struct EntityMap {
            private MultiHashMap<uint, Entity> map;

            public void Allocate() {
                map = new(1024);
            }
            public void Dispose() {
                map.Dispose();
            }

            public void InsertEntity(Int2 pos, Entity entity) {
                uint chunkId = SimToChunkId(pos);
                map.Add(chunkId, entity);
            }
            public void RemoveEntity(Int2 pos, Entity entity) {
                uint chunkId = SimToChunkId(pos);
                map.Remove(chunkId, entity);
            }

            public void MoveEntity(Int2 opos, Int2 npos, Entity entity) {
                RemoveEntity(opos, entity);
                InsertEntity(npos, entity);
            }

            public void RemoveEntityRaw(uint id, Entity entity) {
                map.Remove(id, entity);
            }

            public MultiHashMap<uint, Entity>.Enumerator GetBundle(Int2 chunk) {
                return map.GetValuesForKey(ChunkToId(chunk));
            }
            public MultiHashMap<uint, Entity>.KeyValueEnumerator GetEnumerator() {
                return map.GetEnumerator();
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
                    return Int2.Dot(from, from);
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
                private MultiHashMap<uint, Entity>.Enumerator entityIt;
                public Entity Current => entityIt.Current;
                public NearestIterator(Int2 pnt, int rangeDst2) {
                    Position = pnt;
                    entityIt = default;
                    nearestDst2 = rangeDst2;
                    SpiralIt = new SpiralIterator(SimToChunk(pnt));
                    SpiralIt.Spiral.SetIterationCorner(pnt - (SpiralIt.Spiral.Current * Separation + Separation / 2));
                }
                public bool MoveNext1(in EntityMap map) {
                    if (!SpiralIt.MoveNext() || SpiralIt.GetDistanceSq(Position) >= nearestDst2) return false;
                    entityIt = map.GetBundle(SpiralIt.Current);
                    return true;
                }
                public bool MoveNext2() {
                    return entityIt.MoveNext();
                }
                public bool MarkNearest(int dst2) {
                    if (dst2 >= nearestDst2) return false;
                    nearestDst2 = dst2;
                    return true;
                }
            }
            public NearestIterator CreateNearestIterator(Int2 pnt, int range2) {
                return new NearestIterator(pnt, range2);
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
        }

        public struct MoveContract : IDisposable {
            private PooledHashMap<Entity, uint> previousChunkId;
            public MoveContract(PooledHashMap<Entity, uint> prev) {
                previousChunkId = prev;
            }
            public void Dispose() {
                previousChunkId.Dispose();
            }
            public void Clear() {
                previousChunkId.Clear();
            }
            public void MoveEntity(Entity entity, ref ECTransform tform, Int2 newPos) {
                var oldHash = SimToChunkId(tform.Position);
                var newHash = SimToChunkId(newPos);
                tform.Position = newPos;
                if (oldHash == newHash) return;
                if (previousChunkId.ContainsKey(entity)) return;
                previousChunkId[entity] = oldHash;
            }
            public PooledHashMap<Entity, uint>.Enumerator GetEnumerator() { return previousChunkId.GetEnumerator(); }
        }

        public EntityMap AllEntities;
        private HashSet<Entity> movedEntities;
        private List<IMovedEntitiesListener> movedListeners = new();

        public MoveContract AllocateContract() {
            return new MoveContract(new PooledHashMap<Entity, uint>(32));
        }

        protected override void OnCreate() {
            base.OnCreate();
            LifeSystem = World.GetOrCreateSystem<LifeSystem>();
            AllEntities.Allocate();
            movedEntities = new(256);
            LifeSystem.RegisterCreateListener(this, true);
            LifeSystem.RegisterDestroyListener(this, true);
        }
        protected override void OnDestroy() {
            //movedEntities.Dispose();
            LifeSystem.RegisterDestroyListener(this, false);
            LifeSystem.RegisterCreateListener(this, false);
            AllEntities.Dispose();
            base.OnDestroy();
        }
        protected override void OnUpdate() {
            if (movedEntities.Count > 0) {
                foreach (var listener in movedListeners) listener.NotifyMovedEntities(movedEntities);
                movedEntities.Clear();
            }
            ValidateEntities();
        }
        private bool ValidateEntities() {
            foreach (var kv in AllEntities) {
                var cell = kv.Key;
                var entity = kv.Value;
                if (!World.IsValid(entity)) {
                    Debug.WriteLine("Entity was not removed!");
                    return false;
                }
                var tform = World.GetComponent<ECTransform>(entity);
                var cid = SimToChunkId(tform.Position);
                if (cid != cell) {
                    Debug.WriteLine("Invalid cell id for entity");
                    return false;
                }
            }
            return true;
        }
        public void SetLandscape(LandscapeData landscapeData) {
            LandscapeData = landscapeData;
        }

        public void CommitContract(MoveContract contract) {
            var tformLookup = GetComponentLookup<ECTransform>(true);
            for (var it = contract.GetEnumerator(); it.MoveNext();) {
                var kv = it.Current;
                AllEntities.RemoveEntityRaw(kv.Value, kv.Key);
                var tform = tformLookup[kv.Key];
                AllEntities.InsertEntity(tform.Position, kv.Key);
                movedEntities.Add(kv.Key);
            }
        }

        public static uint ChunkToId(Int2 pos) {
            return (uint)pos.X + (uint)pos.Y * 31513;
        }
        public static Int2 SimToChunk(Int2 pos) {
            return new Int2(
                (pos.X / Separation),
                (pos.Y / Separation)
            );
        }
        public static uint SimToChunkId(Int2 pos) {
            return ChunkToId(SimToChunk(pos));
        }

        public void NotifyCreatedEntities(Span<Entity> entities) {
            var tformLookup = GetComponentLookup<ECTransform>(true);
            foreach (var entity in entities) {
                var tform = tformLookup[entity];
                AllEntities.InsertEntity(tform.Position, entity);
            }
        }
        public void NotifyDestroyedEntities(HashSet<Entity> entities) {
            var tformLookup = GetComponentLookup<ECTransform>(true);
            foreach (var entity in entities) {
                var tform = tformLookup[entity];
                AllEntities.RemoveEntity(tform.Position, entity);
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
            if (!other.ValidateEntities()) {
                Debug.WriteLine("EERREORER: OTHER has invalid map!");
            }
            AllEntities.CopyStateFrom(other.AllEntities);
            Debug.Assert(movedEntities.Count == 0);
            if (!ValidateEntities()) {
                Debug.WriteLine("Validation failed");
                ValidateEntities();
            }
            if (GetHashCode() != other.GetHashCode()) {
                Debug.WriteLine("Hash mismatch");
            }
        }

    }
}
