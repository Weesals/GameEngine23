using System;
using System.Collections;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Weesals.ECS {
    // Reference to an entity in the world
    [DebuggerTypeProxy(typeof(Entity.DebugEntityView))]
    public struct Entity : IEquatable<Entity>, IComparable<Entity> {
#if DEBUG
        public EntityManager Manager;
#endif
        public uint Index;
        public uint Version;
        public Entity(uint index, uint version) { Index = index; Version = version; }
        public readonly bool IsValid => Index > 0;
        [Conditional("DEBUG")]
        public void SetDebugManager(EntityManager manager) {
#if DEBUG
            Manager = manager;
#endif
        }
        public int CompareTo(Entity other) { return Index.CompareTo(other.Index); }
        public bool Equals(Entity other) { return Index == other.Index; }
        public override bool Equals(object? obj) { throw new NotImplementedException(); }
        public override int GetHashCode() { return (int)Index; }
        public override string ToString() {
            var name = IsValid ? $"Entity #{Index}" : "None";
#if DEBUG
            if (Manager != null) name = $"{name}: {Manager.GetEntityMeta(this).Name}";
#endif
            return name;
        }
        public static bool operator ==(Entity left, Entity right) { return left.Equals(right); }
        public static bool operator !=(Entity left, Entity right) { return !(left == right); }
        public static readonly Entity Null = new();

        public struct DebugEntityView {
            [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
            public EntityManager.DebugEntity View;
            public DebugEntityView(Entity entity) {
#if DEBUG
                View = new(entity.Manager, entity);
#else
                View = new(null, entity);
#endif
            }
        }
    }

    public class SystemBootstrap {
        public virtual T CreateSystem<T>(EntityContext context) where T : new() {
            return new T();
        }
    }

    // Context shared between all compatible stages/worlds
    // (so that component IDs are the same)
    public class EntityContext {
        public static ComponentType<Entity> EntityColumnType = new(new TypeId(0));
        public struct DeepBitField : IEquatable<DeepBitField> {
            public BitField Field;
            public bool Equals(DeepBitField other) { return Field.DeepEquals(other.Field); }
            public override bool Equals(object? obj) { throw new NotImplementedException(); }
            public override int GetHashCode() { var hash64 = Field.DeepHash(); return (int)hash64 ^ (int)(hash64 >> 32); }
        }
        private List<ComponentType> componentTypes = new(16);
        private List<ComponentType> sparseComponentTypes = new(16);
        private Dictionary<Type, TypeId> componentsByType = new(32);
        private HashSet<DeepBitField> cachedTypeMasks = new(64);
        public event Action<TypeId>? OnTypeIdCreated;
        public SystemBootstrap? SystemBootstrap;
        public EntityContext() {
            componentsByType.Add(typeof(Entity), EntityColumnType.TypeId);
            Debug.Assert(!EntityColumnType.TypeId.IsSparse);
            componentTypes.Add(EntityColumnType);
        }
        public TypeId RequireComponentTypeId<T>() {
            //if (typeof(T) == typeof(Entity)) return TypeId.Invalid;
            if (componentsByType.TryGetValue(typeof(T), out var typeId)) return typeId;
            lock (componentsByType) {
                if (componentsByType.TryGetValue(typeof(T), out typeId)) return typeId;
                var isFloating = ComponentType.GetIsSparse(typeof(T));
                typeId = new TypeId(isFloating ? sparseComponentTypes.Count : componentTypes.Count, isFloating);
                var cmpType = new ComponentType<T>(typeId);
                (typeId.IsSparse ? sparseComponentTypes : componentTypes).Add(cmpType);
                componentsByType[typeof(T)] = typeId;
                OnTypeIdCreated?.Invoke(typeId);
                return typeId;
            }
        }
        public ComponentType GetComponentType(int index) {
            return ((index & TypeId.Header) == 0 ? componentTypes : sparseComponentTypes)[index & TypeId.Tail];
        }
        unsafe public BitField RequireTypeMask(BitField.Generator generator) {
            if (generator.Pages.Count == 0) return default;
            ulong* fieldPages = stackalloc ulong[generator.Pages.Count];
            for (int i = 0; i < generator.Pages.Count; i++) fieldPages[i] = generator.Pages[i];
            return RequireTypeMask(new BitField(generator.PageIds, fieldPages));
        }
        public BitField RequireTypeMask(BitField field) {
            if (!cachedTypeMasks.TryGetValue(new DeepBitField() { Field = field }, out var result)) {
                result.Field = BitField.Allocate(field);
                cachedTypeMasks.Add(result);
            }
            return result.Field;
        }
        public struct TypeInfoBuilder {
            public readonly EntityContext Context;
            [ThreadStatic] private static BitField.Generator generator;
            public TypeInfoBuilder(EntityContext context) {
                Context = context;
                if (generator == null) generator = new();
                Debug.Assert(generator.IsEmpty);
            }
            public TypeInfoBuilder(EntityContext context, BitField field) : this(context) {
                Append(field);
            }
            public void Append(BitField field) {
                generator.Append(field);
            }
            public TypeId AddComponent<C>() {
                if (typeof(C) == typeof(Entity)) return TypeId.Invalid;
                var index = Context.RequireComponentTypeId<C>();
                generator.Add(index);
                return index;
            }
            public void AddComponent(TypeId typeId) {
                generator.Add(typeId);
            }
            public void RemoveComponent(TypeId typeId) {
                generator.Remove(typeId);
            }
            unsafe public BitField Build() {
                var field = Context.RequireTypeMask(generator);
                generator.Clear();
                return field;
            }
        }
    }

    public ref struct NullableRef<T> {
        public ref T Value;
        public bool HasValue;
        public NullableRef(ref T value) { Value = ref value; HasValue = true; }
        public static implicit operator T(NullableRef<T> v) { return v.Value; }
    }
}
