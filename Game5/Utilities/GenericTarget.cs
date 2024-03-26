using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.ECS;
using Weesals.Engine;
using Weesals.Utility;

namespace Game5.Game {

    public interface IEntityTeam {
        int GetTeam(ulong data);
    }
    public interface IEntityCount {
        int GetCount(ulong data);
    }
    interface IEntityDestroyable {
        void DestroySelf(ulong id);
    }
    interface IEntitySelectable {
        void NotifySelected(ulong id, bool selected);
    }

    public static class ItemReferenceEx {
        public static int GetCount(this ItemReference target) {
            if (target.Owner is IEntityCount icount) return icount.GetCount(target.Data);
            if (target.Owner != null) return 1;
            return 0;
        }
        public static int TryGetOwnerId(this ItemReference target) {
            if (target.Owner is IEntityTeam team) return team.GetTeam(target.Data);
            return -1;
        }
        public static void DestroySelf(this ItemReference target) {
            if (target.Owner is IEntityDestroyable destroyable)
                destroyable.DestroySelf(target.Data);
            else throw new NotImplementedException();
        }
    }

#if false
    public struct GenericTarget : IEquatable<GenericTarget> {

        private const string WorldGridId = "WORLDGRID";

        public static GenericTarget FromLocation(Int2 wpnt) {
            return new GenericTarget() { Owner = WorldGridId, Data = PackInt2(wpnt), };
        }
        public static GenericTarget FromPair(object component, ulong data) {
            return new GenericTarget() { Owner = component, Data = data };
        }
        public static GenericTarget FromEntity(World world, Entity entity) {
            return new GenericTarget(world) { Data = PackEntity(entity), };
        }

        private static ulong PackInt2(Int2 wpnt) {
            return ((ulong)(uint)wpnt.X << 32) | ((ulong)(uint)wpnt.Y);
        }
        public static ulong PackEntity(Entity entity) {
            return ((ulong)(uint)entity.Index << 32) | (uint)entity.Version;
        }
        public static Entity UnpackEntity(ulong id) {
            return new Entity() { Index = (uint)(id >> 32), Version = (uint)id, };
        }

        public int GetCount() {
            if (Owner is IEntityCount icount) return icount.GetCount(Data);
            if (Owner != null) return 1;
            return 0;
        }
        public Entity GetEntity() {
            return UnpackEntity(Data);
        }

        public int TryGetOwnerId() {
            if (Owner is IEntityTeam team) return team.GetTeam(Data);
            return -1;
        }

        public T? GetComponentInParent<T>() where T : class {
            if (HierarchyExt.TryGetRecursive(Owner, out T value)) return value;
            return default;
        }
        public IEnumerable<T> GetComponentsInChildren<T>() {
            return Array.Empty<T>();
        }

        public void DestroySelf() {
            if (Owner is IEntityDestroyable destroyable)
                destroyable.DestroySelf(Data);
            else throw new NotImplementedException();
        }

    }
#endif
}
