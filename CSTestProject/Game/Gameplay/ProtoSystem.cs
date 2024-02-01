using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Weesals.ECS;
using Weesals.Engine;
using Weesals.Utility;

namespace Weesals.Game {

    public struct EntityFootprint {
        public enum Shapes { Box, Capsule, }
        public Int2 Size;
        public int Height;
        public Shapes Shape;
    }
    public class PrototypeData {
        public int Id;
        public int ParentId;
        public int ConstructionProtoId;
        public int ReferenceCount;
        public RangeInt DataBundle;
        public RangeInt Actions;
        public Entity Prefab;
        public EntityFootprint Footprint;
        public int LineOfSightRadius;
        public bool IsMobile;
        public static readonly PrototypeData Default = new PrototypeData() { ParentId = -1, ConstructionProtoId = -1, };
    }

    public class ProtoSystem : SystemBase {

        public PrototypeData GetPrototypeData(int entityIndex) {
            return GetPrototypeData(World.Stage.UnsafeGetEntityByIndex(entityIndex));
        }
        public PrototypeData GetPrototypeData(Entity entity) {
            return World.GetComponent<PrototypeData>(entity);
        }

    }

}
