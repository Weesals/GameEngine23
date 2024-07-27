using System;
using System.Collections;
using System.Diagnostics;
using System.Numerics;

namespace Weesals.ECS {
    public struct ComponentAccessor {
        public readonly EntityManager Manager;
        public readonly ArchetypeId ArchetypeId;
        public bool IsValid => Manager != null;
        public ref Archetype Archetype => ref Manager.GetArchetype(ArchetypeId);
        public Span<Entity> Entities => Archetype.GetEntities(ref Manager.ColumnStorage);
        public int EntityCount => Archetype.EntityCount;
        public ComponentAccessor(EntityManager manager, ArchetypeId archetypeId) {
            Manager = manager;
            ArchetypeId = archetypeId;
        }
        public ref T GetValueRW<T>(int columnIndex, int row, int entityArchetypeRow) {
            return ref Archetype.GetValueRW<T>(ref Manager.ColumnStorage, columnIndex, row, entityArchetypeRow);
        }
        public ref readonly T GetValueRO<T>(int columnIndex, int row) {
            return ref Archetype.GetValueRO<T>(ref Manager.ColumnStorage, columnIndex, row);
        }
        public int RequireSparseIndex(int column1, int row) {
            return Archetype.RequireSparseIndex(ref Manager.ColumnStorage, column1, row);
        }
    }
    public readonly struct EntityComponentAccessor<C1> {
        private readonly ComponentAccessor accessor;
        private readonly int row, column1;
        public readonly int Row => row;
        public ref Archetype Archetype => ref accessor.Archetype;
        public readonly Entity Entity => accessor.Entities[row];
        public readonly ref readonly C1 Component1 => ref accessor.GetValueRO<C1>(column1, GetDenseRow1());
        public readonly ref C1 Component1Ref => ref accessor.GetValueRW<C1>(column1, GetDenseRow1(), row);
        public EntityComponentAccessor(ComponentAccessor _accessor, int _row, int _column1) {
            accessor = _accessor;
            row = _row;
            column1 = _column1;
        }
        private int GetDenseRow1() { return ComponentType<C1>.IsSparse ? accessor.RequireSparseIndex(column1, row) : row; }
        public void Set(C1 value) => Component1Ref = value;
        public static implicit operator Entity(EntityComponentAccessor<C1> accessor) => accessor.Entity;
        public static implicit operator C1(EntityComponentAccessor<C1> accessor) => accessor.Component1;
    }
    public readonly struct EntityComponentAccessor<C1, C2> {
        private readonly ComponentAccessor accessor;
        private readonly int row, column1, column2;
        public readonly int Row => row;
        public ref Archetype Archetype => ref accessor.Archetype;
        public readonly Entity Entity => accessor.Entities[row];
        public readonly ref readonly C1 Component1 => ref accessor.GetValueRO<C1>(column1, GetDenseRow1());
        public readonly ref readonly C2 Component2 => ref accessor.GetValueRO<C2>(column2, GetDenseRow2());
        public readonly ref C1 Component1Ref => ref accessor.GetValueRW<C1>(column1, GetDenseRow1(), row);
        public readonly ref C2 Component2Ref => ref accessor.GetValueRW<C2>(column2, GetDenseRow2(), row);
        public EntityComponentAccessor(ComponentAccessor _accessor, int _row, int _column1, int _column2) {
            accessor = _accessor;
            row = _row;
            column1 = _column1; column2 = _column2;
        }
        private int GetDenseRow1() { return ComponentType<C1>.IsSparse ? accessor.RequireSparseIndex(column1, row) : row; }
        private int GetDenseRow2() { return ComponentType<C2>.IsSparse ? accessor.RequireSparseIndex(column2, row) : row; }
        public void Set(C1 value) => Component1Ref = value;
        public void Set(C2 value) => Component2Ref = value;
        public static implicit operator Entity(EntityComponentAccessor<C1, C2> accessor) => accessor.Entity;
        public static implicit operator C1(EntityComponentAccessor<C1, C2> accessor) => accessor.Component1;
        public static implicit operator C2(EntityComponentAccessor<C1, C2> accessor) => accessor.Component2;
    }
    public readonly struct EntityComponentAccessor<C1, C2, C3> {
        private readonly ComponentAccessor accessor;
        private readonly int row, column1, column2, column3;
        public readonly int Row => row;
        public ref Archetype Archetype => ref accessor.Archetype;
        public readonly Entity Entity => accessor.Entities[row];
        public readonly ref readonly C1 Component1 => ref accessor.GetValueRO<C1>(column1, GetDenseRow1());
        public readonly ref readonly C2 Component2 => ref accessor.GetValueRO<C2>(column2, GetDenseRow2());
        public readonly ref readonly C3 Component3 => ref accessor.GetValueRO<C3>(column3, GetDenseRow3());
        public readonly ref C1 Component1Ref => ref accessor.GetValueRW<C1>(column1, GetDenseRow1(), row);
        public readonly ref C2 Component2Ref => ref accessor.GetValueRW<C2>(column2, GetDenseRow2(), row);
        public readonly ref C3 Component3Ref => ref accessor.GetValueRW<C3>(column3, GetDenseRow3(), row);
        public EntityComponentAccessor(ComponentAccessor _accessor, int _row, int _column1, int _column2, int _column3) {
            accessor = _accessor; row = _row;
            column1 = _column1; column2 = _column2; column3 = _column3;
        }
        private int GetDenseRow1() { return ComponentType<C1>.IsSparse ? accessor.RequireSparseIndex(column1, row) : row; }
        private int GetDenseRow2() { return ComponentType<C2>.IsSparse ? accessor.RequireSparseIndex(column2, row) : row; }
        private int GetDenseRow3() { return ComponentType<C3>.IsSparse ? accessor.RequireSparseIndex(column3, row) : row; }
        public void Set(C1 value) => Component1Ref = value;
        public void Set(C2 value) => Component2Ref = value;
        public void Set(C3 value) => Component3Ref = value;
        public static implicit operator Entity(EntityComponentAccessor<C1, C2, C3> accessor) => accessor.Entity;
        public static implicit operator C1(EntityComponentAccessor<C1, C2, C3> accessor) => accessor.Component1;
        public static implicit operator C2(EntityComponentAccessor<C1, C2, C3> accessor) => accessor.Component2;
        public static implicit operator C3(EntityComponentAccessor<C1, C2, C3> accessor) => accessor.Component3;
    }

