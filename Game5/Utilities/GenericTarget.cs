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

}
