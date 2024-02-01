using System;
using System.Collections;
using System.Diagnostics;
using System.Reflection;

namespace Weesals.ECS {
    public class SparseComponentAttribute : Attribute { }
    public class NoCopyComponentAttribute : Attribute { }

    // Uniquely allocated for each unique component type
    public abstract class ComponentType {
        public enum ComponentFlags { None = 0, Sparse = 1, NoCopy = 2, Tag = 4, }
        public readonly Type Type;
        public readonly TypeId TypeId;
        public readonly ComponentFlags Flags;
        public bool IsSparse => (Flags & ComponentFlags.Sparse) != 0;
        public bool IsNoCopy => (Flags & ComponentFlags.NoCopy) != 0;
        public bool IsTag => (Flags & ComponentFlags.Tag) != 0;
        public ComponentType(Type type, TypeId id, ComponentFlags flags) {
            Type = type;
            TypeId = id;
            Flags = flags;
        }
        public abstract void Resize(ref Array array, int size);
        public override string ToString() { return Type.Name; }
        public static bool GetIsFloating(Type type) {
            return type.GetCustomAttribute<SparseComponentAttribute>() != null;
        }
        public static bool GetIsNoCopy(Type type) {
            return type.GetCustomAttribute<NoCopyComponentAttribute>() != null;
        }
    }
    public readonly struct TypeId {
        public const int Header = 0xf000;
        public const int Tail = ~Header;
        public const int SparseHeader = 0x1000;
        public const int TagHeader = 0x2000;
        public readonly int Packed;
        public readonly int Index => Packed & Tail;
        public readonly bool IsBasic => (Packed & Header) == 0;
        public readonly bool IsSparse => (Packed & Header) == SparseHeader;
        public readonly bool IsTag => (Packed & Header) == TagHeader;
        public TypeId(int index) { Packed = index; }
        public TypeId(int index, bool isFloating) { Packed = index  | (isFloating ? SparseHeader : 0); }
        public override string ToString() { return Packed.ToString(); }
        public static implicit operator int(TypeId id) { return id.Packed; }
        public static TypeId MakeSparse(int typeIndex) { return new TypeId(SparseHeader + typeIndex); }
        public static TypeId MakeTag(int typeIndex) { return new TypeId(TagHeader + typeIndex); }
        public static readonly TypeId Invalid = new(-1);
    }
    public class ComponentType<Component> : ComponentType {
        public ComponentType(TypeId id) : base(typeof(Component), id, GetFlags()) { }
        public override void Resize(ref Array array, int size) {
            Component[] typedArray = (Component[])array;
            Array.Resize(ref typedArray, size);
            array = typedArray;
        }

        public new static readonly bool IsSparse;
        public new static readonly bool IsNoCopy;
        static ComponentType() {
            IsSparse = ComponentType.GetIsFloating(typeof(Component));
            IsNoCopy = ComponentType.GetIsNoCopy(typeof(Component));
        }
        static ComponentFlags GetFlags() {
            return ComponentFlags.None
                | (IsSparse ? ComponentFlags.Sparse : default)
                | (IsNoCopy ? ComponentFlags.NoCopy : default);
        }
    }

}
