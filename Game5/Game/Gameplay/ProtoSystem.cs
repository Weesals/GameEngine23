using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
            return World.Stage.TryGetComponent<PrototypeData>(entityAddr, out var protoData) ? protoData : PrototypeData.Default;
        }

        public int GetConstructionProtoId(int protoId) {
            return -1;
        }

        public int GetPrototypeIdByName(string name) {
            return -1;
        }
    }

}
