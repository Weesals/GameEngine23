using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Weesals.ECS {
    public struct EntityPrefab {
        public readonly int Index;
        public readonly bool IsValid => Index > 0;
        public EntityPrefab(int index) { Index = index; }
        public int CompareTo(EntityPrefab other) { return Index.CompareTo(other.Index); }
        public bool Equals(EntityPrefab other) { return Index == other.Index; }
        public override bool Equals(object? obj) { throw new NotImplementedException(); }
        public override int GetHashCode() { return (int)Index; }
        public override string ToString() { return IsValid ? $"Entity #{Index}" : "None"; }
        public static bool operator ==(EntityPrefab left, EntityPrefab right) { return left.Equals(right); }
        public static bool operator !=(EntityPrefab left, EntityPrefab right) { return !(left == right); }
        public static readonly EntityPrefab Null = new();
    }
    public class SparseWorld {
        public StageContext Context;
        public abstract class SparseColumnBase {
            public int Count;
            public DynamicBitField2 Sparse = new();
            public abstract Array RawArray { get; }
        }
        public class SparseColumn<TComponent> : SparseColumnBase {
            public TComponent?[] Components = Array.Empty<TComponent>();
            public override Array RawArray => Components;
        }
        private int[] typeToColumn = Array.Empty<int>();
        private SparseColumnBase[] columns = Array.Empty<SparseColumnBase>();
        private int columnCount = 0;
        private int entityCount = 0;

        public SparseWorld(StageContext context) {
            Context = context;
        }
        public SparseColumn<TComponent> RequireColumn<TComponent>() {
            var typeId = Context.RequireComponentTypeId<TComponent>().Packed;
            Debug.Assert(typeId < 4096, "Prefabs cannot have sparse components");
            if (typeId >= typeToColumn.Length) {
                int oldSize = typeToColumn.Length;
                Array.Resize(ref typeToColumn, (int)BitOperations.RoundUpToPowerOf2((uint)typeId + 4));
                typeToColumn.AsSpan(oldSize).Fill(-1);
            }
            if (typeToColumn[typeId] == -1) {
                typeToColumn[typeId] = columnCount;
                if (columnCount >= columns.Length)
                    Array.Resize(ref columns, (int)BitOperations.RoundUpToPowerOf2((uint)columnCount + 4));
                columns[columnCount] = new SparseColumn<TComponent>();
                ++columnCount;
            }
            return (SparseColumn<TComponent>)columns[typeToColumn[typeId]];
        }
        public bool GetHasComponent<TComponent>(int entity) {
            var typeId = Context.RequireComponentTypeId<TComponent>();
            return GetHasComponent(typeId, entity);
        }
        public bool GetHasComponent(TypeId typeId, int entity) {
            Debug.Assert(typeId.IsBasic, "Prefabs cannot have sparse components");
            if (typeId >= typeToColumn.Length) return false;
            var columnId = typeToColumn[typeId];
            if (columnId >= columnCount) return false;
            var column = columns[columnId];
            if (column == null) return false;
            if (!column.Sparse.Contains(entity)) return false;
            return true;
        }
        public Array GetRawComponent(TypeId typeId, int entity, out int row) {
            var column = columns[typeToColumn[typeId]];
            row = column.Sparse.GetBitIndex(entity);
            return column.RawArray;
        }
        public int CreateEntity() {
            return entityCount++;
        }
        public ref TComponent? AddComponent<TComponent>(int entity) {
            var column = RequireColumn<TComponent>();
            Debug.Assert(!column.Sparse.Contains(entity));
            column.Sparse.Add(entity);
            var index = column.Sparse.GetBitIndex(entity);
            if (index >= column.Components.Length)
                Array.Resize(ref column.Components, (int)BitOperations.RoundUpToPowerOf2((uint)index + 4));
            Array.Copy(column.Components, index, column.Components, index + 1, column.Count - index);
            ++column.Count;
            column.Components[index] = default;
            return ref column.Components[index];
        }
        public ref TComponent? GetComponent<TComponent>(int entity) {
            var column = RequireColumn<TComponent>();
            Debug.Assert(column.Sparse.Contains(entity));
            var index = column.Sparse.GetBitIndex(entity);
            return ref column.Components[index];
        }
    }
    public class PrefabRegistry {

        public readonly SparseWorld World;

        public struct Prefab {
            public readonly string Name;
            public BitField TypeBitField;
            public Prefab(string name) { Name = name; }
        }

        public PrefabRegistry(StageContext context) {
            World = new(context);
        }

        public struct PrefabBuilder {
            public readonly SparseWorld world;
            public readonly int index;
            private static BitField.Generator bitGenerator = new();
            public PrefabBuilder(SparseWorld _world, int _index) {
                world = _world;
                index = _index;
                Debug.Assert(bitGenerator.IsEmpty);
            }
            public PrefabBuilder AddComponent<TComponent>(TComponent data) {
                if (!ComponentType<TComponent>.IsNoClone)
                    bitGenerator.Add(world.Context.RequireComponentTypeId<TComponent>());
                world.AddComponent<TComponent>(index) = data;
                return this;
            }
            public EntityPrefab Build() {
                ref var prefab = ref world.GetComponent<Prefab>(index);
                prefab.TypeBitField = world.Context.RequireTypeMask(bitGenerator);
                bitGenerator.Clear();
                return new EntityPrefab(index);
            }
        }
        public PrefabBuilder CreatePrefab(string name) {
            var index = World.CreateEntity();
            World.AddComponent<Prefab>(index) = new Prefab(name);
            return new PrefabBuilder(World, index);
        }

        public Stage.EntityMover BeginInstantiate(World world, EntityPrefab prefab) {
            var prefabData = World.GetComponent<Prefab>(prefab.Index);
            var entity = world.Stage.CreateEntity(prefabData.Name);
            var archetypeId = world.Stage.RequireArchetypeIndex(prefabData.TypeBitField);
            using var mover = world.Stage.BeginMoveEntity(entity, archetypeId);
            //var archetype = world.Stage.GetArchetype(archetypeId);
            foreach (var typeId in prefabData.TypeBitField) {
                //var columnId = archetype.RequireTypeIndex(new TypeId(typeId), world.Context);
                //ref var column = ref archetype.Columns[columnId];
                ref var column = ref mover.GetColumn(new TypeId(typeId));
                var array = World.GetRawComponent(new TypeId(typeId), prefab.Index, out var row);
                column.CopyValue(mover.To.Row, array, row);
            }
            return mover;
        }
        public Entity Instantiate(World world, EntityPrefab prefab) {
            var mover = BeginInstantiate(world, prefab);
            mover.Commit();
            return mover.Entity;
        }
        public TComponent GetComponent<TComponent>(EntityPrefab prefab) {
            return World.GetComponent<TComponent>(prefab.Index)!;
        }

        public Entity Instantiate(EntityCommandBuffer command, EntityPrefab prefab) {
            var entity = command.CreateDeferredEntity();
            var prefabData = World.GetComponent<Prefab>(prefab.Index);
            foreach (var typeId in prefabData.TypeBitField) {
                var array = World.GetRawComponent(new TypeId(typeId), prefab.Index, out var row);
                var cmpRef = command.AddComponent(entity, new TypeId(typeId));
                cmpRef.CopyFrom(array, row);
            }
            return entity;
        }
    }
}
