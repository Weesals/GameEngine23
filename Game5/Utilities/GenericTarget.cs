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
    interface IEntityPosition {
        Vector3 GetPosition(ulong id = ulong.MaxValue);
        void SetPosition(Vector3 pos, ulong id = ulong.MaxValue);
        Quaternion GetRotation(ulong id = ulong.MaxValue);
        void SetRotation(Quaternion rot, ulong id = ulong.MaxValue);
    }
    interface IEntityDestroyable {
        void DestroySelf(ulong id);
    }
    interface IEntitySelectable {
        void NotifySelected(ulong id, bool selected);
    }
    interface IEntityRedirect {
        GenericTarget GetOwner(ulong id);
    }
    interface IEntityStringifier {
        string ToString(ulong id);
    }

    public struct GenericTarget : IEquatable<GenericTarget> {

        private const string WorldGridId = "WORLDGRID";

        public object Owner;
        public ulong Data;
        public bool IsValid { get { return Owner != null; } }
        public Int2 DataAsInt2 {
            get { return new Int2((int)(Data >> 32), (int)Data); }
            set { Data = (((ulong)(uint)value.X) << 32) | ((ulong)(uint)value.Y); }
        }

        public GenericTarget(object cmp, ulong data = 0) { Owner = cmp; Data = data; }
        public bool Equals(GenericTarget o) { return Owner == o.Owner && Data == o.Data; }
        public static bool operator ==(GenericTarget t1, GenericTarget t2) { return t1.Equals(t2); }
        public static bool operator !=(GenericTarget t1, GenericTarget t2) { return !t1.Equals(t2); }
        // Never box this type
        public override bool Equals(object? obj) { throw new NotImplementedException(); }
        public override string ToString() {
            if (Owner is IEntityStringifier stringifier) return stringifier.ToString(Data);
            if (Owner is World) return GetEntity().ToString();
            return Owner + ":" + Data;
        }
        public override int GetHashCode() { return (Owner != null ? Owner.GetHashCode() : 0) + (int)Data; }

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

        public void SetWorldPosition(Vector3 pos) {
            if (Owner is IEntityPosition eposition) {
                eposition.SetPosition(pos, Data);
            } else {
                throw new NotImplementedException();
            }
        }
        public void SetWorldRotation(Quaternion rot) {
            if (Owner is IEntityPosition eposition) {
                eposition.SetRotation(rot, Data);
            } else {
                throw new NotImplementedException();
            }
        }
        public Vector3 GetWorldPosition(int index = -1) {
            var pos = TryGetWorldPosition(index);
            return pos ?? default;
        }
        public Vector3? TryGetWorldPosition(int index = -1) {
            if (Owner is IEntityPosition eposition) {
                return eposition.GetPosition(Data);
            } else if (Owner is string label && label == WorldGridId) {
                var wpnt = DataAsInt2;
                return new Vector3(wpnt.X + 0.5f, 0f, wpnt.Y + 0.5f);
            }
            return null;
        }
        public Quaternion GetWorldRotation(int index = -1) {
            if (Owner is IEntityPosition eposition) {
                return eposition.GetRotation(Data);
            }
            return Quaternion.Identity;
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

        public static readonly GenericTarget None = new GenericTarget();
    }
}
