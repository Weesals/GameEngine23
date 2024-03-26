using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Weesals.Engine {

    public interface IItemStringifier {
        string ToString(ulong id);
    }
    public interface IItemPosition {
        Vector3 GetPosition(ulong id = ulong.MaxValue);
        void SetPosition(Vector3 pos, ulong id = ulong.MaxValue);
        Quaternion GetRotation(ulong id = ulong.MaxValue);
        void SetRotation(Quaternion rot, ulong id = ulong.MaxValue);
    }
    public interface IItemRedirect {
        ItemReference GetOwner(ulong id);
    }

    public struct ItemReference : IEquatable<ItemReference> {

        private const string WorldGridId = "WORLDGRID";

        public object Owner;
        public ulong Data;
        public Int2 DataAsInt2 {
            get { return new Int2((int)(Data >> 32), (int)Data); }
            set { Data = (((ulong)(uint)value.X) << 32) | ((ulong)(uint)value.Y); }
        }
        public bool IsValid { get { return Owner != null; } }

        public ItemReference(object cmp, ulong data = 0) { Owner = cmp; Data = data; }
        public bool Equals(ItemReference o) { return Owner == o.Owner && Data == o.Data; }
        public static bool operator ==(ItemReference t1, ItemReference t2) { return t1.Equals(t2); }
        public static bool operator !=(ItemReference t1, ItemReference t2) { return !t1.Equals(t2); }
        // Never box this type
        public override bool Equals(object? obj) { throw new NotImplementedException(); }
        public override string ToString() {
            if (Owner is IItemStringifier stringifier) return stringifier.ToString(Data);
            //if (Owner is World) return GetEntity().ToString();
            return Owner + ":" + Data;
        }
        public override int GetHashCode() { return (Owner != null ? Owner.GetHashCode() : 0) + (int)Data; }

        public void SetWorldPosition(Vector3 pos) {
            if (Owner is IItemPosition eposition) {
                eposition.SetPosition(pos, Data);
            } else {
                throw new NotImplementedException();
            }
        }
        public void SetWorldRotation(Quaternion rot) {
            if (Owner is IItemPosition eposition) {
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
            if (Owner is IItemPosition eposition) {
                return eposition.GetPosition(Data);
            } else if (Owner is string label && label == WorldGridId) {
                var wpnt = DataAsInt2;
                return new Vector3(wpnt.X + 0.5f, 0f, wpnt.Y + 0.5f);
            }
            return null;
        }
        public Quaternion GetWorldRotation(int index = -1) {
            if (Owner is IItemPosition eposition) {
                return eposition.GetRotation(Data);
            }
            return Quaternion.Identity;
        }

        public static readonly ItemReference None = new ItemReference();
    }
}
