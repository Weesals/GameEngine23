using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Game5.Game {
    public struct ResourceAmount {
        public int ResourceIndex;
        public int Amount;
        public ResourceAmount(int type, int amount) {
            ResourceIndex = type;
            Amount = amount;
        }

        public bool IsNone { get { return Amount == 0; } }
        public bool Append(int type, int amnt) {
            if (ResourceIndex == type) { Amount += amnt; return true; }
            ResourceIndex = type;
            Amount = amnt;
            return false;
        }
        public static ResourceAmount operator +(ResourceAmount r1, ResourceAmount r2) {
            if (r1.ResourceIndex == None.ResourceIndex) return r2;
            if (r2.ResourceIndex == None.ResourceIndex) return r1;
            return new ResourceAmount(r1.ResourceIndex, r1.Amount + r2.Amount);
        }
        public static ResourceAmount operator -(ResourceAmount r1, ResourceAmount r2) {
            if (r1.ResourceIndex == None.ResourceIndex) return new ResourceAmount(r2.ResourceIndex, -r2.Amount);
            if (r2.ResourceIndex == None.ResourceIndex) return r1;
            return new ResourceAmount(r1.ResourceIndex, r1.Amount - r2.Amount);
        }
        public static readonly ResourceAmount None = new ResourceAmount(-1, 0);
    }

    public struct ECMobile {
        public int MovementSpeed;
        public ushort TurnSpeed;
        public byte NavMask;
    }
    public struct ECAbilityAttackRanged {
        public int Damage;
        public int Interval;
        public int Range;
    }
    public struct ECAbilityAttackMelee {
        public int Damage;
        public int Interval;
    }
    public struct ECAbilityGatherer {
        public int ResourceId;
        public int Interval;
        public ResourceAmount Holding;

        public void Deliver(ResourceAmount amount) {
            if (Holding.ResourceIndex != amount.ResourceIndex)
                Holding = new ResourceAmount(amount.ResourceIndex, 0);
            Holding.Amount += amount.Amount;
        }
    }

    public struct ECTeam {
        public byte SlotId;
        public override string ToString() { return "Owner " + SlotId; }
    }

    public struct ECConstruction {
        public int ProtoId;
        public int BuildPoints;
        public RequestId ConsumptionId;
        public float BuildPointsN => BuildPoints / 10f;
    }

    public struct ECObstruction {
        public short ObstructionHeight;
    }

}
