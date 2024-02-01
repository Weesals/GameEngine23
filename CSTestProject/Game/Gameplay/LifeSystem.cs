using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Weesals.ECS;
using Weesals.Utility;

namespace Weesals.Game {
    public struct CHitPoints {
        public int Current;
    }

    public partial class LifeSystem : SystemBase
        //, IEntityRegisteredListener
        {

        public interface ICreateListener {
            public void NotifyCreatedEntities(Span<Entity> entities);
        }
        public interface IDamageListener {
            public void NotifyDestroyedEntities(HashSet<Entity> entities);
        }
        public interface IDestroyListener {
            public void NotifyDestroyedEntities(HashSet<Entity> entities);
        }

        public struct DamageApplier : IDisposable {
            public ComponentLookup<CHitPoints> HPLookup;
            public HashSet<Entity> DamagedEntities;
            public HashSet<Entity> DeadEntities;

            public struct DamageResult {
                public int DamageAmount;
                public bool IsKilled;
                public static implicit operator int(DamageResult r) => r.DamageAmount;
                public static DamageResult Dead = new DamageResult() { IsKilled = true, };
                public static DamageResult None = new DamageResult() { IsKilled = false, };
            }
            public bool IsEmpty => DamagedEntities.Count == 0 && DeadEntities.Count == 0;

            public void Allocate() {
                DamagedEntities = new(32);
                DeadEntities = new(16);
            }
            public void Dispose() {
                //DamagedEntities.Dispose();
                //DeadEntities.Dispose();
            }

            public void Begin(LifeSystem lifeSystem) {
                HPLookup = lifeSystem.GetComponentLookup<CHitPoints>(false);
            }
            public DamageResult ApplyDamage(Entity target, int damage) {
                ref var hitPoints = ref HPLookup.GetRefRW(target);
                if (hitPoints.Current <= 0) return DamageResult.Dead;
                damage = Math.Min(damage, hitPoints.Current);
                if (damage <= 0) return DamageResult.None;
                hitPoints.Current -= damage;
                DamagedEntities.Add(target);
                if (hitPoints.Current == 0) DeadEntities.Add(target);
                return new DamageResult() { DamageAmount = damage, IsKilled = hitPoints.Current == 0 };
            }
            public void Flush(LifeSystem lifeSystem) {
                if (DeadEntities.Count != 0) {
                    lifeSystem.MarkDeadEntities(DeadEntities);
                    DeadEntities.Clear();
                }
                if (DamagedEntities.Count != 0) {
                    lifeSystem.MarkDamagedEntities(DamagedEntities);
                    DamagedEntities.Clear();
                }
            }
        }

        //public EntityRegistrySystem EntityRegistrySystem { get; private set; }

        private List<Entity> createdEntities = new();
        private HashSet<Entity> deadEntities;
        private List<ICreateListener> createListeners = new();
        private List<IDamageListener> damageListeners = new();
        private List<IDestroyListener> destroyListeners = new();

        protected override void OnCreate() {
            base.OnCreate();
            //EntityRegistrySystem = World.GetOrCreateSystemManaged<EntityRegistrySystem>();
            deadEntities = new(32);
            //EntityRegistrySystem.RegisterCallback(this, true);
        }
        protected override void OnDestroy() {
            //deadEntities.Dispose();
            base.OnDestroy();
        }

        protected override void OnUpdate() { }

        public void RegisterCreateListener(ICreateListener listener, bool enable) {
            if (enable) createListeners.Add(listener);
            else createListeners.Remove(listener);
        }
        public void RegisterDamageListener(IDamageListener listener, bool enable) {
            if (enable) damageListeners.Add(listener);
            else damageListeners.Remove(listener);
        }
        public void RegisterDestroyListener(IDestroyListener listener, bool enable) {
            if (enable) destroyListeners.Add(listener);
            else destroyListeners.Remove(listener);
        }

        public void PurgeDead() {
            if (createdEntities.Count != 0) {
                foreach (var listener in createListeners) {
                    listener.NotifyCreatedEntities(CollectionsMarshal.AsSpan(createdEntities));
                }
                createdEntities.Clear();
            }
            if (deadEntities.Count != 0) {
                foreach (var listener in destroyListeners) {
                    listener.NotifyDestroyedEntities(deadEntities);
                }
                foreach (var entity in deadEntities) {
                    Stage.DeleteEntity(entity);
                }
                deadEntities.Clear();
            }
        }

        public void MarkDamagedEntities(in HashSet<Entity> damagedEntities) {
            foreach (var listener in damageListeners) listener.NotifyDestroyedEntities(damagedEntities);
        }
        public void MarkDeadEntities(in HashSet<Entity> entities) {
            foreach (var entity in entities) deadEntities.Add(entity);
        }
        public void MarkDeadEntities(Span<Entity> entities) {
            foreach (var entity in entities) deadEntities.Add(entity);
        }
        public void KillEntity(Entity entity) {
            deadEntities.Add(entity);
        }

        public void NotifyEntityRegistered(Span<Entity> entities, bool enable) {
            if (enable) {
                createdEntities.AddRange(entities);
            } else {
                Debug.Fail("Error");
            }
        }

    }
}
