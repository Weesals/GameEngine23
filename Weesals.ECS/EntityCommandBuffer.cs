using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Weesals.ECS {
    public class EntityCommandBuffer {

        public readonly Stage Stage;
        public StageContext Context => Stage.Context;

        public class Boxed {
            public Array Items;
        }
        public class Boxed<T> : Boxed {
            public T[] Values {
                get => (T[])base.Items;
                set => base.Items = value;
            }
            public Boxed() { Items = Array.Empty<T>(); }
        }

        public struct EntityMutation {
            public BitField SetTypes;
            public BitField RemoveTypes;
            public BitField SetSparseTypes;
            public BitField RemoveSparseTypes;
            public Entity Entity;
        }

        private EntityMutation[] mutations = Array.Empty<EntityMutation>();
        private Dictionary<Entity, int> entityIndexLookup = new();
        private Boxed[] values = Array.Empty<Boxed>();
        private Boxed[] sparseValues = Array.Empty<Boxed>();

        public struct ItemMutation {
            public BitField.Generator SetTypes;
            public BitField.Generator RemoveTypes;
            public BitField.Generator SetSparseTypes;
            public BitField.Generator RemoveSparseTypes;
            public Entity Entity;
            public int EntityIndex;
        }
        private ItemMutation active;

        public EntityCommandBuffer(Stage stage) {
            Stage = stage;
            active = new() {
                SetTypes = new(),
                RemoveTypes = new(),
                SetSparseTypes = new(),
                RemoveSparseTypes = new(),
                EntityIndex = -1,
            };
        }

        public void Reset() {
            Debug.Assert(!active.Entity.IsValid);
            entityIndexLookup.Clear();
        }
        public void Commit() {
            PushActiveEntity();
            for (int i = 0; i < entityIndexLookup.Count; i++) {
                ref var mutation = ref mutations[i];
                if ((mutation.Entity.Index & 0x80000000) != 0 && mutation.Entity.Version == 0xffffffff) {
                    mutation.Entity = Stage.CreateEntity();
                }
                if ((int)mutation.Entity.Index < 0) {
                    mutation.Entity.Index = ~mutation.Entity.Index;
                    Stage.DeleteEntity(mutation.Entity);
                    continue;
                }
                active.SetTypes.Clear();

                // Archetype components
                var entityAddr = Stage.RequireEntityAddress(mutation.Entity);
                var archetype = Stage.GetArchetype(entityAddr.ArchetypeId);
                active.SetTypes.Append(archetype.TypeMask);
                active.SetTypes.Remove(mutation.RemoveTypes);
                active.SetTypes.Append(mutation.SetTypes);
                var typeMask = Stage.Context.RequireTypeMask(active.SetTypes);
                if (!typeMask.Equals(archetype.TypeMask)) {
                    var newTableId = Stage.RequireArchetypeIndex(typeMask);
                    var mover = Stage.BeginMoveEntity(mutation.Entity, newTableId);
                    //archetype = Stage.GetArchetype(entityAddr.ArchetypeId);
                    foreach (var typeId in mutation.SetTypes) {
                        ref var column = ref mover.GetColumn(new TypeId(typeId));
                        //var columnId = archetype.RequireTypeIndex(new TypeId(typeId), Context);
                        //ref var column = ref archetype.Columns[columnId];
                        column.CopyValue(mover.To.Row, values[typeId].Items, i);
                        column.NotifyMutation(mover.To.Row);
                    }
                    entityAddr = mover.Commit();
                }

                // Sparse components
                foreach (var typeIndex in mutation.SetSparseTypes) {
                    var columnId = archetype.RequireSparseComponent(TypeId.MakeSparse(typeIndex), Context);
                    var row = archetype.RequireSparseIndex(columnId, entityAddr.Row);
                    ref var column = ref archetype.Columns[columnId];
                    column.CopyValue(row, sparseValues[typeIndex].Items, i);
                    column.NotifyMutation(entityAddr.Row);
                }
                foreach (var typeIndex in mutation.RemoveSparseTypes) {
                    var columnId = archetype.RequireSparseComponent(TypeId.MakeSparse(typeIndex), Context);
                    archetype.ClearSparseIndex(columnId, entityAddr.Row);
                }
            }
            Reset();
        }

        private void SetActiveEntity(Entity entity) {
            if (entity == active.Entity) return;
            PushActiveEntity();
            if (!entityIndexLookup.TryGetValue(entity, out active.EntityIndex)) {
                active.EntityIndex = entityIndexLookup.Count;
                entityIndexLookup.Add(entity, active.EntityIndex);
                if (active.EntityIndex >= mutations.Length)
                    Array.Resize(ref mutations, (int)BitOperations.RoundUpToPowerOf2((uint)active.EntityIndex + 4));
                mutations[active.EntityIndex].Entity = entity;
            }
            active.Entity = entity;
            active.SetTypes.Clear();
            active.RemoveTypes.Clear();
            active.SetSparseTypes.Clear();
            active.RemoveSparseTypes.Clear();
            active.SetTypes.Append(mutations[active.EntityIndex].SetTypes);
            active.RemoveTypes.Append(mutations[active.EntityIndex].RemoveTypes);
            active.SetSparseTypes.Append(mutations[active.EntityIndex].SetSparseTypes);
            active.RemoveSparseTypes.Append(mutations[active.EntityIndex].RemoveSparseTypes);
        }
        private void PushActiveEntity() {
            if (active.EntityIndex == -1) return;
            ref var mutation = ref mutations[active.EntityIndex];
            mutation.SetTypes = Stage.Context.RequireTypeMask(active.SetTypes);
            mutation.RemoveTypes = Stage.Context.RequireTypeMask(active.RemoveTypes);
            mutation.SetSparseTypes = Stage.Context.RequireTypeMask(active.SetSparseTypes);
            mutation.RemoveSparseTypes = Stage.Context.RequireTypeMask(active.RemoveSparseTypes);
            active.Entity = default;
            active.EntityIndex = -1;
        }

        public Entity CreateEntity() {
            return Stage.CreateEntity();
        }
        public Entity CreateDeferredEntity() {
            var entity = new Entity() {
                Index = (uint)entityIndexLookup.Count | 0x80000000,
                Version = 0xffffffff,
            };
            SetActiveEntity(entity);
            return entity;
        }
        public void DeleteEntity(Entity entity) {
            SetActiveEntity(entity);
            ref var mutation = ref mutations[active.EntityIndex];
            mutation.Entity.Index = ~mutation.Entity.Index;
        }

        public ref T AddComponent<T>(Entity entity) {
            SetActiveEntity(entity);

            var typeId = Stage.Context.RequireComponentTypeId<T>();
            (typeId.IsSparse ? active.SetSparseTypes : active.SetTypes).Add(typeId);
            var typeIndex = typeId.Index;
            ref var typeValues = ref (typeId.IsSparse ? ref sparseValues : ref values);
            if (typeIndex >= typeValues.Length) Array.Resize(ref typeValues, typeIndex + 4);
            if (typeValues[typeIndex] == null) typeValues[typeIndex] = new Boxed<T>();
            var boxed = ((Boxed<T>)typeValues[typeIndex]);
            if (active.EntityIndex >= boxed.Values.Length) {
                var boxedValues = boxed.Values;
                Array.Resize<T>(ref boxedValues, mutations.Length);
                boxed.Values = boxedValues;
            }
            return ref boxed.Values[active.EntityIndex];
        }
        public ref T SetComponent<T>(Entity entity, T value) {
            ref var cmp = ref AddComponent<T>(entity);
            cmp = value;
            return ref cmp!;
        }
        public void RemoveComponent<T>(Entity entity) {
            SetActiveEntity(entity);

            var typeId = Stage.Context.RequireComponentTypeId<T>();
            if (typeId.IsSparse) {
                active.SetSparseTypes.Remove(typeId);
                active.RemoveSparseTypes.Add(typeId);
            } else {
                active.SetTypes.Remove(typeId);
                active.RemoveTypes.Add(typeId);
            }
        }
    }
}
