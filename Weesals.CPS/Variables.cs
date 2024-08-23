using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Weesals.CPS {
    public struct StackTypeMeta {
        public Type Type;
        public int StackSize;
        public bool IsUnmanaged;
        public static unsafe StackTypeMeta FromType<T>() where T : unmanaged {
            return new StackTypeMeta() { Type = typeof(T), StackSize = sizeof(T), IsUnmanaged = true, };
        }
    }
    public struct StackType : IEquatable<StackType> {
        public byte Id;
        public int StackSize => GetMeta().StackSize;
        public bool IsUnmanaged => GetMeta().IsUnmanaged;
        public Type RawType => GetMeta().Type;
        public bool IsNone => Id == 0;
        public bool IsAny => Id == AnyType;
        public StackTypeMeta GetMeta() { return Types[Id]; }
        public static implicit operator StackType(int v) { return new StackType() { Id = (byte)v }; }
        public static bool operator ==(StackType t1, StackType t2) { return t1.Id == t2.Id; }
        public static bool operator !=(StackType t1, StackType t2) { return t1.Id != t2.Id; }
        public bool Equals(StackType other) { return this == other; }
        public override bool Equals(object? obj) { return obj is StackType st && this == st; }
        public override int GetHashCode() { return Id; }
        public static readonly StackTypeMeta[] Types = new[] {
                new StackTypeMeta() { Type = null, StackSize = 0, IsUnmanaged = true, },
                StackTypeMeta.FromType<byte>(),
                StackTypeMeta.FromType<short>(),
                StackTypeMeta.FromType<int>(),
                StackTypeMeta.FromType<float>(),
                new StackTypeMeta() { Type = typeof(object), StackSize = 2, },
                new StackTypeMeta() { Type = null, StackSize = 1, }
            };
        public static unsafe int GetType<T>() where T : unmanaged {
            int size = sizeof(T);
            if (size == 1) return ByteType;
            if (size == 2) return ShortType;
            if (size == 4) return typeof(T) == typeof(float) ? FloatType : IntType;
            throw new NotImplementedException();
        }
        public static unsafe int GetDataLength(Span<byte> storage, int typeId) {
            var meta = (StackType)typeId;
            if (meta.StackSize > 0) return meta.StackSize;
            throw new NotImplementedException();
        }
        public static unsafe int WriteData<T>(SparseArray<byte> storage, int typeId, T value) where T : unmanaged {
            var meta = (StackType)typeId;
            if (meta.StackSize > 0) {
                var range = storage.Allocate(meta.StackSize);
                fixed (byte* sourcePtr = &storage.Items[range.Start]) {
                    *(T*)sourcePtr = value;
                }
                return range.Start;
            }
            throw new NotImplementedException();
        }
        public static void DeleteData(SparseArray<byte> storage, int typeId, int offset) {
            var size = GetDataLength(storage.AsSpan().Slice(offset), typeId);
            Debug.Assert(size > 0, "Size is invalid");
            storage.Return(offset, size);
        }

        public const byte None = 0;
        public const byte ByteType = 1;
        public const byte ShortType = 2;
        public const byte IntType = 3;
        public const byte FloatType = 4;
        public const byte ObjectType = 5;
        public const byte AnyType = 6;
    }

    public ref struct Variable {
        public int TypeId;
        public Span<byte> Data;
        public T GetAs<T>() where T : unmanaged {
            switch (TypeId) {
                case 0:
                float floatValue = BitConverter.ToSingle(Data);
                return Unsafe.As<float, T>(ref floatValue);
            }
            return default;
        }
    }
    public struct StackItem {
        public int TypeId;
        public int Offset;
        public bool IsValid => TypeId >= 0;
        public bool HasData => Offset > 0;
        public Variable GetValue(Span<byte> data) {
            return new Variable() {
                TypeId = TypeId,
                Data = data.Slice(Offset),
            };
        }
        public unsafe bool TryGetAsInt(SparseArray<byte> dataStore, out int value) {
            fixed (byte* data = &dataStore.Items[Offset]) {
                if (TypeId == StackType.ByteType) { value = *(byte*)data; return true; }
                if (TypeId == StackType.ShortType) { value = *(short*)data; return true; }
                if (TypeId == StackType.IntType) { value = *(int*)data; return true; }
                if (TypeId == StackType.FloatType) { value = (int)*(float*)data; return true; }
            }
            value = default;
            return false;
        }
        public unsafe bool TryGetAsFloat(SparseArray<byte> dataStore, out float value) {
            fixed (byte* data = &dataStore.Items[Offset]) {
                if (TypeId == StackType.ByteType) { value = *(byte*)data; return true; }
                if (TypeId == StackType.ShortType) { value = *(short*)data; return true; }
                if (TypeId == StackType.IntType) { value = *(int*)data; return true; }
                if (TypeId == StackType.FloatType) { value = *(float*)data; return true; }
            }
            value = default;
            return false;
        }
        public unsafe bool TryGetAsString(SparseArray<byte> dataStore, IReadOnlyList<object> terms, StringBuilder builder) {
            fixed (byte* data = &dataStore.Items[Offset]) {
                if (TypeId == StackType.ByteType) { builder.Append(*(byte*)data); return true; }
                if (TypeId == StackType.ShortType) { builder.Append(*(short*)data); return true; }
                if (TypeId == StackType.IntType) { builder.Append(*(int*)data); return true; }
                if (TypeId == StackType.FloatType) { builder.Append((int)*(float*)data); return true; }
                if (TypeId == StackType.ObjectType) { builder.Append(terms[*(short*)data]); return true; }
            }
            return false;
        }
        public StackItem Clone(SparseArray<byte> srcStore, SparseArray<byte> destStore) {
            var srcData = srcStore.AsSpan().Slice(Offset);
            var len = StackType.GetDataLength(srcData, TypeId);
            var range = destStore.Allocate(len);
            srcData.Slice(0, len).CopyTo(destStore.Slice(range).AsSpan());
            return new StackItem() {
                TypeId = TypeId,
                Offset = range.Start,
            };
        }
        public void Delete(SparseArray<byte> dataStore) {
            StackType.DeleteData(dataStore, TypeId, Offset);
            this = Invalid;
        }
        public bool ExactEquals(SparseArray<byte> dataStore, StackItem other) {
            if (TypeId != other.TypeId) return false;
            var selfData = dataStore.AsSpan().Slice(Offset);
            var otherData = dataStore.AsSpan().Slice(other.Offset);
            var selfSize = StackType.GetDataLength(selfData, TypeId);
            var otherSize = StackType.GetDataLength(otherData, TypeId);
            if (selfSize != otherSize) return false;
            return selfData.Slice(0, selfSize).SequenceEqual(otherData.Slice(0, otherSize));
        }
        public override string ToString() {
            if (!IsValid) return "-invalid-";
            if (TypeId == 0) {
                if (HasData) return "-none-WITH-DATA-";
                return "-none-";
            }
            return $"{((StackType)TypeId).RawType.Name} @{Offset}";
        }

        public static StackItem CreateInt(SparseArray<byte> dataStore, int value) {
            return new StackItem() {
                TypeId = StackType.IntType,
                Offset = StackType.WriteData(dataStore, StackType.IntType, value),
            };
        }
        public static StackItem CreateObject(SparseArray<byte> dataStore, int value) {
            Debug.Assert(value >= 0 && value < short.MaxValue, "Out of range");
            return new StackItem() {
                TypeId = StackType.ShortType,
                Offset = StackType.WriteData(dataStore, StackType.ShortType, value),
            };
        }
        public static StackItem Create<T>(SparseArray<byte> dataStore, T v) where T : unmanaged {
            var typeId = StackType.GetType<T>();
            return new StackItem() {
                TypeId = typeId,
                Offset = StackType.WriteData(dataStore, typeId, v),
            };
        }

        public static readonly StackItem Invalid = new StackItem() { TypeId = -1, Offset = -1, };
        public static readonly StackItem None = new StackItem() { TypeId = 0, Offset = -1, };
    }
}
