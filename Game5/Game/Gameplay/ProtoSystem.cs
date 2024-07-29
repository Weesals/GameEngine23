using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Weesals.ECS;
using Weesals.Engine;
using Weesals.Utility;

namespace Game5.Game {

    public struct EntityFootprint : IEquatable<EntityFootprint> {
        public enum Shapes { Box, Capsule, }
        public Int2 Size;
        public int Height;
        public Shapes Shape;
        public override string ToString() { return $"{Shape}: {Size}x{Height}"; }
        public bool Equals(EntityFootprint other) {
            return Size == other.Size && Height == other.Height && Shape == other.Shape;
        }
    }
    public class PrototypeData {
        public int Id = -1;
        //public int ParentId = -1;
        public int ConstructionProtoId = -1;
        //public int ReferenceCount;
        //public RangeInt DataBundle;
        //public RangeInt Actions;
        public EntityPrefab Prefab;
        public EntityFootprint Footprint;
        //public int LineOfSightRadius;
        //public bool IsMobile;
        public static readonly PrototypeData Default = new PrototypeData();
    }

    public class ProtoSystem : SystemBase {

        public PrefabRegistry PrefabRegistry;
        public PrefabLoader PrefabLoader;

        public struct PrefabBuilder {
            private readonly ProtoSystem protoSystem;
            private readonly PrefabRegistry.PrefabBuilder prefabBuilder;
            private readonly PrototypeData protoData;
            private static BitField.Generator bitGenerator = new();
            public PrefabBuilder(ProtoSystem _protoSystem, string name, PrototypeData? _protoData = null) {
                protoSystem = _protoSystem;
                prefabBuilder = _protoSystem.PrefabRegistry.CreatePrefab(name);
                protoData = _protoData ?? new();
                prefabBuilder.AddComponent(protoData);
                Debug.Assert(bitGenerator.IsEmpty);
            }
            public PrefabBuilder AddComponent<TComponent>(TComponent data) {
                prefabBuilder.AddComponent(data);
                return this;
            }
            public ArrayItem GetComponent<TComponent>() {
                return prefabBuilder.GetComponent<TComponent>();
            }
            public PrototypeData Build() {
                var prefab = prefabBuilder.Build();
                protoData.Id = prefab.Index;
                protoData.Prefab = prefab;
                return protoData;
            }
        }

        public PrefabBuilder CreatePrototype(string name, PrototypeData protoData = null) {
            return new PrefabBuilder(this, name, protoData);
        }

        protected override void OnCreate() {
            PrefabRegistry = new(Context);
            base.OnCreate();
        }

        public PrototypeData GetPrototypeData(EntityPrefab prefab) {
            if (!PrefabRegistry.World.GetHasComponent<PrototypeData>(prefab.Index))
                return PrototypeData.Default;
            return PrefabRegistry.World.GetComponent<PrototypeData>(prefab.Index) ?? PrototypeData.Default;
            //return GetPrototypeData(World.Stage.UnsafeGetEntityByIndex(entityIndex));
        }
        public PrototypeData GetPrototypeData(Entity entity) {
            return World.TryGetComponent<PrototypeData>(entity, out var protoData) ? protoData : PrototypeData.Default;
        }
        public PrototypeData GetPrototypeData(EntityAddress entityAddr) {
            return World.Manager.TryGetComponent<PrototypeData>(entityAddr, out var protoData) ? protoData : PrototypeData.Default;
        }

        public int GetConstructionProtoId(int protoId) {
            return -1;
        }

        public int GetPrototypeIdByName(string name) {
            return -1;
        }
    }

    public class PrefabLoader {

        public ProtoSystem ProtoSystem;

        public abstract class ComponentActivator {
            public abstract ArrayItem CreateInstance(ProtoSystem.PrefabBuilder builder);
        }
        public class ComponentActivator<T> : ComponentActivator {
            public override ArrayItem CreateInstance(ProtoSystem.PrefabBuilder builder) {
                if (typeof(T) != typeof(PrototypeData)) builder.AddComponent<T>(default);
                return builder.GetComponent<T>();
            }
        }
        public abstract class TypeSerializer {
            internal abstract void Serialize(ref object obj, SJson jsonData);
        }
        public class TypeSerializer<T> : TypeSerializer {
            public delegate void SerializeDelegate(ref T obj, SJson jsonData);
            public SerializeDelegate Serializer;
            public TypeSerializer(SerializeDelegate serializer) {
                Serializer = serializer;
            }
            internal override void Serialize(ref object obj, SJson jsonData) {
                T value = (T)obj;
                Serializer(ref value, jsonData);
                obj = value;
            }
        }

