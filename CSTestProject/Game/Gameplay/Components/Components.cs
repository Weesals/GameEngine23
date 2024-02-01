using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Weesals.Game {

    public struct ECMobile {
        public int MovementSpeed;
        public ushort TurnSpeed;
        public byte NavMask;
    }

    public struct ECTeam {
        public byte SlotId;
        public override string ToString() { return "Owner " + SlotId; }
    }

}