    public readonly struct TableComponentAccessor<C1> : IEnumerable<EntityComponentAccessor<C1>> {
        public readonly ComponentAccessor Accessor;
        private readonly int column1;
        public TableComponentAccessor(ComponentAccessor accessor, TypeId typeId) {
            Accessor = accessor;
            column1 = Accessor.Archetype.GetColumnId(typeId);
        }
        public struct Enumerator : IEnumerator<EntityComponentAccessor<C1>> {
            public readonly TableComponentAccessor<C1> TableAccessor;
            private int row;
            public bool IsValid => TableAccessor.Accessor.IsValid;
            public int Row => row;
            public EntityComponentAccessor<C1> Current => new(TableAccessor.Accessor, row, TableAccessor.column1);
            object IEnumerator.Current => Current;
            public Enumerator(TableComponentAccessor<C1> accessor) { TableAccessor = accessor; row = -1; }
            public void Dispose() { }
            public void Reset() { row = -1; }
            public bool MoveNext() { return ++row < TableAccessor.Accessor.EntityCount; }
            public bool MoveNextFiltered(Query query) {
                row = query.GetNextSparseRow(ref TableAccessor.Accessor.Manager.ColumnStorage, ref TableAccessor.Accessor.Archetype, row);
                Debug.Assert(row < TableAccessor.Accessor.EntityCount);
                return row >= 0;
            }
        }
        public Enumerator GetEnumerator() => new(this);
        IEnumerator<EntityComponentAccessor<C1>> IEnumerable<EntityComponentAccessor<C1>>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    public readonly struct TableComponentAccessor<C1, C2> : IEnumerable<EntityComponentAccessor<C1, C2>> {
        public readonly ComponentAccessor Accessor;
        private readonly int column1, column2;
        public TableComponentAccessor(ComponentAccessor accessor, TypeId typeId1, TypeId typeId2) {
            Accessor = accessor;
            column1 = Accessor.Archetype.GetColumnId(typeId1);
            column2 = Accessor.Archetype.GetColumnId(typeId2);
        }
        public struct Enumerator : IEnumerator<EntityComponentAccessor<C1, C2>> {
            public readonly TableComponentAccessor<C1, C2> TableAccessor;
            private int row;
            public bool IsValid => TableAccessor.Accessor.IsValid;
            public EntityComponentAccessor<C1, C2> Current => new(TableAccessor.Accessor, row, TableAccessor.column1, TableAccessor.column2);
            object IEnumerator.Current => Current;
            public Enumerator(TableComponentAccessor<C1, C2> accessor) { TableAccessor = accessor; row = -1; }
            public void Dispose() { }
            public void Reset() { row = -1; }
            public bool MoveNext() { return ++row < TableAccessor.Accessor.EntityCount; }
            public bool MoveNextFiltered(Query query) {
                row = query.GetNextSparseRow(ref TableAccessor.Accessor.Manager.ColumnStorage, ref TableAccessor.Accessor.Archetype, row);
                return row >= 0;
            }
        }
        public Enumerator GetEnumerator() => new(this);
        IEnumerator<EntityComponentAccessor<C1, C2>> IEnumerable<EntityComponentAccessor<C1, C2>>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    public readonly struct TableComponentAccessor<C1, C2, C3> : IEnumerable<EntityComponentAccessor<C1, C2, C3>> {
        public readonly ComponentAccessor Accessor;
        private readonly int column1, column2, column3;
        public TableComponentAccessor(ComponentAccessor accessor, TypeId typeId1, TypeId typeId2, TypeId typeId3) {
            Accessor = accessor;
            column1 = Accessor.Archetype.GetColumnId(typeId1);
            column2 = Accessor.Archetype.GetColumnId(typeId2);
            column3 = Accessor.Archetype.GetColumnId(typeId3);
        }
        public struct Enumerator : IEnumerator<EntityComponentAccessor<C1, C2, C3>> {
            public readonly TableComponentAccessor<C1, C2, C3> TableAccessor;
            private int row;
            public bool IsValid => TableAccessor.Accessor.IsValid;
            public EntityComponentAccessor<C1, C2, C3> Current => new(TableAccessor.Accessor, row, TableAccessor.column1, TableAccessor.column2, TableAccessor.column3);
            object IEnumerator.Current => Current;
            public Enumerator(TableComponentAccessor<C1, C2, C3> accessor) { TableAccessor = accessor; row = -1; }
            public void Dispose() { }
            public void Reset() { row = -1; }
            public bool MoveNext() { return ++row < TableAccessor.Accessor.EntityCount; }
            public bool MoveNextFiltered(Query query) {
                row = query.GetNextSparseRow(ref TableAccessor.Accessor.Manager.ColumnStorage, ref TableAccessor.Accessor.Archetype, row);
                return row >= 0;
            }
        }
        public Enumerator GetEnumerator() => new(this);
        IEnumerator<EntityComponentAccessor<C1, C2, C3>> IEnumerable<EntityComponentAccessor<C1, C2, C3>>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public struct TypedQueryIterator<C1> : IEnumerator<EntityComponentAccessor<C1>>, IEnumerable<EntityComponentAccessor<C1>> {
        public readonly TypeId C1TypeId;
        private EntityManager.ArchetypeQueryEnumerator queryEnumerator;
        public EntityManager Manager => queryEnumerator.Manager;
        public TableComponentAccessor<C1>.Enumerator TableEnumerator;
        public EntityComponentAccessor<C1> Current => TableEnumerator.Current;
        object IEnumerator.Current => Current;
        public TypedQueryIterator(EntityManager.ArchetypeQueryEnumerator tableEn) {
            queryEnumerator = tableEn;
            C1TypeId = Manager.Context.RequireComponentTypeId<C1>();
        }
        public void Reset() { queryEnumerator.Reset(); }
        public void Dispose() { }
        public bool MoveNext() {
            while (true) {
                if (TableEnumerator.IsValid && TableEnumerator.MoveNextFiltered(queryEnumerator.Query)) return true;
                if (!queryEnumerator.MoveNext()) return false;
                var tableAccessor = new TableComponentAccessor<C1>(queryEnumerator.CreateAccessor(), C1TypeId);
                TableEnumerator = tableAccessor.GetEnumerator();
            }
        }
        public TypedQueryIterator<C1> GetEnumerator() { return this; }
        IEnumerator<EntityComponentAccessor<C1>> IEnumerable<EntityComponentAccessor<C1>>.GetEnumerator() => this;
        IEnumerator IEnumerable.GetEnumerator() => this;
    }
    public struct TypedQueryIterator<C1, C2> : IEnumerator<EntityComponentAccessor<C1, C2>>, IEnumerable<EntityComponentAccessor<C1, C2>> {
        public readonly TypeId C1TypeId, C2TypeId;
        private EntityManager.ArchetypeQueryEnumerator queryEnumerator;
        public EntityManager Manager => queryEnumerator.Manager;
        public TableComponentAccessor<C1, C2>.Enumerator TableEnumerator;
        public EntityComponentAccessor<C1, C2> Current => TableEnumerator.Current;
        object IEnumerator.Current => Current;
        public TypedQueryIterator(EntityManager.ArchetypeQueryEnumerator tableEn) {
            queryEnumerator = tableEn;
            C1TypeId = Manager.Context.RequireComponentTypeId<C1>();
            C2TypeId = Manager.Context.RequireComponentTypeId<C2>();
        }
        public void Reset() { queryEnumerator.Reset(); }
        public void Dispose() { }
        public bool MoveNext() {
            while (true) {
                if (TableEnumerator.IsValid && TableEnumerator.MoveNextFiltered(queryEnumerator.Query)) return true;
                if (!queryEnumerator.MoveNext()) return false;
                var tableAccessor = new TableComponentAccessor<C1, C2>(queryEnumerator.CreateAccessor(), C1TypeId, C2TypeId);
                TableEnumerator = tableAccessor.GetEnumerator();
            }
        }
        public TypedQueryIterator<C1, C2> GetEnumerator() { return this; }
        IEnumerator<EntityComponentAccessor<C1, C2>> IEnumerable<EntityComponentAccessor<C1, C2>>.GetEnumerator() => this;
        IEnumerator IEnumerable.GetEnumerator() => this;
    }
    public struct TypedQueryIterator<C1, C2, C3> : IEnumerator<EntityComponentAccessor<C1, C2, C3>>, IEnumerable<EntityComponentAccessor<C1, C2, C3>> {
        public readonly TypeId C1TypeId, C2TypeId, C3TypeId;
        private EntityManager.ArchetypeQueryEnumerator queryEnumerator;
        public EntityManager Manager => queryEnumerator.Manager;
        public TableComponentAccessor<C1, C2, C3>.Enumerator TableEnumerator;
        public EntityComponentAccessor<C1, C2, C3> Current => TableEnumerator.Current;
        object IEnumerator.Current => Current;
        public TypedQueryIterator(EntityManager.ArchetypeQueryEnumerator tableEn) {
            queryEnumerator = tableEn;
            C1TypeId = Manager.Context.RequireComponentTypeId<C1>();
            C2TypeId = Manager.Context.RequireComponentTypeId<C2>();
            C3TypeId = Manager.Context.RequireComponentTypeId<C3>();
        }
        public void Reset() { queryEnumerator.Reset(); }
        public void Dispose() { }
        public bool MoveNext() {
            while (true) {
                if (TableEnumerator.IsValid && TableEnumerator.MoveNextFiltered(queryEnumerator.Query)) return true;
                if (!queryEnumerator.MoveNext()) return false;
                var tableAccessor = new TableComponentAccessor<C1, C2, C3>(queryEnumerator.CreateAccessor(), C1TypeId, C2TypeId, C3TypeId);
                TableEnumerator = tableAccessor.GetEnumerator();
            }
        }
        public TypedQueryIterator<C1, C2, C3> GetEnumerator() { return this; }
        IEnumerator<EntityComponentAccessor<C1, C2, C3>> IEnumerable<EntityComponentAccessor<C1, C2, C3>>.GetEnumerator() => this;
        IEnumerator IEnumerable.GetEnumerator() => this;
    }
}