        private Dictionary<string, ComponentActivator> componentTypes = new();
        private Dictionary<Type, TypeSerializer> typeSerializers = new();

        public PrefabLoader(ProtoSystem protoSystem) {
            ProtoSystem = protoSystem;
            RegisterComponent<PrototypeData>();
            RegisterComponent<CModel>();
            RegisterComponent<CAnimation>();
            RegisterComponent<CHitPoints>();
            RegisterComponent<ECTransform>();
            RegisterComponent<ECMobile>();
            RegisterComponent<ECTeam>();
            RegisterComponent<ECAbilityAttackMelee>();
            RegisterComponent<ECAbilityAttackRanged>();
            RegisterComponent<ECAbilityGatherer>();
            RegisterComponent<ECObstruction>();
        }

        private void RegisterComponent<T>() {
            componentTypes.Add(typeof(T).Name, new ComponentActivator<T>());
        }
        public void RegisterSerializer<T>(TypeSerializer<T>.SerializeDelegate serializer) {
            typeSerializers.Add(typeof(T), new TypeSerializer<T>(serializer));
        }

        public PrototypeData LoadPrototype(string path) {
            ProtoSystem.PrefabBuilder builder = default;
            var json = new SJson(File.ReadAllText(path));
            foreach (var jComponent in json.GetFields()) {
                if (jComponent.Key.Equals("Name")) {
                    builder = ProtoSystem.CreatePrototype(jComponent.Value.ToString());
                    continue;
                }
                var cmpName = jComponent.Key.ToString();
                var cmpActivator = componentTypes[cmpName];
                var cmpRef = cmpActivator.CreateInstance(builder);
                var cmpType = cmpRef.Array.GetType().GetElementType();
                var cmp = cmpRef.Array.GetValue(cmpRef.Index);
                if (cmp == null) cmp = Activator.CreateInstance(cmpType);
                DeserializeObject(ref cmp, jComponent.Value);
                cmpRef.Array.SetValue(cmp, cmpRef.Index);
            }
            return builder.Build();
        }

        private void DeserializeObject(ref object? cmp, SJson jObject) {
            var cmpType = cmp.GetType();
            if (typeSerializers.TryGetValue(cmpType, out var serializer)) {
                serializer.Serialize(ref cmp, jObject);
                return;
            }
            if (jObject.IsObject) {
                foreach (var jField in jObject.GetFields()) {
                    var fieldName = jField.Key.ToString();
                    var fieldValue = jField.Value;
                    var field = cmpType.GetField(fieldName);
                    if (field == null) {
                        Debug.Fail("Could not find field");
                        continue;
                    }
                    if (field.FieldType == typeof(string)) {
                        field.SetValue(cmp, (string)fieldValue);
                    } else if (field.FieldType == typeof(int)) {
                        field.SetValue(cmp, (int)fieldValue);
                    } else if (field.FieldType == typeof(uint)) {
                        field.SetValue(cmp, (uint)fieldValue);
                    } else if (field.FieldType == typeof(short)) {
                        field.SetValue(cmp, (short)fieldValue);
                    } else if (field.FieldType == typeof(ushort)) {
                        field.SetValue(cmp, (ushort)fieldValue);
                    } else if (field.FieldType == typeof(float)) {
                        field.SetValue(cmp, (float)fieldValue);
                    } else if (field.FieldType == typeof(Int2)) {
                        Int2 v = default;
                        if (fieldValue.IsArray) {
                            var en = fieldValue.GetEnumerator();
                            if (en.MoveNext()) v = (int)en.Current;
                            if (en.MoveNext()) v.Y = (int)en.Current;
                        } else {
                            v = (int)fieldValue;
                        }
                        field.SetValue(cmp, v);
                    } else {
                        var obj = field.GetValue(cmp);
                        DeserializeObject(ref obj, fieldValue);
                        field.SetValue(cmp, obj);
                    }
                }
            } else {
                if (cmpType.IsEnum) {
                    cmp = Enum.Parse(cmpType, jObject.ToString());
                } else {
                    cmp = Convert.ChangeType(jObject.ToString(), cmpType);
                }
            }
        }
    }

}
