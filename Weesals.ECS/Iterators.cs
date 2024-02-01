using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Weesals.ECS {
    public readonly struct EntityComponentAccessor<C1> {
        public readonly Archetype Archetype;
        private readonly int row, column1;
        public readonly Entity Entity => Archetype.Entities[row];
        public readonly C1 Component1 => Archetype.GetColumn(column1).GetValue<C1>(row);
        public readonly ref C1 Component1Ref => ref Archetype.GetColumn(column1).GetValueRefMut<C1>(row);
        public EntityComponentAccessor(Archetype archetype, int _row, int _column1) {
            Archetype = archetype; row = _row;
            column1 = _column1;
        }
        public void Set(C1 value) => Component1Ref = value;
        public static implicit operator Entity(EntityComponentAccessor<C1> accessor) => accessor.Entity;
        public static implicit operator C1(EntityComponentAccessor<C1> accessor) => accessor.Component1;
    }
    public readonly struct EntityComponentAccessor<C1, C2> {
        public readonly Archetype Archetype;
        private readonly int row, column1, column2;
        public readonly Entity Entity => Archetype.Entities[row];
        public readonly C1 Component1 => Archetype.GetColumn(column1).GetValue<C1>(row);
        public readonly C2 Component2 => Archetype.GetColumn(column2).GetValue<C2>(row);
        public readonly ref C1 Component1Ref => ref Archetype.GetColumn(column1).GetValueRefMut<C1>(row);
        public readonly ref C2 Component2Ref => ref Archetype.GetColumn(column2).GetValueRefMut<C2>(row);
        public EntityComponentAccessor(Archetype archetype, int _row, int _column1, int _column2) {
            Archetype = archetype; row = _row;
            column1 = _column1; column2 = _column2;
        }
        public void Set(C1 value) => Component1Ref = value;
        public void Set(C2 value) => Component2Ref = value;
        public static implicit operator Entity(EntityComponentAccessor<C1, C2> accessor) => accessor.Entity;
        public static implicit operator C1(EntityComponentAccessor<C1, C2> accessor) => accessor.Component1;
        public static implicit operator C2(EntityComponentAccessor<C1, C2> accessor) => accessor.Component2;
    }
    public readonly struct EntityComponentAccessor<C1, C2, C3> {
        public readonly Archetype Archetype;
        private readonly int row, column1, column2, column3;
        public readonly Entity Entity => Archetype.Entities[row];
        public readonly C1 Component1 => Archetype.GetColumn(column1).GetValue<C1>(row);
        public readonly C2 Component2 => Archetype.GetColumn(column2).GetValue<C2>(row);
        public readonly C3 Component3 => Archetype.GetColumn(column3).GetValue<C3>(row);
        public readonly ref C1 Component1Ref => ref Archetype.GetColumn(column1).GetValueRefMut<C1>(row);
        public readonly ref C2 Component2Ref => ref Archetype.GetColumn(column2).GetValueRefMut<C2>(row);
        public readonly ref C3 Component3Ref => ref Archetype.GetColumn(column3).GetValueRefMut<C3>(row);
        public EntityComponentAccessor(Archetype archetype, int _row, int _column1, int _column2, int _column3) {
            Archetype = archetype; row = _row;
            column1 = _column1; column2 = _column2; column3 = _column3;
        }
        public void Set(C1 value) => Component1Ref = value;
        public void Set(C2 value) => Component2Ref = value;
        public void Set(C3 value) => Component3Ref = value;
        public static implicit operator Entity(EntityComponentAccessor<C1, C2, C3> accessor) => accessor.Entity;
        public static implicit operator C1(EntityComponentAccessor<C1, C2, C3> accessor) => accessor.Component1;
        public static implicit operator C2(EntityComponentAccessor<C1, C2, C3> accessor) => accessor.Component2;
        public static implicit operator C3(EntityComponentAccessor<C1, C2, C3> accessor) => accessor.Component3;
    }

    public readonly struct TableComponentAccessor<C1> : IEnumerable<EntityComponentAccessor<C1>> {
        public readonly Archetype Archetype;
        private readonly int column1;
        public TableComponentAccessor(Archetype archetype, TypeId typeId) {
            Archetype = archetype;
            column1 = archetype.RequireTypeIndex(typeId);
        }
        public struct Enumerator : IEnumerator<EntityComponentAccessor<C1>> {
            public readonly TableComponentAccessor<C1> TableAccessor;
            private int row;
            public bool IsValid => TableAccessor.Archetype != null;
            public int Row => row;
            public EntityComponentAccessor<C1> Current => new(TableAccessor.Archetype, row, TableAccessor.column1);
            object IEnumerator.Current => Current;
            public Enumerator(TableComponentAccessor<C1> accessor) { TableAccessor = accessor; row = -1; }
            public void Dispose() { }
            public void Reset() { row = -1; }
            public bool MoveNext() { return ++row < TableAccessor.Archetype.EntityCount; }
            public bool MoveNextFiltered(Query query) {
                var archetype = TableAccessor.Archetype;
                ++row;
                while (true) {
                    var oldRow = row;
                    if (oldRow >= archetype.EntityCount) break;
                    foreach (var typeId in query.WithSparseTypes) {
                        var column = archetype.RequireSparseComponent(TypeId.MakeSparse(typeId), default!);
                        row = archetype.GetNextSparseRowInclusive(column, oldRow);
                        if (row == -1) break;
                    }
                    if (oldRow == row) return true;
                    else if(row == -1) break;
                }
                return false;
            }
        }
        public Enumerator GetEnumerator() => new(this);
        IEnumerator<EntityComponentAccessor<C1>> IEnumerable<EntityComponentAccessor<C1>>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    public readonly struct TableComponentAccessor<C1, C2> : IEnumerable<EntityComponentAccessor<C1, C2>> {
        public readonly Archetype Archetype;
        private readonly int column1, column2;
        public TableComponentAccessor(Archetype archetype, TypeId typeId1, TypeId typeId2) {
            Archetype = archetype;
            column1 = archetype.RequireTypeIndex(typeId1);
            column2 = archetype.RequireTypeIndex(typeId2);
        }
        public struct Enumerator : IEnumerator<EntityComponentAccessor<C1, C2>> {
            public readonly TableComponentAccessor<C1, C2> TableAccessor;
            private int row;
            public bool IsValid => TableAccessor.Archetype != null;
            public EntityComponentAccessor<C1, C2> Current => new(TableAccessor.Archetype, row, TableAccessor.column1, TableAccessor.column2);
            object IEnumerator.Current => Current;
            public Enumerator(TableComponentAccessor<C1, C2> accessor) { TableAccessor = accessor; row = -1; }
            public void Dispose() { }
            public void Reset() { row = -1; }
            public bool MoveNext() { return ++row < TableAccessor.Archetype.EntityCount; }
            public bool MoveNextFiltered(Query query) {
                var archetype = TableAccessor.Archetype;
                ++row;
                while (true) {
                    var oldRow = row;
                    if (oldRow >= archetype.EntityCount) break;
                    foreach (var typeId in query.WithSparseTypes) {
                        var column = archetype.RequireSparseComponent(TypeId.MakeSparse(typeId), default!);
                        row = archetype.GetNextSparseRowInclusive(column, oldRow);
                        if (row == -1) break;
                    }
                    if (oldRow == row) return true;
                    else if (row == -1) break;
                }
                return false;
            }
        }
        public Enumerator GetEnumerator() => new(this);
        IEnumerator<EntityComponentAccessor<C1, C2>> IEnumerable<EntityComponentAccessor<C1, C2>>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
    public readonly struct TableComponentAccessor<C1, C2, C3> : IEnumerable<EntityComponentAccessor<C1, C2, C3>> {
        public readonly Archetype Archetype;
        private readonly int column1, column2, column3;
        public TableComponentAccessor(Archetype archetype, TypeId typeId1, TypeId typeId2, TypeId typeId3) {
            Archetype = archetype;
            column1 = archetype.RequireTypeIndex(typeId1);
            column2 = archetype.RequireTypeIndex(typeId2);
            column3 = archetype.RequireTypeIndex(typeId3);
        }
        public struct Enumerator : IEnumerator<EntityComponentAccessor<C1, C2, C3>> {
            public readonly TableComponentAccessor<C1, C2, C3> TableAccessor;
            private int row;
            public bool IsValid => TableAccessor.Archetype != null;
            public EntityComponentAccessor<C1, C2, C3> Current => new(TableAccessor.Archetype, row, TableAccessor.column1, TableAccessor.column2, TableAccessor.column3);
            object IEnumerator.Current => Current;
            public Enumerator(TableComponentAccessor<C1, C2, C3> accessor) { TableAccessor = accessor; row = -1; }
            public void Dispose() { }
            public void Reset() { row = -1; }
            public bool MoveNext() { return ++row < TableAccessor.Archetype.EntityCount; }
            public bool MoveNextFiltered(Query query) {
                var archetype = TableAccessor.Archetype;
                ++row;
                while (true) {
                    var oldRow = row;
                    if (oldRow >= archetype.EntityCount) break;
                    foreach (var typeId in query.WithSparseTypes) {
                        var column = archetype.RequireSparseComponent(TypeId.MakeSparse(typeId), default!);
                        row = archetype.GetNextSparseRowInclusive(column, oldRow);
                        if (row == -1) break;
                    }
                    if (oldRow == row) return true;
                    else if (row == -1) break;
                }
                return false;
            }
        }
        public Enumerator GetEnumerator() => new(this);
        IEnumerator<EntityComponentAccessor<C1, C2, C3>> IEnumerable<EntityComponentAccessor<C1, C2, C3>>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public struct TypedQueryIterator<C1> : IEnumerator<EntityComponentAccessor<C1>>, IEnumerable<EntityComponentAccessor<C1>> {
        public readonly TypeId C1TypeId;
        private Stage.ArchetypeQueryEnumerator queryEnumerator;
        public Stage Stage => queryEnumerator.Stage;
        public TableComponentAccessor<C1>.Enumerator TableEnumerator;
        public EntityComponentAccessor<C1> Current => TableEnumerator.Current;
        object IEnumerator.Current => Current;
        public TypedQueryIterator(Stage.ArchetypeQueryEnumerator tableEn) {
            queryEnumerator = tableEn;
            C1TypeId = Stage.Context.RequireComponentTypeId<C1>();
        }
        public void Reset() { queryEnumerator.Reset(); }
        public void Dispose() { }
        public bool MoveNext() {
            while (true) {
                if (TableEnumerator.IsValid && TableEnumerator.MoveNextFiltered(queryEnumerator.Query)) return true;
                if (!queryEnumerator.MoveNext()) return false;
                var tableAccessor = new TableComponentAccessor<C1>(queryEnumerator.CurrentArchetype, C1TypeId);
                TableEnumerator = tableAccessor.GetEnumerator();
            }
        }
        public TypedQueryIterator<C1> GetEnumerator() { return this; }
        IEnumerator<EntityComponentAccessor<C1>> IEnumerable<EntityComponentAccessor<C1>>.GetEnumerator() => this;
        IEnumerator IEnumerable.GetEnumerator() => this;
    }
    public struct TypedQueryIterator<C1, C2> : IEnumerator<EntityComponentAccessor<C1, C2>>, IEnumerable<EntityComponentAccessor<C1, C2>> {
        public readonly TypeId C1TypeId, C2TypeId;
        private Stage.ArchetypeQueryEnumerator queryEnumerator;
        public Stage Stage => queryEnumerator.Stage;
        public TableComponentAccessor<C1, C2>.Enumerator TableEnumerator;
        public EntityComponentAccessor<C1, C2> Current => TableEnumerator.Current;
        object IEnumerator.Current => Current;
        public TypedQueryIterator(Stage.ArchetypeQueryEnumerator tableEn) {
            queryEnumerator = tableEn;
            C1TypeId = Stage.Context.RequireComponentTypeId<C1>();
            C2TypeId = Stage.Context.RequireComponentTypeId<C2>();
        }
        public void Reset() { queryEnumerator.Reset(); }
        public void Dispose() { }
        public bool MoveNext() {
            while (true) {
                if (TableEnumerator.IsValid && TableEnumerator.MoveNextFiltered(queryEnumerator.Query)) return true;
                if (!queryEnumerator.MoveNext()) return false;
                var tableAccessor = new TableComponentAccessor<C1, C2>(queryEnumerator.CurrentArchetype, C1TypeId, C2TypeId);
                TableEnumerator = tableAccessor.GetEnumerator();
            }
        }
        public TypedQueryIterator<C1, C2> GetEnumerator() { return this; }
        IEnumerator<EntityComponentAccessor<C1, C2>> IEnumerable<EntityComponentAccessor<C1, C2>>.GetEnumerator() => this;
        IEnumerator IEnumerable.GetEnumerator() => this;
    }
    public struct TypedQueryIterator<C1, C2, C3> : IEnumerator<EntityComponentAccessor<C1, C2, C3>>, IEnumerable<EntityComponentAccessor<C1, C2, C3>> {
        public readonly TypeId C1TypeId, C2TypeId, C3TypeId;
        private Stage.ArchetypeQueryEnumerator queryEnumerator;
        public Stage Stage => queryEnumerator.Stage;
        public TableComponentAccessor<C1, C2, C3>.Enumerator TableEnumerator;
        public EntityComponentAccessor<C1, C2, C3> Current => TableEnumerator.Current;
        object IEnumerator.Current => Current;
        public TypedQueryIterator(Stage.ArchetypeQueryEnumerator tableEn) {
            queryEnumerator = tableEn;
            C1TypeId = Stage.Context.RequireComponentTypeId<C1>();
            C2TypeId = Stage.Context.RequireComponentTypeId<C2>();
            C3TypeId = Stage.Context.RequireComponentTypeId<C3>();
        }
        public void Reset() { queryEnumerator.Reset(); }
        public void Dispose() { }
        public bool MoveNext() {
            while (true) {
                if (TableEnumerator.IsValid && TableEnumerator.MoveNextFiltered(queryEnumerator.Query)) return true;
                if (!queryEnumerator.MoveNext()) return false;
                var tableAccessor = new TableComponentAccessor<C1, C2, C3>(queryEnumerator.CurrentArchetype, C1TypeId, C2TypeId, C3TypeId);
                TableEnumerator = tableAccessor.GetEnumerator();
            }
        }
        public TypedQueryIterator<C1, C2, C3> GetEnumerator() { return this; }
        IEnumerator<EntityComponentAccessor<C1, C2, C3>> IEnumerable<EntityComponentAccessor<C1, C2, C3>>.GetEnumerator() => this;
        IEnumerator IEnumerable.GetEnumerator() => this;
    }
}
