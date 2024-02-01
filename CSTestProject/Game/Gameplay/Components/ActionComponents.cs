using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Weesals.ECS;
using Weesals.Engine;

namespace Weesals.Game {

    public static class HashInt {
        public static int Compute(string str) {
            int value = 0;
            for (int i = 0; i < str.Length; i++) value = value * 51 + str[i];
            return value;
        }
    }

    /// <summary>
    /// Represents an action that has been issued and can be tracked via its Id
    /// "ActionId" refers to the invoking action
    /// "Pattern" is a unique ID tracking this particular execution
    /// "WithActionId()" creates a "sub" Id for nested invocations
    /// </summary>
    public struct RequestId : IEquatable<RequestId>, IComparable<RequestId> {
        public int Value;
        public int Pattern => Value & 0x00ffffff;
        public byte ActionId => (byte)(Value >> 24);
        public bool IsValid => Value != 0;
        public bool IsAll => Value == -1;
        public RequestId(int value) { Value = value; }
        public RequestId WithActionId(int id) { return new RequestId((id << 24) | (Value & 0x00ffffff)); }
        public static bool operator ==(RequestId r1, RequestId r2) => r1.Equals(r2);
        public static bool operator !=(RequestId r1, RequestId r2) => !r1.Equals(r2);
        public bool Equals(RequestId other) { return Value == other.Value; }
        public int CompareTo(RequestId other) { return Value.CompareTo(other.Value); }
        public bool MatchesPattern(RequestId request) {
            return ((Value ^ request.Value) & 0x00ffffff) == 0;
        }
        public override bool Equals(object? obj) { return obj is RequestId o && o == this; }
        public override int GetHashCode() { return Value; }
        public override string ToString() { return $"{ActionId}:{Pattern}"; }
        public static readonly RequestId Invalid = new RequestId(0);
        public static readonly RequestId All = new RequestId(-1);
    }

    public interface IECAction { RequestId RequestId { get; } }
    public interface IECActionEntity : IECAction { Entity Target { get; } }

    [SparseComponent]
    public struct ECActionMove : IECAction {
        public RequestId RequestId;
        public int Range;
        public Int2 Location;
        RequestId IECAction.RequestId => RequestId;
    }
    [SparseComponent]
    public struct ECActionInteractRange : IECActionEntity {
        public RequestId RequestId;
        public int StartTime;
        public Entity Target;
        RequestId IECAction.RequestId => RequestId;
        Entity IECActionEntity.Target => Target;
    }
    [SparseComponent]
    public struct ECActionInteractMelee : IECActionEntity {
        public RequestId RequestId;
        public int StartTime;
        public int InteractionId;
        public int StrikeInterval;
        public Entity Target;
        RequestId IECAction.RequestId => RequestId;
        Entity IECActionEntity.Target => Target;
    }
    [SparseComponent]
    public struct ECActionGatherer : IECActionEntity {
        public RequestId RequestId;
        public int StartTime;
        public Entity Target;
        RequestId IECAction.RequestId => RequestId;
        Entity IECActionEntity.Target => Target;
    }

}

public enum ActionTypes : byte {
    None = 0x00,
    Move = 0x01, Build = 0x02, Gather = 0x04, GatherDrop = 0x08,
    Attack = 0x30, AttackMelee = 0x10, AttackRanged = 0x20,
    Dual = 0x80,
    All = 0x7f,
};

public struct ActionRequest : IEquatable<ActionRequest> {

    public static readonly Int2 Int2NaN = new Int2(int.MinValue, int.MinValue + 999);

    // -1: No preference. >=0: Force a specific action based on its Id
    public int ActionId;
    public ActionTypes Type;
    // The target entity and position
    public Entity TargetEntity;
    public Int2 TargetLocation;
    public uint Time;
    public int Data1 => TargetLocation.X;
    public int Data2 => TargetLocation.Y;
    public bool HasValidTarget => !TargetEntity.Equals(default) && Type != ActionTypes.None;
    public bool HasValidLocation => Type != ActionTypes.None && !TargetLocation.Equals(default);

    //public ActionRequest() { }
    public ActionRequest(ActionTypes type) {
        Type = type;
        Time = 0;
        TargetEntity = default;
        TargetLocation = Int2NaN;
        ActionId = -1;
    }
    public ActionRequest(Entity target) {
        Type = ActionTypes.All;
        Time = 0;
        TargetEntity = target;
        TargetLocation = Int2NaN;
        ActionId = -1;
    }
    public ActionRequest(Int2 loc) {
        Type = ActionTypes.Move;
        Time = 0;
        TargetEntity = default;
        TargetLocation = loc;
        ActionId = -1;
    }

    // Get the target entities position, or the target position
    public Int2 GetTargetLocation(Int2 from) {
        return GetTargetLocation();
    }
    public Int2 GetTargetLocation() { return TargetLocation; }

    // So that the debug print looks nicer
    public override string ToString() {
        return "E" + TargetEntity + " at (" + TargetLocation + ")";
    }

    public override int GetHashCode() {
        return (int)TargetEntity.Index ^ TargetLocation.GetHashCode();
    }


    // Return if the items within this request reference the same items
    // as the other request
    public bool IsSame(ActionRequest other) {
        return TargetLocation.Equals(other.TargetLocation)
            && TargetEntity == other.TargetEntity;
    }
    public bool HasType(ActionTypes type) {
        return (Type & type) != 0;
    }

    public bool Equals(ActionRequest o) {
        return ActionId == o.ActionId && Type == o.Type &&
            TargetEntity == o.TargetEntity && TargetLocation.Equals(o.TargetLocation);
    }
}
